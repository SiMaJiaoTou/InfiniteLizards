using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using DesktopLizard.Core;

namespace DesktopLizard.Rendering;

/// <summary>
/// Text-only debug HUD hosted by a separate click-through window. Keeping this
/// out of LizardView guarantees that diagnostic text can never cover the pet.
/// </summary>
internal sealed class DebugPanelView : FrameworkElement
{
    private static readonly SolidColorBrush PanelBrush = FrozenBrush(Color.FromArgb(232, 12, 17, 19));
    private static readonly SolidColorBrush BorderBrush = FrozenBrush(Color.FromRgb(61, 88, 72));
    private static readonly SolidColorBrush TextBrush = FrozenBrush(Color.FromRgb(229, 244, 234));
    private static readonly Pen BorderPen = FrozenPen(BorderBrush, 1d);

    private DebugFrameSnapshot? _frame;
    private FormattedText? _leftText;
    private FormattedText? _rightText;
    private long _lastTextTicks;

    public DebugPanelView()
    {
        IsHitTestVisible = false;
        Focusable = false;
        SnapsToDevicePixels = true;
    }

    public void SetFrame(DebugFrameSnapshot frame)
    {
        _frame = frame;
        var now = Stopwatch.GetTimestamp();
        if (_leftText is null ||
            (now - _lastTextTicks) / (double)Stopwatch.Frequency >= 0.10d)
        {
            _lastTextTicks = now;
            BuildText(frame);
        }
        InvalidateVisual();
    }

    public void ClearFrame()
    {
        _frame = null;
        _leftText = null;
        _rightText = null;
        _lastTextTicks = 0;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (_frame is null || _leftText is null || _rightText is null)
        {
            return;
        }

        var dpiScale = Math.Max(1d, _frame.DpiScale);
        var margin = 6d / dpiScale;
        var padding = 6d / dpiScale;
        var gap = 5d / dpiScale;
        var radius = 6d / dpiScale;
        var width = Math.Max(1d, ActualWidth);
        var height = Math.Max(1d, ActualHeight);
        var outer = new Rect(margin, margin, Math.Max(1d, width - margin * 2d), Math.Max(1d, height - margin * 2d));
        drawingContext.DrawRoundedRectangle(PanelBrush, BorderPen, outer, radius, radius);

        var columnWidth = Math.Max(40d, (outer.Width - padding * 2d - gap) * 0.5d);
        var textWidth = Math.Max(32d, columnWidth - padding);
        _leftText.MaxTextWidth = textWidth;
        _rightText.MaxTextWidth = textWidth;

        var top = outer.Y + padding;
        var left = outer.X + padding;
        var right = left + columnWidth + gap;
        drawingContext.DrawText(_leftText, new Point(left, top));
        drawingContext.DrawText(_rightText, new Point(right, top));
    }

    private void BuildText(DebugFrameSnapshot frame)
    {
        var behavior = frame.Behavior;
        var lizard = frame.Lizard;
        var pointerStatus = !frame.PointerAvailable
            ? "不可用"
            : frame.PointerBlocked ? "交互锁定" : "可追踪";

        var pointerDistance = float.IsFinite(frame.PointerDistance)
            ? $"{frame.PointerDistance:F0}px"
            : "--";
        var cycleIndex = behavior.SCurveCycleCount > 0 && behavior.SCurveCycleDuration > 0.01f
            ? Math.Min(
                behavior.SCurveCycleCount,
                (int)(behavior.SCurveElapsed / behavior.SCurveCycleDuration) + 1)
            : 0;
        var estimatedRadius = behavior.SCurveAmplitude > 0.01f
            ? Math.Max(0f, behavior.DesiredSpeed) / behavior.SCurveAmplitude
            : 0f;
        var sCurve = behavior.State.IsSCurveCrawl()
            ? $"段{cycleIndex}/{behavior.SCurveCycleCount}  周{behavior.SCurveCycleDuration:F1}s  半径≈{estimatedRadius:F0}px"
            : "未激活";
        var lostGrip = behavior.State == RoamingState.LostGripFall
            ? $"{LostGripPhaseLabel(behavior.LostGripPhase)}  " +
              $"速度{behavior.LostGripVerticalVelocity:F0}  " +
              $"距离{behavior.LostGripDistance:F0}/{behavior.LostGripTargetDistance:F0}px"
            : "未触发";
        // The lower half is reserved for state buttons. Keep this text layer
        // focused on the state machine rather than repeating every joint value.
        var left = new StringBuilder(160);
        left.AppendLine($"状态  {StateLabel(behavior.State)}");
        left.AppendLine($"切换  {ReasonLabel(behavior.LastTransitionReason)}  #{behavior.TransitionSerial}");
        left.AppendLine($"计时  {Math.Max(0f, behavior.StateTimeRemaining):F1}s / {Math.Max(0f, behavior.BoutTimeRemaining):F1}s");
        left.Append($"速度  {behavior.Speed:F1} → {behavior.DesiredSpeed:F1}");

        var right = new StringBuilder(160);
        right.AppendLine($"转向  {behavior.TurnVelocity:F2} → {behavior.DesiredTurnVelocity:F2}");
        right.AppendLine($"鼠标  {pointerStatus}  {pointerDistance}");
        right.AppendLine(behavior.State == RoamingState.LostGripFall
            ? $"失手  {lostGrip}"
            : $"S弯   {sCurve}");
        right.Append($"步态  {lizard.LastGaitDisplaySpeed:F1}  P{lizard.NextPair}  帧率{frame.FramesPerSecond:F0}");

        var fontSize = 13d / Math.Max(1f, frame.DpiScale);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(
            new FontFamily("Microsoft YaHei UI"),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);
        _leftText = CreateText(left.ToString(), typeface, fontSize, TextBrush, pixelsPerDip);
        _rightText = CreateText(right.ToString(), typeface, fontSize, TextBrush, pixelsPerDip);
    }

    private static FormattedText CreateText(
        string text,
        Typeface typeface,
        double fontSize,
        Brush brush,
        double pixelsPerDip) => new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            pixelsPerDip)
        {
            LineHeight = fontSize * 1.12d,
            Trimming = TextTrimming.None
        };

    internal static string StateLabel(RoamingState state) => DebugLabelCatalog.State(state);

    internal static string LostGripPhaseLabel(LostGripFallPhase phase) =>
        DebugLabelCatalog.LostGripPhase(phase);

    private static string ReasonLabel(StateTransitionReason reason) =>
        DebugLabelCatalog.Reason(reason);

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
