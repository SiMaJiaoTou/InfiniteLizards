using System.Numerics;
using System.Runtime.CompilerServices;
using DesktopPet.Engine;
using DesktopLizard.Core;

namespace DesktopLizard.AppRuntime;

/// <summary>
/// Display-frame input retained only for the legacy WPF host. New desktop
/// hosts use DesktopPetRuntime and never expose display cadence to gameplay.
/// </summary>
internal readonly record struct PetSimulationFrameInput(
    float FrameDelta,
    FloatRect NavigationArea,
    PointerObservation Pointer,
    bool IsDragging,
    float ModelToScreenScale)
{
    public bool CaptureDebugFrame { get; init; }
    public float DpiScale { get; init; }

    public LostGripSafetyContext? LostGripSafety { get; init; }
}

internal readonly record struct PetSimulationFrameOutput(
    float FrameDelta,
    Vector2 Position,
    Vector2 LookDirection)
{
    public float SimulationDelta { get; init; }
    public int SimulationSteps { get; init; }
    public LizardRenderFrame RenderFrame { get; init; }
    public DebugFrameSnapshot? DebugFrame { get; init; }
    public bool IsPaused { get; init; }
}

/// <summary>
/// Compatibility-only display-frame adapter for the original WPF host. This
/// file is deliberately excluded from InfiniteLizards.Gameplay so the portable
/// gameplay assembly cannot own a second display-cadence accumulator.
/// </summary>
internal static class LegacyPetSimulationFrameAdapter
{
    private static readonly ConditionalWeakTable<PetSimulationSession, RunnerState>
        States = new();

    public static PetSimulationFrameOutput Advance(
        this PetSimulationSession session,
        PetSimulationFrameInput input)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = States.GetValue(
            session,
            value => new RunnerState(value));

        lock (state)
        {
            SynchronizeState(session, state);

            var advanceResult = state.Runner.Advance(
                input.FrameDelta,
                1f / session.Profile.Runtime.SimulationRate,
                session.Profile.Runtime.MaximumFrameCatchUp,
                (step, stepsRemaining) => session.AdvanceFixedStep(
                    new PetSimulationFixedStepInput(
                        step,
                        stepsRemaining,
                        input.NavigationArea,
                        input.Pointer,
                        input.IsDragging,
                        input.ModelToScreenScale)
                    {
                        LostGripSafety = input.LostGripSafety
                    }));

            var renderFrame = session.CaptureSnapshot();
            DebugFrameSnapshot? debugFrame = null;
            if (input.CaptureDebugFrame)
            {
                var behaviorDebug =
                    session.BehaviorForDiagnostics.CaptureDebugSnapshot(
                        input.NavigationArea);
                debugFrame = state.DebugTelemetry.Capture(
                    new LegacyDebugTelemetryFrameInput(
                        advanceResult.FrameDelta,
                        NormalizeDpiScale(input.DpiScale),
                        NormalizeModelToScreenScale(input.ModelToScreenScale),
                        input.Pointer),
                    in behaviorDebug,
                    session.LizardForDiagnostics.CaptureDebugSnapshot());
            }

            return new PetSimulationFrameOutput(
                advanceResult.FrameDelta,
                session.Position,
                session.LookDirection)
            {
                SimulationDelta = advanceResult.SimulationDelta,
                SimulationSteps = advanceResult.StepCount,
                RenderFrame = renderFrame,
                DebugFrame = debugFrame,
                IsPaused = session.IsPaused
            };
        }
    }

    public static void ClearDebugPath(this PetSimulationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = States.GetValue(
            session,
            value => new RunnerState(value));

        lock (state)
        {
            SynchronizeState(session, state);
            state.DebugTelemetry.ClearPath();
        }
    }

    private static void SynchronizeState(
        PetSimulationSession session,
        RunnerState state)
    {
        if (state.ResetGeneration != session.ResetGeneration)
        {
            state.Reset(session);
            return;
        }

        var rebaseDelta =
            session.CumulativeWorldRebase - state.AppliedWorldRebase;
        if (rebaseDelta == Vector2.Zero)
        {
            return;
        }

        state.DebugTelemetry.TranslateWorld(rebaseDelta);
        state.AppliedWorldRebase = session.CumulativeWorldRebase;
    }

    private static float NormalizeDpiScale(float value) =>
        float.IsFinite(value) && value > 0.0001f ? value : 1f;

    private static float NormalizeModelToScreenScale(float value) =>
        float.IsFinite(value) && value > 0.0001f ? value : 1f;

    private sealed class RunnerState
    {
        public FixedStepRunner Runner { get; } = new();
        public LegacyDebugTelemetryCoordinator DebugTelemetry { get; private set; }
        public int ResetGeneration { get; private set; }
        public Vector2 AppliedWorldRebase { get; set; }

        public RunnerState(PetSimulationSession session)
        {
            DebugTelemetry = null!;
            Reset(session);
        }

        public void Reset(PetSimulationSession session)
        {
            Runner.Reset();
            DebugTelemetry = new LegacyDebugTelemetryCoordinator(session.Profile);
            ResetGeneration = session.ResetGeneration;
            AppliedWorldRebase = session.CumulativeWorldRebase;
        }
    }
}
