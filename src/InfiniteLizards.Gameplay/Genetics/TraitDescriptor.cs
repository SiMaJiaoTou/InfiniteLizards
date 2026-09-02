using System.Collections.Immutable;
using System.Globalization;

namespace InfiniteLizards.Gameplay.Genetics;

/// <summary>
/// Player-facing groups used by the detail panel and future collection
/// filters. These are domain concepts, not layout or renderer concepts.
/// </summary>
public enum TraitCategory
{
    Pigmentation,
    Pattern,
    Body,
    Head,
    Limbs,
    Skin,
    Appendages,
    Tail,
    Locomotion,
    Temperament,
    PointerResponse,
    BehaviorTransitions,
    Lifecycle
}

public enum TraitValueKind
{
    Continuous,
    Integer,
    Toggle,
    Choice,
    Hue
}

public enum AlleleExpressionMode
{
    Blend,
    DominantHigh,
    DominantLow,
    HeterozygousBoost
}

public enum TraitActivationComparison
{
    AtLeast,
    AtMost
}

/// <summary>
/// Makes a trait visibly expressed only when another expressed trait passes a
/// threshold. The hidden allele remains in the genome and can reappear in a
/// later generation, enabling recessive-looking surprises such as a tail club
/// returning in grandchildren.
/// </summary>
public readonly record struct TraitActivationCondition(
    string TraitId,
    TraitActivationComparison Comparison,
    double Threshold)
{
    internal bool IsSatisfied(double value) => Comparison switch
    {
        TraitActivationComparison.AtLeast => value >= Threshold,
        TraitActivationComparison.AtMost => value <= Threshold,
        _ => false
    };
}

/// <summary>
/// Data-driven definition of one heritable axis. All genes store normalized
/// double alleles; this descriptor controls inheritance expression, mutation,
/// quantization, prerequisites and player-visible formatting.
/// </summary>
public sealed record TraitDescriptor
{
    public string Id { get; init; }
    public string DisplayName { get; init; }
    public string Description { get; init; }
    public TraitCategory Category { get; init; }
    public TraitValueKind ValueKind { get; init; }
    public double Minimum { get; init; }
    public double Maximum { get; init; }
    public double DefaultNormalized { get; init; }
    public double FounderVariation { get; init; }
    public AlleleExpressionMode ExpressionMode { get; init; }
    public double MutationRate { get; init; }
    public double MutationScale { get; init; }
    public double MutationBurstChance { get; init; }
    public double MutationBurstScale { get; init; }
    public string Unit { get; init; }
    public ImmutableArray<string> ChoiceLabels { get; init; }
    public ImmutableArray<TraitActivationCondition> ActivationConditions { get; init; }
    public bool PlayerVisible { get; init; }

