using InfiniteLizards.Gameplay.Genetics;

namespace InfiniteLizards.Gameplay.Phenotypes;

/// <summary>
/// Compiles registry-driven expressed traits into the compact visual contract
/// consumed by the desktop renderer. The compiler is intentionally tolerant of
/// custom registries: absent v1 traits receive conservative legacy-like values.
/// </summary>
public static class BreedablePhenotypeCompiler
{
    public static BreedableVisualPhenotype CompileVisual(LizardPhenotype phenotype)
    {
        ArgumentNullException.ThrowIfNull(phenotype);

        var baseHue = Value(phenotype, "color.base-hue", 121d, 0d, 360d);
        var saturation = Value(phenotype, "color.saturation", 0.35d, 0d, 1d);
        var lightness = Value(phenotype, "color.lightness", 0.53d, 0.05d, 0.95d);
        var accentHue = Value(phenotype, "color.accent-hue", 45d, 0d, 360d);
        var bellyHue = Value(phenotype, "color.belly-hue", 100d, 0d, 360d);
        var bellyLightness = Value(
            phenotype,
            "color.belly-lightness",
            0.68d,
            0.05d,
            0.95d);
        var eyeHue = Value(phenotype, "color.eye-hue", 42d, 0d, 360d);
        var eyeGlow = Value(phenotype, "color.eye-glow", 0.35d, 0d, 1d);
        var iridescence = Value(phenotype, "color.iridescence", 0.22d, 0d, 1d);
        var melanin = Value(phenotype, "color.melanin", 0.42d, 0d, 1d);
        var contrast = Value(phenotype, "pattern.contrast", 0.58d, 0d, 1d);
        var pigmentLightness = Math.Clamp(
            lightness * Lerp(1.08d, 0.58d, melanin),
            0.05d,
            0.95d);
        var pigmentSaturation = Math.Clamp(
            saturation * Lerp(0.92d, 1.12d, iridescence),
            0d,
            1d);
        var secondaryLightness = pigmentLightness >= 0.5d
            ? Math.Max(0.08d, pigmentLightness - 0.18d - contrast * 0.42d)
            : Math.Min(0.92d, pigmentLightness + 0.18d + contrast * 0.42d);
        var palette = new BreedablePalette(
            Hsl(baseHue, pigmentSaturation, pigmentLightness),
            Hsl(
                accentHue + iridescence * 72d,
                Math.Clamp(pigmentSaturation + 0.18d, 0d, 1d),
                secondaryLightness),
            Hsl(
                bellyHue,
                pigmentSaturation * 0.72d,
                Math.Clamp(
                    bellyLightness * Lerp(1.05d, 0.66d, melanin),
                    0.05d,
                    0.95d)),
            Hsl(eyeHue, 0.78d, 0.34d + eyeGlow * 0.42d),
            Float(iridescence, 0f, 1f),
            Float(melanin, 0f, 1f));

        var pattern = new BreedablePattern(
            Choice<BreedablePatternKind>(phenotype, "pattern.type"),
            Float(contrast, 0f, 1f),
            Float(Value(phenotype, "pattern.density", 0.48d, 0d, 1d), 0f, 1f),
            Float(Value(phenotype, "pattern.element-size", 1d, 0.05d, 3d), 0.05f, 3f),
            Float(Value(phenotype, "pattern.stripe-width", 0.7d, 0.05d, 2.5d), 0.05f, 2.5f),
            Integer(phenotype, "pattern.stripe-count", 6, 1, 18),
            Float(Value(phenotype, "pattern.symmetry", 0.72d, 0d, 1d), 0f, 1f),
            Float(Value(phenotype, "pattern.edge-softness", 0.5d, 0d, 1d), 0f, 1f),
            Feature(phenotype, "pattern.glow.present"),
            Feature(phenotype, "pattern.glow.present")
                ? ExpressedRatio(phenotype, "pattern.glow.intensity", 1f)
                : 0f);

        var body = new BreedableBodyMorphology(
            Ratio(phenotype, DefaultLizardTraitIds.BodyLength, 1f, 0.5f, 1.8f),
            Ratio(phenotype, DefaultLizardTraitIds.BodyWidth, 1f, 0.45f, 1.7f),
            Ratio(phenotype, "body.height", 1f, 0.45f, 1.7f),
            Ratio(phenotype, "body.shoulder-mass", 1f, 0.4f, 1.8f),
            Ratio(phenotype, "body.hip-mass", 1f, 0.4f, 1.8f),
            Float(Value(phenotype, "body.taper", 0.5d, 0d, 1d), 0f, 1f),
            Ratio(phenotype, "body.neck-length", 1f, 0.35f, 1.8f),
            Float(Value(phenotype, "body.belly-roundness", 0.5d, 0d, 1d), 0f, 1f),
            Float(Value(phenotype, "body.spine-arch", 0d, -0.35d, 0.65d), -0.35f, 0.65f),
            Float(Value(phenotype, "body.flexibility", 0.62d, 0.25d, 1d), 0.25f, 1f),
            Ratio(phenotype, "head.size", 1f, 0.5f, 1.8f),
            Ratio(phenotype, "head.width", 1f, 0.5f, 1.8f),
            Ratio(phenotype, "head.snout-length", 1f, 0.3f, 2f),
            Choice<BreedableSnoutShape>(phenotype, "head.snout-shape"),
            Ratio(phenotype, "head.eye-size", 1f, 0.45f, 1.9f),
            Ratio(phenotype, "head.eye-spacing", 1f, 0.5f, 1.6f),
            Choice<BreedablePupilShape>(phenotype, "head.pupil-shape"));

        var hasWebbing = Feature(phenotype, "limbs.webbing.present");
        var clawLength = Ratio(phenotype, "limbs.claw-length", 0.35f, 0f, 1.7f);
        var gripPadSize = Ratio(phenotype, "limbs.grip-pad-size", 1f, 0.15f, 1.9f);
        var toeCount = Integer(phenotype, "limbs.toe-count", 4, 2, 7);
        var limbs = new BreedableLimbMorphology(
            Integer(
                phenotype,
                DefaultLizardTraitIds.LegPairCount,
                2,
                BreedableLimbMorphology.MinimumLegPairCount,
                BreedableLimbMorphology.MaximumLegPairCount),
            Integer(
                phenotype,
                DefaultLizardTraitIds.LegJointCount,
                2,
                BreedableLimbMorphology.MinimumVisibleJointCount,
                BreedableLimbMorphology.MaximumVisibleJointCount),
            Ratio(phenotype, "limbs.length", 1f, 0.4f, 2f),
            Ratio(phenotype, "limbs.thickness", 1f, 0.3f, 1.9f),
            Ratio(phenotype, "limbs.front-rear-ratio", 1f, 0.5f, 1.5f),
            Ratio(phenotype, "limbs.foot-size", 1f, 0.4f, 1.9f),
            ResolveFootShape(hasWebbing, clawLength, gripPadSize, toeCount),
            toeCount,
            clawLength,
            hasWebbing,
            hasWebbing
                ? Ratio(phenotype, "limbs.webbing.amount", 0.4f, 0f, 1f)
                : 0f,
            gripPadSize,
            Float(
                Value(phenotype, "limbs.left-right-asymmetry", 0.08d, 0d, 0.28d),
                0f,
                0.28f));

        var skin = new BreedableSkinMorphology(
            Ratio(phenotype, "skin.scale-size", 1f, 0.25f, 2f),
            Float(Value(phenotype, "skin.roughness", 0.5d, 0d, 1d), 0f, 1f),
            Float(Value(phenotype, "skin.gloss", 0.5d, 0d, 1d), 0f, 1f),
            Float(
                Value(phenotype, "skin.translucency", 0.16d, 0d, 0.8d),
                0f,
                0.8f));

        var dorsalFinPresent = Feature(phenotype, DefaultLizardTraitIds.DorsalFinPresent);
        var neckFrillPresent = Feature(phenotype, "appendage.neck-frill.present");
        var whiskersPresent = Feature(phenotype, "appendage.whiskers.present");
        var appendages = new BreedableAppendageMorphology(
            new BreedableDorsalFin(
                dorsalFinPresent,
                dorsalFinPresent
                    ? ExpressedRatio(
                        phenotype,
                        "appendage.dorsal-fin.height",
                        2f)
                    : 0f,
                dorsalFinPresent
                    ? ExpressedRatio(
                        phenotype,
                        "appendage.dorsal-fin.length",
                        1f)
                    : 0f,
                Choice<BreedableDorsalFinShape>(phenotype, "appendage.dorsal-fin.shape")),
            new BreedableNeckFrill(
                neckFrillPresent,
                neckFrillPresent
                    ? ExpressedRatio(
                        phenotype,
                        "appendage.neck-frill.size",
                        2.2f)
                    : 0f),
            new BreedableWhiskers(
                whiskersPresent,
                whiskersPresent
                    ? ExpressedInteger(
                        phenotype,
                        "appendage.whiskers.pairs",
                        1,
                        BreedableWhiskers.MaximumPairCount)
                    : 0,
                whiskersPresent
                    ? ExpressedRatio(
                        phenotype,
                        "appendage.whiskers.length",
                        2.5f)
                    : 0f));

        var tailSailPresent = Feature(phenotype, "tail.sail.present");
        var tailSpikesPresent = Feature(phenotype, "tail.spikes.present");
        var tailClubPresent = Feature(phenotype, DefaultLizardTraitIds.TailClubPresent);
        var tailForkPresent = Feature(phenotype, "tail.fork.present");
        var tail = new BreedableTailMorphology(
            Ratio(phenotype, "tail.length", 1f, 0.4f, 3f),
            Ratio(phenotype, "tail.base-thickness", 1f, 0.25f, 1.9f),
            Float(Value(phenotype, "tail.taper", 0.5d, 0d, 1d), 0f, 1f),
            Float(Value(phenotype, "tail.flexibility", 0.58d, 0.15d, 1d), 0.15f, 1f),
            Integer(phenotype, "tail.segment-count", 9, 3, 18),
            new BreedableTailSail(
                tailSailPresent,
                tailSailPresent
                    ? ExpressedRatio(phenotype, "tail.sail.height", 2f)
                    : 0f,
                tailSailPresent
                    ? ExpressedRatio(phenotype, "tail.sail.length", 1f)
                    : 0f),
            new BreedableTailSpikes(
                tailSpikesPresent,
                tailSpikesPresent
                    ? ExpressedInteger(
                        phenotype,
                        "tail.spikes.count",
                        2,
                        BreedableTailSpikes.MaximumCount)
                    : 0,
                tailSpikesPresent
                    ? ExpressedRatio(phenotype, "tail.spikes.size", 1.8f)
                    : 0f),
            new BreedableTailClub(
                tailClubPresent,
                tailClubPresent
                    ? ExpressedRatio(phenotype, "tail.club.size", 2.2f)
                    : 0f,
                tailClubPresent
                    ? ExpressedRatio(
                        phenotype,
                        "tail.club-spike-length",
                        1.6f)
                    : 0f),
            new BreedableTailFork(
                tailForkPresent,
                tailForkPresent
                    ? ExpressedRatio(phenotype, "tail.fork.length", 0.58f)
                    : 0f));

        var eggAppearance = CompileEggAppearance(phenotype);

        return new BreedableVisualPhenotype(
            palette,
            pattern,
            body,
            limbs,
            skin,
            appendages,
            tail,
            eggAppearance);
    }

