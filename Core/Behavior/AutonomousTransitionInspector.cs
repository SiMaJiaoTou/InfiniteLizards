namespace DesktopLizard.Core;

/// <summary>
/// Identifies the configured transition row that will be sampled when the
/// current autonomous state completes.
/// </summary>
internal enum AutonomousTransitionRow
{
    None,
    AfterForward,
    AfterCurve,
    AfterSCurve,
    AfterFast
}

/// <summary>
/// Read-only companion to <see cref="AutonomousTransitionPolicy"/>. Debug UI
/// uses the same rounded bucket boundaries as the production selector, so a
/// displayed probability cannot drift from the behavior that will be sampled.
/// </summary>
internal static class AutonomousTransitionInspector
{
    public static AutonomousTransitionRow RowFor(RoamingState state) => state switch
    {
        RoamingState.ForwardCrawl => AutonomousTransitionRow.AfterForward,
        RoamingState.CurveCrawl => AutonomousTransitionRow.AfterCurve,
        RoamingState.SCurveCrawl => AutonomousTransitionRow.AfterSCurve,
        RoamingState.FastForwardCrawl or RoamingState.FastSCurveCrawl =>
            AutonomousTransitionRow.AfterFast,
        _ => AutonomousTransitionRow.None
    };

    public static float EffectiveProbability(
        TransitionMatrixConfiguration configuration,
        AutonomousTransitionRow row,
        AutonomousAction requestedAction,
        bool forwardExtended)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var transitionRow = GetRow(configuration, row);
        if (transitionRow is null)
        {
            return float.NaN;
        }

        var probability = 0f;
        var previousBoundary = 0f;
        var cumulativeWeight = 0f;
        for (var index = 0; index < transitionRow.Entries.Length; index++)
        {
            var entry = transitionRow.Entries[index];
            cumulativeWeight += entry.Weight;
            var boundary = index == transitionRow.Entries.Length - 1
                ? 1f
                : MathF.Round(cumulativeWeight, 6);
            var bucketProbability = Math.Max(0f, boundary - previousBoundary);
            previousBoundary = boundary;

            var effectiveAction =
                forwardExtended && entry.Action == AutonomousAction.ForwardExtension
                    ? AutonomousAction.SCurve
                    : entry.Action;
            if (effectiveAction == requestedAction)
            {
                probability += bucketProbability;
            }
        }

        return MathEx.Clamp01(probability);
    }

    private static TransitionRowConfiguration? GetRow(
        TransitionMatrixConfiguration configuration,
        AutonomousTransitionRow row) => row switch
    {
        AutonomousTransitionRow.AfterForward => configuration.AfterForward,
        AutonomousTransitionRow.AfterCurve => configuration.AfterCurve,
        AutonomousTransitionRow.AfterSCurve => configuration.AfterSCurve,
        AutonomousTransitionRow.AfterFast => configuration.AfterFast,
        _ => null
    };
}
