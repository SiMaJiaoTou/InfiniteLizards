using InfiniteLizards.Gameplay.Genetics;

namespace InfiniteLizards.Gameplay.Breeding;

/// <summary>
/// Pure, bounded lifecycle projection for heritable traits. Keeping these
/// calculations free of RNG and mutation makes parent order irrelevant and
/// lets failed breeding commands remain transactionally inert.
/// </summary>
internal static class BreedingTraitEffectResolver
{
    internal static double ResolveMaturationRate(LizardPhenotype phenotype)
    {
        ArgumentNullException.ThrowIfNull(phenotype);
        var configuredRate = Value(
            phenotype,
            DefaultLizardTraitIds.MaturationRate,
            1d);
        var vitality = Normalized(phenotype, "lifecycle.vitality");
        var metabolism = Normalized(phenotype, "lifecycle.metabolism");
        return BoundedRate(
            configuredRate *
            Lerp(0.82d, 1.22d, vitality) *
            Lerp(0.78d, 1.28d, metabolism));
    }

    internal static double ResolveIncubationRate(
        LizardPhenotype offspring,
        LizardPhenotype firstParent,
        LizardPhenotype secondParent)
    {
        ArgumentNullException.ThrowIfNull(offspring);
        ArgumentNullException.ThrowIfNull(firstParent);
        ArgumentNullException.ThrowIfNull(secondParent);
        var configuredRate = Value(
            offspring,
            DefaultLizardTraitIds.IncubationRate,
            1d);
        var metabolism = Normalized(offspring, "lifecycle.metabolism");
        var guarding = Average(
            Normalized(firstParent, "behavior.transition.egg-guarding"),
            Normalized(secondParent, "behavior.transition.egg-guarding"));
        return BoundedRate(
            configuredRate *
            Lerp(0.78d, 1.28d, metabolism) *
            Lerp(0.82d, 1.22d, guarding));
    }

    internal static double ResolveBreedingCooldownRate(
        LizardPhenotype parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var sociability = Normalized(parent, "temperament.sociability");
        var territoriality = Normalized(parent, "temperament.territoriality");
        var socialApproach = Normalized(
            parent,
            "behavior.transition.social-approach");
        var courtship = Normalized(parent, "behavior.transition.courtship");
        return BoundedRate(
            Lerp(0.76d, 1.34d, sociability) *
            Lerp(1.24d, 0.70d, territoriality) *
            Lerp(0.82d, 1.24d, socialApproach) *
            Lerp(0.80d, 1.30d, courtship));
    }

    private static double Normalized(
        LizardPhenotype phenotype,
        string traitId) =>
        phenotype.TryGetTrait(traitId, out var trait) &&
        double.IsFinite(trait.NormalizedValue)
            ? Math.Clamp(trait.NormalizedValue, 0d, 1d)
            : 0.5d;

    private static double Value(
        LizardPhenotype phenotype,
        string traitId,
        double fallback) =>
        phenotype.TryGetTrait(traitId, out var trait) &&
        double.IsFinite(trait.Value) && trait.Value > 0d
            ? trait.Value
            : fallback;

    private static double Average(double first, double second) =>
        (first + second) * 0.5d;

    private static double BoundedRate(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, 0.05d, 20d)
            : 1d;

    private static double Lerp(double from, double to, double amount) =>
        from + (to - from) * Math.Clamp(amount, 0d, 1d);
}