    public static BreedableEggAppearance CompileEggAppearance(
        LizardPhenotype phenotype)
    {
        ArgumentNullException.ThrowIfNull(phenotype);
        return new BreedableEggAppearance(
            Choice<BreedableEggPatternKind>(
                phenotype,
                "lifecycle.egg-shell-pattern"),
            Float(
                Value(phenotype, "lifecycle.egg-shell-hue", 122d, 0d, 360d),
                0f,
                360f));
    }

    internal static double Normalized(
        LizardPhenotype phenotype,
        string traitId,
        double fallback = 0.5d)
    {
        if (!phenotype.TryGetTrait(traitId, out var trait) ||
            !double.IsFinite(trait.NormalizedValue))
        {
            return Math.Clamp(fallback, 0d, 1d);
        }

        return Math.Clamp(trait.NormalizedValue, 0d, 1d);
    }

    internal static double Value(
        LizardPhenotype phenotype,
        string traitId,
        double fallback,
        double minimum,
        double maximum)
    {
        if (!phenotype.TryGetTrait(traitId, out var trait) ||
            !double.IsFinite(trait.Value))
        {
            return Math.Clamp(fallback, minimum, maximum);
        }

        return Math.Clamp(trait.Value, minimum, maximum);
    }

    private static bool Feature(LizardPhenotype phenotype, string traitId) =>
        phenotype.TryGetTrait(traitId, out var trait) &&
        trait.IsExpressed &&
        double.IsFinite(trait.Value) &&
        trait.Value >= 0.5d;

