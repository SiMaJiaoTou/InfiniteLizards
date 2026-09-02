using System.Collections.Immutable;
using InfiniteLizards.Gameplay.Phenotypes;

namespace InfiniteLizards.Desktop.Management;

/// <summary>
/// A deliberately small presentation boundary between the Avalonia game shell
/// and the breeding domain.  The UI never calculates prices, maturity, pair
/// eligibility, or mutation results; it only renders immutable read models and
/// submits explicit commands.
/// </summary>
internal interface IBreedingManagementSource : IDisposable
{
    BreedingManagementSnapshot Current { get; }

    event EventHandler? Changed;

    BreedingActionAvailability GetPairAvailability(
        string? firstParentId,
        string? secondParentId);

    ValueTask<BreedingManagementActionResult> BuyAsync(
        CancellationToken cancellationToken = default);

    ValueTask<BreedingManagementActionResult> SellAsync(
        string lizardId,
        CancellationToken cancellationToken = default);

    ValueTask<BreedingManagementActionResult> BreedAsync(
        string firstParentId,
        string secondParentId,
        CancellationToken cancellationToken = default);

    ValueTask<BreedingManagementActionResult> SetDesktopLizardAsync(
        string lizardId,
        CancellationToken cancellationToken = default);

    ValueTask AdvanceAsync(
        TimeSpan elapsed,
        CancellationToken cancellationToken = default);
}

internal sealed record BreedingManagementSnapshot(
    int Coins,
    int BuyPrice,
    int SellPrice,
    ImmutableArray<BreedingLizardCard> Lizards,
    ImmutableArray<BreedingEggCard> Eggs,
    string? Notice = null)
{
    public static BreedingManagementSnapshot Empty { get; } = new(
        0,
        5,
        1,
        ImmutableArray<BreedingLizardCard>.Empty,
        ImmutableArray<BreedingEggCard>.Empty);

    public bool CanBuy { get; init; }
    public string BuyDisabledReason { get; init; } = string.Empty;
    public bool IdentityCapacityAvailable { get; init; } = true;
    public bool IsPersistent { get; init; } = true;
    public string PersistenceModeLabel { get; init; } = "已保存家园";
    public string? RunningDesktopLizardId { get; init; }
    public string? NextDesktopLizardId { get; init; }
}

internal sealed record BreedingLizardCard(
    string Id,
    string Name,
    string StageLabel,
    string AgeLabel,
    string CooldownLabel,
    int Generation,
    bool CanBreed,
    bool CanSell,
    string SellDisabledReason,
    bool IsRunningDesktopPet,
    bool IsNextDesktopPet,
    LizardPortraitModel Portrait,
    ImmutableArray<BreedingTraitReadout> Traits,
    string? FirstParentId,
    string? SecondParentId,
    bool WasMarketPurchased);

internal sealed record BreedingEggCard(
    string Id,
    string ProgressLabel,
    double Progress,
    string FirstParentName,
    string SecondParentName,
    int Generation,
    EggPortraitModel Shell,
    LizardPortraitModel Preview);

internal sealed record BreedingTraitReadout(
    string Id,
    string Group,
    string Name,
    string Description,
    string Value,
    double NormalizedValue,
    BreedingIcon Icon,
    bool IsExpressed);