    public TraitDescriptor(
        string id,
        string displayName,
        string description,
        TraitCategory category,
        TraitValueKind valueKind,
        double minimum = 0d,
        double maximum = 1d,
        double defaultNormalized = 0.5d,
        double founderVariation = 0.38d,
        AlleleExpressionMode expressionMode = AlleleExpressionMode.Blend,
        double mutationRate = 0.08d,
        double mutationScale = 0.09d,
        double mutationBurstChance = 0.012d,
        double mutationBurstScale = 0.42d,
        string unit = "",
        ImmutableArray<string> choiceLabels = default,
        ImmutableArray<TraitActivationCondition> activationConditions = default,
        bool playerVisible = true)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Category = category;
        ValueKind = valueKind;
        Minimum = minimum;
        Maximum = maximum;
        DefaultNormalized = defaultNormalized;
        FounderVariation = founderVariation;
        ExpressionMode = expressionMode;
        MutationRate = mutationRate;
        MutationScale = mutationScale;
        MutationBurstChance = mutationBurstChance;
        MutationBurstScale = mutationBurstScale;
        Unit = unit;
        ChoiceLabels = choiceLabels.IsDefault
            ? ImmutableArray<string>.Empty
            : choiceLabels;
        ActivationConditions = activationConditions.IsDefault
            ? ImmutableArray<TraitActivationCondition>.Empty
            : activationConditions;
        PlayerVisible = playerVisible;
    }

    internal double ExpressNormalized(double firstAllele, double secondAllele)
    {
        firstAllele = ClampNormalized(firstAllele);
        secondAllele = ClampNormalized(secondAllele);
        if (ValueKind == TraitValueKind.Hue &&
            ExpressionMode is AlleleExpressionMode.Blend or
                AlleleExpressionMode.HeterozygousBoost)
        {
            return CircularMean(firstAllele, secondAllele);
        }

        var expressed = ExpressionMode switch
        {
            AlleleExpressionMode.Blend => (firstAllele + secondAllele) * 0.5d,
            AlleleExpressionMode.DominantHigh => Math.Max(firstAllele, secondAllele),
            AlleleExpressionMode.DominantLow => Math.Min(firstAllele, secondAllele),
            AlleleExpressionMode.HeterozygousBoost =>
                (firstAllele + secondAllele) * 0.5d +
                Math.Abs(firstAllele - secondAllele) * 0.25d,
            _ => (firstAllele + secondAllele) * 0.5d
        };
        return ClampNormalized(expressed);
    }

    internal double Decode(double normalized)
    {
        normalized = ClampNormalized(normalized);
        return ValueKind switch
        {
            TraitValueKind.Toggle => normalized >= 0.5d ? 1d : 0d,
            TraitValueKind.Integer => Math.Round(
                Minimum + (Maximum - Minimum) * normalized,
                MidpointRounding.AwayFromZero),
            TraitValueKind.Choice => ChoiceLabels.Length <= 1
                ? 0d
                : Math.Min(
                    ChoiceLabels.Length - 1,
                    Math.Floor(normalized * ChoiceLabels.Length)),
            TraitValueKind.Hue => Wrap(
                Minimum + (Maximum - Minimum) * normalized,
                Minimum,
                Maximum),
            _ => Minimum + (Maximum - Minimum) * normalized
        };
    }

    internal string Format(double value)
    {
        return ValueKind switch
        {
            TraitValueKind.Toggle => value >= 0.5d ? "有" : "无",
            TraitValueKind.Choice => ChoiceLabels.Length == 0
                ? ((int)value).ToString(CultureInfo.InvariantCulture)
                : ChoiceLabels[Math.Clamp((int)value, 0, ChoiceLabels.Length - 1)],
            TraitValueKind.Integer =>
                value.ToString("0", CultureInfo.InvariantCulture) + Unit,
            TraitValueKind.Hue =>
                value.ToString("0", CultureInfo.InvariantCulture) + "°",
            _ => value.ToString("0.##", CultureInfo.InvariantCulture) + Unit
        };
    }

    internal IReadOnlyList<string> Validate(ISet<string>? knownTraitIds = null)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(Id) ||
            Id.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            failures.Add("Trait IDs must be non-empty ASCII identifiers.");
        }
        if (string.IsNullOrWhiteSpace(DisplayName) ||
            string.IsNullOrWhiteSpace(Description))
        {
            failures.Add($"Trait {Id} needs a player-visible name and description.");
        }
        if (!double.IsFinite(Minimum) ||
            !double.IsFinite(Maximum) ||
            Maximum <= Minimum)
        {
            failures.Add($"Trait {Id} must have a finite increasing range.");
        }
        if (!IsNormalized(DefaultNormalized) ||
            !double.IsFinite(FounderVariation) || FounderVariation < 0d ||
            !IsProbability(MutationRate) ||
            !IsProbability(MutationBurstChance) ||
            !double.IsFinite(MutationScale) || MutationScale < 0d ||
            !double.IsFinite(MutationBurstScale) || MutationBurstScale < 0d)
        {
            failures.Add($"Trait {Id} has invalid normalized mutation settings.");
        }
        if (ValueKind == TraitValueKind.Choice && ChoiceLabels.Length < 2)
        {
            failures.Add($"Choice trait {Id} needs at least two labels.");
        }
        if (ChoiceLabels.Any(string.IsNullOrWhiteSpace) ||
            ChoiceLabels.Distinct(StringComparer.Ordinal).Count() != ChoiceLabels.Length)
        {
            failures.Add($"Trait {Id} choice labels must be unique and non-empty.");
        }
        foreach (var condition in ActivationConditions)
        {
            if (string.IsNullOrWhiteSpace(condition.TraitId) ||
                !double.IsFinite(condition.Threshold))
            {
                failures.Add($"Trait {Id} has an invalid activation condition.");
            }
            else if (knownTraitIds is not null &&
                     !knownTraitIds.Contains(condition.TraitId))
            {
                failures.Add(
                    $"Trait {Id} depends on unknown trait {condition.TraitId}.");
            }
        }

        return failures;
    }

    internal static double ClampNormalized(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : 0.5d;

    private static bool IsNormalized(double value) =>
        double.IsFinite(value) && value is >= 0d and <= 1d;

    private static bool IsProbability(double value) => IsNormalized(value);

    private static double Wrap(double value, double minimum, double maximum)
    {
        var width = maximum - minimum;
        if (!double.IsFinite(value) || width <= 0d)
        {
            return minimum;
        }

        var wrapped = (value - minimum) % width;
        if (wrapped < 0d)
        {
            wrapped += width;
        }

        return minimum + wrapped;
    }

    private static double CircularMean(double first, double second)
    {
        var firstAngle = first * Math.Tau;
        var secondAngle = second * Math.Tau;
        var x = Math.Cos(firstAngle) + Math.Cos(secondAngle);
        var y = Math.Sin(firstAngle) + Math.Sin(secondAngle);
        if (Math.Abs(x) + Math.Abs(y) <= 0.000000000001d)
        {
            // Antipodal hues have no unique mean. Picking the lower encoded
            // allele gives a deterministic answer independent of argument
            // order and avoids platform-dependent atan2 noise.
            return Math.Min(first, second);
        }

        var angle = Math.Atan2(y, x);
        if (angle < 0d)
        {
            angle += Math.Tau;
        }

        return angle / Math.Tau;
    }
}
