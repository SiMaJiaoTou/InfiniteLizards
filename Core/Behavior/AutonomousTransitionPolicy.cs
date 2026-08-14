namespace DesktopLizard.Core;

internal enum AutonomousAction
{
    Forward,
    ForwardExtension,
    Curve,
    SCurve,
    FastForward,
    FastSCurve,
    TurnAround,
    LostGripFall
}

/// <summary>
/// Pure mapping from one unit-interval sample to the next autonomous action.
/// Random sampling remains owned by BehaviorController so extraction cannot
/// change the shared seeded random sequence.
/// </summary>
internal static class AutonomousTransitionPolicy
{
    private static readonly TransitionMatrixConfiguration DefaultConfiguration =
        LizardProfile.Default.Behavior.TransitionMatrix;

    static AutonomousTransitionPolicy()
    {
        if (!ValidateConfiguration(DefaultConfiguration, out var failure))
        {
            throw new InvalidOperationException(failure);
        }
    }

    public static AutonomousAction ChooseAfterForward(float sample, bool forwardExtended) =>
        ChooseAfterForward(sample, forwardExtended, DefaultConfiguration);

    public static AutonomousAction ChooseAfterForward(
        float sample,
        bool forwardExtended,
        TransitionMatrixConfiguration configuration) =>
        Choose(configuration.AfterForward, sample, forwardExtended);

    public static AutonomousAction ChooseAfterCurve(float sample) =>
        ChooseAfterCurve(sample, DefaultConfiguration);

    public static AutonomousAction ChooseAfterCurve(
        float sample,
        TransitionMatrixConfiguration configuration) =>
        Choose(configuration.AfterCurve, sample, forwardExtended: false);

    public static AutonomousAction ChooseAfterSCurve(float sample) =>
        ChooseAfterSCurve(sample, DefaultConfiguration);

    public static AutonomousAction ChooseAfterSCurve(
        float sample,
        TransitionMatrixConfiguration configuration) =>
        Choose(configuration.AfterSCurve, sample, forwardExtended: false);

    public static AutonomousAction ChooseAfterFast(float sample) =>
        ChooseAfterFast(sample, DefaultConfiguration);

    public static AutonomousAction ChooseAfterFast(
        float sample,
        TransitionMatrixConfiguration configuration) =>
        Choose(configuration.AfterFast, sample, forwardExtended: false);

    public static bool ValidateConfiguration(out string failure) =>
        ValidateConfiguration(DefaultConfiguration, out failure);

    public static bool ValidateConfiguration(
        TransitionMatrixConfiguration configuration,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var failures = new List<string>();
        configuration.Validate(failures);
        if (failures.Count > 0)
        {
            failure = string.Join(" ", failures);
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static AutonomousAction Choose(
        TransitionRowConfiguration row,
        float sample,
        bool forwardExtended)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Entries.Length == 0)
        {
            throw new InvalidOperationException("An autonomous transition row cannot be empty.");
        }

        sample = MathEx.Clamp01(sample);
        var cumulativeWeight = 0f;
        for (var index = 0; index < row.Entries.Length; index++)
        {
            var entry = row.Entries[index];
            cumulativeWeight += entry.Weight;
            // Configuration weights are human-authored decimal probabilities.
            // Canonicalizing the cumulative boundary removes one-ULP artifacts
            // such as 0.55f + 0.27f becoming greater than the authored 0.82f.
            // It also preserves the historical exact-boundary semantics where
            // a sample equal to a bucket edge selects the following bucket.
            var boundary = MathF.Round(cumulativeWeight, 6);
            if (sample < boundary || index == row.Entries.Length - 1)
            {
                // Only one consecutive Forward extension is allowed. When a
                // configured extension bucket is selected again, redirect the
                // same sample to S-curve without consuming more randomness.
                return forwardExtended && entry.Action == AutonomousAction.ForwardExtension
                    ? AutonomousAction.SCurve
                    : entry.Action;
            }
        }

        throw new InvalidOperationException("Unable to select an autonomous transition.");
    }
}
