using System.Numerics;

namespace DesktopLizard.Windows;

internal enum DebugPanelSide
{
    None,
    Right,
    Left,
    Below,
    Above
}

/// <summary>
/// Lightweight physical-pixel rectangle used by debug-panel placement. It is
/// intentionally independent of WPF and Win32 rectangle types.
/// </summary>
internal readonly record struct DebugPanelPlacementRect(
    float Left,
    float Top,
    float Right,
    float Bottom)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;

    public bool IsFinite =>
        float.IsFinite(Left) &&
        float.IsFinite(Top) &&
        float.IsFinite(Right) &&
        float.IsFinite(Bottom);

    public bool Contains(DebugPanelPlacementRect other) =>
        other.Left >= Left &&
        other.Top >= Top &&
        other.Right <= Right &&
        other.Bottom <= Bottom;

    public bool Intersects(DebugPanelPlacementRect other) =>
        Left < other.Right &&
        Right > other.Left &&
        Top < other.Bottom &&
        Bottom > other.Top;

    public static DebugPanelPlacementRect FromCenter(
        Vector2 center,
        float width,
        float height)
    {
        var halfWidth = width * 0.5f;
        var halfHeight = height * 0.5f;
        return new DebugPanelPlacementRect(
            center.X - halfWidth,
            center.Y - halfHeight,
            center.X + halfWidth,
            center.Y + halfHeight);
    }
}

internal readonly record struct DebugPanelPlacementResult(
    bool IsVisible,
    DebugPanelSide Side,
    Vector2 Center)
{
    public static DebugPanelPlacementResult Hidden =>
        new(false, DebugPanelSide.None, default);
}

/// <summary>
/// Pure physical-pixel layout policy for the debug sidecar. Candidate order is
/// stable, but a still-valid current side wins to prevent needless jumping.
/// </summary>
internal static class DebugPanelPlacementCalculator
{
    public static DebugPanelPlacementResult Calculate(
        Vector2 petCenter,
        DebugPanelPlacementRect workArea,
        float panelWidth,
        float panelHeight,
        float petRenderRadius,
        float gap,
        DebugPanelSide currentSide)
    {
        if (!IsFinite(petCenter) ||
            !workArea.IsFinite ||
            workArea.Width < 0f ||
            workArea.Height < 0f ||
            !float.IsFinite(panelWidth) ||
            panelWidth <= 0f ||
            !float.IsFinite(panelHeight) ||
            panelHeight <= 0f ||
            !float.IsFinite(petRenderRadius) ||
            petRenderRadius < 0f ||
            !float.IsFinite(gap) ||
            gap < 0f)
        {
            return DebugPanelPlacementResult.Hidden;
        }

        var halfWidth = panelWidth * 0.5f;
        var halfHeight = panelHeight * 0.5f;
        var minCenterX = workArea.Left + halfWidth;
        var maxCenterX = workArea.Right - halfWidth;
        var minCenterY = workArea.Top + halfHeight;
        var maxCenterY = workArea.Bottom - halfHeight;
        if (minCenterX > maxCenterX || minCenterY > maxCenterY)
        {
            return DebugPanelPlacementResult.Hidden;
        }

        if (TryGetCandidate(currentSide, out var placement) ||
            TryGetCandidate(DebugPanelSide.Right, out placement) ||
            TryGetCandidate(DebugPanelSide.Left, out placement) ||
            TryGetCandidate(DebugPanelSide.Below, out placement) ||
            TryGetCandidate(DebugPanelSide.Above, out placement))
        {
            return placement;
        }

        // On a physically tiny work area there may be no rectangle that can
        // contain both surfaces. Hiding is preferable to covering the animal.
        return DebugPanelPlacementResult.Hidden;

        bool TryGetCandidate(
            DebugPanelSide side,
            out DebugPanelPlacementResult result)
        {
            result = DebugPanelPlacementResult.Hidden;
            if (side == DebugPanelSide.None)
            {
                return false;
            }

            var centeredY = Math.Clamp(petCenter.Y, minCenterY, maxCenterY);
            var centeredX = Math.Clamp(petCenter.X, minCenterX, maxCenterX);
            var center = side switch
            {
                DebugPanelSide.Right => new Vector2(
                    petCenter.X + petRenderRadius + gap + halfWidth,
                    centeredY),
                DebugPanelSide.Left => new Vector2(
                    petCenter.X - petRenderRadius - gap - halfWidth,
                    centeredY),
                DebugPanelSide.Below => new Vector2(
                    centeredX,
                    petCenter.Y + petRenderRadius + gap + halfHeight),
                DebugPanelSide.Above => new Vector2(
                    centeredX,
                    petCenter.Y - petRenderRadius - gap - halfHeight),
                _ => default
            };
            if (center.X < minCenterX || center.X > maxCenterX ||
                center.Y < minCenterY || center.Y > maxCenterY)
            {
                return false;
            }

            result = new DebugPanelPlacementResult(true, side, center);
            return true;
        }
    }

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);
}
