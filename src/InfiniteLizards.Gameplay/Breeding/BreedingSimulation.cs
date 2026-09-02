using System.Collections.Immutable;
using InfiniteLizards.Gameplay.Genetics;

namespace InfiniteLizards.Gameplay.Breeding;

/// <summary>
/// Platform-independent breeding, lifecycle and economy aggregate. Callers
/// advance explicit elapsed time; no wall clock or render cadence is owned by
/// this class.
/// </summary>
public sealed class BreedingSimulation
{
    private static readonly string[] NamePrefixes =
    [
        "苔", "琥珀", "月", "雾", "墨", "星", "栗", "青", "珊瑚", "霜",
        "云", "蒲公英", "松石", "紫苏", "银", "雨", "萤", "岩"
    ];

    private static readonly string[] NameSuffixes =
    [
        "尾", "爪", "鳍", "团", "豆", "闪", "弯", "芽", "点", "冠",
        "锤", "须", "纹", "泡", "风", "影", "糖", "星"
    ];

    private readonly LizardTraitRegistry _registry;
    private readonly BreedingRules _rules;
    private readonly Dictionary<string, BreedingLizardSnapshot> _lizards;
    private readonly Dictionary<string, BreedingEggSnapshot> _eggs;
    private DeterministicRandom _random;
    private long _nextSequence;
    private int _coins;

    public LizardTraitRegistry TraitRegistry => _registry;
    public BreedingRules Rules => _rules;
    public int Coins => _coins;
    public int LizardCount => _lizards.Count;
    public int EggCount => _eggs.Count;
    public ImmutableArray<BreedingLizardSnapshot> Lizards =>
        _lizards.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToImmutableArray();
    public ImmutableArray<BreedingEggSnapshot> Eggs =>
        _eggs.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToImmutableArray();

    private BreedingSimulation(
        LizardTraitRegistry registry,
        BreedingRules rules,
        int coins,
        long nextSequence,
        DeterministicRandom random,
        IEnumerable<BreedingLizardSnapshot>? lizards = null,
        IEnumerable<BreedingEggSnapshot>? eggs = null)
    {
        _registry = registry;
        _rules = rules;
        _coins = coins;
        _nextSequence = nextSequence;
        _random = random;
        _lizards = (lizards ?? [])
            .ToDictionary(lizard => lizard.Id, StringComparer.Ordinal);
        _eggs = (eggs ?? [])
            .ToDictionary(egg => egg.Id, StringComparer.Ordinal);
    }

