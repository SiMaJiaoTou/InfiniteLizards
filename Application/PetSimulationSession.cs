using System.Numerics;
using DesktopLizard.Core;

namespace DesktopLizard.AppRuntime;

internal readonly record struct PetSimulationFrameInput(
    float FrameDelta,
    FloatRect NavigationArea,
    PointerObservation Pointer,
    bool IsDragging,
    float ModelToScreenScale)
{
    public bool CaptureDebugFrame { get; init; }
    public float DpiScale { get; init; }

    /// <summary>
    /// Optional host-derived physical safety contract. Null keeps the legacy
    /// pure-core path, which derives the area from <see cref="NavigationArea"/>.
    /// </summary>
    public LostGripSafetyContext? LostGripSafety { get; init; }
}

internal readonly record struct PetSimulationFrameOutput(
    float FrameDelta,
    Vector2 Position,
    Vector2 LookDirection)
{
    /// <summary>The fixed-step time actually consumed by this display frame.</summary>
    public float SimulationDelta { get; init; }

    /// <summary>The number of complete fixed steps consumed by this display frame.</summary>
    public int SimulationSteps { get; init; }

    /// <summary>A coherent immutable pose captured once after all substeps.</summary>
    public LizardRenderFrame RenderFrame { get; init; }

    /// <summary>Optional immutable diagnostics captured from the same completed frame.</summary>
    public DebugFrameSnapshot? DebugFrame { get; init; }

    public bool IsPaused { get; init; }
}

/// <summary>
/// Owns the behavior and procedural pose models and advances them in their
/// required order. Window, pointer, DPI and work-area sampling stay outside of
/// this application-layer session.
/// </summary>
internal sealed class PetSimulationSession
{
    private readonly FixedStepRunner _fixedStepRunner = new();
    private readonly DebugTelemetryCoordinator _debugTelemetry;
    private readonly BehaviorController _behavior;
    private readonly ProceduralLizard _lizard;
    private Vector2 _lastModelScreenPosition;
    private LizardRenderFrame _renderFrame;

    public LizardProfile Profile { get; }
    public Vector2 Position => _behavior.Position;
    public bool IsPaused => _behavior.IsPaused;
    public LizardRenderFrame InitialRenderFrame => _renderFrame;

    // Diagnostics deliberately opt into the mutable seam by name. Runtime UI
    // code consumes commands, scalar state and immutable frame snapshots only.
    internal BehaviorController BehaviorForDiagnostics => _behavior;
    internal ProceduralLizard LizardForDiagnostics => _lizard;

    public PetSimulationSession(int behaviorSeed, LizardProfile? profile = null)
    {
        Profile = profile ?? LizardProfile.Default;
        _behavior = new BehaviorController(behaviorSeed, Profile);
        _lizard = new ProceduralLizard(Profile);
        _debugTelemetry = new DebugTelemetryCoordinator(Profile);
        _renderFrame = _lizard.CaptureRenderFrame();
    }

    public void Reset(Vector2 position, float heading)
    {
        _fixedStepRunner.Reset();
        _debugTelemetry.ClearPath();
        _behavior.Reset(position, heading);
        _lastModelScreenPosition = position;
    }