    private static int Integer(
        LizardPhenotype phenotype,
        string traitId,
        int fallback,
        int minimum,
        int maximum) =>
        Math.Clamp(
            (int)Math.Round(
                Value(phenotype, traitId, fallback, minimum, maximum),
                MidpointRounding.AwayFromZero),
            minimum,
            maximum);

    private static float Ratio(
        LizardPhenotype phenotype,
        string traitId,
        float fallback,
        float minimum,
        float maximum) =>
        Float(Value(phenotype, traitId, fallback, minimum, maximum), minimum, maximum);

    private static float ExpressedRatio(
        LizardPhenotype phenotype,
        string traitId,
        float maximum)
    {
        if (!phenotype.TryGetTrait(traitId, out var trait) ||
            !trait.IsExpressed ||
            !double.IsFinite(trait.Value))
        {
            return 0f;
        }

        return Float(trait.Value, 0f, maximum);
    }

    private static int ExpressedInteger(
        LizardPhenotype phenotype,
        string traitId,
        int minimum,
        int maximum)
    {
        if (!phenotype.TryGetTrait(traitId, out var trait) ||
            !trait.IsExpressed ||
            !double.IsFinite(trait.Value))
        {
            return 0;
        }

        return Math.Clamp(
            (int)Math.Round(trait.Value, MidpointRounding.AwayFromZero),
            minimum,
            maximum);
    }

