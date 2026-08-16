using System.Collections.Immutable;
using DesktopLizard.Core;

namespace DesktopLizard.Rendering;

/// <summary>
/// Portable representation of the action list exposed by the WPF debug strip.
/// The production view is intentionally not linked because it derives from WPF
/// controls; deriving the list from the engine enum keeps this adapter current.
/// </summary>
internal static class DebugStateControlsView
{
    internal static readonly ImmutableArray<AutonomousAction> Actions =
        Enum.GetValues<AutonomousAction>().ToImmutableArray();
}

/// <summary>
/// Portable representation of the two label helpers exercised by the lost-grip
/// diagnostic. The production WPF view delegates these helpers to the same
/// platform-neutral label catalog.
/// </summary>
internal static class DebugPanelView
{
    internal static string StateLabel(RoamingState state) =>
        DebugLabelCatalog.State(state);

    internal static string LostGripPhaseLabel(LostGripFallPhase phase) =>
        DebugLabelCatalog.LostGripPhase(phase);

    internal static string LostGripProgressLabel(
        LostGripFallPhase phase,
        LostGripCatchReason catchReason,
        float reachProgress,
        float regripProgress) => phase switch
        {
            LostGripFallPhase.Falling =>
                $"伸手{FormatPercent(reachProgress)}",
            LostGripFallPhase.Regripping
                when catchReason == LostGripCatchReason.SafetyForced =>
                $"安全恢复{FormatPercent(regripProgress)}",
            LostGripFallPhase.Regripping =>
                $"抓稳{FormatPercent(regripProgress)}",
            _ => "等待"
        };

    private static string FormatPercent(float value) =>
        (MathEx.Clamp01(float.IsFinite(value) ? value : 0f) * 100f)
        .ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "%";
}
