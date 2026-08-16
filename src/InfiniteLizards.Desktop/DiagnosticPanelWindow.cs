using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Diagnostics;

namespace InfiniteLizards.Desktop;

/// <summary>
/// Cross-platform counterpart of the original WPF debug sidecar. The window
/// owns only Avalonia layout/lifecycle; lizard-specific state is supplied as
/// immutable presentation data by the host bridge.
/// </summary>
internal sealed class DiagnosticPanelWindow : Window
{
    internal const int PanelWidthPixels = 360;
    internal const int PanelHeightPixels = 132;
    internal const int PanelGapPixels = 13;

    private readonly PetWindowHost _owner;
    private readonly DesktopPetDebugSettings _settings;
    private readonly DebugPanelContentView _content;
    private readonly DiagnosticPanelNativeBehavior _nativeBehavior = new();
    private readonly Stopwatch _telemetryClock = Stopwatch.StartNew();
    private TimeSpan _lastTelemetryUpdate;
    private TimeSpan _lastDomainTelemetryUpdate;
    private DebugPanelSide _side;
    private bool _ownerIsClosing;
    private bool _panelIsClosing;
    private bool _ownerOpened;
    private bool _enabled;
    private bool _nativeBehaviorAttached;
    private bool _hasDomainTelemetryUpdate;

    public DiagnosticPanelWindow(PetWindowHost owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _settings = owner.DebugSettings;
        _content = new DebugPanelContentView();
        _content.ActionRequested += actionId => ActionRequested?.Invoke(actionId);

        Title = "蜥蜴调试面板";
        Width = _settings.PanelWidthPixels;
        Height = _settings.PanelHeightPixels;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        TransparencyBackgroundFallback = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(-10000, -10000);
        Content = _content;

        _owner.DiagnosticSnapshotUpdated += OnSnapshotUpdated;
        _owner.DebugOverlayEnabledChanged += OnDebugOverlayEnabledChanged;
        _owner.Window.Closing += OnOwnerClosing;
        _owner.Closed += OnOwnerClosed;
        Opened += OnPanelOpened;
        Closing += OnPanelClosing;
        Closed += OnPanelClosed;
    }

    internal event Action<string>? ActionRequested;

    internal DebugPanelContentView PanelContent => _content;

    internal void ShowForOwner(Window ownerWindow)
    {
        ArgumentNullException.ThrowIfNull(ownerWindow);
        _ownerOpened = true;
        _enabled = _owner.DebugOverlayEnabled;
        ApplyDisplayScale(Math.Max(0.25d, ownerWindow.RenderScaling));
        if (_enabled && PlaceNearOwner())
        {
            Show(ownerWindow);
        }
    }

    internal void SetDebugPresentation(DebugPanelPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        var now = _telemetryClock.Elapsed;
        if (_hasDomainTelemetryUpdate &&
            now - _lastDomainTelemetryUpdate < TimeSpan.FromMilliseconds(100))
        {
            return;
        }
        _hasDomainTelemetryUpdate = true;
        _lastDomainTelemetryUpdate = now;
        _content.SetPresentation(presentation);
    }

    private void OnSnapshotUpdated(object? sender, DesktopPetDiagnosticSnapshot snapshot)
    {
        if (!_enabled)
        {
            return;
        }
        ApplyDisplayScale(Math.Max(0.25d, _owner.Window.RenderScaling));
        var canPlace = PlaceNearOwner();
        if (!canPlace)
        {
            if (IsVisible) Hide();
            return;
        }
        if (_ownerOpened && !IsVisible)
        {
            Show(_owner.Window);
        }
        var now = _telemetryClock.Elapsed;
        if (snapshot.DisplayFrame == 1 ||
            now - _lastTelemetryUpdate >= TimeSpan.FromMilliseconds(100))
        {
            _lastTelemetryUpdate = now;
            _content.SetHostTelemetry(snapshot);
        }
    }

    private void ApplyDisplayScale(double displayScale)
    {
        var scale = double.IsFinite(displayScale)
            ? Math.Max(0.25d, displayScale)
            : 1d;
        Width = _settings.PanelWidthPixels / scale;
        Height = _settings.PanelHeightPixels / scale;
        _content.ApplyDisplayScale(scale);
    }

