using System.Collections.Immutable;

namespace InfiniteLizards.Gameplay.Genetics;

/// <summary>
/// Stable IDs used by lifecycle/application rules. Rendering and detail UI
/// should still enumerate the registry instead of hard-coding its full list.
/// </summary>
public static class DefaultLizardTraitIds
{
    public const string BodyLength = "body.length";
    public const string BodyWidth = "body.width";
    public const string LegPairCount = "limbs.leg-pairs";
    public const string LegJointCount = "limbs.joint-count";
    public const string DorsalFinPresent = "appendage.dorsal-fin.present";
    public const string TailClubPresent = "tail.club.present";
    public const string MaximumSpeed = "locomotion.maximum-speed";
    public const string PointerDisposition = "pointer.disposition";
    public const string IncubationRate = "lifecycle.incubation-rate";
    public const string MaturationRate = "lifecycle.maturation-rate";
    public const string MutationSensitivity = "lifecycle.mutation-sensitivity";
}

/// <summary>
/// Exact shape of a predecessor registry that may be upgraded by an explicit
/// descriptor migration. Listing every former trait lets the loader distinguish
/// a genuine older genome from a damaged partial genome or an unrelated registry
/// that happens to share a few IDs. Removed loci are deliberately discarded;
/// unchanged loci retain their exact allele bits and added loci use defaults.
/// </summary>
public sealed record TraitRegistryCompatibility(
    string RegistryId,
    ImmutableArray<string> TraitIds);

/// <summary>
/// Versioned, data-driven catalogue of every heritable axis. The algorithms
/// below never switch on concrete trait IDs: adding a descriptor automatically
/// adds founder generation, diploid inheritance, mutation, persistence and
/// phenotype/detail-panel support.
/// </summary>
public sealed class LizardTraitRegistry
{
    public const string DefaultRegistryId = "infinite-lizards.genetics.v2";
    public const string PreviousDefaultRegistryId = "infinite-lizards.genetics.v1";

    private readonly IReadOnlyDictionary<string, TraitDescriptor> _byId;
    private readonly IReadOnlyDictionary<string, TraitRegistryCompatibility>
        _compatiblePredecessors;
    private readonly ImmutableArray<TraitDescriptor> _canonicalDescriptors;

    public static LizardTraitRegistry Default { get; } = CreateDefaultRegistry();

    public string RegistryId { get; }
    public ImmutableArray<TraitDescriptor> Descriptors { get; }
    public ImmutableArray<TraitRegistryCompatibility> CompatiblePredecessors { get; }

    public LizardTraitRegistry(
        string registryId,
        IEnumerable<TraitDescriptor> descriptors,
        IEnumerable<TraitRegistryCompatibility>? compatiblePredecessors = null)
    {
        if (string.IsNullOrWhiteSpace(registryId))
        {
            throw new ArgumentException(
                "A trait registry needs a stable version ID.",
                nameof(registryId));
        }

        ArgumentNullException.ThrowIfNull(descriptors);
        RegistryId = registryId;
        Descriptors = descriptors.ToImmutableArray();
        _canonicalDescriptors = Descriptors
            .OrderBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        _byId = Descriptors
            .GroupBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        CompatiblePredecessors = (compatiblePredecessors ?? [])
            .OrderBy(predecessor => predecessor.RegistryId, StringComparer.Ordinal)
            .ToImmutableArray();
        _compatiblePredecessors = CompatiblePredecessors
            .GroupBy(predecessor => predecessor.RegistryId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);

        var failures = Validate();
        if (failures.Count > 0)
        {
            throw new ArgumentException(
                string.Join(Environment.NewLine, failures),
                nameof(descriptors));
        }
    }

