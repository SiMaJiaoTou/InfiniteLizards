using System.Numerics;

namespace InfiniteLizards.Desktop;

internal readonly record struct DebugPanelCoordinateMetrics(
    float PanelWidth,
    float PanelHeight,
    float Gap,
    float OwnerWidth,
    float OwnerHeight)
{
    public static DebugPanelCoordinateMetrics Calculate(
        double renderScaling,
        bool usesCocoaPoints,
        double ownerWidthDip,
        double ownerHeightDip,
        int panelWidthPixels = DiagnosticPanelWindow.PanelWidthPixels,
        int panelHeightPixels = DiagnosticPanelWindow.PanelHeightPixels,
        int panelGapPixels = DiagnosticPanelWindow.PanelGapPixels)
    {
        var scale = double.IsFinite(renderScaling)
            ? Math.Max(0.25d, renderScaling)
            : 1d;
        var coordinateScale = usesCocoaPoints ? 1d / scale : 1d;
        var ownerScale = usesCocoaPoints ? 1d : scale;
        return new DebugPanelCoordinateMetrics(
            (float)(panelWidthPixels * coordinateScale),
            (float)(panelHeightPixels * coordinateScale),
            (float)(panelGapPixels * coordinateScale),
            (float)(ownerWidthDip * ownerScale),
            (float)(ownerHeightDip * ownerScale));
    }
}

internal enum DebugPanelSide { None, Right, Left, Below, Above }

internal readonly record struct DebugPanelPlacementRect(
    float Left,
    float Top,
    float Right,
    float Bottom)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;
    public bool IsFinite =>
        float.IsFinite(Left) && float.IsFinite(Top) &&
        float.IsFinite(Right) && float.IsFinite(Bottom);
}

internal readonly record struct DebugPanelPlacementResult(
    bool IsVisible,
    DebugPanelSide Side,
    Vector2 Center)
{
    public static DebugPanelPlacementResult Hidden =>
        new(false, DebugPanelSide.None, default);
}

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
        if (!float.IsFinite(petCenter.X) || !float.IsFinite(petCenter.Y) ||
            !workArea.IsFinite || workArea.Width < 0f || workArea.Height < 0f ||
            !float.IsFinite(panelWidth) || panelWidth <= 0f ||
            !float.IsFinite(panelHeight) || panelHeight <= 0f ||
            !float.IsFinite(petRenderRadius) || petRenderRadius < 0f ||
            !float.IsFinite(gap) || gap < 0f)
        {
            return DebugPanelPlacementResult.Hidden;
        }

        var halfWidth = panelWidth * 0.5f;
        var halfHeight = panelHeight * 0.5f;
        var minX = workArea.Left + halfWidth;
        var maxX = workArea.Right - halfWidth;
        var minY = workArea.Top + halfHeight;
        var maxY = workArea.Bottom - halfHeight;
        if (minX > maxX || minY > maxY)
        {
            return DebugPanelPlacementResult.Hidden;
        }

        if (TryCandidate(currentSide, out var result) ||
            TryCandidate(DebugPanelSide.Right, out result) ||
            TryCandidate(DebugPanelSide.Left, out result) ||
            TryCandidate(DebugPanelSide.Below, out result) ||
            TryCandidate(DebugPanelSide.Above, out result))
        {
            return result;
        }
        return DebugPanelPlacementResult.Hidden;

        bool TryCandidate(DebugPanelSide side, out DebugPanelPlacementResult candidate)
        {
            candidate = DebugPanelPlacementResult.Hidden;
            if (side == DebugPanelSide.None)
            {
                return false;
            }
            var center = side switch
            {
                DebugPanelSide.Right => new Vector2(
                    petCenter.X + petRenderRadius + gap + halfWidth,
                    Math.Clamp(petCenter.Y, minY, maxY)),
                DebugPanelSide.Left => new Vector2(
                    petCenter.X - petRenderRadius - gap - halfWidth,
                    Math.Clamp(petCenter.Y, minY, maxY)),
                DebugPanelSide.Below => new Vector2(
                    Math.Clamp(petCenter.X, minX, maxX),
                    petCenter.Y + petRenderRadius + gap + halfHeight),
                DebugPanelSide.Above => new Vector2(
                    Math.Clamp(petCenter.X, minX, maxX),
                    petCenter.Y - petRenderRadius - gap - halfHeight),
                _ => default
            };
            if (center.X < minX || center.X > maxX ||
                center.Y < minY || center.Y > maxY)
            {
                return false;
            }
            candidate = new DebugPanelPlacementResult(true, side, center);
            return true;
        }
    }
}
