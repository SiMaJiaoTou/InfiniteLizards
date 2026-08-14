namespace DesktopLizard.Core;

internal sealed record AppearanceConfiguration
{
    public float CreatureCanvasSize { get; init; } = 360f;
    public float RenderCanvasSize { get; init; } = 600f;
    public float VisualScale { get; init; } = 0.6f;
    public float SpineLinkLength { get; init; } = 16f;
    public float SpineMaximumBend { get; init; } = MathF.PI / 9f;
    public float HeadAnchorOffset { get; init; } = 50f;
    public float SpineRestCurveAmplitude { get; init; } = 13f;
    public float[] BodyWidths { get; init; } =
    [
        12f, 17f, 20f, 19.5f, 18.5f, 17.5f, 16.5f,
        15f, 13f, 11f, 9f, 7f, 4.5f, 0.8f
    ];
    public RgbConfiguration BodyColor { get; init; } = new(94, 176, 96);
    public RgbConfiguration ShadowColor { get; init; } = new(94, 112, 91);
    public RgbConfiguration PupilColor { get; init; } = new(41, 41, 41);
    public float ShadowOpacity { get; init; } = 58f / 255f;
    public float LimbWidth { get; init; } = 12.5f;
    public float ShadowLimbWidth { get; init; } = 14.5f;
    public float HitLimbWidth { get; init; } = 24f;

    internal void Validate(List<string> failures)
    {
        BehaviorConfiguration.RequirePositive(CreatureCanvasSize, "creature canvas size", failures);
        BehaviorConfiguration.RequirePositive(RenderCanvasSize, "render canvas size", failures);
        BehaviorConfiguration.RequirePositive(VisualScale, "visual scale", failures);
        BehaviorConfiguration.RequirePositive(SpineLinkLength, "spine link length", failures);
        BehaviorConfiguration.RequirePositive(SpineMaximumBend, "spine maximum bend", failures);
        if (!float.IsFinite(HeadAnchorOffset))
        {
            failures.Add("head anchor offset must be finite.");
        }
        BehaviorConfiguration.RequireNonNegative(SpineRestCurveAmplitude, "spine rest-curve amplitude", failures);
        if (BodyWidths is null ||
            BodyWidths.Length < 5 ||
            BodyWidths.Any(value => !float.IsFinite(value) || value < 0f))
        {
            failures.Add("appearance body widths must contain at least five finite non-negative values.");
        }
        BodyColor.Validate("body color", failures);
        ShadowColor.Validate("shadow color", failures);
        PupilColor.Validate("pupil color", failures);
        if (!float.IsFinite(ShadowOpacity) || ShadowOpacity is < 0f or > 1f)
        {
            failures.Add("shadow opacity must be in [0,1].");
        }
        BehaviorConfiguration.RequirePositive(LimbWidth, "limb width", failures);
        BehaviorConfiguration.RequirePositive(ShadowLimbWidth, "shadow limb width", failures);
        BehaviorConfiguration.RequirePositive(HitLimbWidth, "hit limb width", failures);
    }
}

internal sealed record RgbConfiguration(int Red, int Green, int Blue)
{
    internal void Validate(string name, List<string> failures)
    {
        if (Red is < 0 or > 255 || Green is < 0 or > 255 || Blue is < 0 or > 255)
        {
            failures.Add($"{name} channels must be in [0,255].");
        }
    }
}
