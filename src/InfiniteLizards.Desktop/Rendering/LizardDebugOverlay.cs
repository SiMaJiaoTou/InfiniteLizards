using System.Collections.Immutable;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using InfiniteLizards.Gameplay;

namespace InfiniteLizards.Desktop.Rendering;

internal readonly record struct LizardDebugOverlayLeg(
    int Pair,
    Vector2 Shoulder,
    Vector2 Elbow,
    Vector2 Foot,
    Vector2 StepTo,
    bool IsStepping,
    float MaximumReach);

internal sealed record LizardDebugOverlayFrame(
    Vector2 CanvasCenter,
    ImmutableArray<Vector2> TrailPoints,
    Vector2 TargetPoint,
    Vector2 LookTargetPoint,
    Vector2 PointerPoint,
    bool PointerAvailable,
    ImmutableArray<Vector2> SpineJoints,
    ImmutableArray<LizardDebugOverlayLeg> Legs,
    float Heading);

internal sealed class LizardDebugOverlayCoordinator
{
    private readonly PortableDebugPathPolicy _policy;
    private readonly Vector2 _canvasCenter;
    private readonly float _modelToWorldScale;
    private readonly Queue<Vector2> _worldPath;
    private float _sampleTimer;
    private int _simulationReset = int.MinValue;
    private int _pathReset = int.MinValue;
    private Vector2 _cumulativeRebase;

    public LizardDebugOverlayCoordinator(
        PortableDebugPathPolicy policy,
        Vector2 canvasCenter,
        float modelToWorldScale)
    {
        _policy = policy;
        _canvasCenter = canvasCenter;
        _modelToWorldScale = Math.Max(0.0001f, modelToWorldScale);
        _worldPath = new Queue<Vector2>(Math.Max(2, policy.Capacity));
    }

    public LizardDebugOverlayFrame Capture(
        PortableLizardDebugSnapshot frame,
        DesktopPetDiagnosticSnapshot host)
    {
        if (frame.SimulationResetGeneration != _simulationReset ||
            frame.PathResetGeneration != _pathReset)
        {
            _worldPath.Clear();
            _sampleTimer = 0f;
            _simulationReset = frame.SimulationResetGeneration;
            _pathReset = frame.PathResetGeneration;
            _cumulativeRebase = frame.CumulativeWorldRebase;
        }
        else
        {
            TranslatePath(frame.CumulativeWorldRebase - _cumulativeRebase);
            _cumulativeRebase = frame.CumulativeWorldRebase;
        }

        _sampleTimer += host.FrameDelta;
        var shouldSample = _worldPath.Count == 0 ||
            (_sampleTimer >= _policy.SampleInterval &&
             Vector2.DistanceSquared(_worldPath.Last(), frame.Position) >=
             _policy.MinimumDistance * _policy.MinimumDistance);
        if (shouldSample)
        {
            _sampleTimer = 0f;
            _worldPath.Enqueue(frame.Position);
            while (_worldPath.Count > _policy.Capacity)
            {
                _worldPath.Dequeue();
            }
        }

        Vector2 ToModel(Vector2 world) =>
            _canvasCenter + (world - frame.Position) / _modelToWorldScale;
        var legs = frame.Legs.Select(leg => new LizardDebugOverlayLeg(
            leg.Pair, leg.Shoulder, leg.Elbow, leg.Foot, leg.StepTo,
            leg.IsStepping, leg.MaximumReach)).ToImmutableArray();
        return new LizardDebugOverlayFrame(
            _canvasCenter,
            _worldPath.Select(ToModel).ToImmutableArray(),
            ToModel(frame.Target),
            ToModel(frame.LookTarget),
            ToModel(host.PointerAvailable ? host.PointerPosition : frame.Position),
            host.PointerAvailable,
            frame.SpineJoints,
            legs,
            frame.LizardHeading);
    }

    private void TranslatePath(Vector2 delta)
    {
        if (delta == Vector2.Zero || _worldPath.Count == 0)
        {
            return;
        }
        var translated = _worldPath.Select(point => point + delta).ToArray();
        _worldPath.Clear();
        foreach (var point in translated)
        {
            _worldPath.Enqueue(point);
        }
    }
}

