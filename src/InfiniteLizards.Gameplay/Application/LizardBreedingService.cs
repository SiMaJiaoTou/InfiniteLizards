using System.Collections.Immutable;
using InfiniteLizards.Gameplay.Breeding;
using InfiniteLizards.Gameplay.Genetics;

namespace InfiniteLizards.Gameplay.Application;

public sealed record LizardTraitDetailRow(
    string TraitId,
    string Name,
    string Description,
    TraitCategory Category,
    string DisplayValue,
    double NormalizedValue,
    bool IsExpressed);

public sealed record LizardBreedingDetails(
    string Id,
    string Name,
    BreedingLifeStage LifeStage,
    double MaturityProgress,
    bool CanBreed,
    TimeSpan BreedingCooldown,
    string HabitatId,
    string? FirstParentId,
    string? SecondParentId,
    int Generation,
    int SellPrice,
    string PortraitKey,
    ImmutableArray<LizardTraitDetailRow> Traits);

public sealed record EggBreedingDetails(
    string Id,
    BreedingLifeStage LifeStage,
    double IncubationProgress,
    TimeSpan IncubationRemaining,
    string HabitatId,
    string FirstParentId,
    string SecondParentId,
    int Generation,
    string PortraitKey);

public sealed record BreedingCollectionView(
    int Coins,
    int BuyPrice,
    int SellPrice,
    ImmutableArray<LizardBreedingDetails> Lizards,
    ImmutableArray<EggBreedingDetails> Eggs);

/// <summary>
/// Application-facing facade for future desktop panels. It exposes immutable
/// values and a stable portrait cache key while leaving image generation,
/// window placement, DPI and input handling to the desktop host.
/// </summary>
public sealed class LizardBreedingService
{
    private readonly BreedingSimulation _simulation;

    public LizardBreedingService(BreedingSimulation simulation)
    {
        _simulation = simulation ??
            throw new ArgumentNullException(nameof(simulation));
    }

    public int Coins => _simulation.Coins;

    public BreedingActionResult Buy(string habitatId, string? name = null) =>
        _simulation.BuyLizard(habitatId, name);

    public BreedingActionResult Sell(string lizardId) =>
        _simulation.SellLizard(lizardId);

    public BreedingActionResult Move(string lizardId, string habitatId) =>
        _simulation.MoveLizard(lizardId, habitatId);

    public BreedingActionResult Breed(
        string firstParentId,
        string secondParentId) =>
        _simulation.Breed(firstParentId, secondParentId);

    public BreedingAdvanceResult Advance(TimeSpan elapsed) =>
        _simulation.Advance(elapsed);

    public BreedingWorldSnapshot CaptureSnapshot() =>
        _simulation.CaptureSnapshot();

    public LizardBreedingDetails GetLizardDetails(string lizardId)
    {
        if (!_simulation.TryGetLizard(lizardId, out var lizard))
        {
            throw new KeyNotFoundException($"Lizard '{lizardId}' was not found.");
        }

        var phenotype = _simulation.TraitRegistry.Express(lizard.Genome);
        var rows = phenotype.Traits
            .Select(trait => new LizardTraitDetailRow(
                trait.TraitId,
                trait.DisplayName,
                trait.Description,
                trait.Category,
                trait.IsExpressed
                    ? trait.DisplayValue
                    : $"未表达（携带 {trait.DisplayValue}）",
                trait.NormalizedValue,
                trait.IsExpressed))
            .ToImmutableArray();
        return new LizardBreedingDetails(
            lizard.Id,
            lizard.Name,
            lizard.LifeStage,
            Ratio(lizard.AgeTicks, lizard.RequiredMaturationTicks),
            lizard.CanBreed,
            lizard.BreedingCooldown,
            lizard.HabitatId,
            lizard.FirstParentId,
            lizard.SecondParentId,
            lizard.Generation,
            LizardMarketPrices.Sell,
            BuildPortraitKey(lizard.Genome),
            rows);
    }

    public EggBreedingDetails GetEggDetails(string eggId)
    {
        if (!_simulation.TryGetEgg(eggId, out var egg))
        {
            throw new KeyNotFoundException($"Egg '{eggId}' was not found.");
        }

        return new EggBreedingDetails(
            egg.Id,
            BreedingLifeStage.Egg,
            Ratio(egg.IncubationElapsedTicks, egg.RequiredIncubationTicks),
            TimeSpan.FromTicks(Math.Max(
                0L,
                egg.RequiredIncubationTicks - egg.IncubationElapsedTicks)),
            egg.HabitatId,
            egg.FirstParentId,
            egg.SecondParentId,
            egg.Generation,
            BuildPortraitKey(egg.Genome));
    }

    public BreedingCollectionView CaptureCollection()
    {
        var lizards = _simulation.Lizards
            .Select(lizard => GetLizardDetails(lizard.Id))
            .ToImmutableArray();
        var eggs = _simulation.Eggs
            .Select(egg => GetEggDetails(egg.Id))
            .ToImmutableArray();
        return new BreedingCollectionView(
            _simulation.Coins,
            LizardMarketPrices.Buy,
            LizardMarketPrices.Sell,
            lizards,
            eggs);
    }

    private static double Ratio(long value, long maximum) =>
        maximum <= 0L
            ? 1d
            : Math.Clamp((double)value / maximum, 0d, 1d);

    private static string BuildPortraitKey(LizardGenome genome)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var gene in genome.Genes)
        {
            foreach (var character in gene.TraitId)
            {
                hash = unchecked((hash ^ (byte)character) * prime);
                hash = unchecked((hash ^ (byte)(character >> 8)) * prime);
            }
            hash = Mix(hash, BitConverter.DoubleToUInt64Bits(gene.FirstAllele), prime);
            hash = Mix(hash, BitConverter.DoubleToUInt64Bits(gene.SecondAllele), prime);
        }

        return $"portrait-{genome.RegistryId}-{hash:X16}";
    }

    private static ulong Mix(ulong hash, ulong value, ulong prime)
    {
        for (var shift = 0; shift < 64; shift += 8)
        {
            hash = unchecked((hash ^ (byte)(value >> shift)) * prime);
        }

        return hash;
    }
}
