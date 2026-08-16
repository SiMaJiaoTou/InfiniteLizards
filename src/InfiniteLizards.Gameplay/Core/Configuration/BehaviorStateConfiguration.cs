using System.Text.Json.Serialization;

namespace DesktopLizard.Core;

internal sealed record SCurveConfiguration
{
    public float SettleDuration { get; init; } = 0.30f;
    public SCurveModeConfiguration Normal { get; init; } = new()
    {
        MinimumCycleCount = 2,
        MaximumCycleCount = 3,
        ExtraCycleProbability = 0.25f,
        MinimumCycleDuration = 5.6f,
        MaximumCycleDuration = 6.4f,
        MinimumAmplitude = 0.26f,
        MaximumAmplitude = 0.30f,
        SpeedFactor = 0.94f,
        SteeringResponse = 2.4f
    };
    public SCurveModeConfiguration Fast { get; init; } = new()
    {
        MinimumCycleCount = 2,
        MaximumCycleCount = 2,
        ExtraCycleProbability = 0f,
        MinimumCycleDuration = 3.0f,
        MaximumCycleDuration = 3.4f,
        MinimumAmplitude = 0.50f,
        MaximumAmplitude = 0.54f,
        SpeedFactor = 1f,
        SteeringResponse = 2.8f
    };

    internal void Validate(List<string> failures)
    {
        BehaviorConfiguration.RequireNonNegative(SettleDuration, "S-curve settle duration", failures);
        Normal.Validate("normal S-curve", failures);
        Fast.Validate("fast S-curve", failures);
    }
}

internal sealed record SCurveModeConfiguration
{
    public int MinimumCycleCount { get; init; }
    public int MaximumCycleCount { get; init; }
    public float ExtraCycleProbability { get; init; }
    public float MinimumCycleDuration { get; init; }
    public float MaximumCycleDuration { get; init; }
    public float MinimumAmplitude { get; init; }
    public float MaximumAmplitude { get; init; }
    public float SpeedFactor { get; init; }
    public float SteeringResponse { get; init; }

    public float MinimumDuration(float settleDuration) => MinimumCycleCount * MinimumCycleDuration + settleDuration;
    public float MaximumDuration(float settleDuration) => MaximumCycleCount * MaximumCycleDuration + settleDuration;

    internal void Validate(string name, List<string> failures)
    {
        if (MinimumCycleCount < 1 || MaximumCycleCount < MinimumCycleCount)
        {
            failures.Add($"Invalid {name} cycle count range.");
        }
        if (!float.IsFinite(ExtraCycleProbability) || ExtraCycleProbability < 0f || ExtraCycleProbability > 1f)
        {
            failures.Add($"Invalid {name} extra-cycle probability.");
        }
        BehaviorConfiguration.ValidateRange(MinimumCycleDuration, MaximumCycleDuration, 0.05f, $"{name} cycle duration", failures);
        BehaviorConfiguration.ValidateRange(MinimumAmplitude, MaximumAmplitude, 0f, $"{name} amplitude", failures);
        BehaviorConfiguration.RequirePositive(SpeedFactor, $"{name} speed factor", failures);
        BehaviorConfiguration.RequirePositive(SteeringResponse, $"{name} steering response", failures);
    }
}

internal sealed record FastForwardConfiguration
{
    public float MinimumDuration { get; init; } = 1.25f;
    public float MaximumDuration { get; init; } = 2.10f;
    public float MaximumHeadingJitter { get; init; } = 0.035f;

    internal void Validate(List<string> failures)
    {
        BehaviorConfiguration.ValidateRange(MinimumDuration, MaximumDuration, 0.05f, "fast-forward duration", failures);
        BehaviorConfiguration.RequireNonNegative(MaximumHeadingJitter, "fast-forward heading jitter", failures);
    }
}

internal sealed record RestConfiguration
{
    public RestBandConfiguration[] Bands { get; init; } =
    [
        new("短休", 0.30f, 1.0f, 2.8f),
        new("中休", 0.35f, 2.8f, 7.0f),
        new("长休", 0.25f, 7.0f, 13f),
        new("超长休", 0.10f, 13f, 65f)
    ];

    [JsonIgnore]
    public float MinimumDuration => Bands.Length == 0 ? 0f : Bands[0].MinimumDuration;

    [JsonIgnore]
    public float MaximumDuration => Bands.Length == 0 ? 0f : Bands[^1].MaximumDuration;

    internal void Validate(List<string> failures)
    {
        if (Bands.Length == 0)
        {
            failures.Add("rest duration distribution must contain at least one band.");
            return;
        }

        var probability = 0f;
        var previousMaximum = 0f;
        for (var index = 0; index < Bands.Length; index++)
        {
            var band = Bands[index];
            if (string.IsNullOrWhiteSpace(band.Name) ||
                !float.IsFinite(band.Weight) || band.Weight < 0f ||
                !float.IsFinite(band.MinimumDuration) || !float.IsFinite(band.MaximumDuration) ||
                band.MinimumDuration < 0.05f || band.MaximumDuration < band.MinimumDuration)
            {
                failures.Add($"Invalid rest band at index {index}.");
                continue;
            }
            if (index > 0 && MathF.Abs(band.MinimumDuration - previousMaximum) > 0.001f)
            {
                failures.Add($"Rest bands must be continuous at index {index}.");
            }
            probability += band.Weight;
            previousMaximum = band.MaximumDuration;
        }

        if (MathF.Abs(probability - 1f) > 0.0001f)
        {
            failures.Add($"Rest-band weights must sum to 1 (actual {probability}).");
        }
    }
}

