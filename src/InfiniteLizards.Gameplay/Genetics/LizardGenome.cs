using System.Collections.Immutable;

namespace InfiniteLizards.Gameplay.Genetics;

/// <summary>
/// A diploid locus. Values are normalized to [0,1], which lets one stable
/// inheritance algorithm support measurements, switches, counts and choices.
/// </summary>
public sealed record GenePair(
    string TraitId,
    double FirstAllele,
    double SecondAllele);

/// <summary>
/// Persistence-friendly genome. Genes are kept in canonical ordinal order so
/// snapshots, equality checks and diffs stay deterministic.
/// </summary>
public sealed record LizardGenome(
    int SchemaVersion,
    string RegistryId,
    ImmutableArray<GenePair> Genes)
{
    public const int CurrentSchemaVersion = 1;

    public GenePair GetGene(string traitId)
    {
        foreach (var gene in Genes)
        {
            if (string.Equals(gene.TraitId, traitId, StringComparison.Ordinal))
            {
                return gene;
            }
        }

        throw new KeyNotFoundException($"Genome does not contain trait '{traitId}'.");
    }

    public bool TryGetGene(string traitId, out GenePair gene)
    {
        foreach (var candidate in Genes)
        {
            if (string.Equals(candidate.TraitId, traitId, StringComparison.Ordinal))
            {
                gene = candidate;
                return true;
            }
        }

        gene = null!;
        return false;
    }
}

public sealed record ExpressedTrait(
    string TraitId,
    string DisplayName,
    string Description,
    TraitCategory Category,
    TraitValueKind ValueKind,
    double NormalizedValue,
    double Value,
    bool IsExpressed,
    string DisplayValue);

/// <summary>
/// Immutable, renderer-neutral phenotype consumed by simulation, detail UI
/// and collection filters. Hidden traits remain queryable so renderers may
/// preview dormant genes without treating them as visible anatomy.
/// </summary>
public sealed class LizardPhenotype
{
    private readonly IReadOnlyDictionary<string, ExpressedTrait> _byId;

    public string RegistryId { get; }
    public ImmutableArray<ExpressedTrait> Traits { get; }
    public IEnumerable<ExpressedTrait> VisibleTraits =>
        Traits.Where(trait => trait.IsExpressed);

    internal LizardPhenotype(
        string registryId,
        ImmutableArray<ExpressedTrait> traits)
    {
        RegistryId = registryId;
        Traits = traits;
        _byId = traits.ToDictionary(
            trait => trait.TraitId,
            StringComparer.Ordinal);
    }

    public ExpressedTrait GetTrait(string traitId) =>
        _byId.TryGetValue(traitId, out var trait)
            ? trait
            : throw new KeyNotFoundException(
                $"Phenotype does not contain trait '{traitId}'.");

    public bool TryGetTrait(string traitId, out ExpressedTrait trait) =>
        _byId.TryGetValue(traitId, out trait!);

    public double GetValue(string traitId) => GetTrait(traitId).Value;

    public double GetNormalizedValue(string traitId) =>
        GetTrait(traitId).NormalizedValue;

    public bool HasFeature(string traitId) =>
        GetTrait(traitId) is { IsExpressed: true, Value: >= 0.5d };
}

public readonly record struct GeneticBreedingOptions(
    double MutationMultiplier = 1d,
    double BurstMutationMultiplier = 1d)
{
    public static GeneticBreedingOptions Default { get; } = new(1d, 1d);

    internal void EnsureValid()
    {
        if (!double.IsFinite(MutationMultiplier) || MutationMultiplier < 0d ||
            !double.IsFinite(BurstMutationMultiplier) ||
            BurstMutationMultiplier < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MutationMultiplier),
                "Mutation multipliers must be finite and non-negative.");
        }
    }
}