    private static float Float(double value, float minimum, float maximum) =>
        float.IsFinite((float)value)
            ? Math.Clamp((float)value, minimum, maximum)
            : Math.Clamp((minimum + maximum) * 0.5f, minimum, maximum);

    private static T Choice<T>(LizardPhenotype phenotype, string traitId)
        where T : struct, Enum
    {
        var count = Enum.GetValues<T>().Length;
        var value = (int)Math.Round(
            Value(phenotype, traitId, 0d, 0d, count - 1d),
            MidpointRounding.AwayFromZero);
        return (T)Enum.ToObject(typeof(T), Math.Clamp(value, 0, count - 1));
    }

    private static BreedableFootShape ResolveFootShape(
        bool hasWebbing,
        float clawLength,
        float gripPadSize,
        int toeCount)
    {
        if (hasWebbing)
        {
            return BreedableFootShape.Webbed;
        }
        if (gripPadSize >= 1.25f)
        {
            return BreedableFootShape.AdhesivePads;
        }
        if (clawLength >= 0.8f)
        {
            return BreedableFootShape.Clawed;
        }
        return toeCount >= 4
            ? BreedableFootShape.Toed
            : BreedableFootShape.Rounded;
    }

    private static BreedableColor Hsl(double hueDegrees, double saturation, double lightness)
    {
        hueDegrees = double.IsFinite(hueDegrees) ? hueDegrees : 0d;
        hueDegrees %= 360d;
        if (hueDegrees < 0d)
        {
            hueDegrees += 360d;
        }
        saturation = double.IsFinite(saturation) ? Math.Clamp(saturation, 0d, 1d) : 0d;
        lightness = double.IsFinite(lightness) ? Math.Clamp(lightness, 0d, 1d) : 0.5d;

        var chroma = (1d - Math.Abs(2d * lightness - 1d)) * saturation;
        var sector = hueDegrees / 60d;
        var intermediate = chroma * (1d - Math.Abs(sector % 2d - 1d));
        var (red, green, blue) = sector switch
        {
            < 1d => (chroma, intermediate, 0d),
            < 2d => (intermediate, chroma, 0d),
            < 3d => (0d, chroma, intermediate),
            < 4d => (0d, intermediate, chroma),
            < 5d => (intermediate, 0d, chroma),
            _ => (chroma, 0d, intermediate)
        };
        var match = lightness - chroma * 0.5d;
        return new BreedableColor(
            Byte(red + match),
            Byte(green + match),
            Byte(blue + match));
    }

    private static byte Byte(double value) =>
        (byte)Math.Clamp(
            (int)Math.Round(value * 255d, MidpointRounding.AwayFromZero),
            0,
            255);

    private static double Lerp(double from, double to, double amount) =>
        from + (to - from) * Math.Clamp(amount, 0d, 1d);
}
