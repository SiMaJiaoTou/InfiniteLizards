using System.Collections.Immutable;
using InfiniteLizards.Gameplay.Genetics;

namespace InfiniteLizards.Gameplay.Breeding;

public enum BreedingLifeStage
{
    Egg,
    Juvenile,
    Mature
}

public static class LizardMarketPrices
{
    public const int Buy = 5;
    public const int Sell = 1;
}

/// <summary>
/// Species/world pacing. Individual incubation and maturation genes scale
/// these durations without changing the save format or market prices.
/// </summary>
public sealed record BreedingRules
{
    public TimeSpan BaseIncubationDuration { get; init; } = TimeSpan.FromMinutes(20);
    public TimeSpan BaseMaturationDuration { get; init; } = TimeSpan.FromHours(6);
    public TimeSpan BreedingCooldown { get; init; } = TimeSpan.FromMinutes(12);
    public double MutationMultiplier { get; init; } = 1d;
    public double BurstMutationMultiplier { get; init; } = 1d;

    public static BreedingRules Default { get; } = new();

    public IReadOnlyList<string> Validate()
    {
        var failures = new List<string>();
        if (BaseIncubationDuration <= TimeSpan.Zero)
        {
            failures.Add("Base incubation duration must be positive.");
        }
        if (BaseMaturationDuration <= TimeSpan.Zero)
        {
            failures.Add("Base maturation duration must be positive.");
        }
        if (BreedingCooldown < TimeSpan.Zero)
        {
            failures.Add("Breeding cooldown cannot be negative.");
        }
        if (!double.IsFinite(MutationMultiplier) || MutationMultiplier < 0d ||
            !double.IsFinite(BurstMutationMultiplier) ||
            BurstMutationMultiplier < 0d)
        {
            failures.Add("Mutation multipliers must be finite and non-negative.");
        }

        return failures;
    }

    internal void EnsureValid()
    {
        var failures = Validate();
        if (failures.Count > 0)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, failures));
        }
    }
}

public sealed record BreedingLizardSnapshot(
    string Id,
    string Name,
    LizardGenome Genome,
    long AgeTicks,
    long RequiredMaturationTicks,
    long BreedingCooldownTicks,
    string HabitatId,
    string? FirstParentId,
    string? SecondParentId,
    int Generation,
    bool WasMarketPurchased)
{
    public BreedingLifeStage LifeStage =>
        AgeTicks >= RequiredMaturationTicks
            ? BreedingLifeStage.Mature
            : BreedingLifeStage.Juvenile;

    public TimeSpan Age => TimeSpan.FromTicks(AgeTicks);
    public TimeSpan RequiredMaturation =>
        TimeSpan.FromTicks(RequiredMaturationTicks);
    public TimeSpan BreedingCooldown =>
        TimeSpan.FromTicks(BreedingCooldownTicks);
    public bool CanBreed =>
        LifeStage == BreedingLifeStage.Mature && BreedingCooldownTicks == 0L;
}

public sealed record BreedingEggSnapshot(
    string Id,
    LizardGenome Genome,
    long IncubationElapsedTicks,
    long RequiredIncubationTicks,
    long RequiredMaturationTicks,
    string HabitatId,
    string FirstParentId,
    string SecondParentId,
    int Generation)
{
    public BreedingLifeStage LifeStage => BreedingLifeStage.Egg;
    public TimeSpan IncubationElapsed =>
        TimeSpan.FromTicks(IncubationElapsedTicks);
    public TimeSpan RequiredIncubation =>
        TimeSpan.FromTicks(RequiredIncubationTicks);
}

/// <summary>
/// Complete persistence boundary. No live dictionaries, UI images, wall clock
/// timestamps or platform coordinates leak into a save.
/// </summary>
public sealed record BreedingWorldSnapshot(
    int SchemaVersion,
    string TraitRegistryId,
    BreedingRules Rules,
    int Coins,
    long NextSequence,
    DeterministicRandomState RandomState,
    ImmutableArray<BreedingLizardSnapshot> Lizards,
    ImmutableArray<BreedingEggSnapshot> Eggs)
{
    public const int CurrentSchemaVersion = 1;
}

public enum BreedingActionStatus
{
    Success,
    InvalidArgument,
    LizardNotFound,
    SameParent,
    JuvenileCannotBreed,
    DifferentHabitat,
    BreedingCooldown,
    InsufficientFunds,
    WouldStrandCollection,
    IdentitySequenceExhausted,
    EconomyLimitReached
}

public sealed record BreedingActionResult(
    BreedingActionStatus Status,
    string Message,
    int Coins,
    string? LizardId = null,
    string? EggId = null)
{
    public bool Succeeded => Status == BreedingActionStatus.Success;
}

public sealed record BreedingAdvanceResult(
    ImmutableArray<string> HatchedEggIds,
    ImmutableArray<string> HatchlingLizardIds,
    ImmutableArray<string> NewlyMaturedLizardIds)
{
    public static BreedingAdvanceResult Empty { get; } = new(
        ImmutableArray<string>.Empty,
        ImmutableArray<string>.Empty,
        ImmutableArray<string>.Empty);
}