    public PetSimulationFrameOutput Advance(PetSimulationFrameInput input)
    {
        var modelToScreenScale = float.IsFinite(input.ModelToScreenScale) &&
                                 input.ModelToScreenScale > 0.0001f
            ? input.ModelToScreenScale
            : 1f;

        // MouseMove updates a grabbed pet once per display frame, while the
        // ragdoll normally advances through two or more 120 Hz substeps. Feed
        // that external window delta proportionally across those substeps;
        // applying it all to the first one produces alternating large/zero
        // anchor velocities and makes the hanging joints visibly shiver.
        var spreadGrabDelta =
            input.IsDragging &&
            _behavior.State == RoamingState.Grabbed;
        var grabDeltaRemaining = spreadGrabDelta
            ? (_behavior.Position - _lastModelScreenPosition) / modelToScreenScale
            : Vector2.Zero;

        var advanceResult = _fixedStepRunner.Advance(
            input.FrameDelta,
            1f / Profile.Runtime.SimulationRate,
            Profile.Runtime.MaximumFrameCatchUp,
            (step, stepsRemaining) =>
        {
            var previousLostGripPhase = _behavior.LostGripPhase;
            var previousLostGripReachProgress = _behavior.LostGripReachProgress;
            if (input.LostGripSafety is { } lostGripSafety)
            {
                _behavior.Update(
                    step,
                    input.NavigationArea,
                    lostGripSafety,
                    input.Pointer);
            }
            else
            {
                _behavior.Update(step, input.NavigationArea, input.Pointer);
            }

            Vector2 screenDeltaModel;
            if (spreadGrabDelta)
            {
                // Keep an external drag delta pending when a high-refresh
                // display frame produces zero simulation steps. Once a full
                // step is available, distribute the entire pending movement
                // evenly over the steps consumed by that rendered frame.
                screenDeltaModel = grabDeltaRemaining / stepsRemaining;
                grabDeltaRemaining -= screenDeltaModel;
            }
            else
            {
                screenDeltaModel =
                    (_behavior.Position - _lastModelScreenPosition) /
                    modelToScreenScale;
                _lastModelScreenPosition = _behavior.Position;
            }

            // Any Falling -> Regripping step that still contains physical
            // window travel must be consumed by the free-fall rig. A normal
            // endpoint latches a complete reach; an emergency safety stop keeps
            // the previous reach intent so it cannot fabricate contact while
            // still preserving the particle rig's reference-frame continuity.
            var animationLostGripPhase =
                previousLostGripPhase == LostGripFallPhase.Falling &&
                _behavior.LostGripPhase == LostGripFallPhase.Regripping &&
                screenDeltaModel.LengthSquared() > 0.000001f
                    ? LostGripFallPhase.Falling
                    : _behavior.LostGripPhase;
            var completingCatchOnMovingStep =
                previousLostGripPhase == LostGripFallPhase.Falling &&
                _behavior.LostGripPhase == LostGripFallPhase.Regripping &&
                animationLostGripPhase == LostGripFallPhase.Falling;
            var catchPreparationProgress =
                animationLostGripPhase == LostGripFallPhase.Falling
                    ? completingCatchOnMovingStep
                        ? _behavior.LostGripCatchReason ==
                          LostGripCatchReason.ReachedTarget
                            ? 1f
                            : previousLostGripReachProgress
                        : _behavior.LostGripReachProgress
                    : 0f;
            var animationInput = new LizardAnimationInput(
                _behavior.Heading,
                _behavior.NormalizedSpeed,
                ToPoseMode(_behavior.State, animationLostGripPhase),
                _behavior.Emotion,
                ToPoseProgress(_behavior, animationLostGripPhase),
                screenDeltaModel)
            {
                CatchPreparationProgress = catchPreparationProgress
            };
            _lizard.Update(step, animationInput);
        });

        if (spreadGrabDelta && advanceResult.StepCount > 0)
        {
            _lastModelScreenPosition = _behavior.Position;
        }

        // Reuse the immutable pose on high-refresh display frames that did not
        // consume a fixed step. This keeps rendering coherent without paying
        // for two new geometry buffers when the simulation did not change.
        if (advanceResult.StepCount > 0)
        {
            _renderFrame = _lizard.CaptureRenderFrame();
        }
        DebugFrameSnapshot? debugFrame = null;
        if (input.CaptureDebugFrame)
        {
            var dpiScale = float.IsFinite(input.DpiScale) && input.DpiScale > 0.0001f
                ? input.DpiScale
                : 1f;
            var behaviorDebug = _behavior.CaptureDebugSnapshot(input.NavigationArea);
            debugFrame = _debugTelemetry.Capture(
                new DebugTelemetryFrameInput(
                    advanceResult.FrameDelta,
                    dpiScale,
                    modelToScreenScale / dpiScale,
                    input.Pointer),
                in behaviorDebug,
                _lizard.CaptureDebugSnapshot());
        }

        return new PetSimulationFrameOutput(
            advanceResult.FrameDelta,
            _behavior.Position,
            _behavior.LookTarget - _behavior.Position)
        {
            SimulationDelta = advanceResult.SimulationDelta,
            SimulationSteps = advanceResult.StepCount,
            RenderFrame = _renderFrame,
            DebugFrame = debugFrame,
            IsPaused = _behavior.IsPaused
        };
    }

    public void BeginGrab(Vector2 modelPoint)
    {
        _lizard.BeginGrab(modelPoint);
        _behavior.BeginGrab();
    }

    public void DragTo(Vector2 screenPosition) => _behavior.DragTo(screenPosition);

    public void EndGrab(Vector2 settledPosition)
    {
        _lastModelScreenPosition = settledPosition;
        _behavior.EndGrab(settledPosition);
    }

    public bool TogglePaused()
    {
        _behavior.IsPaused = !_behavior.IsPaused;
        return _behavior.IsPaused;
    }

    public Vector2 MoveToCenter(Vector2 center)
    {
        _behavior.MoveToCenter(center);
        _lastModelScreenPosition = _behavior.Position;
        return _behavior.Position;
    }

    public DebugPlaybackResult TryPlayDebugAction(
        AutonomousAction action,
        FloatRect navigationArea) =>
        _behavior.TryPlayDebugAction(action, navigationArea);

    public DebugPlaybackResult TryPlayDebugAction(
        AutonomousAction action,
        FloatRect navigationArea,
        LostGripSafetyContext lostGripSafety) =>
        _behavior.TryPlayDebugAction(action, navigationArea, lostGripSafety);

    public void ClearDebugPath() => _debugTelemetry.ClearPath();

    private static LizardPoseMode ToPoseMode(
        RoamingState state,
        LostGripFallPhase lostGripPhase) => state switch
    {
        RoamingState.Spawn or RoamingState.Idle => LizardPoseMode.Rest,
        RoamingState.Observe => LizardPoseMode.Observe,
        RoamingState.FastSCurveCrawl => LizardPoseMode.FastSCurve,
        RoamingState.Grabbed => LizardPoseMode.Grabbed,
        RoamingState.ReleaseSettle => LizardPoseMode.ReleaseSettle,
        RoamingState.LostGripFall => lostGripPhase switch
        {
            LostGripFallPhase.Falling => LizardPoseMode.FreeFall,
            LostGripFallPhase.Regripping => LizardPoseMode.Regrip,
            _ => throw new InvalidOperationException(
                "LostGripFall requires an animation phase.")
        },
        RoamingState.ForwardCrawl or
        RoamingState.FastForwardCrawl or
        RoamingState.CurveCrawl or
        RoamingState.SCurveCrawl or
        RoamingState.TurnAround or
        RoamingState.MouseChase or
        RoamingState.EdgeTurn or
        RoamingState.EscapeSprint => LizardPoseMode.Locomotion,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    private static float ToPoseProgress(
        BehaviorController behavior,
        LostGripFallPhase lostGripPhase) => lostGripPhase switch
    {
        LostGripFallPhase.Falling => behavior.LostGripFallProgress,
        LostGripFallPhase.Regripping => behavior.LostGripRegripProgress,
        _ => behavior.DropProgress
    };
}
