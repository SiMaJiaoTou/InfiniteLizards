namespace DesktopLizard.Core;

/// <summary>
/// WPF-independent visual dimensions and render refresh policy. Colors and
/// body proportions remain in <see cref="AppearanceConfiguration"/>.
/// </summary>
internal sealed record RenderingConfiguration
{
    public float ShadowOffsetX { get; init; } = 3f;
    public float ShadowOffsetY { get; init; } = 7f;
    public float NoseRadius { get; init; } = 13.5f;
    public float FrontFootRadius { get; init; } = 10.5f;
    public float RearFootRadius { get; init; } = 10f;
    public float LiftFootContraction { get; init; } = 0.20f;
    public float MinimumBlinkScale { get; init; } = 0.10f;
    public float BlinkClosure { get; init; } = 0.92f;
    public float EyeShadowRadius { get; init; } = 15.5f;
    public float EyeRadius { get; init; } = 15.2f;
    public float PupilOffset { get; init; } = 3f;
    public float PupilRadius { get; init; } = 12.5f;
    public float MinimumPupilRadius { get; init; } = 1.2f;
    public float PupilVisibilityBlinkScale { get; init; } = 0.14f;
    public float HitFootRadius { get; init; } = 14f;
    public float HitEyeRadius { get; init; } = 17f;
    public float HitTestTolerance { get; init; } = 1.6f;
    public float HitGeometryRefreshRate { get; init; } = 30f;
    public RgbConfiguration EyeWhiteColor { get; init; } = new(255, 255, 255);

    internal void Validate(List<string> failures)
    {
        if (!float.IsFinite(ShadowOffsetX) || !float.IsFinite(ShadowOffsetY))
        {
            failures.Add("shadow offsets must be finite.");
        }
        BehaviorConfiguration.RequirePositive(NoseRadius, nameof(NoseRadius), failures);
        BehaviorConfiguration.RequirePositive(FrontFootRadius, nameof(FrontFootRadius), failures);
        BehaviorConfiguration.RequirePositive(RearFootRadius, nameof(RearFootRadius), failures);
        RequireUnit(LiftFootContraction, nameof(LiftFootContraction), failures);
        RequireUnit(MinimumBlinkScale, nameof(MinimumBlinkScale), failures);
        RequireUnit(BlinkClosure, nameof(BlinkClosure), failures);
        BehaviorConfiguration.RequirePositive(EyeShadowRadius, nameof(EyeShadowRadius), failures);
        BehaviorConfiguration.RequirePositive(EyeRadius, nameof(EyeRadius), failures);
        BehaviorConfiguration.RequireNonNegative(PupilOffset, nameof(PupilOffset), failures);
        BehaviorConfiguration.RequirePositive(PupilRadius, nameof(PupilRadius), failures);
        BehaviorConfiguration.RequirePositive(MinimumPupilRadius, nameof(MinimumPupilRadius), failures);
        RequireUnit(PupilVisibilityBlinkScale, nameof(PupilVisibilityBlinkScale), failures);
        BehaviorConfiguration.RequirePositive(HitFootRadius, nameof(HitFootRadius), failures);
        BehaviorConfiguration.RequirePositive(HitEyeRadius, nameof(HitEyeRadius), failures);
        BehaviorConfiguration.RequirePositive(HitTestTolerance, nameof(HitTestTolerance), failures);
        BehaviorConfiguration.RequirePositive(HitGeometryRefreshRate, nameof(HitGeometryRefreshRate), failures);
        EyeWhiteColor.Validate("eye-white color", failures);
    }

    private static void RequireUnit(float value, string name, List<string> failures)
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
        {
            failures.Add($"{name} must be in [0,1].");
        }
    }
}
