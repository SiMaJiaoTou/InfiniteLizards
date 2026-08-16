using System.Collections.Immutable;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace InfiniteLizards.Desktop;

internal sealed record DebugPanelActionPresentation(
    string Id,
    string Label,
    float Probability,
    string ToolTip);

internal sealed record DebugPanelPresentation(
    string LeftText,
    string RightText,
    string ProbabilityContext,
    bool HasCurrentProbabilityRow,
    bool ActionsEnabled,
    ImmutableArray<DebugPanelActionPresentation> Actions)
{
    public static ImmutableArray<DebugPanelActionPresentation> DefaultActions { get; } =
    [
        Action("forward", "前进"),
        Action("forward-extension", "续爬"),
        Action("curve", "曲线"),
        Action("s-curve", "S弯"),
        Action("fast-forward", "快进"),
        Action("fast-s-curve", "快S"),
        Action("turn-around", "掉头"),
        Action("lost-grip-fall", "失手")
    ];

    public static DebugPanelPresentation Waiting { get; } = new(
        "状态  等待首帧\n切换  --\n计时  --\n速度  --",
        "转向  --\n鼠标  --\nS弯   --\n步态  --",
        "无随机行",
        HasCurrentProbabilityRow: false,
        ActionsEnabled: false,
        DefaultActions);

    private static DebugPanelActionPresentation Action(string id, string label) =>
        new(id, label, float.NaN, $"立即播放：{label}");
}

/// <summary>
/// Avalonia programmatic port of DebugPanelView + DebugStateControlsView.
/// All measurements are divided by the active display scale so the physical
/// raster dimensions remain identical to the 360×132 WPF panel.
/// </summary>
internal sealed class DebugPanelContentView : Grid
{
    internal static readonly Color PanelColor = Color.FromArgb(232, 12, 17, 19);
    internal static readonly Color BorderColor = Color.FromRgb(61, 88, 72);
    internal static readonly Color TextColor = Color.FromRgb(229, 244, 234);
    internal static readonly Color HeaderColor = Color.FromRgb(174, 210, 187);
    internal static readonly Color ButtonColor = Color.FromRgb(31, 49, 39);
    internal static readonly Color ButtonBorderColor = Color.FromRgb(73, 111, 88);
    internal static readonly FontFamily DebugFontFamily = OperatingSystem.IsMacOS()
        ? new FontFamily("PingFang SC")
        : OperatingSystem.IsWindows()
            ? new FontFamily("Microsoft YaHei UI")
            : FontFamily.Default;

    private readonly Border _panel;
    private readonly Grid _telemetryColumns;
    private readonly TextBlock _leftText;
    private readonly TextBlock _rightText;
    private readonly Grid _controls;
    private readonly TextBlock _header;
    private readonly DebugActionButton[] _buttons;
    private DebugPanelPresentation _presentation = DebugPanelPresentation.Waiting;
    private double _displayScale = 1d;
    private bool _hasDomainPresentation;

