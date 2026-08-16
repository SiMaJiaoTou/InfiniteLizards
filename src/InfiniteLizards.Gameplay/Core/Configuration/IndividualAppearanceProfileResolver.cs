namespace DesktopLizard.Core;

internal static class IndividualAppearanceProfileResolver
{
    internal static AppearanceConfiguration ResolveAppearance(
        AppearanceConfiguration sourceAppearance,
        IndividualVariationConfiguration variation,
        IndividualVariationContext context)
    {
        var colorAmount = variation.ColorVariation;
        var coefficients = variation.Appearance;
        var appearance = sourceAppearance with
        {
            SpineLinkLength = sourceAppearance.SpineLinkLength * context.BodyMultiplier,
            HeadAnchorOffset = sourceAppearance.HeadAnchorOffset * context.BodyMultiplier,
            SpineRestCurveAmplitude = sourceAppearance.SpineRestCurveAmplitude * context.BodyMultiplier,
            BodyWidths = sourceAppearance.BodyWidths.Select(value => value * context.BodyMultiplier).ToArray(),
            LimbWidth = sourceAppearance.LimbWidth * context.BodyMultiplier,
            ShadowLimbWidth = sourceAppearance.ShadowLimbWidth * context.BodyMultiplier,
            HitLimbWidth = sourceAppearance.HitLimbWidth * context.BodyMultiplier,
            BodyColor = VaryColor(
                sourceAppearance.BodyColor,
                context.Curiosity * coefficients.BodyCuriosity,
                context.Build * coefficients.BodyBuild,
                colorAmount,
                coefficients),
            ShadowColor = VaryColor(
                sourceAppearance.ShadowColor,
                context.Curiosity * coefficients.ShadowCuriosity,
                context.Build * coefficients.ShadowBuild,
                colorAmount * coefficients.ShadowColorAmount,
                coefficients)
        };
        return appearance;
    }

    internal static RenderingConfiguration ResolveRendering(
        RenderingConfiguration sourceRendering,
        IndividualVariationContext context)
    {
        var rendering = sourceRendering with
        {
            NoseRadius = sourceRendering.NoseRadius * context.BodyMultiplier,
            FrontFootRadius = sourceRendering.FrontFootRadius * context.GaitMultiplier,
            RearFootRadius = sourceRendering.RearFootRadius * context.GaitMultiplier,
            EyeShadowRadius = sourceRendering.EyeShadowRadius * context.BodyMultiplier,
            EyeRadius = sourceRendering.EyeRadius * context.BodyMultiplier,
            PupilOffset = sourceRendering.PupilOffset * context.BodyMultiplier,
            PupilRadius = sourceRendering.PupilRadius * context.BodyMultiplier,
            HitFootRadius = sourceRendering.HitFootRadius * context.GaitMultiplier,
            HitEyeRadius = sourceRendering.HitEyeRadius * context.BodyMultiplier
        };
        return rendering;
    }

    private static float Centered(float value) => Math.Clamp(value, 0f, 1f) * 2f - 1f;

    private static RgbConfiguration VaryColor(
        RgbConfiguration source,
        float hueTrait,
        float buildTrait,
        float amount,
        IndividualAppearanceVariationCoefficients coefficients)
    {
        var warm = Centered((hueTrait + 1f) * 0.5f) * amount;
        var dark = Centered((buildTrait + 1f) * 0.5f) * amount * coefficients.ColorDarkness;
        return new RgbConfiguration(
            ClampByte(source.Red * (1f + warm * coefficients.RedWarmth - dark)),
            ClampByte(source.Green * (1f - warm * coefficients.GreenWarmth - dark)),
            ClampByte(source.Blue * (1f - warm * coefficients.BlueWarmth - dark)));
    }

    private static int ClampByte(float value) => Math.Clamp((int)MathF.Round(value), 0, 255);
}