    public TraitDescriptor GetDescriptor(string traitId) =>
        _byId.TryGetValue(traitId, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException(
                $"Trait registry '{RegistryId}' does not define '{traitId}'.");

    public IReadOnlyList<string> Validate()
    {
        var failures = new List<string>();
        if (Descriptors.Length == 0)
        {
            failures.Add("A trait registry must define at least one trait.");
            return failures;
        }

        var duplicateIds = Descriptors
            .GroupBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        foreach (var duplicateId in duplicateIds)
        {
            failures.Add($"Trait ID '{duplicateId}' is duplicated.");
        }

        var knownIds = Descriptors
            .Select(descriptor => descriptor.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var descriptor in Descriptors)
        {
            failures.AddRange(descriptor.Validate(knownIds));
        }

        var duplicatePredecessors = CompatiblePredecessors
            .GroupBy(predecessor => predecessor.RegistryId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        foreach (var duplicate in duplicatePredecessors)
        {
            failures.Add($"Compatible predecessor registry '{duplicate}' is duplicated.");
        }
        foreach (var predecessor in CompatiblePredecessors)
        {
            ValidateCompatibility(predecessor, knownIds, failures);
        }

        DetectActivationCycles(knownIds, failures);
        return failures;
    }

    public bool CanUpgradeFrom(string registryId) =>
        !string.IsNullOrWhiteSpace(registryId) &&
        _compatiblePredecessors.ContainsKey(registryId);

    public TraitRegistryCompatibility CreateCompatibilityManifest() => new(
        RegistryId,
        _canonicalDescriptors
            .Select(descriptor => descriptor.Id)
            .ToImmutableArray());

    public LizardGenome CreateFounder(ulong seed)
    {
        var random = new DeterministicRandom(seed);
        return CreateFounder(random);
    }

    public LizardGenome CreateOffspring(
        LizardGenome firstParent,
        LizardGenome secondParent,
        ulong seed,
        GeneticBreedingOptions? options = null)
    {
        var random = new DeterministicRandom(seed);
        return CreateOffspring(
            firstParent,
            secondParent,
            random,
            options ?? GeneticBreedingOptions.Default);
    }

    public LizardGenome UpgradeGenome(LizardGenome genome)
    {
        ArgumentNullException.ThrowIfNull(genome);
        if (string.Equals(genome.RegistryId, RegistryId, StringComparison.Ordinal))
        {
            EnsureValidGenome(genome);
            return genome;
        }
        if (!_compatiblePredecessors.TryGetValue(
                genome.RegistryId,
                out var predecessor))
        {
            throw new ArgumentException(
                $"Genome registry '{genome.RegistryId}' is not an explicitly " +
                $"compatible predecessor of '{RegistryId}'.",
                nameof(genome));
        }

        var failures = ValidatePredecessorGenome(genome, predecessor);
        if (failures.Count > 0)
        {
            throw new ArgumentException(
                string.Join(Environment.NewLine, failures),
                nameof(genome));
        }

        var knownGenes = genome.Genes.ToDictionary(
            gene => gene.TraitId,
            StringComparer.Ordinal);
        var upgraded = ImmutableArray.CreateBuilder<GenePair>(Descriptors.Length);
        foreach (var descriptor in _canonicalDescriptors)
        {
            if (knownGenes.TryGetValue(descriptor.Id, out var gene))
            {
                // Never normalize, quantize or otherwise reinterpret a saved
                // predecessor allele. Compatibility manifests guarantee that
                // these values are already valid for the unchanged locus;
                // explicitly removed predecessor loci are ignored by this loop.
                upgraded.Add(new GenePair(
                    descriptor.Id,
                    gene.FirstAllele,
                    gene.SecondAllele));
            }
            else
            {
                upgraded.Add(new GenePair(
                    descriptor.Id,
                    descriptor.DefaultNormalized,
                    descriptor.DefaultNormalized));
            }
        }

        return new LizardGenome(
            LizardGenome.CurrentSchemaVersion,
            RegistryId,
            upgraded.MoveToImmutable());
    }

    private static IReadOnlyList<string> ValidatePredecessorGenome(
        LizardGenome genome,
        TraitRegistryCompatibility predecessor)
    {
        var failures = new List<string>();
        if (genome.SchemaVersion != LizardGenome.CurrentSchemaVersion)
        {
            failures.Add(
                $"Predecessor genome schema {genome.SchemaVersion} is unsupported.");
        }
        if (!string.Equals(
                genome.RegistryId,
                predecessor.RegistryId,
                StringComparison.Ordinal))
        {
            failures.Add("Predecessor genome registry does not match its manifest.");
        }
        if (genome.Genes.IsDefault)
        {
            failures.Add("Predecessor genome genes are uninitialized.");
            return failures;
        }
        if (genome.Genes.Length != predecessor.TraitIds.Length)
        {
            failures.Add(
                "Predecessor genome trait set does not exactly match its " +
                "compatibility manifest; missing and unknown old traits are rejected.");
            return failures;
        }

        for (var index = 0; index < genome.Genes.Length; index++)
        {
            var gene = genome.Genes[index];
            var expectedId = predecessor.TraitIds[index];
            if (gene is null ||
                !string.Equals(gene.TraitId, expectedId, StringComparison.Ordinal))
            {
                failures.Add(
                    $"Predecessor gene at index {index} must be '{expectedId}'.");
                continue;
            }
            if (!IsNormalizedFinite(gene.FirstAllele) ||
                !IsNormalizedFinite(gene.SecondAllele))
            {
                failures.Add(
                    $"Predecessor gene '{gene.TraitId}' alleles must be finite and in [0,1].");
            }
        }

        return failures;
    }

    public IReadOnlyList<string> ValidateGenome(LizardGenome genome)
    {
        var failures = new List<string>();
        if (genome is null)
        {
            failures.Add("Genome is missing.");
            return failures;
        }
        if (genome.SchemaVersion != LizardGenome.CurrentSchemaVersion)
        {
            failures.Add(
                $"Genome schema {genome.SchemaVersion} is not supported.");
        }
        if (!string.Equals(genome.RegistryId, RegistryId, StringComparison.Ordinal))
        {
            failures.Add(
                $"Genome registry '{genome.RegistryId}' does not match '{RegistryId}'.");
        }
        if (genome.Genes.IsDefault)
        {
            failures.Add("Genome genes are uninitialized.");
            return failures;
        }

        var previousId = string.Empty;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var gene in genome.Genes)
        {
            if (gene is null || string.IsNullOrWhiteSpace(gene.TraitId))
            {
                failures.Add("Genome contains an unnamed gene.");
                continue;
            }
            if (!seen.Add(gene.TraitId))
            {
                failures.Add($"Genome contains duplicate gene '{gene.TraitId}'.");
            }
            if (previousId.Length > 0 &&
                StringComparer.Ordinal.Compare(previousId, gene.TraitId) >= 0)
            {
                failures.Add("Genome genes must be in canonical ordinal order.");
            }
            previousId = gene.TraitId;
            if (!_byId.ContainsKey(gene.TraitId))
            {
                failures.Add($"Genome contains unknown trait '{gene.TraitId}'.");
            }
            if (!IsNormalizedFinite(gene.FirstAllele) ||
                !IsNormalizedFinite(gene.SecondAllele))
            {
                failures.Add(
                    $"Gene '{gene.TraitId}' alleles must be finite and in [0,1].");
            }
        }

        foreach (var descriptor in _canonicalDescriptors)
        {
            if (!seen.Contains(descriptor.Id))
            {
                failures.Add($"Genome is missing trait '{descriptor.Id}'.");
            }
        }

        return failures;
    }

    public LizardPhenotype Express(LizardGenome genome)
    {
        EnsureValidGenome(genome);
        var normalized = new Dictionary<string, double>(StringComparer.Ordinal);
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var descriptor in Descriptors)
        {
            var gene = genome.GetGene(descriptor.Id);
            var expressedNormalized = descriptor.ExpressNormalized(
                gene.FirstAllele,
                gene.SecondAllele);
            normalized.Add(descriptor.Id, expressedNormalized);
            values.Add(descriptor.Id, descriptor.Decode(expressedNormalized));
        }

        var visibility = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool ResolveVisibility(string traitId)
        {
            if (visibility.TryGetValue(traitId, out var cached))
            {
                return cached;
            }

            var descriptor = _byId[traitId];
            var visible = descriptor.PlayerVisible;
            foreach (var condition in descriptor.ActivationConditions)
            {
                visible = visible &&
                          ResolveVisibility(condition.TraitId) &&
                          condition.IsSatisfied(values[condition.TraitId]);
            }
            visibility.Add(traitId, visible);
            return visible;
        }

        var traits = ImmutableArray.CreateBuilder<ExpressedTrait>(Descriptors.Length);
        foreach (var descriptor in Descriptors)
        {
            var value = values[descriptor.Id];
            traits.Add(new ExpressedTrait(
                descriptor.Id,
                descriptor.DisplayName,
                descriptor.Description,
                descriptor.Category,
                descriptor.ValueKind,
                normalized[descriptor.Id],
                value,
                ResolveVisibility(descriptor.Id),
                descriptor.Format(value)));
        }

        return new LizardPhenotype(RegistryId, traits.MoveToImmutable());
    }

