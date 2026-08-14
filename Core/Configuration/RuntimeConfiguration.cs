namespace DesktopLizard.Core;

internal sealed record RuntimeConfiguration
{
    internal const int MaximumDebugPathCapacity = 10_000;

    public float SimulationRate { get; init; } = 120f;
    public float MaximumFrameCatchUp { get; init; } = 0.25f;
    public float CompositionFallbackDelay { get; init; } = 0.025f;
    public float CompositionFallbackMaximumFrameSteps { get; init; } = 2f;
    public float NavigationMarginModel { get; init; } = 10f;
    public float ReleaseRenderMarginPixels { get; init; } = 2f;
    public float InitialHeading { get; init; } = -0.18f;
    public float SpawnFadeDuration { get; init; } = 0.52f;
    public float DebugPathSampleInterval { get; init; } = 0.10f;
    public float DebugPathMinimumDistance { get; init; } = 1.5f;
    public int DebugPathCapacity { get; init; } = 300;
    public float DebugFpsResponse { get; init; } = 4f;
    public float DebugMaximumFps { get; init; } = 999f;
    public int DebugPanelWidthPixels { get; init; } = 360;
    public int DebugPanelHeightPixels { get; init; } = 132;
    public int DebugPanelGapPixels { get; init; } = 13;

    internal void Validate(List<string> failures)
    {
        if (!float.IsFinite(SimulationRate) || SimulationRate is < 60f or > 480f)
        {
            failures.Add("simulation rate must be finite and in [60,480] Hz.");
        }
        if (!float.IsFinite(MaximumFrameCatchUp) || MaximumFrameCatchUp is <= 0f or > 1f)
        {
            failures.Add("maximum frame catch-up must be finite and in (0,1] seconds.");
        }
        BehaviorConfiguration.RequirePositive(CompositionFallbackDelay, "composition fallback delay", failures);
        BehaviorConfiguration.RequirePositive(
            CompositionFallbackMaximumFrameSteps,
            "composition fallback maximum frame steps",
            failures);
        BehaviorConfiguration.RequireNonNegative(NavigationMarginModel, "navigation margin", failures);
        BehaviorConfiguration.RequireNonNegative(
            ReleaseRenderMarginPixels,
            "release render margin",
            failures);
        if (!float.IsFinite(InitialHeading))
        {
            failures.Add("initial heading must be finite.");
        }
        BehaviorConfiguration.RequirePositive(SpawnFadeDuration, "spawn fade duration", failures);
        BehaviorConfiguration.RequirePositive(DebugPathSampleInterval, "debug path sample interval", failures);
        BehaviorConfiguration.RequireNonNegative(
            DebugPathMinimumDistance,
            "debug path minimum distance",
            failures);
        if (DebugPathCapacity is < 2 or > MaximumDebugPathCapacity)
        {
            failures.Add(
                $"debug path capacity must be in [2,{MaximumDebugPathCapacity}].");
        }
        if (DebugPanelWidthPixels < 80 || DebugPanelHeightPixels < 40 || DebugPanelGapPixels < 0)
        {
            failures.Add("invalid debug-panel dimensions.");
        }
        BehaviorConfiguration.RequirePositive(DebugFpsResponse, "debug FPS response", failures);
        BehaviorConfiguration.RequirePositive(DebugMaximumFps, "debug maximum FPS", failures);
    }
}
