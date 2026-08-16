using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using DesktopLizard.Core;

namespace InfiniteLizards.Desktop.Rendering;

/// <summary>
/// The single cross-platform vector renderer. Avalonia dispatches this drawing
/// list to the same Skia backend on Windows and macOS, and hit testing consumes
/// the same immutable pose instead of a platform-specific native geometry.
/// </summary>
internal sealed class LizardView :
    Control,
    IDesktopPetPresenter<LizardRenderFrame>,
    IDesktopPetInputRegionProvider,
    IDesktopPetRenderCommitSource,
    IDesktopPetPresentationHitTester
{
    private const int MaximumRetainedHitTestPresentations = 16;

    private readonly AppearanceConfiguration _appearance;
    private readonly RenderingConfiguration _rendering;
    private readonly SolidColorBrush _bodyBrush;
    private readonly SolidColorBrush _eyeWhiteBrush;
    private readonly SolidColorBrush _pupilBrush;
    private readonly SolidColorBrush _shadowBrush;
    private readonly Pen _limbPen;
    private readonly Pen _shadowLimbPen;
    private readonly Pen _hitLimbPen;
    private LizardRenderFrame _frame;
    private Geometry _bodyGeometry;
    private Geometry _legGeometry;
    private Vector2 _lookDirection = Vector2.UnitX;
    private long _inputRegionVersion;
    private readonly DesktopPetRenderFenceCoalescer _renderFenceCoalescer = new();
    private readonly DesktopPetRenderFenceAttemptTracker _renderFenceAttempts = new();
    private readonly DesktopPetRenderCommitNotifier _renderCommitNotifier = new();
    private readonly SortedDictionary<long, HitTestPose> _hitTestPoses = [];
    private long _committedHitTestVersion;
    private LizardDebugOverlayFrame? _debugOverlay;

    public float ModelToWorldScale => Math.Max(0.01f, _appearance.VisualScale);
    internal Vector2 CanvasCenterModel => new(_appearance.RenderCanvasSize * 0.5f);

    public Control View => this;

    public long HitGeometryVersion => _inputRegionVersion;

    public long InputRegionVersion => HitGeometryVersion;

    public long PresentationVersion => _inputRegionVersion;

    public double InputRegionRefreshRate => _rendering.HitGeometryRefreshRate;

    public event EventHandler<DesktopPetRenderCommitAcknowledgedEventArgs>?
        RenderCommitAcknowledged
    {
        add => _renderCommitNotifier.RenderCommitAcknowledged += value;
        remove => _renderCommitNotifier.RenderCommitAcknowledged -= value;
    }

    public LizardView(LizardProfile profile, in LizardRenderFrame initialFrame)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _appearance = profile.Appearance;
        _rendering = profile.Rendering;
        _frame = initialFrame;
        _bodyBrush = new SolidColorBrush(ToColor(_appearance.BodyColor));
        _eyeWhiteBrush = new SolidColorBrush(ToColor(_rendering.EyeWhiteColor));
        _pupilBrush = new SolidColorBrush(ToColor(_appearance.PupilColor));
        _shadowBrush = new SolidColorBrush(ToColor(_appearance.ShadowColor));
        _limbPen = CreateRoundPen(_bodyBrush, _appearance.LimbWidth);
        _shadowLimbPen = CreateRoundPen(_shadowBrush, _appearance.ShadowLimbWidth);
        _hitLimbPen = CreateRoundPen(
            _bodyBrush,
            DesktopPetVisibleInputSizing.MainLimbThickness(
                _appearance.HitLimbWidth,
                _rendering.HitTestTolerance,
                _appearance.LimbWidth));
        _bodyGeometry = BuildBodyGeometry(initialFrame.BodyOutline);
        _legGeometry = BuildLegGeometry(initialFrame.Legs);
        _hitTestPoses.Add(
            _inputRegionVersion,
            new HitTestPose(initialFrame, _bodyGeometry, _legGeometry));
        IsHitTestVisible = true;
        Focusable = false;
    }

    public void Present(in LizardRenderFrame frame, Vector2 lookDirection)
    {
        _frame = frame;
        _bodyGeometry = BuildBodyGeometry(frame.BodyOutline);
        _legGeometry = BuildLegGeometry(frame.Legs);
        _lookDirection = MathEx.SafeNormalize(
            lookDirection,
            MathEx.FromAngle(frame.Heading));
        _inputRegionVersion = checked(_inputRegionVersion + 1);
        RememberHitTestPose(
            _inputRegionVersion,
            new HitTestPose(frame, _bodyGeometry, _legGeometry));
        InvalidateVisual();
    }

    public void SetSpawnOpacity(float opacity) => Opacity = Math.Clamp(opacity, 0f, 1f);

    internal void SetDebugOverlay(LizardDebugOverlayFrame frame)
    {
        _debugOverlay = frame ?? throw new ArgumentNullException(nameof(frame));
        InvalidateVisual();
    }

    internal void ClearDebugOverlay()
    {
        _debugOverlay = null;
        InvalidateVisual();
    }

    public Vector2 ViewToModel(Point point)
    {
        var scale = ModelToWorldScale;
        return new Vector2((float)(point.X / scale), (float)(point.Y / scale));
    }

    public bool IsPointOnLizard(Point point) => IsPointOnLizard(
        point,
        new HitTestPose(_frame, _bodyGeometry, _legGeometry));

    public bool HitTestPresentation(long presentationVersion, Point viewPoint)
    {
        if (!_hitTestPoses.TryGetValue(presentationVersion, out var pose))
        {
            throw new InvalidOperationException(
                $"Presenter pose v{presentationVersion} is no longer retained for hit testing.");
        }

        return IsPointOnLizard(viewPoint, pose);
    }

    public void CommitPresentation(long presentationVersion)
    {
        if (!_hitTestPoses.ContainsKey(presentationVersion))
        {
            throw new InvalidOperationException(
                $"Cannot commit unknown presenter pose v{presentationVersion}.");
        }

        _committedHitTestVersion = presentationVersion;
        foreach (var retiredVersion in _hitTestPoses.Keys
                     .TakeWhile(candidate => candidate < presentationVersion)
                     .ToArray())
        {
            _hitTestPoses.Remove(retiredVersion);
        }
    }

    public void DiscardUncommittedPresentations(long committedPresentationVersion)
    {
        if (!_hitTestPoses.ContainsKey(committedPresentationVersion))
        {
            throw new InvalidOperationException(
                $"Cannot retain unknown presenter pose v{committedPresentationVersion}.");
        }

        _committedHitTestVersion = committedPresentationVersion;
        foreach (var version in _hitTestPoses.Keys
                     .Where(candidate => candidate != committedPresentationVersion)
                     .ToArray())
        {
            _hitTestPoses.Remove(version);
        }
    }

    private bool IsPointOnLizard(Point point, HitTestPose pose)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            return false;
        }

        var modelPoint = ViewToModel(point);
        var modelAvaloniaPoint = ToPoint(modelPoint);
        var shadowOffset = new Vector2(_rendering.ShadowOffsetX, _rendering.ShadowOffsetY);
        var shadowPoint = modelPoint - shadowOffset;
        if (pose.BodyGeometry.FillContains(modelAvaloniaPoint) ||
            pose.LegGeometry.StrokeContains(_hitLimbPen, modelAvaloniaPoint) ||
            pose.BodyGeometry.FillContains(ToPoint(shadowPoint)) ||
            pose.LegGeometry.StrokeContains(_shadowLimbPen, ToPoint(shadowPoint)))
        {
            return true;
        }

        var frame = pose.Frame;
        foreach (var leg in frame.Legs)
        {
            var visibleFootRadius = (leg.IsFront
                    ? _rendering.FrontFootRadius
                    : _rendering.RearFootRadius) *
                (1f - leg.Lift * _rendering.LiftFootContraction);
            var mainFootRadius = DesktopPetVisibleInputSizing.MainFootRadius(
                _rendering.HitFootRadius,
                _rendering.HitTestTolerance,
                visibleFootRadius);
            if (Vector2.DistanceSquared(modelPoint, leg.Foot) <=
                    Square(mainFootRadius) ||
                HasVisibleArea(visibleFootRadius, visibleFootRadius) &&
                Vector2.DistanceSquared(modelPoint, leg.Foot + shadowOffset) <=
                    Square(visibleFootRadius))
            {
                return true;
            }
        }

        var noseHitRadius = _rendering.NoseRadius + _rendering.HitTestTolerance;
        if (Vector2.DistanceSquared(modelPoint, frame.HeadNose) <= Square(noseHitRadius) ||
            Vector2.DistanceSquared(modelPoint, frame.HeadNose + shadowOffset) <=
                Square(_rendering.NoseRadius))
        {
            return true;
        }

        var eyeRadius = DesktopPetVisibleInputSizing.MainEyeRadius(
            _rendering.HitEyeRadius,
            _rendering.HitTestTolerance,
            _rendering.EyeRadius,
            _rendering.PupilOffset,
            _rendering.PupilRadius,
            _rendering.MinimumPupilRadius);
        if (Vector2.DistanceSquared(modelPoint, frame.NegativeEyeCenter) <= Square(eyeRadius) ||
            Vector2.DistanceSquared(modelPoint, frame.PositiveEyeCenter) <= Square(eyeRadius))
        {
            return true;
        }

        var blinkScale = Math.Max(
            _rendering.MinimumBlinkScale,
            1f - frame.BlinkAmount * _rendering.BlinkClosure);
        return ContainsEllipse(
                   modelPoint,
                   frame.NegativeEyeCenter + shadowOffset,
                   _rendering.EyeShadowRadius,
                   _rendering.EyeShadowRadius * blinkScale) ||
               ContainsEllipse(
                   modelPoint,
                   frame.PositiveEyeCenter + shadowOffset,
                   _rendering.EyeShadowRadius,
                   _rendering.EyeShadowRadius * blinkScale);
    }

    private void RememberHitTestPose(long version, HitTestPose pose)
    {
        _hitTestPoses.Add(version, pose);
        while (_hitTestPoses.Count > MaximumRetainedHitTestPresentations)
        {
            var removable = _hitTestPoses.Keys.FirstOrDefault(
                candidate => candidate != _committedHitTestVersion);
            if (removable == _committedHitTestVersion)
            {
                throw new InvalidOperationException(
                    "No uncommitted presenter pose can be retired.");
            }
            _hitTestPoses.Remove(removable);
        }
    }

    private sealed record HitTestPose(
        LizardRenderFrame Frame,
        Geometry BodyGeometry,
        Geometry LegGeometry);

    bool IDesktopPetPresenter<LizardRenderFrame>.HitTest(Point viewPoint) =>
        IsPointOnLizard(viewPoint);

    public DesktopPetInputRegion CaptureInputRegion()
    {
        var frame = _frame;
        var scale = ModelToWorldScale;
        var shadowOffset = new Vector2(
            _rendering.ShadowOffsetX,
            _rendering.ShadowOffsetY);

        var fills = ImmutableArray.CreateBuilder<DesktopPetBezierPath>(2);
        fills.Add(BuildBodyInputPath(frame.BodyOutline, Vector2.Zero, scale));
        fills.Add(BuildBodyInputPath(frame.BodyOutline, shadowOffset, scale));

        var strokes = ImmutableArray.CreateBuilder<DesktopPetStrokedBezierPath>(
            frame.Legs.Length * 2);
        var hitLimbWidth = DesktopPetVisibleInputSizing.MainLimbThickness(
            _appearance.HitLimbWidth,
            _rendering.HitTestTolerance,
            _appearance.LimbWidth) * scale;
        var shadowLimbWidth = _appearance.ShadowLimbWidth * scale;
        foreach (var leg in frame.Legs)
        {
            strokes.Add(new DesktopPetStrokedBezierPath(
                BuildLegInputPath(leg, Vector2.Zero, scale),
                hitLimbWidth));
            strokes.Add(new DesktopPetStrokedBezierPath(
                BuildLegInputPath(leg, shadowOffset, scale),
                shadowLimbWidth));
        }

        var ellipses = ImmutableArray.CreateBuilder<DesktopPetRegionEllipse>(
            frame.Legs.Length * 2 + 8);
        foreach (var leg in frame.Legs)
        {
            var visibleFootRadius = (leg.IsFront
                    ? _rendering.FrontFootRadius
                    : _rendering.RearFootRadius) *
                (1f - leg.Lift * _rendering.LiftFootContraction) * scale;
            var hitFootRadius = DesktopPetVisibleInputSizing.MainFootRadius(
                _rendering.HitFootRadius,
                _rendering.HitTestTolerance,
                visibleFootRadius / scale) * scale;
            ellipses.Add(InputCircle(leg.Foot, hitFootRadius, scale));
            AddInputEllipseIfVisible(
                leg.Foot + shadowOffset,
                visibleFootRadius,
                visibleFootRadius);
        }

        var noseHitRadius =
            (_rendering.NoseRadius + _rendering.HitTestTolerance) * scale;
        ellipses.Add(InputCircle(
            frame.HeadNose,
            noseHitRadius,
            scale));
        ellipses.Add(InputCircle(
            frame.HeadNose + shadowOffset,
            _rendering.NoseRadius * scale,
            scale));

        var hitEyeRadius = DesktopPetVisibleInputSizing.MainEyeRadius(
            _rendering.HitEyeRadius,
            _rendering.HitTestTolerance,
            _rendering.EyeRadius,
            _rendering.PupilOffset,
            _rendering.PupilRadius,
            _rendering.MinimumPupilRadius) * scale;
        ellipses.Add(InputCircle(
            frame.NegativeEyeCenter,
            hitEyeRadius,
            scale));
        ellipses.Add(InputCircle(
            frame.PositiveEyeCenter,
            hitEyeRadius,
            scale));

        var blinkScale = Math.Max(
            _rendering.MinimumBlinkScale,
            1f - frame.BlinkAmount * _rendering.BlinkClosure);
        AddShadowEye(frame.NegativeEyeCenter);
        AddShadowEye(frame.PositiveEyeCenter);

        return new DesktopPetInputRegion(
            fills.ToImmutable(),
            strokes.ToImmutable(),
            ellipses.ToImmutable());

        void AddShadowEye(Vector2 center)
        {
            AddInputEllipseIfVisible(
                center + shadowOffset,
                _rendering.EyeShadowRadius * scale,
                _rendering.EyeShadowRadius * blinkScale * scale);
        }

        void AddInputEllipseIfVisible(
            Vector2 center,
            double radiusXInView,
            double radiusYInView)
        {
            if (!HasVisibleArea(radiusXInView, radiusYInView))
            {
                return;
            }

            ellipses.Add(new DesktopPetRegionEllipse(
                ToViewPoint(center, scale),
                radiusXInView,
                radiusYInView));
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var scale = ModelToWorldScale;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            if (_debugOverlay is { } debugOverlay)
            {
                LizardDebugOverlayRenderer.DrawPath(context, debugOverlay);
            }
            DrawFrame(context, in _frame);
            if (_debugOverlay is { } skeletonOverlay)
            {
                LizardDebugOverlayRenderer.DrawSkeleton(context, skeletonOverlay);
            }
        }

        QueueRenderCommitFence();
    }

    private void QueueRenderCommitFence()
    {
        if (!_renderCommitNotifier.HasSubscribers)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            // Headless raster tests have no composition target. A real desktop
            // surface is attached to a TopLevel before its first native Show.
            return;
        }

        var renderScale = topLevel.RenderScaling;
        if (!double.IsFinite(renderScale) || renderScale <= 0d)
        {
            Trace.WriteLine(
                "[InfiniteLizards] skipped a compositor fence because RenderScaling " +
                $"was invalid: {renderScale}.");
            return;
        }

        // Loaded runs after the current layout/render pass. Requesting a
        // marker composition batch there places its Rendered task after the
        // retained drawing list produced above, without calling USER32 from
        // Control.Render or from Avalonia's renderer thread.
        if (_renderFenceCoalescer.ObserveRender(
                new DesktopPetRenderedPoseToken(
                    _inputRegionVersion,
                    renderScale)))
        {
            try
            {
                Dispatcher.UIThread.Post(
                    RequestLatestRenderCommitFence,
                    DispatcherPriority.Loaded);
            }
            catch (Exception exception)
            {
                _renderFenceCoalescer.CancelPendingDispatch();
                Trace.WriteLine(
                    "[InfiniteLizards] could not enqueue the desktop-pet render " +
                    $"fence: {exception}");
            }
        }
    }

    private void RequestLatestRenderCommitFence()
    {
        if (!_renderCommitNotifier.HasSubscribers)
        {
            _renderFenceCoalescer.CancelPendingDispatch();
            return;
        }

        DesktopPetRenderedPoseToken token;
        try
        {
            token = _renderFenceCoalescer.TakeLatestForFence();
        }
        catch (Exception exception)
        {
            Trace.WriteLine(
                "[InfiniteLizards] could not dequeue the desktop-pet render " +
                $"fence token: {exception}");
            return;
        }

        BeginRenderCommitFence(token);
    }

    private void BeginRenderCommitFence(DesktopPetRenderedPoseToken token)
    {
        if (!_renderFenceAttempts.TryBegin(token, out var sequence))
        {
            return;
        }

        try
        {
            var visual = ElementComposition.GetElementVisual(this) ??
                throw new InvalidOperationException(
                    "Avalonia did not expose a composition visual.");
            var batch = visual.Compositor.RequestCompositionBatchCommitAsync();
            _ = ObserveRenderedBatchAsync(batch.Rendered, token, sequence);
        }
        catch (Exception exception)
        {
            HandleRenderFenceFailure(token, sequence, exception);
        }
    }

    private async Task ObserveRenderedBatchAsync(
        Task rendered,
        DesktopPetRenderedPoseToken token,
        long sequence)
    {
        try
        {
            await rendered.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TryPostRenderFenceCompletion(
                () => HandleRenderFenceFailure(token, sequence, exception));
            return;
        }

        TryPostRenderFenceCompletion(
            () => HandleRenderFenceSuccess(token, sequence));
    }

    private void HandleRenderFenceSuccess(
        DesktopPetRenderedPoseToken token,
        long sequence)
    {
        if (!_renderFenceAttempts.TryFinish(
                token,
                sequence,
                out var deferredLatest))
        {
            return;
        }

        if (!_renderCommitNotifier.HasSubscribers)
        {
            return;
        }

        _renderCommitNotifier.Publish(sequence, token);
        if (deferredLatest is { } next)
        {
            BeginRenderCommitFence(next);
        }
    }

    private static void TryPostRenderFenceCompletion(Action completion)
    {
        try
        {
            Dispatcher.UIThread.Post(completion, DispatcherPriority.Normal);
        }
        catch (Exception exception)
        {
            // A composition task may finish after the TopLevel and dispatcher
            // have begun shutting down. There is no native work to perform in
            // that case, and the detached notifier must remain a no-op.
            Trace.WriteLine(
                "[InfiniteLizards] discarded a compositor fence completion " +
                $"during dispatcher shutdown: {exception}");
        }
    }

    private void HandleRenderFenceFailure(
        DesktopPetRenderedPoseToken token,
        long sequence,
        Exception exception)
    {
        Trace.WriteLine(
            "[InfiniteLizards] Avalonia composition fence failed; the native " +
            $"region remains conservative: {exception}");
        if (!_renderFenceAttempts.TryFinish(
                token,
                sequence,
                out var deferredLatest) ||
            !_renderCommitNotifier.HasSubscribers)
        {
            return;
        }

        if (deferredLatest is { } next)
        {
            BeginRenderCommitFence(next);
        }
        else
        {
            // A failed marker did not acknowledge this token. Make the same
            // version/scale eligible again and request another render pass;
            // otherwise the bounded pending queue could freeze.
            InvalidateVisual();
        }
    }

    private void DrawFrame(DrawingContext context, in LizardRenderFrame frame)
    {
        var shadowOffset = new Vector2(_rendering.ShadowOffsetX, _rendering.ShadowOffsetY);

        using (context.PushOpacity(_appearance.ShadowOpacity))
        using (context.PushTransform(Matrix.CreateTranslation(shadowOffset.X, shadowOffset.Y)))
        {
            context.DrawGeometry(_shadowBrush, null, _bodyGeometry);
            DrawCircle(context, _shadowBrush, frame.HeadNose, _rendering.NoseRadius);
            DrawLegs(context, in frame, _legGeometry, shadow: true);
            DrawEyeShadows(context, in frame);
        }

        DrawLegs(context, in frame, _legGeometry, shadow: false);
        context.DrawGeometry(_bodyBrush, null, _bodyGeometry);
        DrawCircle(context, _bodyBrush, frame.HeadNose, _rendering.NoseRadius);
        DrawEyes(context, in frame);
    }

    private void DrawLegs(
        DrawingContext context,
        in LizardRenderFrame frame,
        Geometry legs,
        bool shadow)
    {
        context.DrawGeometry(null, shadow ? _shadowLimbPen : _limbPen, legs);
        foreach (var leg in frame.Legs)
        {
            var radius = (leg.IsFront
                    ? _rendering.FrontFootRadius
                    : _rendering.RearFootRadius) *
                (1f - leg.Lift * _rendering.LiftFootContraction);
            DrawCircle(context, shadow ? _shadowBrush : _bodyBrush, leg.Foot, radius);
        }
    }

    private void DrawEyeShadows(DrawingContext context, in LizardRenderFrame frame)
    {
        var blinkScale = Math.Max(
            _rendering.MinimumBlinkScale,
            1f - frame.BlinkAmount * _rendering.BlinkClosure);
        DrawEllipse(
            context,
            _shadowBrush,
            frame.NegativeEyeCenter,
            _rendering.EyeShadowRadius,
            _rendering.EyeShadowRadius * blinkScale);
        DrawEllipse(
            context,
            _shadowBrush,
            frame.PositiveEyeCenter,
            _rendering.EyeShadowRadius,
            _rendering.EyeShadowRadius * blinkScale);
    }

    private void DrawEyes(DrawingContext context, in LizardRenderFrame frame)
    {
        var blinkScale = Math.Max(
            _rendering.MinimumBlinkScale,
            1f - frame.BlinkAmount * _rendering.BlinkClosure);
        var pupilOffset = _lookDirection * _rendering.PupilOffset;
        DrawEye(context, frame.NegativeEyeCenter, pupilOffset, blinkScale);
        DrawEye(context, frame.PositiveEyeCenter, pupilOffset, blinkScale);
    }

    private void DrawEye(
        DrawingContext context,
        Vector2 center,
        Vector2 pupilOffset,
        double blinkScale)
    {
        DrawEllipse(
            context,
            _eyeWhiteBrush,
            center,
            _rendering.EyeRadius,
            _rendering.EyeRadius * blinkScale);
        if (blinkScale <= _rendering.PupilVisibilityBlinkScale)
        {
            return;
        }

        DrawEllipse(
            context,
            _pupilBrush,
            center + pupilOffset,
            _rendering.PupilRadius,
            Math.Max(_rendering.MinimumPupilRadius, _rendering.PupilRadius * blinkScale));
    }

    private static Geometry BuildBodyGeometry(ImmutableArray<Vector2> points)
    {
        var geometry = new StreamGeometry();
        if (points.IsDefaultOrEmpty)
        {
            return geometry;
        }

        using var path = geometry.Open();
        path.BeginFigure(ToPoint(points[0]), true);
        for (var index = 0; index < points.Length; index++)
        {
            var p0 = points[(index - 1 + points.Length) % points.Length];
            var p1 = points[index];
            var p2 = points[(index + 1) % points.Length];
            var p3 = points[(index + 2) % points.Length];
            var control1 = p1 + (p2 - p0) / 6f;
            var control2 = p2 - (p3 - p1) / 6f;
            path.CubicBezierTo(ToPoint(control1), ToPoint(control2), ToPoint(p2));
        }
        path.EndFigure(true);
        return geometry;
    }

    private static Geometry BuildLegGeometry(ImmutableArray<LizardRenderLegPose> legs)
    {
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        foreach (var leg in legs)
        {
            path.BeginFigure(ToPoint(leg.Shoulder), false);
            path.CubicBezierTo(ToPoint(leg.Elbow), ToPoint(leg.Elbow), ToPoint(leg.Foot));
            path.EndFigure(false);
        }
        return geometry;
    }

    private static DesktopPetBezierPath BuildBodyInputPath(
        ImmutableArray<Vector2> points,
        Vector2 offset,
        double scale)
    {
        if (points.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException(
                "A rendered lizard frame must contain a body outline.");
        }

        var segments = ImmutableArray.CreateBuilder<DesktopPetBezierSegment>(points.Length);
        for (var index = 0; index < points.Length; index++)
        {
            var p0 = points[(index - 1 + points.Length) % points.Length];
            var p1 = points[index];
            var p2 = points[(index + 1) % points.Length];
            var p3 = points[(index + 2) % points.Length];
            var control1 = p1 + (p2 - p0) / 6f;
            var control2 = p2 - (p3 - p1) / 6f;
            segments.Add(new DesktopPetBezierSegment(
                ToViewPoint(control1 + offset, scale),
                ToViewPoint(control2 + offset, scale),
                ToViewPoint(p2 + offset, scale)));
        }

        return new DesktopPetBezierPath(
            ToViewPoint(points[0] + offset, scale),
            segments.MoveToImmutable());
    }

    private static DesktopPetBezierPath BuildLegInputPath(
        LizardRenderLegPose leg,
        Vector2 offset,
        double scale) => new(
        ToViewPoint(leg.Shoulder + offset, scale),
        ImmutableArray.Create(new DesktopPetBezierSegment(
            ToViewPoint(leg.Elbow + offset, scale),
            ToViewPoint(leg.Elbow + offset, scale),
            ToViewPoint(leg.Foot + offset, scale))));

    private static DesktopPetRegionEllipse InputCircle(
        Vector2 center,
        double radiusInView,
        double coordinateScale) => new(
        ToViewPoint(center, coordinateScale),
        radiusInView,
        radiusInView);

    private static Point ToViewPoint(Vector2 point, double scale) =>
        new(point.X * scale, point.Y * scale);

    private static Pen CreateRoundPen(IBrush brush, double thickness) => new(
        brush,
        thickness,
        lineCap: PenLineCap.Round,
        lineJoin: PenLineJoin.Round);

    private static Color ToColor(RgbConfiguration color) =>
        Color.FromRgb((byte)color.Red, (byte)color.Green, (byte)color.Blue);

    private static void DrawCircle(
        DrawingContext context,
        IBrush brush,
        Vector2 center,
        double radius) => DrawEllipse(context, brush, center, radius, radius);

    private static void DrawEllipse(
        DrawingContext context,
        IBrush brush,
        Vector2 center,
        double radiusX,
        double radiusY)
    {
        if (!HasVisibleArea(radiusX, radiusY))
        {
            return;
        }

        context.DrawEllipse(
            brush,
            null,
            new Rect(center.X - radiusX, center.Y - radiusY, radiusX * 2d, radiusY * 2d));
    }

    private static Point ToPoint(Vector2 point) => new(point.X, point.Y);

    private static double Square(double value) => value * value;

    private static bool ContainsEllipse(
        Vector2 point,
        Vector2 center,
        float radiusX,
        float radiusY)
    {
        if (!HasVisibleArea(radiusX, radiusY))
        {
            return false;
        }

        var x = (point.X - center.X) / radiusX;
        var y = (point.Y - center.Y) / radiusY;
        return x * x + y * y <= 1f;
    }

    private static bool HasVisibleArea(double radiusX, double radiusY) =>
        double.IsFinite(radiusX) &&
        double.IsFinite(radiusY) &&
        radiusX > 0d &&
        radiusY > 0d;
}