    public DebugPanelContentView()
    {
        Focusable = false;
        Background = Brushes.Transparent;

        _panel = new Border
        {
            Background = new SolidColorBrush(PanelColor),
            BorderBrush = new SolidColorBrush(BorderColor)
        };
        Children.Add(_panel);

        _telemetryColumns = new Grid();
        _telemetryColumns.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1d, GridUnitType.Star)));
        _telemetryColumns.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1d, GridUnitType.Star)));
        _leftText = CreateTelemetryText();
        _rightText = CreateTelemetryText();
        _telemetryColumns.Children.Add(_leftText);
        SetColumn(_rightText, 1);
        _telemetryColumns.Children.Add(_rightText);
        Children.Add(_telemetryColumns);

        _controls = new Grid
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent
        };
        _controls.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _controls.RowDefinitions.Add(new RowDefinition(new GridLength(1d, GridUnitType.Star)));
        _header = new TextBlock
        {
            Foreground = new SolidColorBrush(HeaderColor),
            FontFamily = DebugFontFamily,
            FontWeight = FontWeight.Medium,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };
        _controls.Children.Add(_header);

        var actionGrid = new UniformGrid { Rows = 2, Columns = 4 };
        SetRow(actionGrid, 1);
        _controls.Children.Add(actionGrid);
        _buttons = new DebugActionButton[DebugPanelPresentation.DefaultActions.Length];
        for (var index = 0; index < _buttons.Length; index++)
        {
            var button = CreateButton(index);
            _buttons[index] = button;
            actionGrid.Children.Add(button);
        }
        Children.Add(_controls);

        ApplyDisplayScale(1d);
        ApplyPresentation();
    }

    internal event Action<string>? ActionRequested;

    internal string LeftText => _leftText.Text ?? string.Empty;
    internal string RightText => _rightText.Text ?? string.Empty;
    internal string HeaderText => _header.Text ?? string.Empty;
    internal bool ActionsEnabled => _controls.IsEnabled;
    internal double PhysicalPanelMargin => _panel.Margin.Left * _displayScale;
    internal double PhysicalControlsHeight => _controls.Height * _displayScale;
    internal double PhysicalTelemetryFontSize => _leftText.FontSize * _displayScale;

    internal string ButtonLabelAt(int index) =>
        _buttons[index].Text;

    internal void SetPresentation(DebugPanelPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (presentation.Actions.Length != _buttons.Length)
        {
            throw new ArgumentException(
                $"The debug panel requires exactly {_buttons.Length} actions.",
                nameof(presentation));
        }
        if (presentation.Actions.Any(action =>
                string.IsNullOrWhiteSpace(action.Id) ||
                string.IsNullOrWhiteSpace(action.Label)))
        {
            throw new ArgumentException("Debug actions require stable IDs and labels.",
                nameof(presentation));
        }

        _hasDomainPresentation = true;
        _presentation = presentation;
        ApplyPresentation();
    }

    internal void ClearPresentation()
    {
        _hasDomainPresentation = false;
        _presentation = DebugPanelPresentation.Waiting;
        ApplyPresentation();
    }

    internal void SetHostTelemetry(DesktopPetDiagnosticSnapshot snapshot)
    {
        if (_hasDomainPresentation)
        {
            return;
        }

        _presentation = DebugPanelPresentation.Waiting with
        {
            LeftText = string.Join('\n',
                $"状态  {(snapshot.IsPaused ? "暂停" : "运行")}",
                $"切换  主机帧  #{snapshot.DisplayFrame}",
                $"计时  {snapshot.FrameDelta * 1000f:F1}ms / {snapshot.FixedStep * 1000f:F1}ms",
                $"速度  固定步 ×{snapshot.SimulationSteps}"),
            RightText = string.Join('\n',
                $"转向  --",
                $"鼠标  {(snapshot.IsDragging ? "交互锁定" : "可追踪")}",
                $"位置  {snapshot.Position.X:F0}, {snapshot.Position.Y:F0}",
                $"步态  --  帧率{snapshot.FramesPerSecond:F0}")
        };
        ApplyPresentation();
    }

    internal void ApplyDisplayScale(double displayScale)
    {
        _displayScale = double.IsFinite(displayScale)
            ? Math.Max(0.25d, displayScale)
            : 1d;
        var scale = _displayScale;
        _panel.Margin = new Thickness(6d / scale);
        _panel.CornerRadius = new CornerRadius(6d / scale);
        _panel.BorderThickness = new Thickness(1d / scale);

        _telemetryColumns.Margin = new Thickness(
            12d / scale,
            12d / scale,
            12d / scale,
            66d / scale);
        _telemetryColumns.ColumnSpacing = 5d / scale;
        _leftText.Margin = new Thickness(0d);
        _rightText.Margin = new Thickness(0d);
        foreach (var text in new[] { _leftText, _rightText })
        {
            text.FontSize = 13d / scale;
            text.LineHeight = 13d * 1.12d / scale;
        }

        _controls.Height = 66d / scale;
        _header.FontSize = 10.5d / scale;
        foreach (var button in _buttons)
        {
            button.FontSize = 11.5d / scale;
            button.Margin = new Thickness(1.5d / scale);
            button.BorderThickness = new Thickness(0.75d / scale);
            button.CornerRadius = new CornerRadius(2d / scale);
        }
    }

    private void ApplyPresentation()
    {
        _leftText.Text = _presentation.LeftText;
        _rightText.Text = _presentation.RightText;
        _header.Text = _presentation.HasCurrentProbabilityRow
            ? $"播放状态 · 下一动作概率：{_presentation.ProbabilityContext}"
            : $"播放状态 · 最近矩阵概率：{_presentation.ProbabilityContext}";
        _controls.IsEnabled = _presentation.ActionsEnabled;
        _controls.Opacity = _presentation.ActionsEnabled ? 1d : 0.45d;

        for (var index = 0; index < _buttons.Length; index++)
        {
            var action = _presentation.Actions[index];
            _buttons[index].Text = $"{action.Label} {FormatProbability(action.Probability)}";
            ToolTip.SetTip(_buttons[index], action.ToolTip);
        }
    }

    private static TextBlock CreateTelemetryText() => new()
    {
        Foreground = new SolidColorBrush(TextColor),
        FontFamily = DebugFontFamily,
        FontWeight = FontWeight.Normal,
        TextWrapping = TextWrapping.NoWrap,
        IsHitTestVisible = false,
        VerticalAlignment = VerticalAlignment.Top
    };

    private DebugActionButton CreateButton(int index)
    {
        var button = new DebugActionButton
        {
            Padding = new Thickness(0d),
            Foreground = new SolidColorBrush(TextColor),
            Background = new SolidColorBrush(ButtonColor),
            BorderBrush = new SolidColorBrush(ButtonBorderColor),
            FontFamily = DebugFontFamily
        };
        button.Clicked += () =>
        {
            if (_controls.IsEnabled)
            {
                ActionRequested?.Invoke(_presentation.Actions[index].Id);
            }
        };
        return button;
    }

    private static string FormatProbability(float probability)
    {
        if (!float.IsFinite(probability))
        {
            return "—";
        }

        var percentage = probability * 100f;
        var format = MathF.Abs(percentage - MathF.Round(percentage)) < 0.05f
            ? "F0"
            : "F1";
        return percentage.ToString(format, CultureInfo.InvariantCulture) + "%";
    }
}

/// <summary>
/// Small explicit action control. It intentionally does not derive from
/// Button, so Fluent/default Button templates cannot change the 2×4 debug
/// strip's padding, minimum size, colors or disabled appearance.
/// </summary>
internal sealed class DebugActionButton : Border
{
    private readonly TextBlock _label = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        FontWeight = FontWeight.Medium,
        IsHitTestVisible = false
    };

    public DebugActionButton()
    {
        Focusable = false;
        Child = _label;
        PointerReleased += (_, e) =>
        {
            if (IsEnabled && e.InitialPressMouseButton == MouseButton.Left)
            {
                Clicked?.Invoke();
                e.Handled = true;
            }
        };
    }

    internal event Action? Clicked;

    internal string Text
    {
        get => _label.Text ?? string.Empty;
        set => _label.Text = value;
    }

    internal double FontSize
    {
        get => _label.FontSize;
        set => _label.FontSize = value;
    }

    internal FontFamily FontFamily
    {
        get => _label.FontFamily;
        set => _label.FontFamily = value;
    }

    internal IBrush? Foreground
    {
        get => _label.Foreground;
        set => _label.Foreground = value;
    }
}
