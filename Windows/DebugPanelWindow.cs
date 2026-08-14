using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopLizard.Core;
using DesktopLizard.Rendering;

namespace DesktopLizard.Windows;

/// <summary>
/// A non-activating sidecar for debug telemetry and explicit state playback.
/// It follows the pet but always occupies a non-overlapping side of the full
/// render surface.
/// </summary>
internal sealed class DebugPanelWindow : Window
{
    // Default aliases retained for older diagnostics. Live windows use the
    // corresponding per-profile fields below.
    internal const int PanelWidthPixels = 360;
    internal const int PanelHeightPixels = 132;
    private readonly int _panelWidthPixels;
    private readonly int _panelHeightPixels;
    private readonly int _gapPixels;
    private readonly float _petRenderRadiusPixels;

    private readonly DebugPanelView _view;
    private readonly DebugStateControlsView _controls;
    private nint _handle;
    private float _lastDpiScale;
    private bool _closed;
    private DebugPanelSide _side;

    public DebugPanelWindow()
        : this(LizardProfile.Default)
    {
    }

    public DebugPanelWindow(LizardProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _view = new DebugPanelView();
        _controls = new DebugStateControlsView(profile.Behavior.TransitionMatrix);
        _controls.ActionRequested += action => ActionRequested?.Invoke(action);
        _panelWidthPixels = profile.Runtime.DebugPanelWidthPixels;
        _panelHeightPixels = profile.Runtime.DebugPanelHeightPixels;
        // The configured gap includes the historical extra rounding pixel, so
        // native centers may round independently without covering the animal.
        _gapPixels = profile.Runtime.DebugPanelGapPixels;
        _petRenderRadiusPixels =
            profile.Appearance.RenderCanvasSize * profile.Appearance.VisualScale * 0.5f;
        Title = "蜥蜴调试面板";
        Width = _panelWidthPixels;
        Height = _panelHeightPixels;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;
        Top = -10000;
        var root = new Grid();
        root.Children.Add(_view);
        root.Children.Add(_controls);
        Content = root;

        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => _closed = true;
    }

    public event Action<AutonomousAction>? ActionRequested;

    public void UpdateFrame(DebugFrameSnapshot frame)
    {
        if (_closed)
        {
            return;
        }
        _view.SetFrame(frame);
        _controls.SetFrame(frame);
    }

    public void SetEnabled(bool enabled)
    {
        if (_closed)
        {
            return;
        }
        if (!enabled)
        {
            _view.ClearFrame();
            _controls.ClearFrame();
            if (IsVisible)
            {
                Hide();
            }
        }
    }

    public void PlaceNear(Vector2 petCenter, NativeMethods.Rect workArea, float dpiScale)
    {
        if (_closed)
        {
            return;
        }

        dpiScale = Math.Max(1f, dpiScale);
        if (MathF.Abs(dpiScale - _lastDpiScale) > 0.001f)
        {
            _lastDpiScale = dpiScale;
            Width = _panelWidthPixels / dpiScale;
            Height = _panelHeightPixels / dpiScale;
        }

        var placement = DebugPanelPlacementCalculator.Calculate(
            petCenter,
            new DebugPanelPlacementRect(
                workArea.Left,
                workArea.Top,
                workArea.Right,
                workArea.Bottom),
            _panelWidthPixels,
            _panelHeightPixels,
            _petRenderRadiusPixels,
            _gapPixels,
            _side);
        _side = placement.Side;
        if (!placement.IsVisible)
        {
            if (IsVisible)
            {
                Hide();
            }
            return;
        }

        if (!IsVisible)
        {
            Show();
        }
        if (_handle != nint.Zero)
        {
            NativeMethods.SetTopmostCenter(
                _handle,
                placement.Center,
                _panelWidthPixels,
                _panelHeightPixels);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        // Buttons must receive clicks, while WS_EX_NOACTIVATE keeps the desktop
        // and the user's current app focused.
        NativeMethods.ApplyPetWindowStyles(_handle, false);
        if (HwndSource.FromHwnd(_handle) is { } source)
        {
            source.AddHook(WindowHook);
        }
    }

    private static nint WindowHook(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == NativeMethods.WmMouseActivate)
        {
            handled = true;
            return NativeMethods.MaNoActivate;
        }
        return nint.Zero;
    }
}
