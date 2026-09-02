namespace InfiniteLizards.Gameplay.Phenotypes;

/// <summary>
/// Renderer-neutral RGB color compiled once from heritable pigment traits.
/// Desktop renderers must not need to query the trait registry in their hot path.
/// </summary>
public readonly record struct BreedableColor(byte Red, byte Green, byte Blue);

public enum BreedablePatternKind
{
    Solid,
    LongitudinalStripes,
    CrossBands,
    LeopardSpots,
    Reticulation,
    Marbled,
    StarSpeckles,
    ColorBlocks
}

public enum BreedableSnoutShape
{
    Rounded,
    Wedge,
    Pointed,
    Shovel,
    Beak
}

public enum BreedablePupilShape
{
    Round,
    VerticalSlit,
    HorizontalSlit,
    Diamond,
    Star
}

public enum BreedableFootShape
{
    Rounded,
    Toed,
    Webbed,
    Clawed,
    AdhesivePads
}

public enum BreedableDorsalFinShape
{
    RoundedSail,
    TriangularSerration,
    Wave,
    Feathered,
    DoublePeak
}

public enum BreedableEggPatternKind
{
    FineSpeckles,
    LargeBlotches,
    Rings,
    Cracks,
    Gradient,
    Stars
}

public sealed record BreedablePalette(
    BreedableColor Primary,
    BreedableColor Secondary,
    BreedableColor Belly,
    BreedableColor Eye,
    float Iridescence,
    float Melanin);

public sealed record BreedablePattern(
    BreedablePatternKind Kind,
    float Strength,
    float Density,
    float ElementScale,
    float StripeWidth,
    int StripeCount,
    float Symmetry,
    float EdgeSoftness,
    bool IsGlowing,
    float GlowIntensity);

public sealed record BreedableBodyMorphology(
    float LengthRatio,
    float WidthRatio,
    float HeightRatio,
    float ShoulderMassRatio,
    float HipMassRatio,
    float Taper,
    float NeckLengthRatio,
    float BellyRoundness,
    float SpineArch,
    float Flexibility,
    float HeadSizeRatio,
    float HeadWidthRatio,
    float SnoutLengthRatio,
    BreedableSnoutShape SnoutShape,
    float EyeSizeRatio,
    float EyeSpacingRatio,
    BreedablePupilShape PupilShape);

/// <summary>
/// Leg pair and joint counts are presentation morphology in phenotype engine v1.
/// Runtime locomotion, grabbing and regripping deliberately retain the proven
/// four-support-leg/two-bone rig until the topology solver is generalized.
/// The public maxima also give portrait/decorative renderers a fixed primitive
/// budget: at most ten legs and fifty visible joint/segment elements.
/// </summary>
public sealed record BreedableLimbMorphology(
    int LegPairCount,
    int VisibleJointCount,
    float LengthRatio,
    float ThicknessRatio,
    float FrontRearRatio,
    float FootSizeRatio,
    BreedableFootShape FootShape,
    int ToeCount,
    float ClawLengthRatio,
    bool HasWebbing,
    float WebbingAmount,
    float GripPadSizeRatio,
    float LeftRightAsymmetry)
{
    public const int MinimumLegPairCount = 1;
    public const int MaximumLegPairCount = 5;
    public const int MinimumVisibleJointCount = 1;
    public const int MaximumVisibleJointCount = 5;
    public const int RuntimeSupportLegCount = 4;
    public const int MaximumPresentationLegCount = MaximumLegPairCount * 2;
    public const int MaximumVisibleLegSegmentCount =
        MaximumPresentationLegCount * MaximumVisibleJointCount;

    public int PresentationLegCount => LegPairCount * 2;
}

public sealed record BreedableDorsalFin(
    bool IsPresent,
    float HeightRatio,
    float LengthRatio,
    BreedableDorsalFinShape Shape);

public sealed record BreedableNeckFrill(
    bool IsPresent,
    float SizeRatio);

public sealed record BreedableWhiskers(
    bool IsPresent,
    int PairCount,
    float LengthRatio)
{
    public const int MaximumPairCount = 4;
}

public sealed record BreedableAppendageMorphology(
    BreedableDorsalFin DorsalFin,
    BreedableNeckFrill NeckFrill,
    BreedableWhiskers Whiskers);

public sealed record BreedableTailSail(
    bool IsPresent,
    float HeightRatio,
    float LengthRatio);

public sealed record BreedableTailSpikes(
    bool IsPresent,
    int Count,
    float SizeRatio)
{
    public const int MaximumCount = 24;
}

public sealed record BreedableTailClub(
    bool IsPresent,
    float SizeRatio,
    float SpikeLengthRatio);

public sealed record BreedableTailFork(
    bool IsPresent,
    float LengthRatio);

public sealed record BreedableTailMorphology(
    float LengthRatio,
    float BaseThicknessRatio,
    float Taper,
    float Flexibility,
    int SegmentCount,
    BreedableTailSail Sail,
    BreedableTailSpikes Spikes,
    BreedableTailClub Club,
    BreedableTailFork Fork);

public sealed record BreedableSkinMorphology(
    float ScaleSizeRatio,
    float Roughness,
    float Gloss,
    float Translucency);

public sealed record BreedableEggAppearance(
    BreedableEggPatternKind Pattern,
    float HueDegrees);

/// <summary>
/// A compact, immutable visual contract. It is compiled at hatch/load time and
/// retained by <c>LizardProfile</c>; render frames contain pose only.
/// </summary>
public sealed record BreedableVisualPhenotype(
    BreedablePalette Palette,
    BreedablePattern Pattern,
    BreedableBodyMorphology Body,
    BreedableLimbMorphology Limbs,
    BreedableSkinMorphology Skin,
    BreedableAppendageMorphology Appendages,
    BreedableTailMorphology Tail,
    BreedableEggAppearance EggAppearance);
