using System.Numerics;
using System.Windows.Media;
using DesktopLizard.Core;

namespace DesktopLizard.Rendering;

internal static class DebugOverlayRenderer
{
    private static readonly SolidColorBrush DebugPathBrush = FrozenBrush(Color.FromArgb(205, 73, 227, 234));
    private static readonly SolidColorBrush DebugSpineBrush = FrozenBrush(Color.FromRgb(255, 226, 87));
    private static readonly SolidColorBrush DebugPairZeroBrush = FrozenBrush(Color.FromRgb(255, 137, 70));
    private static readonly SolidColorBrush DebugPairOneBrush = FrozenBrush(Color.FromRgb(174, 122, 255));
    private static readonly SolidColorBrush DebugTargetBrush = FrozenBrush(Color.FromRgb(90, 236, 160));
    private static readonly SolidColorBrush DebugPointerBrush = FrozenBrush(Color.FromRgb(255, 100, 150));
    private static readonly Pen DebugPathPen = FrozenPen(DebugPathBrush, 2.4f);
    private static readonly Pen DebugSpinePen = FrozenPen(DebugSpineBrush, 2.2f);
    private static readonly Pen DebugPairZeroPen = FrozenPen(DebugPairZeroBrush, 2.2f);
    private static readonly Pen DebugPairOnePen = FrozenPen(DebugPairOneBrush, 2.2f);
    private static readonly Pen DebugPairZeroActivePen = FrozenPen(DebugPairZeroBrush, 4.2f);
    private static readonly Pen DebugPairOneActivePen = FrozenPen(DebugPairOneBrush, 4.2f);
    private static readonly Pen DebugTargetPen = FrozenDashedPen(DebugTargetBrush, 1.8f, 5f, 4f);
    private static readonly Pen DebugPointerPen = FrozenDashedPen(DebugPointerBrush, 1.8f, 3f, 3f);

    internal static void DrawDebugPath(DrawingContext drawingContext, DebugFrameSnapshot frame)
    {
        var trail = frame.TrailPoints;
        if (trail.Length > 1)
        {
            drawingContext.PushOpacity(0.82);
            try
            {
                for (var index = 1; index < trail.Length; index++)
                {
                    drawingContext.DrawLine(DebugPathPen, ToPoint(trail[index - 1]), ToPoint(trail[index]));
                }
            }
            finally
            {
                drawingContext.Pop();
            }
        }

        var center = frame.CanvasCenter;
        drawingContext.DrawLine(DebugTargetPen, ToPoint(center), ToPoint(frame.TargetPoint));
        drawingContext.DrawEllipse(null, DebugTargetPen, ToPoint(frame.TargetPoint), 7f, 7f);
        drawingContext.DrawLine(DebugTargetPen, ToPoint(center), ToPoint(frame.LookTargetPoint));

        if (frame.PointerAvailable)
        {
            drawingContext.DrawLine(DebugPointerPen, ToPoint(center), ToPoint(frame.MousePoint));
            drawingContext.DrawEllipse(null, DebugPointerPen, ToPoint(frame.MousePoint), 9f, 9f);
        }
    }

    internal static void DrawDebugSkeleton(DrawingContext drawingContext, DebugFrameSnapshot frame)
    {
        var lizard = frame.Lizard;
        var joints = lizard.SpineJoints;
        for (var index = 1; index < joints.Length; index++)
        {
            drawingContext.DrawLine(DebugSpinePen, ToPoint(joints[index - 1]), ToPoint(joints[index]));
        }

        for (var index = 0; index < joints.Length; index++)
        {
            var radius = index is 0 or 4 or 13 ? 4.2 : 2.8;
            drawingContext.DrawEllipse(DebugSpineBrush, null, ToPoint(joints[index]), radius, radius);
        }

        foreach (var leg in lizard.Legs)
        {
            var normalPen = leg.Pair == 0 ? DebugPairZeroPen : DebugPairOnePen;
            var activePen = leg.Pair == 0 ? DebugPairZeroActivePen : DebugPairOneActivePen;
            var pen = leg.IsStepping ? activePen : normalPen;
            var brush = leg.Pair == 0 ? DebugPairZeroBrush : DebugPairOneBrush;
            drawingContext.DrawLine(pen, ToPoint(leg.Shoulder), ToPoint(leg.Elbow));
            drawingContext.DrawLine(pen, ToPoint(leg.Elbow), ToPoint(leg.Foot));
            drawingContext.DrawEllipse(brush, null, ToPoint(leg.Shoulder), 3.6, 3.6);
            drawingContext.DrawEllipse(brush, null, ToPoint(leg.Elbow), 4.0, 4.0);
            drawingContext.DrawEllipse(brush, null, ToPoint(leg.Foot), 4.4, 4.4);

            if (leg.IsStepping)
            {
                drawingContext.PushOpacity(0.24);
                try
                {
                    drawingContext.DrawEllipse(
                        null,
                        normalPen,
                        ToPoint(leg.Shoulder),
                        leg.MaximumReach,
                        leg.MaximumReach);
                    drawingContext.DrawLine(normalPen, ToPoint(leg.Foot), ToPoint(leg.StepTo));
                    drawingContext.DrawEllipse(null, normalPen, ToPoint(leg.StepTo), 5.5, 5.5);
                }
                finally
                {
                    drawingContext.Pop();
                }
            }
        }

        if (joints.Length > 0)
        {
            var headingEnd = joints[0] + MathEx.FromAngle(lizard.Heading) * 44f;
            drawingContext.DrawLine(DebugTargetPen, ToPoint(joints[0]), ToPoint(headingEnd));
            drawingContext.DrawEllipse(DebugTargetBrush, null, ToPoint(headingEnd), 3.6, 3.6);
        }
    }

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }

    private static Pen FrozenDashedPen(
        Brush brush,
        double thickness,
        double dash,
        double gap)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
            DashStyle = new DashStyle(new[] { dash, gap }, 0d)
        };
        pen.Freeze();
        return pen;
    }

    private static System.Windows.Point ToPoint(Vector2 point) => new(point.X, point.Y);
}