internal static class LizardDebugOverlayRenderer
{
    private static readonly Pen PathPen = Pen(Color.FromArgb(205, 73, 227, 234), 2.4);
    private static readonly Pen SpinePen = Pen(Color.FromRgb(255, 226, 87), 2.2);
    private static readonly IBrush SpineBrush = Brush(Color.FromRgb(255, 226, 87));
    private static readonly IBrush PairZeroBrush = Brush(Color.FromRgb(255, 137, 70));
    private static readonly IBrush PairOneBrush = Brush(Color.FromRgb(174, 122, 255));
    private static readonly Pen PairZeroPen = Pen(Color.FromRgb(255, 137, 70), 2.2);
    private static readonly Pen PairOnePen = Pen(Color.FromRgb(174, 122, 255), 2.2);
    private static readonly Pen PairZeroActivePen = Pen(Color.FromRgb(255, 137, 70), 4.2);
    private static readonly Pen PairOneActivePen = Pen(Color.FromRgb(174, 122, 255), 4.2);
    private static readonly Pen TargetPen = DashedPen(Color.FromRgb(90, 236, 160), 1.8, 5, 4);
    private static readonly IBrush TargetBrush = Brush(Color.FromRgb(90, 236, 160));
    private static readonly Pen PointerPen = DashedPen(Color.FromRgb(255, 100, 150), 1.8, 3, 3);

    public static void DrawPath(DrawingContext context, LizardDebugOverlayFrame frame)
    {
        using (context.PushOpacity(0.82))
        {
            for (var i = 1; i < frame.TrailPoints.Length; i++)
            {
                context.DrawLine(PathPen, Point(frame.TrailPoints[i - 1]), Point(frame.TrailPoints[i]));
            }
        }
        context.DrawLine(TargetPen, Point(frame.CanvasCenter), Point(frame.TargetPoint));
        context.DrawEllipse(null, TargetPen, Point(frame.TargetPoint), 7, 7);
        context.DrawLine(TargetPen, Point(frame.CanvasCenter), Point(frame.LookTargetPoint));
        if (frame.PointerAvailable)
        {
            context.DrawLine(PointerPen, Point(frame.CanvasCenter), Point(frame.PointerPoint));
            context.DrawEllipse(null, PointerPen, Point(frame.PointerPoint), 9, 9);
        }
    }

    public static void DrawSkeleton(DrawingContext context, LizardDebugOverlayFrame frame)
    {
        for (var i = 1; i < frame.SpineJoints.Length; i++)
        {
            context.DrawLine(SpinePen, Point(frame.SpineJoints[i - 1]), Point(frame.SpineJoints[i]));
        }
        for (var i = 0; i < frame.SpineJoints.Length; i++)
        {
            var radius = i is 0 or 4 or 13 ? 4.2 : 2.8;
            context.DrawEllipse(SpineBrush, null, Point(frame.SpineJoints[i]), radius, radius);
        }
        foreach (var leg in frame.Legs)
        {
            var pen = leg.Pair == 0
                ? leg.IsStepping ? PairZeroActivePen : PairZeroPen
                : leg.IsStepping ? PairOneActivePen : PairOnePen;
            var guidePen = leg.Pair == 0 ? PairZeroPen : PairOnePen;
            var brush = leg.Pair == 0 ? PairZeroBrush : PairOneBrush;
            context.DrawLine(pen, Point(leg.Shoulder), Point(leg.Elbow));
            context.DrawLine(pen, Point(leg.Elbow), Point(leg.Foot));
            context.DrawEllipse(brush, null, Point(leg.Shoulder), 3.6, 3.6);
            context.DrawEllipse(brush, null, Point(leg.Elbow), 4, 4);
            context.DrawEllipse(brush, null, Point(leg.Foot), 4.4, 4.4);
            if (leg.IsStepping)
            {
                using (context.PushOpacity(0.24))
                {
                    context.DrawEllipse(null, guidePen, Point(leg.Shoulder), leg.MaximumReach, leg.MaximumReach);
                    context.DrawLine(guidePen, Point(leg.Foot), Point(leg.StepTo));
                    context.DrawEllipse(null, guidePen, Point(leg.StepTo), 5.5, 5.5);
                }
            }
        }
        if (!frame.SpineJoints.IsDefaultOrEmpty)
        {
            var heading = new Vector2(MathF.Cos(frame.Heading), MathF.Sin(frame.Heading));
            var end = frame.SpineJoints[0] + heading * 44f;
            context.DrawLine(TargetPen, Point(frame.SpineJoints[0]), Point(end));
            context.DrawEllipse(TargetBrush, null, Point(end), 3.6, 3.6);
        }
    }

    private static IBrush Brush(Color color) => new SolidColorBrush(color);
    private static Pen Pen(Color color, double thickness) => new(Brush(color), thickness,
        lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static Pen DashedPen(Color color, double thickness, double dash, double gap) =>
        new(Brush(color), thickness, new DashStyle([dash, gap], 0),
            PenLineCap.Round, PenLineJoin.Round);
    private static Point Point(Vector2 point) => new(point.X, point.Y);
}