internal sealed record LizardPortraitModel(
    BreedableVisualPhenotype Visual,
    bool HasHeadHorns,
    double HornLength,
    double HornCurvature,
    bool HasHeadCrest,
    double HeadCrestHeight,
    bool HasSideFins,
    int SideFinPairs,
    double SideFinSize,
    bool HasGillTufts,
    double GillTuftLength)
{
    public double BodyLength => Visual.Body.LengthRatio;
    public double BodyWidth => Visual.Body.WidthRatio;
    public double HeadSize => Visual.Body.HeadSizeRatio;
    public double TailLength => Visual.Tail.LengthRatio;
    public int LegPairs => Visual.Limbs.LegPairCount;
    public int VisibleJoints => Visual.Limbs.VisibleJointCount;
    public bool HasDorsalFin => Visual.Appendages.DorsalFin.IsPresent;
    public double DorsalFinSize => Visual.Appendages.DorsalFin.HeightRatio;
    public double DorsalFinLength => Visual.Appendages.DorsalFin.LengthRatio;
    public BreedableDorsalFinShape DorsalFinShape => Visual.Appendages.DorsalFin.Shape;
    public bool HasWhiskers => Visual.Appendages.Whiskers.IsPresent;
    public double WhiskerLength => Visual.Appendages.Whiskers.LengthRatio;
    public int WhiskerPairs => Visual.Appendages.Whiskers.PairCount;
    public bool HasTailSail => Visual.Tail.Sail.IsPresent;
    public double TailSailHeight => Visual.Tail.Sail.HeightRatio;
    public bool HasTailSpikes => Visual.Tail.Spikes.IsPresent;
    public int TailSpikeCount => Visual.Tail.Spikes.Count;
    public double TailSpikeSize => Visual.Tail.Spikes.SizeRatio;
    public bool HasTailClub => Visual.Tail.Club.IsPresent;
    public double TailDecorationSize => Visual.Tail.Club.SizeRatio;
    public BreedablePupilShape PupilShape => Visual.Body.PupilShape;
    public BreedablePatternKind PatternKind => Visual.Pattern.Kind;
    public double PatternDensity => Visual.Pattern.Density;
    public double PatternScale => Visual.Pattern.ElementScale;
    public double PatternContrast => Visual.Pattern.Strength;

    internal static LizardPortraitModel Default { get; } = new(
        CreateDefaultVisual(),
        false,
        0d,
        0d,
        false,
        0d,
        false,
        1,
        0d,
        false,
        0d);

    private static BreedableVisualPhenotype CreateDefaultVisual() => new(
        new BreedablePalette(
            new BreedableColor(69, 143, 91),
            new BreedableColor(218, 169, 62),
            new BreedableColor(151, 188, 143),
            new BreedableColor(224, 184, 58),
            0.2f,
            0.4f),
        new BreedablePattern(
            BreedablePatternKind.Solid,
            0.55f,
            0.4f,
            1f,
            0.7f,
            6,
            0.72f,
            0.5f,
            false,
            0f),
        new BreedableBodyMorphology(
            1f, 1f, 1f, 1f, 1f, 0.5f, 1f, 0.5f, 0f, 0.62f,
            1f, 1f, 1f, BreedableSnoutShape.Rounded,
            1f, 1f, BreedablePupilShape.VerticalSlit),
        new BreedableLimbMorphology(
            2, 2, 1f, 1f, 1f, 1f, BreedableFootShape.Toed,
            4, 0.35f, false, 0f, 1f, 0.08f),
        new BreedableSkinMorphology(1f, 0.5f, 0.35f, 0.12f),
        new BreedableAppendageMorphology(
            new BreedableDorsalFin(
                false, 0f, 0f, BreedableDorsalFinShape.RoundedSail),
            new BreedableNeckFrill(false, 0f),
            new BreedableWhiskers(false, 0, 0f)),
        new BreedableTailMorphology(
            1f,
            1f,
            0.5f,
            0.58f,
            9,
            new BreedableTailSail(false, 0f, 0f),
            new BreedableTailSpikes(false, 0, 0f),
            new BreedableTailClub(false, 0f, 0f),
            new BreedableTailFork(false, 0f)),
        new BreedableEggAppearance(BreedableEggPatternKind.FineSpeckles, 112f));
}

internal sealed record EggPortraitModel(
    BreedableEggAppearance Appearance,
    double Progress)
{
    internal static EggPortraitModel Default { get; } = new(
        new BreedableEggAppearance(BreedableEggPatternKind.FineSpeckles, 112f),
        0d);
}

internal readonly record struct BreedingActionAvailability(
    bool IsAllowed,
    string Reason)
{
    public static BreedingActionAvailability Allowed { get; } = new(true, string.Empty);
}

internal readonly record struct BreedingManagementActionResult(
    bool Succeeded,
    string Message);
