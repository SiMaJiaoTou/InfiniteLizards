using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// Computes the conservative model-space bounds shared by configuration,
/// native-window placement and release safety. Keeping this calculation in
/// one place prevents configurable body proportions from silently outgrowing
/// the transparent render surface or the desktop-edge navigation margin.
/// </summary>
internal readonly record struct LizardGeometryEnvelope(
    float NormalModelRadius,
    float DanglingModelRadius)
{
    private const int FrontShoulderIndex = 1;
    private const int RearShoulderIndex = 4;
    private const float RasterSafetyMargin = 2f;

    public static LizardGeometryEnvelope Calculate(
        AppearanceConfiguration appearance,
        GaitConfiguration gait,
        SecondaryMotionConfiguration secondaryMotion,
        RenderingConfiguration rendering)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(gait);
        ArgumentNullException.ThrowIfNull(secondaryMotion);
        ArgumentNullException.ThrowIfNull(rendering);

        var bodyWidths = appearance.BodyWidths ?? [];
        if (bodyWidths.Length == 0)
        {
            return new LizardGeometryEnvelope(float.PositiveInfinity, float.PositiveInfinity);
        }

        var shadowTravel = new Vector2(
            rendering.ShadowOffsetX,
            rendering.ShadowOffsetY).Length();
        var tailMotion = MathF.Max(
            secondaryMotion.IdleTailAmplitude,
            secondaryMotion.ObserveTailAmplitude) *
            (1f + secondaryMotion.TailSecondaryWeight);
        var breathingScale = 1f + secondaryMotion.BreathingAmplitude +
                             secondaryMotion.LandingSquashAmplitude;
        var maximumBodyWidth = bodyWidths.Max() * breathingScale;
        var maximumFootRadius = MathF.Max(
            rendering.FrontFootRadius,
            rendering.RearFootRadius);
        var maximumLegReach = MathF.Max(
            gait.FrontLegLinkLength,
            gait.RearLegLinkLength) * gait.MaximumReachFactor;
        var maximumDecorationRadius = MathF.Max(
            MathF.Max(rendering.NoseRadius, rendering.EyeShadowRadius),
            maximumFootRadius);

        var normalRadius = 0f;
        for (var index = 0; index < bodyWidths.Length; index++)
        {
            var longitudinal = MathF.Abs(
                appearance.HeadAnchorOffset - index * appearance.SpineLinkLength);
            normalRadius = MathF.Max(
                normalRadius,
                longitudinal + bodyWidths[index] * breathingScale + tailMotion);
        }

        var headCenterRadius = MathF.Abs(appearance.HeadAnchorOffset);
        var noseRadius = headCenterRadius + secondaryMotion.NoseForwardOffset + rendering.NoseRadius;
        var eyeCenterOffset = new Vector2(
            secondaryMotion.EyeForwardOffset,
            secondaryMotion.EyeLateralOffset).Length();
        var eyeRadius = headCenterRadius + eyeCenterOffset +
                        secondaryMotion.LandingBobAmplitude * secondaryMotion.EyeBobFactor +
                        rendering.EyeShadowRadius;
        normalRadius = MathF.Max(normalRadius, MathF.Max(noseRadius, eyeRadius));

        normalRadius = MathF.Max(
            normalRadius,
            LegRadius(FrontShoulderIndex, gait.FrontLegLinkLength));
        normalRadius = MathF.Max(
            normalRadius,
            LegRadius(RearShoulderIndex, gait.RearLegLinkLength));
        normalRadius += shadowTravel + RasterSafetyMargin;

        // A dangling pose can be grabbed at either end of the topology. Its
        // farthest drawable point is therefore a full spine span plus the
        // largest appendage/decorative extent, independent of normal stance.
        var spineSpan = (bodyWidths.Length - 1) * appearance.SpineLinkLength;
        var danglingAppendage = MathF.Max(
            maximumBodyWidth + tailMotion,
            MathF.Max(
                maximumLegReach + maximumFootRadius,
                MathF.Max(
                    secondaryMotion.NoseForwardOffset + rendering.NoseRadius,
                    eyeCenterOffset + rendering.EyeShadowRadius)));
        var danglingRadius = spineSpan + danglingAppendage +
                             MathF.Max(
                                 shadowTravel + appearance.ShadowLimbWidth * 0.5f,
                                 maximumDecorationRadius) +
                             RasterSafetyMargin;

        return new LizardGeometryEnvelope(normalRadius, danglingRadius);

        float LegRadius(int requestedShoulderIndex, float linkLength)
        {
            var index = Math.Clamp(requestedShoulderIndex, 0, bodyWidths.Length - 1);
            var longitudinal = MathF.Abs(
                appearance.HeadAnchorOffset - index * appearance.SpineLinkLength);
            var shoulderOffset = bodyWidths[index] * (1f - gait.ShoulderInsetFactor);
            var reach = linkLength * gait.MaximumReachFactor;
            return longitudinal + shoulderOffset + reach + maximumFootRadius;
        }
    }

    public AppearanceConfiguration EnsureCanvasCapacity(AppearanceConfiguration appearance)
    {
        var minimumCreatureCanvas = MathF.Ceiling(NormalModelRadius * 2f);
        var minimumRenderCanvas = MathF.Ceiling(DanglingModelRadius * 2f);
        if (appearance.CreatureCanvasSize >= minimumCreatureCanvas &&
            appearance.RenderCanvasSize >= minimumRenderCanvas)
        {
            return appearance;
        }

        return appearance with
        {
            CreatureCanvasSize = MathF.Max(
                appearance.CreatureCanvasSize,
                minimumCreatureCanvas),
            RenderCanvasSize = MathF.Max(
                appearance.RenderCanvasSize,
                minimumRenderCanvas)
        };
    }

    /// <summary>
    /// Extra screen-space clearance needed when navigation uses the compact
    /// normal-pose radius but an event renders the full transparent canvas.
    /// </summary>
    public float RequiredFallBottomSafetyInset(
        AppearanceConfiguration appearance,
        RuntimeConfiguration runtime)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(runtime);

        var normalNavigationRadius =
            (Math.Max(
                 appearance.CreatureCanvasSize * 0.5f,
                 NormalModelRadius) +
             runtime.NavigationMarginModel) *
            appearance.VisualScale;
        var fullRenderRadius =
            appearance.RenderCanvasSize * 0.5f * appearance.VisualScale +
            runtime.ReleaseRenderMarginPixels;
        return Math.Max(0f, fullRenderRadius - normalNavigationRadius);
    }
}