internal sealed record RestBandConfiguration(
    string Name,
    float Weight,
    float MinimumDuration,
    float MaximumDuration);

internal sealed record TransitionMatrixConfiguration
{
    public TransitionRowConfiguration AfterForward { get; init; } = new();
    public TransitionRowConfiguration AfterCurve { get; init; } = new();
    public TransitionRowConfiguration AfterSCurve { get; init; } = new();
    public TransitionRowConfiguration AfterFast { get; init; } = new();

    public static TransitionMatrixConfiguration CreateDefault() => new()
    {
        AfterForward = new TransitionRowConfiguration
        {
            Entries =
            [
                new(AutonomousAction.SCurve, 0.40f),
                new(AutonomousAction.Curve, 0.20f),
                new(AutonomousAction.TurnAround, 0.08f),
                new(AutonomousAction.FastForward, 0.06f),
                new(AutonomousAction.FastSCurve, 0.04f),
                new(AutonomousAction.ForwardExtension, 0.20f),
                new(AutonomousAction.LostGripFall, 0.02f)
            ]
        },
        AfterCurve = new TransitionRowConfiguration
        {
            Entries =
            [
                new(AutonomousAction.Forward, 0.55f),
                new(AutonomousAction.SCurve, 0.27f),
                new(AutonomousAction.FastForward, 0.06f),
                new(AutonomousAction.FastSCurve, 0.04f),
                new(AutonomousAction.TurnAround, 0.06f),
                new(AutonomousAction.LostGripFall, 0.02f)
            ]
        },
        AfterSCurve = new TransitionRowConfiguration
        {
            Entries =
            [
                new(AutonomousAction.Forward, 0.63f),
                new(AutonomousAction.Curve, 0.19f),
                new(AutonomousAction.FastForward, 0.06f),
                new(AutonomousAction.FastSCurve, 0.04f),
                new(AutonomousAction.TurnAround, 0.06f),
                new(AutonomousAction.LostGripFall, 0.02f)
            ]
        },
        AfterFast = new TransitionRowConfiguration
        {
            Entries =
            [
                new(AutonomousAction.Forward, 0.68f),
                new(AutonomousAction.SCurve, 0.22f),
                new(AutonomousAction.Curve, 0.08f),
                new(AutonomousAction.LostGripFall, 0.02f)
            ]
        }
    };

    internal void Validate(List<string> failures)
    {
        AfterForward.Validate("AfterForward", failures);
        AfterCurve.Validate("AfterCurve", failures);
        AfterSCurve.Validate("AfterSCurve", failures);
        // The same row is consumed from both fast-forward and fast-S states.
        // Only their common legal destinations are valid; accepting another
        // fast state or TurnAround would make one source throw at runtime.
        AfterFast.Validate(
            "AfterFast",
            failures,
            AutonomousAction.Forward,
            AutonomousAction.ForwardExtension,
            AutonomousAction.Curve,
            AutonomousAction.SCurve,
            AutonomousAction.LostGripFall);
    }
}

internal sealed record TransitionRowConfiguration
{
    public WeightedTransitionConfiguration[] Entries { get; init; } = Array.Empty<WeightedTransitionConfiguration>();

    internal void Validate(
        string name,
        List<string> failures,
        params AutonomousAction[] allowedActions)
    {
        if (Entries is null)
        {
            failures.Add($"Transition row {name} entries cannot be null.");
            return;
        }
        if (Entries.Length == 0)
        {
            failures.Add($"Transition row {name} cannot be empty.");
            return;
        }

        var total = 0f;
        var seen = new HashSet<AutonomousAction>();
        foreach (var entry in Entries)
        {
            if (entry is null)
            {
                failures.Add($"Transition row {name} contains a null entry.");
                continue;
            }
            if (!Enum.IsDefined(entry.Action))
            {
                failures.Add($"Transition row {name} contains unknown action {(int)entry.Action}.");
                continue;
            }
            if (allowedActions.Length > 0 && !allowedActions.Contains(entry.Action))
            {
                failures.Add($"Transition row {name} does not allow action {entry.Action}.");
            }
            if (!seen.Add(entry.Action))
            {
                failures.Add($"Transition row {name} contains duplicate action {entry.Action}.");
            }
            if (!float.IsFinite(entry.Weight) || entry.Weight < 0f)
            {
                failures.Add($"Transition row {name} contains invalid weight for {entry.Action}.");
            }
            total += entry.Weight;
        }

        if (MathF.Abs(total - 1f) > 0.0001f)
        {
            failures.Add($"Transition row {name} weights must sum to 1 (actual {total}).");
        }
    }
}

internal sealed record WeightedTransitionConfiguration(AutonomousAction Action, float Weight);
