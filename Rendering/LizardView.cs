using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using DesktopLizard.Core;

namespace DesktopLizard.Rendering;

internal sealed class LizardView : FrameworkElement
{
    private readonly AppearanceConfiguration _appearance;
    private readonly RenderingConfiguration _rendering;
    private readonly SolidColorBrush _bodyBrush;
    private readonly SolidColorBrush _eyeWhiteBrush;
    private readonly SolidColorBrush _pupilBrush;
    private readonly SolidColorBrush _shadowBrush;
    private readonly Pen _limbPen;
    private readonly Pen _shadowLimbPen;
    private readonly Pen _hitLimbPen;
    private readonly bool _cancelMonitorDpi;
    private LizardRenderFrame _frame;
    private Geometry _hitGeometry = Geometry.Empty;
    private Vector2 _lookDirection = Vector2.UnitX;
    private float _spawnOpacity = 1f;
    private long _lastHitGeometryTicks;
    private DebugFrameSnapshot? _debugFrame;

    public float ModelToViewScale => GetModelToViewScale();
    public int HitGeometryVersion { get; private set; }

    public LizardView(
        LizardProfile profile,
        in LizardRenderFrame initialFrame,
        bool cancelMonitorDpi = true)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _frame = initialFrame;
        _appearance = profile.Appearance;
        _rendering = profile.Rendering;
        _bodyBrush = FrozenBrush(ToColor(_appearance.BodyColor));
        _eyeWhiteBrush = FrozenBrush(ToColor(_rendering.EyeWhiteColor));
        _pupilBrush = FrozenBrush(ToColor(_appearance.PupilColor));
        _shadowBrush = FrozenBrush(ToColor(_appearance.ShadowColor));
        _limbPen = FrozenPen(_bodyBrush, _appearance.LimbWidth);
        _shadowLimbPen = FrozenPen(_shadowBrush, _appearance.ShadowLimbWidth);
        _hitLimbPen = FrozenPen(Brushes.Black, _appearance.HitLimbWidth);
        _cancelMonitorDpi = cancelMonitorDpi;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        Focusable = false;
    }

    public void Present(in LizardRenderFrame frame, Vector2 lookDirection)
    {
        _frame = frame;
        _lookDirection = MathEx.SafeNormalize(
            lookDirection,
            MathEx.FromAngle(frame.Heading));
        InvalidateVisual();
    }

    public void SetSpawnOpacity(float opacity)
    {
        _spawnOpacity = MathEx.Clamp01(opacity);
        Opacity = _spawnOpacity;
    }

    public void SetDebugFrame(DebugFrameSnapshot frame)
    {
        _debugFrame = frame;
        InvalidateVisual();
    }

    public void ClearDebugFrame()
    {
        _debugFrame = null;
        InvalidateVisual();
    }

    public bool IsPointOnLizard(System.Windows.Point point)
    {
        if (double.IsNaN(point.X) || double.IsInfinity(point.X) ||
            double.IsNaN(point.Y) || double.IsInfinity(point.Y))
        {
            return false;
        }

        var scale = GetModelToViewScale();
        var modelPoint = new System.Windows.Point(point.X / scale, point.Y / scale);
        return _hitGeometry.FillContains(
            modelPoint,
            _rendering.HitTestTolerance,
            ToleranceType.Absolute);
    }

    public Vector2 ViewToModel(System.Windows.Point point)
    {
        var scale = GetModelToViewScale();
        return new Vector2((float)(point.X / scale), (float)(point.Y / scale));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var modelToViewScale = GetModelToViewScale();
        drawingContext.PushTransform(new ScaleTransform(modelToViewScale, modelToViewScale));
        try
        {
            // Retain one coherent value for the whole render pass, including
            // the lower-frequency hit-test geometry refresh.
            var frame = _frame;
            if (_debugFrame is { } debugFrame)
            {
                DebugOverlayRenderer.DrawDebugPath(drawingContext, debugFrame);
            }

            var shadowOffset = new Vector2(
                _rendering.ShadowOffsetX,
                _rendering.ShadowOffsetY);
            var body = LizardGeometryBuilder.BuildBody(frame);
            var legs = LizardGeometryBuilder.BuildLegs(frame);

            // One opacity group makes every overlapping body part contribute to
            // a single silhouette. Its alpha stays constant at joints instead
            // of becoming darker for every semi-transparent primitive.
            drawingContext.PushOpacity(_appearance.ShadowOpacity);
            try
            {
                drawingContext.PushTransform(new TranslateTransform(shadowOffset.X, shadowOffset.Y));
                try
                {
                    drawingContext.DrawGeometry(_shadowBrush, null, body);
                    drawingContext.DrawEllipse(
                        _shadowBrush,
                        null,
                        ToPoint(frame.HeadNose),
                        _rendering.NoseRadius,
                        _rendering.NoseRadius);
                    DrawLegs(drawingContext, frame, legs, true);
                    DrawEyeShadows(drawingContext, frame, Vector2.Zero);
                }
                finally
                {
                    drawingContext.Pop();
                }
            }
            finally
            {
                drawingContext.Pop();
            }

            DrawLegs(drawingContext, frame, legs, false);
            drawingContext.DrawGeometry(_bodyBrush, null, body);
            drawingContext.DrawEllipse(
                _bodyBrush,
                null,
                ToPoint(frame.HeadNose),
                _rendering.NoseRadius,
                _rendering.NoseRadius);
            DrawEyes(drawingContext, frame);

            if (_debugFrame is { } skeletonFrame)
            {
                DebugOverlayRenderer.DrawDebugSkeleton(drawingContext, skeletonFrame);
            }

            var now = Stopwatch.GetTimestamp();
            if (HitGeometryVersion == 0 ||
                (now - _lastHitGeometryTicks) / (double)Stopwatch.Frequency >=
                1d / _rendering.HitGeometryRefreshRate)
            {
                _hitGeometry = LizardGeometryBuilder.BuildHitGeometry(
                    frame,
                    body,
                    legs,
                    _hitLimbPen,
                    _rendering.HitFootRadius,
                    _rendering.HitEyeRadius);
                _lastHitGeometryTicks = now;
                HitGeometryVersion++;
            }
        }
        finally
        {
            drawingContext.Pop();
        }

    }

    private void DrawLegs(
        DrawingContext drawingContext,
        in LizardRenderFrame frame,
        Geometry legs,
        bool shadow)
    {
        var pen = shadow ? _shadowLimbPen : _limbPen;
        drawingContext.DrawGeometry(null, pen, legs);
        foreach (var leg in frame.Legs)
        {
            var foot = leg.Foot;
            // The reference conveys lift mostly through a brief contraction of
            // the paw silhouette; the foot then returns to full size on contact.
            var radius = (leg.IsFront
                    ? _rendering.FrontFootRadius
                    : _rendering.RearFootRadius) *
                (1f - leg.Lift * _rendering.LiftFootContraction);
            drawingContext.DrawEllipse(
                shadow ? _shadowBrush : _bodyBrush,
                null,
                ToPoint(foot),
                radius,
                radius);
        }
    }

    private void DrawEyeShadows(
        DrawingContext drawingContext,
        in LizardRenderFrame frame,
        Vector2 shadowOffset)
    {
        var blinkScale = Math.Max(
            _rendering.MinimumBlinkScale,
            1f - frame.BlinkAmount * _rendering.BlinkClosure);
        DrawEyeShadow(drawingContext, frame.NegativeEyeCenter + shadowOffset, blinkScale);
        DrawEyeShadow(drawingContext, frame.PositiveEyeCenter + shadowOffset, blinkScale);
    }

    private void DrawEyeShadow(
        DrawingContext drawingContext,
        Vector2 center,
        double blinkScale)
    {
        drawingContext.DrawEllipse(
            _shadowBrush,
            null,
            ToPoint(center),
            _rendering.EyeShadowRadius,
            _rendering.EyeShadowRadius * blinkScale);
    }

    private void DrawEyes(DrawingContext drawingContext, in LizardRenderFrame frame)
    {
        var blinkScale = Math.Max(
            _rendering.MinimumBlinkScale,
            1f - frame.BlinkAmount * _rendering.BlinkClosure);
        var pupilOffset = _lookDirection * _rendering.PupilOffset;

        DrawEye(drawingContext, frame.NegativeEyeCenter, pupilOffset, blinkScale);
        DrawEye(drawingContext, frame.PositiveEyeCenter, pupilOffset, blinkScale);
    }

    private void DrawEye(
        DrawingContext drawingContext,
        Vector2 center,
        Vector2 pupilOffset,
        double blinkScale)
    {
        drawingContext.DrawEllipse(
            _eyeWhiteBrush,
            null,
            ToPoint(center),
            _rendering.EyeRadius,
            _rendering.EyeRadius * blinkScale);

        if (blinkScale > _rendering.PupilVisibilityBlinkScale)
        {
            var pupilCenter = center + pupilOffset;
            drawingContext.DrawEllipse(
                _pupilBrush,
                null,
                ToPoint(pupilCenter),
                _rendering.PupilRadius,
                Math.Max(
                    _rendering.MinimumPupilRadius,
                    _rendering.PupilRadius * blinkScale));
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

    private float GetModelToViewScale()
    {
        // The pet window is kept at a fixed physical pixel size. Cancel WPF's
        // monitor DPI transform here so 60% means the same visible size on
        // 100%, 175% and 200% monitors. Off-screen preview visuals report 1.0.
        var dpi = VisualTreeHelper.GetDpi(this);
        var dpiScale = _cancelMonitorDpi
            ? Math.Max(1d, Math.Min(dpi.DpiScaleX, dpi.DpiScaleY))
            : 1d;
        return Math.Max(0.01f, _appearance.VisualScale / (float)dpiScale);
    }

    private static Color ToColor(RgbConfiguration color) =>
        Color.FromRgb((byte)color.Red, (byte)color.Green, (byte)color.Blue);

    private static System.Windows.Point ToPoint(Vector2 point) => new(point.X, point.Y);
}
