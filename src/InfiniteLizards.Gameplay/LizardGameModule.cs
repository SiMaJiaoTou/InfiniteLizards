using System.Numerics;
using DesktopPet.Engine;
using DesktopLizard.AppRuntime;
using DesktopLizard.Core;

namespace InfiniteLizards.Gameplay;

/// <summary>
/// The only adapter between the generic desktop-pet engine contract and the
/// lizard-specific simulation. Platform hosts never reach into behavior,
/// animation, or physics state.
/// </summary>
internal sealed class LizardGameModule : IDesktopPetGame<LizardRenderFrame>
{
    private readonly PetSimulationSession _session;
    private readonly LizardPortableDebugBridge _debugBridge;

    public LizardProfile Profile { get; }
    public DesktopPetMetrics Metrics { get; }
    public DesktopPetTiming Timing { get; }
    public Vector2 Position => _session.Position;
    public Vector2 LookDirection => _session.LookDirection;
    public bool IsPaused => _session.IsPaused;
    internal ILizardPortableDebugBridge DebugBridge => _debugBridge;

    public LizardGameModule(LizardProfile profile, int behaviorSeed)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _session = new PetSimulationSession(behaviorSeed, profile);
        _debugBridge = new LizardPortableDebugBridge(_session, profile);

        var appearance = profile.Appearance;
        var runtime = profile.Runtime;
        Timing = new DesktopPetTiming(
            1f / runtime.SimulationRate,
            runtime.MaximumFrameCatchUp);
        var envelope = LizardGeometryEnvelope.Calculate(
            appearance,
            profile.Gait,
            profile.SecondaryMotion,
            profile.Rendering);
        var modelToWorld = appearance.VisualScale;
        var canvasSizeWorld = appearance.RenderCanvasSize * modelToWorld;
        Metrics = new DesktopPetMetrics(
            WorldSize.Square(canvasSizeWorld),
            (Math.Max(
                 appearance.CreatureCanvasSize * 0.5f,
                 envelope.NormalModelRadius) +
             runtime.NavigationMarginModel) * modelToWorld,
            appearance.RenderCanvasSize * 0.5f * modelToWorld +
            runtime.ReleaseRenderMarginPixels,
            (Math.Max(
                 appearance.CreatureCanvasSize * 0.5f,
                 envelope.NormalModelRadius) +
             runtime.NavigationMarginModel) * modelToWorld +
            profile.Behavior.LostGripFall.BottomSafetyInset,
            modelToWorld,
            runtime.InitialHeading,
            runtime.SpawnFadeDuration);
    }

    public void Reset(Vector2 position)
    {
        _session.Reset(position, Metrics.InitialHeading);
        _debugBridge.ObserveSimulationReset();
    }

    public void AdvanceFixedStep(in DesktopPetFixedStepInput input)
    {
        _debugBridge.ObserveEnvironment(input);
        _session.AdvanceFixedStep(new PetSimulationFixedStepInput(
            input.StepDelta,
            input.StepsRemaining,
            ToCore(input.NavigationArea),
            new PointerObservation(
                input.Pointer.Position,
                input.Pointer.IsAvailable,
                input.Pointer.IsInteractionBlocked),
            input.IsPrimaryInteractionActive,
            Metrics.ModelToWorldScale)
        {
            LostGripSafety = new LostGripSafetyContext(
                ToCore(input.FullRenderSafety.Area),
                input.FullRenderSafety.IsAvailable)
        });
    }

    public LizardRenderFrame CaptureSnapshot() => _session.CaptureSnapshot();

    public void BeginPrimaryInteraction(Vector2 modelPoint) =>
        _session.BeginGrab(modelPoint);

    public void DragTo(Vector2 worldPosition) => _session.DragTo(worldPosition);

    public void EndPrimaryInteraction(Vector2 settledWorldPosition) =>
        _session.EndGrab(settledWorldPosition);

    public bool TogglePaused() => _session.TogglePaused();

    public void MoveTo(Vector2 worldPosition) => _session.MoveToCenter(worldPosition);

    public void RebaseWorldPosition(Vector2 delta) =>
        _session.RebaseWorldPosition(delta);

    private static FloatRect ToCore(WorldRect value) => new(
        value.Left,
        value.Top,
        value.Right,
        value.Bottom);
}
