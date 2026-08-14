using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopLizard.AppRuntime;
using DesktopLizard.Core;
using DesktopLizard.Rendering;
using DesktopLizard.Services;
using Microsoft.Win32;

namespace DesktopLizard.Windows;

internal sealed partial class PetWindow : Window
{
    private readonly LizardProfile _profile;
    private readonly string? _configurationPath;
    private readonly float _windowSizePixels;
    private readonly float _windowRadiusPixels;
    private readonly float _releaseRenderRadiusPixels;
    private readonly float _compositionFallbackDelay;
    private readonly PetSimulationSession _simulation;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _renderFallbackTimer;
    private readonly LizardView _view;
    private readonly DebugPanelWindow _debugPanelWindow;
    private readonly ContextMenu _menu;
    private readonly MenuItem _pauseItem;
    private readonly MenuItem _autostartItem;
    private readonly MenuItem _debugItem;

    private nint _handle;
    private long _lastFrameTicks;
    private long _lastCompositionTicks;
    private NativeMethods.Rect _activeWorkArea;
    private Vector2 _dragOffset;
    private bool _isDragging;
    private bool _contextMenuOpen;
    private bool _clickThrough = true;
    private bool _displaySettingsDirty;
    private float _spawnVisualTime;
    private readonly bool _diagnosticMode;
    private int _lastWindowX = int.MinValue;
    private int _lastWindowY = int.MinValue;
    private Vector2 _lastHitTestCursor = new(float.NaN, float.NaN);
    private int _lastHitTestWindowX = int.MinValue;
    private int _lastHitTestWindowY = int.MinValue;
    private int _lastHitGeometryVersion = -1;
    private float _lastLayoutDpiScale;
    private bool _debugOverlayEnabled;

    public PetWindow(bool diagnosticMode = false, bool debugOverlayEnabled = false)
        : this(LizardProfile.Default, null, diagnosticMode, debugOverlayEnabled)
    {
    }

    public PetWindow(
        LizardProfile profile,
        bool diagnosticMode = false,
        bool debugOverlayEnabled = false)
        : this(profile, null, diagnosticMode, debugOverlayEnabled)
    {
    }

    public PetWindow(
        LizardProfile profile,
        string? configurationPath,
        bool diagnosticMode = false,
        bool debugOverlayEnabled = false)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _configurationPath = configurationPath;
        var appearance = _profile.Appearance;
        var runtime = _profile.Runtime;
        var geometryEnvelope = LizardGeometryEnvelope.Calculate(
            appearance,
            _profile.Gait,
            _profile.SecondaryMotion,
            _profile.Rendering);
        _windowSizePixels = appearance.RenderCanvasSize * appearance.VisualScale;
        // Navigation follows the larger of the configured visible canvas and
        // the resolved normal-pose geometry envelope. The full render canvas
        // remains reserved for the larger release/dangling pose.
        _windowRadiusPixels =
            (Math.Max(
                 appearance.CreatureCanvasSize * 0.5f,
                 geometryEnvelope.NormalModelRadius) +
             runtime.NavigationMarginModel) *
            appearance.VisualScale;
        _releaseRenderRadiusPixels =
            _windowSizePixels * 0.5f + runtime.ReleaseRenderMarginPixels;
        _compositionFallbackDelay = runtime.CompositionFallbackDelay;
        _simulation = new PetSimulationSession(
            Environment.TickCount ^ 0x4C495A41,
            _profile);
        _debugPanelWindow = new DebugPanelWindow(_profile);
        _debugPanelWindow.ActionRequested += OnDebugActionRequested;
        _diagnosticMode = diagnosticMode;
        _debugOverlayEnabled = debugOverlayEnabled;
        Title = diagnosticMode ? "桌面小蜥（验收窗口）" : "桌面小蜥";
        Width = _windowSizePixels;
        Height = _windowSizePixels;
        MinWidth = _windowSizePixels;
        MinHeight = _windowSizePixels;
        MaxWidth = _windowSizePixels;
        MaxHeight = _windowSizePixels;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = diagnosticMode;
        ShowActivated = diagnosticMode;
        Focusable = diagnosticMode;
        Topmost = true;