    private bool PlaceNearOwner()
    {
        var ownerWindow = _owner.Window;
        var screen = ownerWindow.Screens.ScreenFromWindow(ownerWindow)
                     ?? ownerWindow.Screens.Primary;
        if (screen is null)
        {
            // Without a work area there is no way to prove that the sidecar
            // fits without covering the pet. Match the WPF fail-closed policy.
            return false;
        }

        var metrics = DebugPanelCoordinateMetrics.Calculate(
            ownerWindow.RenderScaling,
            OperatingSystem.IsMacOS(),
            ownerWindow.Bounds.Width,
            ownerWindow.Bounds.Height,
            _settings.PanelWidthPixels,
            _settings.PanelHeightPixels,
            _settings.PanelGapPixels);
        var ownerWidth = metrics.OwnerWidth;
        var ownerHeight = metrics.OwnerHeight;
        var ownerCenter = new System.Numerics.Vector2(
            ownerWindow.Position.X + ownerWidth * 0.5f,
            ownerWindow.Position.Y + ownerHeight * 0.5f);
        var work = screen.WorkingArea;
        var placement = DebugPanelPlacementCalculator.Calculate(
            ownerCenter,
            new DebugPanelPlacementRect(work.X, work.Y, work.Right, work.Bottom),
            metrics.PanelWidth,
            metrics.PanelHeight,
            Math.Max(ownerWidth, ownerHeight) * 0.5f,
            metrics.Gap,
            _side);
        if (!placement.IsVisible)
        {
            return false;
        }

        _side = placement.Side;
        Position = new PixelPoint(
            checked((int)Math.Round(placement.Center.X - metrics.PanelWidth * 0.5f)),
            checked((int)Math.Round(placement.Center.Y - metrics.PanelHeight * 0.5f)));
        return true;
    }

    private void OnDebugOverlayEnabledChanged(object? sender, bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            _hasDomainTelemetryUpdate = false;
            _lastDomainTelemetryUpdate = default;
            _content.ClearPresentation();
            if (IsVisible) Hide();
            return;
        }
        if (_ownerOpened && PlaceNearOwner() && !IsVisible)
        {
            Show(_owner.Window);
        }
    }

    private void OnOwnerClosing(object? sender, WindowClosingEventArgs e) =>
        _ownerIsClosing = true;

    private void OnPanelOpened(object? sender, EventArgs e)
    {
        if (_nativeBehaviorAttached)
        {
            return;
        }
        _nativeBehaviorAttached = true;
        _nativeBehavior.Attach(this);
    }

    private void OnOwnerClosed(object? sender, EventArgs e)
    {
        _ownerIsClosing = true;
        if (!_panelIsClosing)
        {
            Close();
        }
    }

    private void OnPanelClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_ownerIsClosing)
        {
            e.Cancel = true;
            _owner.SetDebugOverlayEnabled(false);
            Hide();
            return;
        }
        _panelIsClosing = true;
    }

    private void OnPanelClosed(object? sender, EventArgs e)
    {
        _owner.DiagnosticSnapshotUpdated -= OnSnapshotUpdated;
        _owner.DebugOverlayEnabledChanged -= OnDebugOverlayEnabledChanged;
        _owner.Window.Closing -= OnOwnerClosing;
        _owner.Closed -= OnOwnerClosed;
        Opened -= OnPanelOpened;
        Closing -= OnPanelClosing;
        Closed -= OnPanelClosed;
        _nativeBehavior.Dispose();
    }
}

internal interface PetWindowHost
{
    Window Window { get; }
    bool DebugOverlayEnabled { get; }
    DesktopPetDebugSettings DebugSettings { get; }
    event EventHandler<DesktopPetDiagnosticSnapshot>? DiagnosticSnapshotUpdated;
    event EventHandler<bool>? DebugOverlayEnabledChanged;
    event EventHandler? DebugPathResetRequested;
    event EventHandler? Closed;
    void SetDebugOverlayEnabled(bool enabled);
}
