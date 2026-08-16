using System.Numerics;
using DesktopLizard.Core;

namespace DesktopLizard.AppRuntime;

internal readonly record struct PetSimulationFixedStepInput(
    float StepDelta,
    int StepsRemaining,
    FloatRect NavigationArea,
    PointerObservation Pointer,
    bool IsDragging,
    float ModelToScreenScale)
{
    public LostGripSafetyContext? LostGripSafety { get; init; }
}

/// <summary>
/// Owns the behavior and procedural pose models and advances them in their
/// required order. Window, pointer, DPI and work-area sampling stay outside of
/// this application-layer session.
/// </summary>
internal sealed class PetSimulationSession
{
    private readonly int _behaviorSeed;
    private BehaviorController _behavior;
    private ProceduralLizard _lizard;
    private Vector2 _cumulativeWorldRebase;
    private Vector2 _lastModelScreenPosition;
    private LizardRenderFrame _renderFrame;
    private bool _renderFrameDirty;
    private int _resetGeneration;

    public LizardProfile Profile { get; }
    public Vector2 Position => _behavior.Position;
    public Vector2 LookDirection => _behavior.LookTarget - _behavior.Position;
    public bool IsPaused => _behavior.IsPaused;
    public LizardRenderFrame InitialRenderFrame => _renderFrame;
    internal int ResetGeneration => _resetGeneration;
    internal Vector2 CumulativeWorldRebase => _cumulativeWorldRebase;

    // Diagnostics deliberately opt into the mutable seam by name. Runtime UI
    // code consumes commands, scalar state and immutable frame snapshots only.
    internal BehaviorController BehaviorForDiagnostics => _behavior;
    internal ProceduralLizard LizardForDiagnostics => _lizard;

    public PetSimulationSession(int behaviorSeed, LizardProfile? profile = null)
    {
        Profile = profile ?? LizardProfile.Default;
        _behaviorSeed = behaviorSeed;
        _behavior = null!;
        _lizard = null!;
        RecreateSimulationModels();
        _renderFrame = _lizard.CaptureRenderFrame();
        _renderFrameDirty = false;
    }

    public void Reset(Vector2 position, float heading)
    {
        RecreateSimulationModels();
        _behavior.Reset(position, heading);
        _cumulativeWorldRebase = Vector2.Zero;
        _lastModelScreenPosition = position;
        _renderFrame = _lizard.CaptureRenderFrame();
        _renderFrameDirty = false;
        _resetGeneration = unchecked(_resetGeneration + 1);
    }

    /// <summary>
    /// Advances exactly one engine-authorized fixed step. Display-frame
    /// accumulation belongs to DesktopPetRuntime; the legacy WPF host keeps
    /// its compatibility adapter outside the gameplay assembly.
    /// </summary>
    public void AdvanceFixedStep(in PetSimulationFixedStepInput input)
    {
        if (!float.IsFinite(input.StepDelta) || input.StepDelta <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "The fixed step must be finite and positive.");
        }
        if (input.StepsRemaining <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "StepsRemaining must include the current fixed step.");
        }

        var modelToScreenScale = NormalizeModelToScreenScale(
            input.ModelToScreenScale);
        var spreadGrabDelta =
            input.IsDragging &&
            _behavior.State == RoamingState.Grabbed;
        var previousLostGripPhase = _behavior.LostGripPhase;
        var previousLostGripReachProgress = _behavior.LostGripReachProgress;
        if (input.LostGripSafety is { } lostGripSafety)
        {
            _behavior.Update(
                input.StepDelta,
                input.NavigationArea,
                lostGripSafety,
                input.Pointer);
        }
        else
        {
            _behavior.Update(input.StepDelta, input.NavigationArea, input.Pointer);
        }

        Vector2 screenDeltaModel;
        if (spreadGrabDelta)
        {
            // A display callback may move the host farther than one 120 Hz
            // step. Consume an equal share of the still-pending world delta;
            // zero-step display frames naturally leave the delta pending.
            var remainingWorldDelta = _behavior.Position - _lastModelScreenPosition;
            screenDeltaModel =
                remainingWorldDelta /
                modelToScreenScale /
                input.StepsRemaining;
            _lastModelScreenPosition = input.StepsRemaining == 1
                ? _behavior.Position
                : _lastModelScreenPosition + screenDeltaModel * modelToScreenScale;
        }
        else
        {
            screenDeltaModel =
                (_behavior.Position - _lastModelScreenPosition) /
                modelToScreenScale;
            _lastModelScreenPosition = _behavior.Position;
        }

        // Any Falling -> Regripping step that still contains physical window
        // travel must be consumed by the free-fall rig. A normal endpoint
        // latches a complete reach; an emergency safety stop keeps the prior
        // reach intent and must not fabricate contact.
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
        var reachedCatchTarget =
            previousLostGripPhase == LostGripFallPhase.Falling &&
            _behavior.LostGripPhase == LostGripFallPhase.Regripping &&
            _behavior.LostGripCatchReason == LostGripCatchReason.ReachedTarget;
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
            CatchPreparationProgress = catchPreparationProgress,
            CatchPreparationContactAllowed = reachedCatchTarget
        };
        _lizard.Update(input.StepDelta, animationInput);
        _renderFrameDirty = true;
    }

    public LizardRenderFrame CaptureSnapshot()
    {
        if (_renderFrameDirty)
        {
            _renderFrame = _lizard.CaptureRenderFrame();
            _renderFrameDirty = false;
        }

        return _renderFrame;
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

    public Vector2 RebaseWorldPosition(Vector2 delta)
    {
        if (delta == Vector2.Zero)
        {
            return _behavior.Position;
        }

        var translatedLastPosition = _lastModelScreenPosition + delta;
        if (!float.IsFinite(translatedLastPosition.X) ||
            !float.IsFinite(translatedLastPosition.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                "The rebased model position must remain finite.");
        }

        var translatedCumulativeRebase = _cumulativeWorldRebase + delta;
        if (!float.IsFinite(translatedCumulativeRebase.X) ||
            !float.IsFinite(translatedCumulativeRebase.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                "The cumulative world-coordinate rebase must remain finite.");
        }

        _behavior.TranslateWorld(delta);
        _cumulativeWorldRebase = translatedCumulativeRebase;
        _lastModelScreenPosition = translatedLastPosition;
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

    private void RecreateSimulationModels()
    {
        _behavior = new BehaviorController(_behaviorSeed, Profile);
        _lizard = new ProceduralLizard(Profile);
    }

    private static float NormalizeModelToScreenScale(float value) =>
        float.IsFinite(value) && value > 0.0001f ? value : 1f;

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