        _view = new LizardView(_profile, _simulation.InitialRenderFrame);
        Content = _view;
        (_menu, _pauseItem, _autostartItem, _debugItem) = CreateContextMenu();
        _renderFallbackTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(Math.Min(
                runtime.CompositionFallbackDelay,
                runtime.CompositionFallbackMaximumFrameSteps /
                runtime.SimulationRate))
        };
        _renderFallbackTimer.Tick += OnRenderFallbackTick;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;

        _view.MouseLeftButtonDown += OnMouseLeftButtonDown;
        _view.MouseMove += OnMouseMove;
        _view.MouseLeftButtonUp += OnMouseLeftButtonUp;
        _view.LostMouseCapture += OnLostMouseCapture;
        _view.MouseRightButtonUp += OnMouseRightButtonUp;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        if (!_diagnosticMode)
        {
            NativeMethods.ApplyPetWindowStyles(_handle, true);
        }
        if (HwndSource.FromHwnd(_handle) is { } source)
        {
            source.AddHook(WindowHook);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _debugPanelWindow.Owner = this;
        var cursor = NativeMethods.CursorPosition;
        _activeWorkArea = NativeMethods.GetWorkingArea(cursor);
        var workArea = ToFloatRect(_activeWorkArea);
        var startPosition = workArea.Center;

        _simulation.Reset(startPosition, _profile.Runtime.InitialHeading);
        _spawnVisualTime = 0f;

        var dpiScale = _handle == nint.Zero
            ? 1f
            : Math.Max(1f, NativeMethods.GetDpiForWindow(_handle) / 96f);
        EnsurePhysicalWindowSize(dpiScale);
        SetPetWindowCenter(startPosition);

        _lastFrameTicks = _clock.ElapsedTicks;
        _lastCompositionTicks = _lastFrameTicks;
        CompositionTarget.Rendering += OnRendering;
        _renderFallbackTimer.Start();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.ElapsedTicks;
        _lastCompositionTicks = now;
        AdvanceFrame(now);
    }

    private void OnRenderFallbackTick(object? sender, EventArgs e)
    {
        var now = _clock.ElapsedTicks;
        var secondsSinceComposition = (now - _lastCompositionTicks) / (double)Stopwatch.Frequency;
        if (secondsSinceComposition >= _compositionFallbackDelay)
        {
            AdvanceFrame(now);
        }
    }

    private void AdvanceFrame(long now)
    {
        var frameDt = (float)((now - _lastFrameTicks) / (double)Stopwatch.Frequency);
        _lastFrameTicks = now;

        if (_displaySettingsDirty)
        {
            _displaySettingsDirty = false;
            _activeWorkArea = NativeMethods.GetWorkingArea(_simulation.Position);
            _lastWindowX = int.MinValue;
            _lastWindowY = int.MinValue;
            ClearDebugPath();
        }

        // Capture can be lost without a WPF MouseUp (for example when another
        // top-level window takes the mouse). Never leave the behavior locked in
        // Grabbed after the physical button/capture has gone away. Resolve it
        // before deriving this frame's navigation area because FinishDrag may
        // switch the active monitor.
        if (_isDragging && (Mouse.Captured != _view || Mouse.LeftButton != MouseButtonState.Pressed))
        {
            FinishDrag(Mouse.Captured != _view);
        }

        var dpiScale = _handle == nint.Zero ? 1f : Math.Max(1f, NativeMethods.GetDpiForWindow(_handle) / 96f);
        EnsurePhysicalWindowSize(dpiScale);
        var workArea = ToFloatRect(_activeWorkArea);
        var navigationArea = SafeInset(workArea, _windowRadiusPixels);
        var lostGripSafety = CreateLostGripSafety(
            workArea,
            _windowRadiusPixels +
            _profile.Behavior.LostGripFall.BottomSafetyInset);

        // Sample the physical screen cursor once per rendered frame and reuse
        // that snapshot for every simulation substep. Core behavior stays
        // deterministic and never depends on WPF focus or client-DIP coords.
        var hasPointer = NativeMethods.TryGetCursorPosition(out var pointerPosition);
        var pointerBlocked =
            _isDragging ||
            _contextMenuOpen ||
            NativeMethods.IsAnyMouseButtonPressed;
        var pointer = new PointerObservation(pointerPosition, hasPointer, pointerBlocked);

        // CompositionTarget.Rendering is already synchronized to DWM. Consume
        // every callback; the application session splits only the simulation
        // into stable 120 Hz steps. The previous 45 Hz gate aliasing reduced
        // 60 Hz displays to about 30 FPS.
        var modelToViewScale = _view.ModelToViewScale;
        var modelToScreenScale = Math.Max(0.0001f, dpiScale * modelToViewScale);
        var simulationFrame = _simulation.Advance(new PetSimulationFrameInput(
            frameDt,
            navigationArea,
            pointer,
            _isDragging,
            modelToScreenScale)
        {
            CaptureDebugFrame = _debugOverlayEnabled,
            DpiScale = dpiScale,
            LostGripSafety = lostGripSafety
        });
        frameDt = simulationFrame.FrameDelta;

        _view.Present(simulationFrame.RenderFrame, simulationFrame.LookDirection);

        if (simulationFrame.DebugFrame is { } debugFrame)
        {
            UpdateDebugOverlay(
                debugFrame,
                dpiScale,
                simulationFrame.Position);
        }

        _spawnVisualTime = Math.Min(
            _profile.Runtime.SpawnFadeDuration,
            _spawnVisualTime + frameDt);
        _view.SetSpawnOpacity(SmoothStep(
            _spawnVisualTime / _profile.Runtime.SpawnFadeDuration));

        SetPetWindowCenter(simulationFrame.Position);

        if (hasPointer)
        {
            UpdateClickThrough(pointerPosition);
        }
    }

    private void UpdateDebugOverlay(
        DebugFrameSnapshot frame,
        float dpiScale,
        Vector2 position)
    {
        _view.SetDebugFrame(frame);
        _debugPanelWindow.UpdateFrame(frame);
        var panelWorkArea = _isDragging
            ? NativeMethods.GetWorkingArea(position)
            : _activeWorkArea;
        _debugPanelWindow.PlaceNear(position, panelWorkArea, dpiScale);
    }

    private void SetDebugOverlay(bool enabled)
    {
        _debugOverlayEnabled = enabled;
        _debugItem.IsChecked = enabled;
        ClearDebugPath();
        if (!enabled)
        {
            _view.ClearDebugFrame();
            _debugPanelWindow.SetEnabled(false);
        }
    }

    private void OnDebugActionRequested(AutonomousAction action)
    {
        if (!_debugOverlayEnabled)
        {
            return;
        }

        var workArea = NativeMethods.GetWorkingArea(_simulation.Position);
        var physicalWorkArea = ToFloatRect(workArea);
        var navigationArea = SafeInset(physicalWorkArea, _windowRadiusPixels);
        var lostGripSafety = CreateLostGripSafety(
            physicalWorkArea,
            _windowRadiusPixels +
            _profile.Behavior.LostGripFall.BottomSafetyInset);
        var result = _simulation.TryPlayDebugAction(
            action,
            navigationArea,
            lostGripSafety);
        if (result.Accepted)
        {
            // A fresh trail makes the selected curve or turn immediately
            // readable instead of mixing it with the previous random state.
            ClearDebugPath();
        }
    }

    private void ClearDebugPath()
    {
        _simulation.ClearDebugPath();
    }



    private static float SmoothStep(float value)
    {
        var t = MathEx.Clamp01(value);
        return t * t * (3f - 2f * t);
    }
}