    internal LizardGenome CreateFounder(DeterministicRandom random)
    {
        // One birth always consumes exactly one root value. Per-locus streams
        // are then derived from stable trait IDs, so registering a new trait
        // never rerolls any existing founder characteristic.
        var birthSeed = random.NextUInt64();
        var genes = ImmutableArray.CreateBuilder<GenePair>(Descriptors.Length);
        foreach (var descriptor in _canonicalDescriptors)
        {
            var traitRandom = CreateTraitRandom(birthSeed, descriptor.Id, 0xF04EUL);
            genes.Add(new GenePair(
                descriptor.Id,
                CreateFounderAllele(descriptor, traitRandom),
                CreateFounderAllele(descriptor, traitRandom)));
        }

        return new LizardGenome(
            LizardGenome.CurrentSchemaVersion,
            RegistryId,
            genes.MoveToImmutable());
    }

    internal LizardGenome CreateOffspring(
        LizardGenome firstParent,
        LizardGenome secondParent,
        DeterministicRandom random,
        GeneticBreedingOptions options)
    {
        ArgumentNullException.ThrowIfNull(firstParent);
        ArgumentNullException.ThrowIfNull(secondParent);
        ArgumentNullException.ThrowIfNull(random);
        options.EnsureValid();
        EnsureValidGenome(firstParent);
        EnsureValidGenome(secondParent);

        // As with founders, offspring consume a fixed-size root seed and each
        // trait owns a stable substream. Descriptor insertion/reordering is
        // therefore backward-compatible for every already registered locus.
        var birthSeed = random.NextUInt64();
        var genes = ImmutableArray.CreateBuilder<GenePair>(Descriptors.Length);
        foreach (var descriptor in _canonicalDescriptors)
        {
            var traitRandom = CreateTraitRandom(birthSeed, descriptor.Id, 0x0FF5UL);
            var firstGene = firstParent.GetGene(descriptor.Id);
            var secondGene = secondParent.GetGene(descriptor.Id);
            var firstAllele = traitRandom.NextBoolean()
                ? firstGene.FirstAllele
                : firstGene.SecondAllele;
            var secondAllele = traitRandom.NextBoolean()
                ? secondGene.FirstAllele
                : secondGene.SecondAllele;
            genes.Add(new GenePair(
                descriptor.Id,
                Mutate(firstAllele, descriptor, traitRandom, options),
                Mutate(secondAllele, descriptor, traitRandom, options)));
        }

        return new LizardGenome(
            LizardGenome.CurrentSchemaVersion,
            RegistryId,
            genes.MoveToImmutable());
    }

    private void EnsureValidGenome(LizardGenome genome)
    {
        var failures = ValidateGenome(genome);
        if (failures.Count > 0)
        {
            throw new ArgumentException(
                string.Join(Environment.NewLine, failures),
                nameof(genome));
        }
    }

    private static double CreateFounderAllele(
        TraitDescriptor descriptor,
        DeterministicRandom random)
    {
        if (descriptor.ValueKind == TraitValueKind.Toggle)
        {
            return random.NextChance(descriptor.DefaultNormalized) ? 1d : 0d;
        }

        var value = descriptor.DefaultNormalized +
                    random.NextSymmetricTriangular() * descriptor.FounderVariation;
        if (descriptor.ValueKind == TraitValueKind.Hue)
        {
            return WrapNormalized(value);
        }

        return TraitDescriptor.ClampNormalized(value);
    }

    private static double Mutate(
        double allele,
        TraitDescriptor descriptor,
        DeterministicRandom random,
        GeneticBreedingOptions options)
    {
        allele = TraitDescriptor.ClampNormalized(allele);
        var mutationProbability = Math.Clamp(
            descriptor.MutationRate * options.MutationMultiplier,
            0d,
            1d);
        if (!random.NextChance(mutationProbability))
        {
            return allele;
        }

        var isBurst = random.NextChance(Math.Clamp(
            descriptor.MutationBurstChance * options.BurstMutationMultiplier,
            0d,
            1d));
        if (descriptor.ValueKind == TraitValueKind.Toggle)
        {
            return allele >= 0.5d ? 0d : 1d;
        }
        if (descriptor.ValueKind == TraitValueKind.Choice)
        {
            var choiceCount = descriptor.ChoiceLabels.Length;
            var current = Math.Clamp(
                (int)Math.Floor(allele * choiceCount),
                0,
                choiceCount - 1);
            var next = isBurst
                ? random.NextInt(choiceCount)
                : Math.Clamp(
                    current + (random.NextBoolean() ? 1 : -1),
                    0,
                    choiceCount - 1);
            return (next + 0.5d) / choiceCount;
        }

        var scale = isBurst
            ? descriptor.MutationBurstScale
            : descriptor.MutationScale;
        var mutated = allele + random.NextSymmetricTriangular() * scale;
        return descriptor.ValueKind == TraitValueKind.Hue
            ? WrapNormalized(mutated)
            : ReflectNormalized(mutated);
    }

