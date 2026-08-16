using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// Pure boundary geometry used by every locomotion state. It deliberately
/// does not consume randomness or perform state transitions, so safety policy
/// can be tested and tuned independently from the roaming FSM.
/// </summary>
internal sealed class BoundaryNavigator
{
    private readonly BoundaryNavigationConfiguration _configuration;

    public BoundaryNavigator(BoundaryNavigationConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsNearEdge(FloatRect area, Vector2 point, float margin) =>
        point.X - area.Left < margin || area.Right - point.X < margin ||
        point.Y - area.Top < margin || area.Bottom - point.Y < margin;

    public float EdgeClearance(FloatRect area, Vector2 point) => Math.Min(
        Math.Min(point.X - area.Left, area.Right - point.X),
        Math.Min(point.Y - area.Top, area.Bottom - point.Y));

    public float MinimumClearance(FloatRect area, Vector2 point) => EdgeClearance(area, point);

    public (float Positive, float Negative) GetSideClearances(
        FloatRect area,
        Vector2 position,
        float heading)
    {
        var distance = _configuration.SideProbeDistance;
        var positiveProbe = position + MathEx.FromAngle(heading + MathF.PI * 0.5f) * distance;
        var negativeProbe = position + MathEx.FromAngle(heading - MathF.PI * 0.5f) * distance;
        return (EdgeClearance(area, positiveProbe), EdgeClearance(area, negativeProbe));
    }

    public bool TryPickSaferTurnSign(
        FloatRect area,
        Vector2 position,
        float heading,
        out float sign)
    {
        var clearances = GetSideClearances(area, position, heading);
        if (MathF.Abs(clearances.Positive - clearances.Negative) <=
            _configuration.SidePreferenceThreshold)
        {
            sign = 0f;
            return false;
        }

        sign = clearances.Positive > clearances.Negative ? 1f : -1f;
        return true;
    }

    public FloatRect SafeInset(FloatRect area, float requestedInset)
    {
        var maximumInset = Math.Max(0f, Math.Min(area.Width, area.Height) * 0.5f - 1f);
        return area.Inset(Math.Min(requestedInset, maximumInset));
    }

    /// <summary>
    /// Creates the requested inset without silently reducing it. The fallback
    /// area is still the largest usable inset so an already-running recovery
    /// can clamp to the best physically possible position after a display or
    /// taskbar change.
    /// </summary>
    public bool TryCreateExactInset(
        FloatRect area,
        float requestedInset,
        out FloatRect insetArea)
    {
        var requiredSpan = requestedInset * 2f;
        if (float.IsFinite(requestedInset) &&
            requestedInset >= 0f &&
            float.IsFinite(area.Width) &&
            float.IsFinite(area.Height) &&
            area.Width >= requiredSpan &&
            area.Height >= requiredSpan)
        {
            insetArea = area.Inset(requestedInset);
            return true;
        }

        insetArea = SafeInset(area, Math.Max(0f, requestedInset));
        return false;
    }
}