    public static BreedingSimulation Create(
        ulong seed,
        int initialCoins = 10,
        BreedingRules? rules = null,
        LizardTraitRegistry? registry = null)
    {
        if (initialCoins < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCoins));
        }

        rules ??= BreedingRules.Default;
        rules.EnsureValid();
        registry ??= LizardTraitRegistry.Default;
        return new BreedingSimulation(
            registry,
            rules,
            initialCoins,
            0L,
            new DeterministicRandom(seed));
    }

    public static BreedingSimulation Restore(
        BreedingWorldSnapshot snapshot,
        LizardTraitRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        registry ??= LizardTraitRegistry.Default;
        snapshot = UpgradeSnapshotForRegistry(snapshot, registry);
        ValidateSnapshot(snapshot, registry);
        return new BreedingSimulation(
            registry,
            snapshot.Rules,
            snapshot.Coins,
            snapshot.NextSequence,
            new DeterministicRandom(snapshot.RandomState),
            snapshot.Lizards,
            snapshot.Eggs);
    }

    private static BreedingWorldSnapshot UpgradeSnapshotForRegistry(
        BreedingWorldSnapshot snapshot,
        LizardTraitRegistry registry)
    {
        if (string.Equals(
                snapshot.TraitRegistryId,
                registry.RegistryId,
                StringComparison.Ordinal))
        {
            return snapshot;
        }
        if (!registry.CanUpgradeFrom(snapshot.TraitRegistryId))
        {
            throw new ArgumentException(
                $"Breeding snapshot registry '{snapshot.TraitRegistryId}' is " +
                $"not a compatible predecessor of '{registry.RegistryId}'.",
                nameof(snapshot));
        }

        var structuralFailures = ValidateSnapshotStructure(snapshot);
        if (structuralFailures.Count > 0)
        {
            throw new ArgumentException(
                string.Join(Environment.NewLine, structuralFailures),
                nameof(snapshot));
        }

        LizardGenome UpgradeBoundGenome(LizardGenome genome)
        {
            if (!string.Equals(
                    genome.RegistryId,
                    snapshot.TraitRegistryId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Every genome in a predecessor snapshot must use the " +
                    "snapshot's declared trait registry.",
                    nameof(snapshot));
            }

            return registry.UpgradeGenome(genome);
        }

        var upgradedLizards = snapshot.Lizards
            .Select(lizard => lizard with
            {
                Genome = UpgradeBoundGenome(lizard.Genome)
            })
            .ToImmutableArray();
        var upgradedEggs = snapshot.Eggs
            .Select(egg => egg with
            {
                Genome = UpgradeBoundGenome(egg.Genome)
            })
            .ToImmutableArray();
        return snapshot with
        {
            TraitRegistryId = registry.RegistryId,
            Lizards = upgradedLizards,
            Eggs = upgradedEggs
        };
    }

    public BreedingWorldSnapshot CaptureSnapshot() => new(
        BreedingWorldSnapshot.CurrentSchemaVersion,
        _registry.RegistryId,
        _rules,
        _coins,
        _nextSequence,
        _random.CaptureState(),
        Lizards,
        Eggs);

    public bool TryGetLizard(string id, out BreedingLizardSnapshot lizard) =>
        _lizards.TryGetValue(id, out lizard!);

    public bool TryGetEgg(string id, out BreedingEggSnapshot egg) =>
        _eggs.TryGetValue(id, out egg!);

    public LizardPhenotype GetPhenotype(string lizardId)
    {
        if (!_lizards.TryGetValue(lizardId, out var lizard))
        {
            throw new KeyNotFoundException($"Lizard '{lizardId}' was not found.");
        }

        return _registry.Express(lizard.Genome);
    }

    public BreedingActionResult BuyLizard(
        string habitatId,
        string? name = null)
    {
        if (!TryNormalizeHabitat(habitatId, out habitatId) ||
            name is not null && string.IsNullOrWhiteSpace(name))
        {
            return Failure(
                BreedingActionStatus.InvalidArgument,
                "栖息地和自定义名字不能为空。");
        }
        if (_coins < LizardMarketPrices.Buy)
        {
            return Failure(
                BreedingActionStatus.InsufficientFunds,
                $"买入蜥蜴需要 {LizardMarketPrices.Buy} 元。");
        }
        if (!HasIdentityCapacity(1))
        {
            return Failure(
                BreedingActionStatus.IdentitySequenceExhausted,
                "存档的个体编号空间已用尽，无法再买入蜥蜴。");
        }

        var genome = _registry.CreateFounder(_random);
        var phenotype = _registry.Express(genome);
        var requiredMaturationTicks = CalculateDurationTicks(
            _rules.BaseMaturationDuration,
            BreedingTraitEffectResolver.ResolveMaturationRate(phenotype));
        var id = NextId("lizard");
        var finalName = string.IsNullOrWhiteSpace(name)
            ? GenerateName()
            : name.Trim();
        var lizard = new BreedingLizardSnapshot(
            id,
            finalName,
            genome,
            requiredMaturationTicks,
            requiredMaturationTicks,
            0L,
            habitatId,
            null,
            null,
            0,
            true);
        _lizards.Add(id, lizard);
        _coins -= LizardMarketPrices.Buy;
        return Success(
            $"已花费 {LizardMarketPrices.Buy} 元买入 {finalName}。",
            lizardId: id);
    }

    public BreedingActionResult SellLizard(string lizardId)
    {
        if (string.IsNullOrWhiteSpace(lizardId))
        {
            return Failure(
                BreedingActionStatus.InvalidArgument,
                "蜥蜴 ID 不能为空。");
        }
        if (!_lizards.TryGetValue(lizardId, out var lizard))
        {
            return Failure(
                BreedingActionStatus.LizardNotFound,
                "要卖出的蜥蜴不存在。");
        }
        if (_coins > int.MaxValue - LizardMarketPrices.Sell)
        {
            return Failure(
                BreedingActionStatus.EconomyLimitReached,
                "金币已达到存档可表示的上限。");
        }

        var resultingCoins = _coins + LizardMarketPrices.Sell;
        var potentialPopulation =
            (long)_lizards.Count - 1L +
            _eggs.Count +
            resultingCoins / LizardMarketPrices.Buy;
        if (potentialPopulation < 2L)
        {
            return Failure(
                BreedingActionStatus.WouldStrandCollection,
                "卖出后现有蜥蜴、可孵化蛋与余额可买入的蜥蜴合计不足两只，" +
                "将无法继续繁育，已取消交易。");
        }

        _lizards.Remove(lizardId);
        _coins = resultingCoins;
        return Success(
            $"已卖出 {lizard.Name}，获得 {LizardMarketPrices.Sell} 元。",
            lizardId: lizardId);
    }

    public BreedingActionResult MoveLizard(string lizardId, string habitatId)
    {
        if (string.IsNullOrWhiteSpace(lizardId) ||
            !TryNormalizeHabitat(habitatId, out habitatId))
        {
            return Failure(
                BreedingActionStatus.InvalidArgument,
                "蜥蜴 ID 和栖息地不能为空。");
        }
        if (!_lizards.TryGetValue(lizardId, out var lizard))
        {
            return Failure(
                BreedingActionStatus.LizardNotFound,
                "要移动的蜥蜴不存在。");
        }

        _lizards[lizardId] = lizard with { HabitatId = habitatId };
        return Success(
            $"已把 {lizard.Name} 放到 {habitatId}。",
            lizardId: lizardId);
    }

    /// <summary>
    /// Reproduction is role-neutral: any two different mature lizards may
    /// contribute one allele per locus. This preserves player freedom and
    /// avoids a hidden sex roll making a prized pair unusable.
    /// </summary>
    public BreedingActionResult Breed(
        string firstParentId,
        string secondParentId)
    {
        if (string.IsNullOrWhiteSpace(firstParentId) ||
            string.IsNullOrWhiteSpace(secondParentId))
        {
            return Failure(
                BreedingActionStatus.InvalidArgument,
                "亲本 ID 不能为空。");
        }
        if (string.Equals(firstParentId, secondParentId, StringComparison.Ordinal))
        {
            return Failure(
                BreedingActionStatus.SameParent,
                "繁育需要两只不同的蜥蜴。");
        }
        if (!_lizards.TryGetValue(firstParentId, out var firstParent) ||
            !_lizards.TryGetValue(secondParentId, out var secondParent))
        {
            return Failure(
                BreedingActionStatus.LizardNotFound,
                "至少一只亲本不存在。");
        }
        if (firstParent.LifeStage != BreedingLifeStage.Mature ||
            secondParent.LifeStage != BreedingLifeStage.Mature)
        {
            return Failure(
                BreedingActionStatus.JuvenileCannotBreed,
                "两只蜥蜴都成熟后才能繁育。");
        }
        if (!string.Equals(
                firstParent.HabitatId,
                secondParent.HabitatId,
                StringComparison.Ordinal))
        {
            return Failure(
                BreedingActionStatus.DifferentHabitat,
                "需要先把两只蜥蜴放到同一个繁育地点。");
        }
        if (firstParent.BreedingCooldownTicks > 0L ||
            secondParent.BreedingCooldownTicks > 0L)
        {
            return Failure(
                BreedingActionStatus.BreedingCooldown,
                "亲本仍在繁育冷却中。");
        }
        if (!HasIdentityCapacity(1))
        {
            return Failure(
                BreedingActionStatus.IdentitySequenceExhausted,
                "存档的个体编号空间已用尽，无法创建新的蛋。");
        }

        var firstPhenotype = _registry.Express(firstParent.Genome);
        var secondPhenotype = _registry.Express(secondParent.Genome);
        var inheritedMutationSensitivity =
            (firstPhenotype.GetValue(DefaultLizardTraitIds.MutationSensitivity) +
             secondPhenotype.GetValue(DefaultLizardTraitIds.MutationSensitivity)) * 0.5d;
        var offspring = _registry.CreateOffspring(
            firstParent.Genome,
            secondParent.Genome,
            _random,
            new GeneticBreedingOptions(
                SaturatingMultiplyFiniteNonNegative(
                    _rules.MutationMultiplier,
                    inheritedMutationSensitivity),
                _rules.BurstMutationMultiplier));
        var offspringPhenotype = _registry.Express(offspring);
        var eggId = NextId("egg");
        var generation = Math.Max(firstParent.Generation, secondParent.Generation) + 1;
        var egg = new BreedingEggSnapshot(
            eggId,
            offspring,
            0L,
            CalculateDurationTicks(
                _rules.BaseIncubationDuration,
                BreedingTraitEffectResolver.ResolveIncubationRate(
                    offspringPhenotype,
                    firstPhenotype,
                    secondPhenotype)),
            CalculateDurationTicks(
                _rules.BaseMaturationDuration,
                BreedingTraitEffectResolver.ResolveMaturationRate(
                    offspringPhenotype)),
            firstParent.HabitatId,
            firstParent.Id,
            secondParent.Id,
            generation);
        _eggs.Add(eggId, egg);
        var firstCooldownTicks = CalculateDurationTicks(
            _rules.BreedingCooldown,
            BreedingTraitEffectResolver.ResolveBreedingCooldownRate(
                firstPhenotype));
        var secondCooldownTicks = CalculateDurationTicks(
            _rules.BreedingCooldown,
            BreedingTraitEffectResolver.ResolveBreedingCooldownRate(
                secondPhenotype));
        _lizards[firstParentId] = firstParent with
        {
            BreedingCooldownTicks = firstCooldownTicks
        };
        _lizards[secondParentId] = secondParent with
        {
            BreedingCooldownTicks = secondCooldownTicks
        };
        return Success("繁育成功，产下了一个蛋。", eggId: eggId);
    }

    public BreedingAdvanceResult Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsed),
                "Breeding time cannot run backwards.");
        }
        if (elapsed == TimeSpan.Zero)
        {
            return BreedingAdvanceResult.Empty;
        }

        var elapsedTicks = elapsed.Ticks;
        var hatching = _eggs.Values
            .Select(egg => new
            {
                Egg = egg,
                Remaining = Math.Max(
                    0L,
                    egg.RequiredIncubationTicks - egg.IncubationElapsedTicks)
            })
            .OrderBy(item => item.Remaining)
            .ThenBy(item => item.Egg.Id, StringComparer.Ordinal)
            .ToArray();
        var hatchCount = hatching.Count(item => elapsedTicks >= item.Remaining);
        if (!HasIdentityCapacity(hatchCount))
        {
            // This check intentionally precedes age, cooldown, egg progress,
            // removals and RNG/name generation, making a failed offline tick
            // a fully transactionally inert operation.
            throw new InvalidOperationException(
                "The breeding identity sequence has insufficient capacity " +
                $"for {hatchCount} hatching lizard(s).");
        }

        var newlyMatured = ImmutableArray.CreateBuilder<string>();
        foreach (var lizard in Lizards)
        {
            var wasMature = lizard.LifeStage == BreedingLifeStage.Mature;
            var advanced = lizard with
            {
                AgeTicks = SaturatingAdd(lizard.AgeTicks, elapsedTicks),
                BreedingCooldownTicks = Math.Max(
                    0L,
                    lizard.BreedingCooldownTicks - elapsedTicks)
            };
            _lizards[lizard.Id] = advanced;
            if (!wasMature && advanced.LifeStage == BreedingLifeStage.Mature)
            {
                newlyMatured.Add(lizard.Id);
            }
        }
        var hatchedEggIds = ImmutableArray.CreateBuilder<string>();
        var hatchlingIds = ImmutableArray.CreateBuilder<string>();
        foreach (var item in hatching)
        {
            var egg = item.Egg;
            if (elapsedTicks < item.Remaining)
            {
                _eggs[egg.Id] = egg with
                {
                    IncubationElapsedTicks = SaturatingAdd(
                        egg.IncubationElapsedTicks,
                        elapsedTicks)
                };
                continue;
            }

            _eggs.Remove(egg.Id);
            var ageAtEndOfStep = elapsedTicks - item.Remaining;
            var id = NextId("lizard");
            var hatchling = new BreedingLizardSnapshot(
                id,
                GenerateName(),
                egg.Genome,
                ageAtEndOfStep,
                egg.RequiredMaturationTicks,
                0L,
                egg.HabitatId,
                egg.FirstParentId,
                egg.SecondParentId,
                egg.Generation,
                false);
            _lizards.Add(id, hatchling);
            hatchedEggIds.Add(egg.Id);
            hatchlingIds.Add(id);
            if (hatchling.LifeStage == BreedingLifeStage.Mature)
            {
                newlyMatured.Add(id);
            }
        }

        return new BreedingAdvanceResult(
            hatchedEggIds.ToImmutable(),
            hatchlingIds.ToImmutable(),
            newlyMatured.ToImmutable());
    }

    private static void ValidateSnapshot(
        BreedingWorldSnapshot snapshot,
        LizardTraitRegistry registry)
    {
        var failures = ValidateSnapshotStructure(snapshot);
        if (!string.Equals(
                snapshot.TraitRegistryId,
                registry.RegistryId,
                StringComparison.Ordinal))
        {
            failures.Add("Breeding snapshot trait registry does not match runtime registry.");
        }
        if (!snapshot.Lizards.IsDefault)
        {
            foreach (var lizard in snapshot.Lizards)
            {
                if (lizard?.Genome is not null)
                {
                    failures.AddRange(registry.ValidateGenome(lizard.Genome));
                }
            }
        }
        if (!snapshot.Eggs.IsDefault)
        {
            foreach (var egg in snapshot.Eggs)
            {
                if (egg?.Genome is not null)
                {
                    failures.AddRange(registry.ValidateGenome(egg.Genome));
                }
            }
        }

        if (failures.Count > 0)
        {
            throw new ArgumentException(
                string.Join(Environment.NewLine, failures),
                nameof(snapshot));
        }
    }

    private static List<string> ValidateSnapshotStructure(
        BreedingWorldSnapshot snapshot)
    {
        var failures = new List<string>();
        if (snapshot.SchemaVersion != BreedingWorldSnapshot.CurrentSchemaVersion)
        {
            failures.Add(
                $"Breeding snapshot schema {snapshot.SchemaVersion} is unsupported.");
        }
        if (string.IsNullOrWhiteSpace(snapshot.TraitRegistryId))
        {
            failures.Add("Breeding snapshot trait registry ID is missing.");
        }
        if (snapshot.Rules is null)
        {
            failures.Add("Breeding snapshot rules are missing.");
        }
        else
        {
            failures.AddRange(snapshot.Rules.Validate());
        }
        if (snapshot.Coins < 0 || snapshot.NextSequence < 0L)
        {
            failures.Add("Breeding economy and identity counters cannot be negative.");
        }
        if ((snapshot.RandomState.Increment & 1UL) == 0UL)
        {
            failures.Add("Breeding random stream state is invalid.");
        }
        if (snapshot.Lizards.IsDefault || snapshot.Eggs.IsDefault)
        {
            failures.Add("Breeding snapshot collections are uninitialized.");
        }
        else
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var lizard in snapshot.Lizards)
            {
                if (lizard is null ||
                    string.IsNullOrWhiteSpace(lizard.Id) ||
                    string.IsNullOrWhiteSpace(lizard.Name) ||
                    string.IsNullOrWhiteSpace(lizard.HabitatId) ||
                    lizard.Genome is null ||
                    lizard.AgeTicks < 0L ||
                    lizard.RequiredMaturationTicks <= 0L ||
                    lizard.BreedingCooldownTicks < 0L ||
                    lizard.Generation < 0)
                {
                    failures.Add("Breeding snapshot contains an invalid lizard.");
                    continue;
                }
                if (!ids.Add(lizard.Id))
                {
                    failures.Add($"Breeding snapshot duplicates ID '{lizard.Id}'.");
                }
            }
            foreach (var egg in snapshot.Eggs)
            {
                if (egg is null ||
                    string.IsNullOrWhiteSpace(egg.Id) ||
                    string.IsNullOrWhiteSpace(egg.HabitatId) ||
                    string.IsNullOrWhiteSpace(egg.FirstParentId) ||
                    string.IsNullOrWhiteSpace(egg.SecondParentId) ||
                    egg.Genome is null ||
                    egg.IncubationElapsedTicks < 0L ||
                    egg.RequiredIncubationTicks <= 0L ||
                    egg.IncubationElapsedTicks >= egg.RequiredIncubationTicks ||
                    egg.RequiredMaturationTicks <= 0L ||
                    egg.Generation <= 0)
                {
                    failures.Add("Breeding snapshot contains an invalid egg.");
                    continue;
                }
                if (!ids.Add(egg.Id))
                {
                    failures.Add($"Breeding snapshot duplicates ID '{egg.Id}'.");
                }
            }
        }

        return failures;
    }

    private BreedingActionResult Success(
        string message,
        string? lizardId = null,
        string? eggId = null) =>
        new(BreedingActionStatus.Success, message, _coins, lizardId, eggId);

    private BreedingActionResult Failure(
        BreedingActionStatus status,
        string message) =>
        new(status, message, _coins);

    private string NextId(string kind)
    {
        if (_nextSequence == long.MaxValue)
        {
            throw new InvalidOperationException(
                "The breeding identity sequence is exhausted.");
        }

        _nextSequence++;
        return $"{kind}-{_nextSequence:X12}-{_random.NextUInt64():X16}";
    }

    private bool HasIdentityCapacity(int requiredIds) =>
        requiredIds >= 0 &&
        _nextSequence <= long.MaxValue - (long)requiredIds;

    private string GenerateName() =>
        NamePrefixes[_random.NextInt(NamePrefixes.Length)] +
        NameSuffixes[_random.NextInt(NameSuffixes.Length)];

    private static long CalculateDurationTicks(TimeSpan baseDuration, double rate)
    {
        // Cooldown is the one lifecycle duration whose rules explicitly allow
        // zero. Preserve that opt-out exactly instead of rounding it up to one
        // tick while still enforcing positive egg/maturation durations.
        if (baseDuration == TimeSpan.Zero)
        {
            return 0L;
        }
        if (!double.IsFinite(rate) || rate <= 0d)
        {
            rate = 1d;
        }
        var scaled = baseDuration.Ticks / Math.Clamp(rate, 0.05d, 20d);
        if (!double.IsFinite(scaled) || scaled >= long.MaxValue)
        {
            return long.MaxValue;
        }

        return Math.Max(
            1L,
            (long)Math.Round(scaled, MidpointRounding.AwayFromZero));
    }

    private static double SaturatingMultiplyFiniteNonNegative(
        double left,
        double right)
    {
        if (!double.IsFinite(left) || left < 0d ||
            !double.IsFinite(right) || right < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(left),
                "Breeding multipliers must be finite and non-negative.");
        }
        if (left == 0d || right == 0d)
        {
            return 0d;
        }

        return left > double.MaxValue / right
            ? double.MaxValue
            : left * right;
    }

    private static long SaturatingAdd(long value, long increment) =>
        value > long.MaxValue - increment ? long.MaxValue : value + increment;

    private static bool TryNormalizeHabitat(
        string? habitatId,
        out string normalized)
    {
        normalized = habitatId?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= 128;
    }
}