    private void ValidateCompatibility(
        TraitRegistryCompatibility? predecessor,
        ISet<string> knownIds,
        List<string> failures)
    {
        if (predecessor is null ||
            string.IsNullOrWhiteSpace(predecessor.RegistryId))
        {
            failures.Add("Compatible predecessor registry IDs cannot be empty.");
            return;
        }
        if (string.Equals(
                predecessor.RegistryId,
                RegistryId,
                StringComparison.Ordinal))
        {
            failures.Add(
                "A registry cannot list itself as a compatible predecessor.");
        }
        if (predecessor.TraitIds.IsDefaultOrEmpty)
        {
            failures.Add(
                $"Compatible predecessor '{predecessor.RegistryId}' needs an exact trait manifest.");
            return;
        }

        var previousId = string.Empty;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var traitId in predecessor.TraitIds)
        {
            if (string.IsNullOrWhiteSpace(traitId) || !seen.Add(traitId))
            {
                failures.Add(
                    $"Compatible predecessor '{predecessor.RegistryId}' has an empty or duplicate trait ID.");
                continue;
            }
            if (previousId.Length > 0 &&
                StringComparer.Ordinal.Compare(previousId, traitId) >= 0)
            {
                failures.Add(
                    $"Compatible predecessor '{predecessor.RegistryId}' trait IDs must be in canonical ordinal order.");
            }
            previousId = traitId;
        }
        if (!predecessor.TraitIds.Any(knownIds.Contains))
        {
            failures.Add(
                $"Compatible predecessor '{predecessor.RegistryId}' shares no retained traits with the current registry.");
        }
    }

    private void DetectActivationCycles(
        ISet<string> knownIds,
        List<string> failures)
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        bool Visit(string id)
        {
            if (visited.Contains(id))
            {
                return false;
            }
            if (!visiting.Add(id))
            {
                return true;
            }

            if (_byId.TryGetValue(id, out var descriptor))
            {
                foreach (var condition in descriptor.ActivationConditions)
                {
                    if (knownIds.Contains(condition.TraitId) &&
                        Visit(condition.TraitId))
                    {
                        return true;
                    }
                }
            }

            visiting.Remove(id);
            visited.Add(id);
            return false;
        }

        foreach (var id in knownIds)
        {
            visiting.Clear();
            if (Visit(id))
            {
                failures.Add($"Trait activation graph contains a cycle at '{id}'.");
                return;
            }
        }
    }

    private static bool IsNormalizedFinite(double value) =>
        double.IsFinite(value) && value is >= 0d and <= 1d;

    private static double WrapNormalized(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0.5d;
        }

        value %= 1d;
        return value < 0d ? value + 1d : value;
    }

    private static double ReflectNormalized(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0.5d;
        }

        // Mirror on both boundaries with period two. Unlike clamping, this
        // keeps high-mutation populations from accumulating artificial spikes
        // at exactly zero and one, even when a burst crosses several widths.
        value %= 2d;
        if (value < 0d)
        {
            value += 2d;
        }

        return value <= 1d ? value : 2d - value;
    }

    private static DeterministicRandom CreateTraitRandom(
        ulong birthSeed,
        string traitId,
        ulong purpose)
    {
        var traitHash = StableTraitHash(traitId);
        var seed = Mix64(birthSeed ^ traitHash ^ purpose);
        var stream = Mix64(traitHash + purpose + 0x9E3779B97F4A7C15UL);
        return new DeterministicRandom(seed, stream);
    }

    private static ulong StableTraitHash(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var character in value)
        {
            hash = unchecked((hash ^ (byte)character) * prime);
            hash = unchecked((hash ^ (byte)(character >> 8)) * prime);
        }

        return hash;
    }

    private static ulong Mix64(ulong value)
    {
        value = unchecked(value + 0x9E3779B97F4A7C15UL);
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }

    private static LizardTraitRegistry CreateDefaultRegistry()
    {
        var descriptors = CreateDefaultDescriptors().ToImmutableArray();
        var predecessorIds = descriptors
            .Select(descriptor => descriptor.Id)
            .Append("lifecycle.longevity")
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToImmutableArray();
        return new LizardTraitRegistry(
            DefaultRegistryId,
            descriptors,
            [new TraitRegistryCompatibility(
                PreviousDefaultRegistryId,
                predecessorIds)]);
    }

    private static IEnumerable<TraitDescriptor> CreateDefaultDescriptors()
    {
        var list = new List<TraitDescriptor>();

        void Continuous(
            string id,
            string name,
            string description,
            TraitCategory category,
            double minimum,
            double maximum,
            double defaultNormalized = 0.5d,
            double founderVariation = 0.38d,
            string unit = "",
            AlleleExpressionMode expression = AlleleExpressionMode.Blend,
            double mutationRate = 0.08d,
            double mutationScale = 0.09d,
            ImmutableArray<TraitActivationCondition> conditions = default)
        {
            list.Add(new TraitDescriptor(
                id, name, description, category, TraitValueKind.Continuous,
                minimum, maximum, defaultNormalized, founderVariation,
                expression, mutationRate, mutationScale, 0.012d, 0.42d,
                unit, activationConditions: conditions));
        }

        void Integer(
            string id,
            string name,
            string description,
            TraitCategory category,
            int minimum,
            int maximum,
            double defaultNormalized = 0.5d,
            double founderVariation = 0.38d,
            string unit = "",
            ImmutableArray<TraitActivationCondition> conditions = default)
        {
            list.Add(new TraitDescriptor(
                id, name, description, category, TraitValueKind.Integer,
                minimum, maximum, defaultNormalized, founderVariation,
                AlleleExpressionMode.Blend, 0.07d, 0.13d, 0.015d, 0.46d,
                unit, activationConditions: conditions));
        }

        void Toggle(
            string id,
            string name,
            string description,
            TraitCategory category,
            double founderPresence,
            AlleleExpressionMode expression = AlleleExpressionMode.DominantHigh,
            ImmutableArray<TraitActivationCondition> conditions = default)
        {
            list.Add(new TraitDescriptor(
                id, name, description, category, TraitValueKind.Toggle,
                0d, 1d, founderPresence, 0d, expression,
                0.035d, 1d, 0.04d, 1d,
                activationConditions: conditions));
        }

        void Choice(
            string id,
            string name,
            string description,
            TraitCategory category,
            string[] labels,
            double defaultNormalized = 0.5d,
            double founderVariation = 0.48d,
            ImmutableArray<TraitActivationCondition> conditions = default)
        {
            list.Add(new TraitDescriptor(
                id, name, description, category, TraitValueKind.Choice,
                0d, labels.Length - 1, defaultNormalized, founderVariation,
                AlleleExpressionMode.Blend, 0.055d, 0.2d, 0.025d, 0.8d,
                choiceLabels: labels.ToImmutableArray(),
                activationConditions: conditions));
        }

        void Hue(
            string id,
            string name,
            string description,
            TraitCategory category = TraitCategory.Pigmentation)
        {
            list.Add(new TraitDescriptor(
                id, name, description, category,
                TraitValueKind.Hue, 0d, 360d, 0.34d, 0.75d,
                AlleleExpressionMode.Blend, 0.12d, 0.08d, 0.018d, 0.5d));
        }

        static ImmutableArray<TraitActivationCondition> When(string id) =>
            [new TraitActivationCondition(
                id,
                TraitActivationComparison.AtLeast,
                0.5d)];

        AddPigmentationAndPatternTraits();
        AddBodyAndHeadTraits();
        AddLimbAndAppendageTraits();
        AddTailTraits();
        AddMovementAndBehaviorTraits();
        AddLifecycleTraits();
        return list;

        void AddPigmentationAndPatternTraits()
        {
            Hue("color.base-hue", "主色相", "身体大面积鳞片的主色。不同光照下仍可直接辨认。 ");
            Continuous("color.saturation", "主色饱和度", "决定体色鲜艳或灰雾。", TraitCategory.Pigmentation, 0.18d, 1d, 0.62d);
            Continuous("color.lightness", "主色明度", "决定身体整体偏深或偏亮。", TraitCategory.Pigmentation, 0.12d, 0.88d, 0.5d);
            Hue("color.belly-hue", "腹部色相", "腹部与背部可形成明显撞色。 ");
            Continuous("color.belly-lightness", "腹部明度", "腹部亮斑的明暗。", TraitCategory.Pigmentation, 0.2d, 0.95d, 0.62d);
            Hue("color.accent-hue", "饰纹色相", "条纹、斑点和鳍缘共用的强调色。 ");
            Hue("color.eye-hue", "眼睛色相", "虹膜的主色。 ");
            Continuous("color.eye-glow", "眼睛亮度", "眼睛高光和发光感的强弱。", TraitCategory.Pigmentation, 0d, 1d, 0.35d);
            Continuous("color.iridescence", "虹彩强度", "转身时鳞片出现的变色金属光泽。", TraitCategory.Pigmentation, 0d, 1d, 0.22d);
            Continuous("color.melanin", "黑色素覆盖", "控制深色鳞片覆盖比例。", TraitCategory.Pigmentation, 0d, 1d, 0.42d);

            Choice("pattern.type", "花纹类型", "决定最醒目的身体纹样。", TraitCategory.Pattern,
                ["纯色", "纵纹", "横带", "豹斑", "网纹", "云纹", "星点", "双色块"]);
            Continuous("pattern.density", "花纹密度", "单位身体面积内花纹的多少。", TraitCategory.Pattern, 0.05d, 1d, 0.48d);
            Continuous("pattern.contrast", "花纹对比度", "花纹与底色的视觉反差。", TraitCategory.Pattern, 0.08d, 1d, 0.58d);
            Continuous("pattern.element-size", "纹样尺寸", "单个斑点、网格或色块的大小。", TraitCategory.Pattern, 0.2d, 2.4d);
            Continuous("pattern.stripe-width", "条带宽度", "条纹与横带在身体上的宽度。", TraitCategory.Pattern, 0.1d, 1.8d);
            Integer("pattern.stripe-count", "条带数量", "从头到尾可辨识的主条带数量。", TraitCategory.Pattern, 1, 18, 0.42d, unit: "条");
            Continuous("pattern.symmetry", "花纹对称度", "左右花纹从镜像到完全错位的程度。", TraitCategory.Pattern, 0d, 1d, 0.72d);
            Continuous("pattern.edge-softness", "花纹边缘", "从锐利几何边到柔和晕染。", TraitCategory.Pattern, 0d, 1d);
            Toggle("pattern.glow.present", "发光纹", "夜间是否出现可见的生物荧光花纹。", TraitCategory.Pattern, 0.12d);
            Continuous("pattern.glow.intensity", "发光纹强度", "荧光花纹的亮度和呼吸感。", TraitCategory.Pattern, 0.15d, 1d, conditions: When("pattern.glow.present"));
        }

        void AddBodyAndHeadTraits()
        {
            Continuous(DefaultLizardTraitIds.BodyLength, "躯干长度", "肩胛到尾根的可见长度。", TraitCategory.Body, 0.62d, 1.65d, 0.48d, unit: "×");
            Continuous(DefaultLizardTraitIds.BodyWidth, "躯干宽度", "决定细长型或宽扁型轮廓。", TraitCategory.Body, 0.55d, 1.55d, 0.48d, unit: "×");
            Continuous("body.height", "躯干厚度", "影响腹部离桌面的高度和侧面体积。", TraitCategory.Body, 0.5d, 1.6d, unit: "×");
            Continuous("body.shoulder-mass", "肩部体量", "前肢根部肌肉与肩线的厚重程度。", TraitCategory.Body, 0.45d, 1.7d, unit: "×");
            Continuous("body.hip-mass", "胯部体量", "后肢根部与尾根的厚重程度。", TraitCategory.Body, 0.45d, 1.7d, unit: "×");
            Continuous("body.taper", "躯干收束", "从胸部到尾根变细的明显程度。", TraitCategory.Body, 0d, 1d);
            Continuous("body.neck-length", "颈部长度", "头部与肩部之间的间距。", TraitCategory.Body, 0.35d, 1.8d, unit: "×");
            Continuous("body.belly-roundness", "腹部圆润度", "腹线从紧致到圆鼓的变化。", TraitCategory.Body, 0d, 1d);
            Continuous("body.spine-arch", "背部弧度", "静止时脊线隆起的程度。", TraitCategory.Body, -0.35d, 0.65d);
            Continuous("body.flexibility", "身体柔韧度", "转弯时躯干形成波形的幅度。", TraitCategory.Body, 0.25d, 1d);

            Continuous("head.size", "头部大小", "头部相对躯干的比例。", TraitCategory.Head, 0.55d, 1.65d, unit: "×");
            Continuous("head.width", "头部宽度", "决定窄吻型或宽颅型轮廓。", TraitCategory.Head, 0.55d, 1.65d, unit: "×");
            Continuous("head.snout-length", "吻部长度", "眼睛前方吻部的突出距离。", TraitCategory.Head, 0.35d, 1.9d, unit: "×");
            Choice("head.snout-shape", "吻部形状", "吻端的剪影类型。", TraitCategory.Head,
                ["圆钝", "楔形", "尖吻", "铲形", "喙形"]);
            Continuous("head.eye-size", "眼睛大小", "眼睛相对头部的尺寸。", TraitCategory.Head, 0.5d, 1.8d, unit: "×");
            Continuous("head.eye-spacing", "眼距", "双眼在头部两侧的间距。", TraitCategory.Head, 0.55d, 1.5d, unit: "×");
            Choice("head.pupil-shape", "瞳孔形状", "近看照片时可见的瞳孔轮廓。", TraitCategory.Head,
                ["圆形", "竖缝", "横缝", "菱形", "星形"]);
            Toggle("head.horns.present", "头角", "头顶是否长有成对角突。", TraitCategory.Head, 0.18d);
            Continuous("head.horns.length", "头角长度", "头角向后或向外延伸的长度。", TraitCategory.Head, 0.15d, 1.8d, conditions: When("head.horns.present"));
            Continuous("head.horns.curvature", "头角弯曲", "头角从笔直到卷曲的程度。", TraitCategory.Head, 0d, 1d, conditions: When("head.horns.present"));
            Toggle("head.crest.present", "头冠", "眼后是否长有可见骨冠。", TraitCategory.Head, 0.2d);
            Continuous("head.crest.height", "头冠高度", "头冠高出头部轮廓的比例。", TraitCategory.Head, 0.1d, 1.5d, conditions: When("head.crest.present"));
        }

        void AddLimbAndAppendageTraits()
        {
            Integer(DefaultLizardTraitIds.LegPairCount, "腿的对数", "身体拥有一至五对腿，是最醒目的结构变异之一。", TraitCategory.Limbs, 1, 5, 0.28d, 0.55d, "对");
            Integer(DefaultLizardTraitIds.LegJointCount, "每腿关节数", "每条腿一至五段关节，改变轮廓和步态。", TraitCategory.Limbs, 1, 5, 0.3d, 0.5d, "个");
            Continuous("limbs.length", "腿长", "腿相对身体的总长度。", TraitCategory.Limbs, 0.45d, 1.9d, unit: "×");
            Continuous("limbs.thickness", "腿粗", "腿部骨骼和肌肉的视觉粗细。", TraitCategory.Limbs, 0.35d, 1.8d, unit: "×");
            Continuous("limbs.front-rear-ratio", "前后腿比例", "前腿和后腿长度差异。", TraitCategory.Limbs, 0.55d, 1.45d);
            Continuous("limbs.foot-size", "脚掌大小", "脚掌相对腿长的比例。", TraitCategory.Limbs, 0.45d, 1.8d, unit: "×");
            Integer("limbs.toe-count", "每脚趾数", "每只脚可见的趾头数量。", TraitCategory.Limbs, 2, 7, 0.45d, 0.52d, "根");
            Continuous("limbs.claw-length", "爪长", "趾端爪子的突出程度。", TraitCategory.Limbs, 0d, 1.6d, 0.36d, unit: "×");
            Toggle("limbs.webbing.present", "蹼", "脚趾之间是否有可见蹼膜。", TraitCategory.Limbs, 0.2d);
            Continuous("limbs.webbing.amount", "蹼宽", "蹼膜覆盖趾间的比例。", TraitCategory.Limbs, 0.15d, 1d, conditions: When("limbs.webbing.present"));
            Continuous("limbs.grip-pad-size", "吸附垫大小", "指趾末端吸附垫的尺寸。", TraitCategory.Limbs, 0.2d, 1.8d, unit: "×");
            Continuous("limbs.left-right-asymmetry", "肢体不对称", "左右腿长度和落脚相位的轻微差异。", TraitCategory.Limbs, 0d, 0.28d, 0.18d);

            Continuous("skin.scale-size", "鳞片大小", "近照中单片鳞片的尺寸。", TraitCategory.Skin, 0.25d, 2d, unit: "×");
            Continuous("skin.roughness", "鳞片粗糙度", "从光滑皮肤到甲片质感。", TraitCategory.Skin, 0d, 1d);
            Continuous("skin.gloss", "皮肤光泽", "高光从哑光到湿润镜面。", TraitCategory.Skin, 0d, 1d);
            Continuous("skin.translucency", "皮肤半透明", "薄处透光与血色的可见程度。", TraitCategory.Skin, 0d, 0.8d, 0.2d);

            Toggle(DefaultLizardTraitIds.DorsalFinPresent, "背鳍", "背部是否长有连续鳍或帆。", TraitCategory.Appendages, 0.24d);
            Continuous("appendage.dorsal-fin.height", "背鳍高度", "背鳍高出脊线的比例。", TraitCategory.Appendages, 0.12d, 1.8d, conditions: When(DefaultLizardTraitIds.DorsalFinPresent));
            Continuous("appendage.dorsal-fin.length", "背鳍长度", "背鳍覆盖背部和尾部的范围。", TraitCategory.Appendages, 0.15d, 1d, conditions: When(DefaultLizardTraitIds.DorsalFinPresent));
            Choice("appendage.dorsal-fin.shape", "背鳍形状", "背鳍边缘的整体剪影。", TraitCategory.Appendages,
                ["圆帆", "三角锯齿", "波浪", "羽片", "双峰"],
                conditions: When(DefaultLizardTraitIds.DorsalFinPresent));
            Toggle("appendage.side-fins.present", "侧鳍", "身体两侧是否长有翼状鳍片。", TraitCategory.Appendages, 0.14d);
            Integer("appendage.side-fins.pairs", "侧鳍对数", "身体两侧鳍片的对数。", TraitCategory.Appendages, 1, 4, conditions: When("appendage.side-fins.present"));
            Continuous("appendage.side-fins.size", "侧鳍大小", "侧鳍展开后的面积。", TraitCategory.Appendages, 0.15d, 1.6d, conditions: When("appendage.side-fins.present"));
            Toggle("appendage.neck-frill.present", "颈褶", "头后是否拥有可展开的扇形颈褶。", TraitCategory.Appendages, 0.18d);
            Continuous("appendage.neck-frill.size", "颈褶大小", "颈褶展开时相对头部的直径。", TraitCategory.Appendages, 0.2d, 2d, conditions: When("appendage.neck-frill.present"));
            Toggle("appendage.gill-tuft.present", "外鳃羽", "颈侧是否长有羽状外鳃。", TraitCategory.Appendages, 0.13d);
            Continuous("appendage.gill-tuft.length", "外鳃羽长度", "羽状外鳃向外伸展的长度。", TraitCategory.Appendages, 0.15d, 1.7d, conditions: When("appendage.gill-tuft.present"));
            Toggle("appendage.whiskers.present", "触须", "吻部是否长有可摆动触须。", TraitCategory.Appendages, 0.19d);
            Integer("appendage.whiskers.pairs", "触须对数", "吻部两侧触须的对数。", TraitCategory.Appendages, 1, 4, conditions: When("appendage.whiskers.present"));
            Continuous("appendage.whiskers.length", "触须长度", "触须相对头部的延伸长度。", TraitCategory.Appendages, 0.3d, 2.4d, conditions: When("appendage.whiskers.present"));
        }

        void AddTailTraits()
        {
            Continuous("tail.length", "尾巴长度", "尾巴相对躯干的长度。", TraitCategory.Tail, 0.45d, 2.8d, unit: "×");
            Continuous("tail.base-thickness", "尾根粗细", "尾根相对胯部的粗细。", TraitCategory.Tail, 0.3d, 1.8d, unit: "×");
            Continuous("tail.taper", "尾巴收尖", "尾巴从尾根到末端变细的速度。", TraitCategory.Tail, 0d, 1d);
            Continuous("tail.flexibility", "尾巴柔韧度", "摆尾时能形成的弯曲幅度。", TraitCategory.Tail, 0.15d, 1d);
            Integer("tail.segment-count", "尾部节段", "尾巴运动与装甲分段的数量。", TraitCategory.Tail, 3, 18, unit: "节");
            Toggle("tail.sail.present", "尾翼", "尾巴是否长有连续的垂直尾翼。", TraitCategory.Tail, 0.2d);
            Continuous("tail.sail.height", "尾翼高度", "尾翼高出尾骨的比例。", TraitCategory.Tail, 0.1d, 1.8d, conditions: When("tail.sail.present"));
            Continuous("tail.sail.length", "尾翼覆盖", "尾翼覆盖整条尾巴的比例。", TraitCategory.Tail, 0.18d, 1d, conditions: When("tail.sail.present"));
            Toggle("tail.spikes.present", "尾刺", "尾巴是否排列有可见棘刺。", TraitCategory.Tail, 0.21d);
            Integer("tail.spikes.count", "尾刺数量", "尾部主棘刺的数量。", TraitCategory.Tail, 2, 24, conditions: When("tail.spikes.present"));
            Continuous("tail.spikes.size", "尾刺大小", "尾刺的平均长度和粗度。", TraitCategory.Tail, 0.12d, 1.7d, conditions: When("tail.spikes.present"));
            Toggle(DefaultLizardTraitIds.TailClubPresent, "尾锤", "尾端是否长有沉重的钉锤结构。", TraitCategory.Tail, 0.12d, AlleleExpressionMode.DominantLow);
            Continuous("tail.club.size", "尾锤大小", "尾锤相对头部的体积。", TraitCategory.Tail, 0.3d, 2d, conditions: When(DefaultLizardTraitIds.TailClubPresent));
            Continuous("tail.club-spike-length", "尾锤钉刺", "尾锤表面钉刺的长度。", TraitCategory.Tail, 0d, 1.5d, conditions: When(DefaultLizardTraitIds.TailClubPresent));
            Toggle("tail.fork.present", "分叉尾", "尾端是否分成两支。", TraitCategory.Tail, 0.1d, AlleleExpressionMode.DominantLow);
            Continuous("tail.fork.length", "分叉长度", "尾端分叉占尾长的比例。", TraitCategory.Tail, 0.12d, 0.58d, conditions: When("tail.fork.present"));
        }

        void AddMovementAndBehaviorTraits()
        {
            Continuous(DefaultLizardTraitIds.MaximumSpeed, "最高速度", "桌面爬行可达到的最高速度。", TraitCategory.Locomotion, 0.55d, 1.8d, unit: "×");
            Continuous("locomotion.acceleration", "加速度", "从静止进入冲刺的快慢。", TraitCategory.Locomotion, 0.45d, 1.9d, unit: "×");
            Continuous("locomotion.turn-agility", "转向灵活度", "改变方向时身体弯曲和响应的速度。", TraitCategory.Locomotion, 0.4d, 1.8d, unit: "×");
            Choice("locomotion.gait", "主要步态", "日常移动最常使用的节奏。", TraitCategory.Locomotion,
                ["交替步", "对角步", "波浪步", "弹跳步", "侧行", "多足涟漪"]);
            Continuous("locomotion.stride", "步幅", "每一步向前覆盖的距离。", TraitCategory.Locomotion, 0.45d, 1.7d, unit: "×");
            Continuous("locomotion.step-height", "抬腿高度", "足端离开桌面的明显程度。", TraitCategory.Locomotion, 0.25d, 1.8d, unit: "×");
            Continuous("locomotion.grip", "抓附力", "贴边和失手后重新抓住的能力。", TraitCategory.Locomotion, 0.35d, 1d);
            Continuous("locomotion.endurance", "耐力", "连续活跃后才进入休息的时间。", TraitCategory.Locomotion, 0.25d, 1d);
            Continuous("locomotion.balance", "平衡能力", "多足协调和急转时保持身体稳定的能力。", TraitCategory.Locomotion, 0.2d, 1d);
            Continuous("locomotion.fall-control", "空中控制", "失手下落时调整姿态和伸肢的能力。", TraitCategory.Locomotion, 0.15d, 1d);
            Continuous("locomotion.swim-affinity", "波浪摆动倾向", "改变桌面 S 形爬行的幅度、节奏与出现频率。", TraitCategory.Locomotion, 0d, 1d);
            Continuous("locomotion.idle-sway", "待机摇摆", "静止时尾巴和身体摆动的幅度。", TraitCategory.Locomotion, 0d, 1d);

            Continuous("temperament.activity", "活跃", "主动探索和移动的总体频率。", TraitCategory.Temperament, 0d, 1d);
            Continuous("temperament.curiosity", "好奇", "靠近新物体和观察鼠标的倾向。", TraitCategory.Temperament, 0d, 1d);
            Continuous("temperament.playfulness", "爱玩", "做快速曲线、追逐和夸张动作的倾向。", TraitCategory.Temperament, 0d, 1d);
            Continuous("temperament.boldness", "胆量", "面对鼠标和边缘时保持接近的倾向。", TraitCategory.Temperament, 0d, 1d);
            Continuous("temperament.calmness", "平静", "动作稳定、长休息和缓慢反应的倾向。", TraitCategory.Temperament, 0d, 1d);
            Continuous("temperament.sociability", "合群恢复", "合群程度越高，繁育后的个体冷却时间越短。", TraitCategory.Temperament, 0d, 1d);
            Continuous("temperament.territoriality", "独处需求", "领地性越高，繁育后恢复到可再次配对所需时间越长。", TraitCategory.Temperament, 0d, 1d);
            Continuous("temperament.patience", "耐心", "观察或等待时维持状态的时长。", TraitCategory.Temperament, 0d, 1d);
            Continuous("temperament.food-drive", "觅食探索欲", "更偏好快速移动与快速搜索路径的程度。", TraitCategory.Temperament, 0d, 1d);
            Continuous("temperament.sleepiness", "嗜睡", "进入长时间休息的倾向。", TraitCategory.Temperament, 0d, 1d);

            Continuous(DefaultLizardTraitIds.PointerDisposition, "鼠标态度", "从强烈躲避到主动追逐鼠标。", TraitCategory.PointerResponse, -1d, 1d);
            Continuous("pointer.attention-radius", "鼠标注意范围", "多远开始看向鼠标。", TraitCategory.PointerResponse, 0.45d, 1.9d, unit: "×");
            Continuous("pointer.reaction-delay", "鼠标反应延迟", "发现鼠标后开始行动的等待时间。", TraitCategory.PointerResponse, 0.04d, 1.4d, unit: "秒");
            Continuous("pointer.chase-persistence", "追逐持续", "鼠标离开后继续追踪的时间。", TraitCategory.PointerResponse, 0d, 1d);
            Continuous("pointer.avoidance-radius", "警戒距离", "胆怯个体开始逃离鼠标的距离。", TraitCategory.PointerResponse, 0.35d, 1.8d, unit: "×");
            Continuous("pointer.click-sensitivity", "松手敏感", "被拖动并松手后快速逃跑的距离与加速度。", TraitCategory.PointerResponse, 0d, 1d);
            Continuous("pointer.drag-tolerance", "拖动耐受", "被玩家拖动时挣扎与放松的平衡。", TraitCategory.PointerResponse, 0d, 1d);
            Continuous("pointer.recovery", "受惊恢复", "受鼠标惊吓后恢复日常状态的速度。", TraitCategory.PointerResponse, 0.2d, 1.8d, unit: "×");

            AddTransition("idle-to-explore", "闲置→探索", "从待机主动开始探索的相对权重。");
            AddTransition("explore-to-idle", "探索→闲置", "探索后停下来休息的相对权重。");
            AddTransition("observe-to-chase", "观察→追逐", "观察鼠标后开始追逐的相对权重。");
            AddTransition("chase-to-sprint", "追逐→冲刺", "追逐中突然加速的相对权重。");
            AddTransition("wander-to-curve", "漫游→转弯", "直行中进入曲线爬行的相对权重。");
            AddTransition("curve-to-s-curve", "转弯→S形", "普通曲线变成摆动 S 形的相对权重。");
            AddTransition("rest-to-long-rest", "短休→长休", "把一次短暂停留延长为睡眠的相对权重。");
            AddTransition("edge-to-panic", "边缘→慌乱", "靠近危险边缘时失手或逃跑的相对权重。");
            AddTransition("social-approach", "社交恢复", "提高繁育后恢复速度，缩短再次配对的冷却时间。");
            AddTransition("courtship", "求偶准备", "提高配对准备效率，缩短繁育后的冷却时间。");
            AddTransition("egg-guarding", "护蛋效率", "双方亲本共同决定蛋的孵化速度；数值越高，孵化时间越短。");
            AddTransition("novelty-seeking", "尝试新动作", "低概率切换到少见行为的权重。");

            void AddTransition(string id, string name, string description) =>
                Continuous(
                    "behavior.transition." + id,
                    name,
                    description,
                    TraitCategory.BehaviorTransitions,
                    0.05d,
                    2.5d,
                    0.38d,
                    0.46d,
                    "×",
                    AlleleExpressionMode.HeterozygousBoost,
                    0.1d,
                    0.12d);
        }

        void AddLifecycleTraits()
        {
            Continuous(DefaultLizardTraitIds.IncubationRate, "孵化速度", "蛋纹中的发育节奏，决定破壳所需时间。", TraitCategory.Lifecycle, 0.65d, 1.45d, unit: "×");
            Continuous(DefaultLizardTraitIds.MaturationRate, "成熟速度", "幼体长成可繁育成体所需时间的倍率。", TraitCategory.Lifecycle, 0.65d, 1.45d, unit: "×");
            Continuous("lifecycle.vitality", "成长活力", "活力越高，幼体成熟所需时间越短。", TraitCategory.Lifecycle, 0.2d, 1d);
            Continuous(DefaultLizardTraitIds.MutationSensitivity, "变异敏感度", "该个体成为亲本时子代发生新变异的倾向。", TraitCategory.Lifecycle, 0.65d, 1.55d, unit: "×");
            Continuous("lifecycle.metabolism", "发育代谢", "同时加快蛋的孵化与幼体成熟节奏。", TraitCategory.Lifecycle, 0.55d, 1.65d, unit: "×");
            Choice("lifecycle.egg-shell-pattern", "蛋壳花纹", "该血统产下的蛋所呈现的主要纹样。", TraitCategory.Lifecycle,
                ["细点", "大斑", "环带", "裂纹", "渐变", "星纹"]);
            Hue(
                "lifecycle.egg-shell-hue",
                "蛋壳色相",
                "该血统所产蛋壳的主色。 ",
                TraitCategory.Lifecycle);
        }
    }
}
