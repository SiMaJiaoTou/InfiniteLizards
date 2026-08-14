using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DesktopLizard.Core;

namespace DesktopLizard.Rendering;

/// <summary>
/// Non-focusing debug action strip. Buttons display the effective conditional
/// probability from the resolved individual profile's current transition row.
/// </summary>
internal sealed class DebugStateControlsView : Grid
{
    internal static readonly ImmutableArray<AutonomousAction> Actions =
    [
        AutonomousAction.Forward,
        AutonomousAction.ForwardExtension,
        AutonomousAction.Curve,
        AutonomousAction.SCurve,
        AutonomousAction.FastForward,
        AutonomousAction.FastSCurve,
        AutonomousAction.TurnAround,
        AutonomousAction.LostGripFall
    ];

    private const double ControlsHeightPixels = 66d;
    private readonly TransitionMatrixConfiguration _matrix;
    private readonly TextBlock _header;
    private readonly Button[] _buttons;
    private AutonomousTransitionRow _displayedRow = AutonomousTransitionRow.AfterForward;
    private bool _displayedForwardExtended;
    private bool _hasCurrentRow;

    public DebugStateControlsView(TransitionMatrixConfiguration matrix)
    {
        _matrix = matrix ?? throw new ArgumentNullException(nameof(matrix));
        Focusable = false;
        VerticalAlignment = VerticalAlignment.Bottom;
        Background = Brushes.Transparent;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1d, GridUnitType.Star) });

        _header = new TextBlock
        {
            Foreground = FrozenBrush(Color.FromRgb(174, 210, 187)),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontWeight = FontWeights.Medium,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };
        Children.Add(_header);

        var grid = new UniformGrid { Rows = 2, Columns = 4 };
        SetRow(grid, 1);
        Children.Add(grid);

        _buttons = new Button[Actions.Length];
        for (var index = 0; index < Actions.Length; index++)
        {
            var action = Actions[index];
            var button = CreateButton(action);
            _buttons[index] = button;
            grid.Children.Add(button);
        }

        UpdateLabels();
        IsEnabled = false;
    }

    public event Action<AutonomousAction>? ActionRequested;

    public void SetFrame(DebugFrameSnapshot frame)
    {
        var dpiScale = Math.Max(1f, frame.DpiScale);
        Height = ControlsHeightPixels / dpiScale;
        _header.FontSize = 10.5d / dpiScale;
        foreach (var button in _buttons)
        {
            button.FontSize = 11.5d / dpiScale;
            button.Margin = new Thickness(1.5d / dpiScale);
            button.BorderThickness = new Thickness(0.75d / dpiScale);
        }

        var row = AutonomousTransitionInspector.RowFor(frame.Behavior.State);
        _hasCurrentRow = row != AutonomousTransitionRow.None;
        if (_hasCurrentRow)
        {
            _displayedRow = row;
            _displayedForwardExtended =
                row == AutonomousTransitionRow.AfterForward &&
                frame.Behavior.ForwardExtended;
        }

        IsEnabled = DebugPlaybackPolicy.CanRequest(
            frame.Behavior.State,
            frame.Behavior.IsPaused);
        UpdateLabels();
    }

    public void ClearFrame()
    {
        IsEnabled = false;
        _hasCurrentRow = false;
        UpdateLabels();
    }

    internal string LabelFor(AutonomousAction action)
    {
        var index = Actions.IndexOf(action);
        return index >= 0 ? _buttons[index].Content?.ToString() ?? string.Empty : string.Empty;
    }

    private Button CreateButton(AutonomousAction action)
    {
        var button = new Button
        {
            Focusable = false,
            Cursor = Cursors.Hand,
            Padding = new Thickness(0d),
            Foreground = FrozenBrush(Color.FromRgb(229, 244, 234)),
            Background = FrozenBrush(Color.FromRgb(31, 49, 39)),
            BorderBrush = FrozenBrush(Color.FromRgb(73, 111, 88)),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontWeight = FontWeights.Medium,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = $"立即播放：{DebugLabelCatalog.Action(action)}"
        };
        button.Click += (_, _) => ActionRequested?.Invoke(action);
        return button;
    }

    private void UpdateLabels()
    {
        var context = DebugLabelCatalog.TransitionRow(_displayedRow);
        _header.Text = _hasCurrentRow
            ? $"播放状态 · 下一动作概率：{context}"
            : $"播放状态 · 最近矩阵概率：{context}";

        for (var index = 0; index < Actions.Length; index++)
        {
            var action = Actions[index];
            var probability = AutonomousTransitionInspector.EffectiveProbability(
                _matrix,
                _displayedRow,
                action,
                _displayedForwardExtended);
            _buttons[index].Content =
                $"{DebugLabelCatalog.Action(action)} {FormatProbability(probability)}";
        }
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

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
