using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using DesktopPet.Engine;
using InfiniteLizards.Desktop.Platform;

namespace InfiniteLizards.Desktop;

internal sealed class PetWindow<TSnapshot> : Window, PetWindowHost
{
    private readonly DesktopPetRuntime<TSnapshot> _runtime;
    private readonly IDesktopPetPresenter<TSnapshot> _presenter;
    private readonly Control _view;
    private readonly IOverlayWindowBackend _platform;
    private readonly DispatcherTimer _frameTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly string? _configurationPath;
    private readonly bool _diagnosticMode;
    private readonly DesktopPetDebugSettings _debugSettings;
    private readonly ContextMenu _menu;
    private readonly MenuItem _pauseItem;
    private readonly IDesktopPetRenderCommitSource? _renderCommitSource;
    private readonly DesktopPetSurfacePlacementTransaction? _surfacePlacementTransaction;
    private readonly DesktopPetInputRegionTransaction? _inputRegionTransaction;
    private DesktopPetDiagnosticTelemetry? _diagnosticTelemetry;

    private DisplayTopology? _topology;
    private ActiveDisplaySpace? _displaySpace;
    private string _topologySignature = string.Empty;
    private long _lastFrameTicks;
    private long _lastTopologyRefreshTicks;
    private long _lastDiagnosticTicks;
    private long _lastInputRegionRefreshTicks;
    private long _lastInputRegionVersion;
    private double _lastInputRegionDisplayScale = 1d;
    private Vector2 _dragOffsetWorld;
    private bool _isDragging;
    private bool _contextMenuOpen;
    private bool _clickThrough = true;
    private bool _hasSimulationPosition;
    private bool _preparedForShow;
    private bool _hasAttemptedInputRegion;
    private bool _lastInputRegionAttemptSucceeded;
    private bool _isClosed;
    private bool _renderScaleSynchronizationQueued;
    private bool _debugOverlayEnabled;
    private float _spawnVisualTime;
    private IPointer? _capturedPointer;
    private SurfacePlacement _lastPlacement = new(int.MinValue, int.MinValue, 0, 0);

    public event EventHandler<DesktopPetDiagnosticSnapshot>? DiagnosticSnapshotUpdated;
    public event EventHandler<bool>? DebugOverlayEnabledChanged;
    public event EventHandler? DebugPathResetRequested;

    public bool DebugOverlayEnabled => _debugOverlayEnabled;
    public DesktopPetDebugSettings DebugSettings => _debugSettings;

    Window PetWindowHost.Window => this;

    public PetWindow(
        DesktopPetRuntime<TSnapshot> runtime,
        IDesktopPetPresenter<TSnapshot> presenter,
        string? configurationPath,
        bool diagnosticMode,
        bool debugOverlayEnabled,
        DesktopPetDebugSettings? debugSettings = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _view = presenter.View ?? throw new ArgumentException(
            "The presenter must expose a view.",
            nameof(presenter));
        _configurationPath = configurationPath;
        _diagnosticMode = diagnosticMode;
        _debugSettings = debugSettings ?? DesktopPetDebugSettings.Default;
        _debugOverlayEnabled = debugOverlayEnabled;
        _diagnosticTelemetry = debugOverlayEnabled
            ? new DesktopPetDiagnosticTelemetry(_debugSettings)
            : null;
        _platform = OverlayWindowBackendFactory.Create();
        if (presenter is IDesktopPetRenderCommitSource renderCommitSource)
        {
            _renderCommitSource = renderCommitSource;
            _surfacePlacementTransaction = new DesktopPetSurfacePlacementTransaction();
            _renderCommitSource.RenderCommitAcknowledged += OnRenderCommitAcknowledged;
        }
        if (_platform is IOverlayInputRegionBackend &&
            presenter is IDesktopPetInputRegionProvider &&
            _renderCommitSource is not null)
        {
            _inputRegionTransaction = new DesktopPetInputRegionTransaction();
            PropertyChanged += OnWindowPropertyChanged;
        }

        Title = diagnosticMode ? "Infinite Lizards（诊断）" : "Infinite Lizards";
        Width = _runtime.Metrics.CanvasSizeWorld.Width;
        Height = _runtime.Metrics.CanvasSizeWorld.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        TransparencyBackgroundFallback = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = diagnosticMode;
        ShowActivated = diagnosticMode;
        Focusable = diagnosticMode;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _view;

        (_menu, _pauseItem) = CreateContextMenu();
        _view.ContextMenu = _menu;

        _view.PointerPressed += OnPointerPressed;
        _view.PointerMoved += OnPointerMoved;
        _view.PointerExited += OnPointerExited;
        _view.PointerReleased += OnPointerReleased;
        _view.PointerCaptureLost += OnPointerCaptureLost;
        Opened += OnOpened;
        Closed += OnClosed;

        _frameTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1d / 120d)
        };
        _frameTimer.Tick += OnFrameTimer;
    }

    internal void PrepareForShow()
    {
        if (_preparedForShow)
        {
            return;
        }

        if (!_diagnosticMode &&
            _platform is IOverlayInputRegionBackend &&
            (_presenter is not IDesktopPetInputRegionProvider ||
             _renderCommitSource is null ||
             _inputRegionTransaction is null))
        {
            _platform.Dispose();
            throw new NativeOverlaySafetyException(
                "This production overlay backend requires presenter-supplied " +
                "visible/input geometry and compositor acknowledgements before " +
                "it can show a safe shaped window.");
        }

        _presenter.SetSpawnOpacity(0f);
        try
        {
            _platform.Attach(this, _diagnosticMode);
        }
        catch (NativeOverlaySafetyException)
        {
            _platform.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            if (!_diagnosticMode)
            {
                _platform.Dispose();
                throw;
            }
            Trace.WriteLine($"[InfiniteLizards] native overlay setup failed: {exception}");
        }

        RefreshTopology(force: true);
        var topology = _topology ?? throw new InvalidOperationException("No desktop display is available.");
        var startDisplay = _platform.TryGetGlobalPointer(out var pointer)
            ? topology.FindByDevicePoint(pointer)
            : topology.Primary;
        _displaySpace = new ActiveDisplaySpace(topology, startDisplay);
        var start = startDisplay.WorldWorkingArea;
        _runtime.Reset(new Vector2(
            (float)start.CenterX,
            (float)start.CenterY));
        _hasSimulationPosition = true;
        _spawnVisualTime = 0f;
        ResetSurfacePlacement(_runtime.Position);
        SynchronizeInputRegion(force: true, _clock.ElapsedTicks);
        _preparedForShow = true;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // App calls this before Avalonia's first native Show so AppKit can bind
        // the window to the correct display/Space. Keep the fallback for hosts
        // that construct PetWindow directly.
        PrepareForShow();
        _lastFrameTicks = _clock.ElapsedTicks;
        _lastTopologyRefreshTicks = _lastFrameTicks;
        // Opened is the first lifecycle point at which every backend must have
        // its final native window handle. Establish and verify fail-closed
        // native state synchronously before the timer can advance the spawn
        // fade or dispatch any input.
        _platform.EnsureVisible();
        _frameTimer.Start();
        var topology = _topology ?? throw new InvalidOperationException("No desktop display is available.");
        WriteDiagnostic(
            $"spawn world={_runtime.Position}, native={Position}, displays={topology.Displays.Count}");
    }

    private void OnFrameTimer(object? sender, EventArgs e)
    {
        var now = _clock.ElapsedTicks;
        var frameDelta = (float)((now - _lastFrameTicks) / (double)Stopwatch.Frequency);
        _lastFrameTicks = now;
        if ((now - _lastTopologyRefreshTicks) / (double)Stopwatch.Frequency >= 0.5d)
        {
            _lastTopologyRefreshTicks = now;
            RefreshTopology(force: false);
        }

        var topology = _topology;
        var displaySpace = _displaySpace;
        if (topology is null || displaySpace is null)
        {
            return;
        }

        var hasPointer = _platform.TryGetGlobalPointer(out var devicePointer);
        if (_isDragging && hasPointer)
        {
            DragToPointer(displaySpace, devicePointer);
        }
        var worldPointer = hasPointer ? displaySpace.DeviceToWorld(devicePointer) : default;
        var pointerVector = new Vector2((float)worldPointer.X, (float)worldPointer.Y);

        var activeDisplay = displaySpace.Display;
        var workArea = ToWorldRect(activeDisplay.WorldWorkingArea);
        var navigationArea = SafeInset(workArea, _runtime.Metrics.NavigationRadiusWorld);
        var fullRenderSafety = CreateSafetyArea(
            workArea,
            _runtime.Metrics.FullRenderSafetyInsetWorld);
        var anyMouseButtonPressed = _platform.IsAnyMouseButtonPressed;
        var pointerBlocked =
            _isDragging ||
            _contextMenuOpen ||
            anyMouseButtonPressed;
        var frame = _runtime.Advance(new DesktopPetInput(
            frameDelta,
            navigationArea,
            fullRenderSafety,
            new PointerSample(pointerVector, hasPointer, pointerBlocked),
            _isDragging));

        var wasPresented = DesktopPetPresentationSubmissionPolicy.CanSubmit(
                _inputRegionTransaction,
                _hasAttemptedInputRegion,
                _lastInputRegionAttemptSucceeded) &&
            (_isDragging ||
             (_surfacePlacementTransaction?.CanSubmitPresentation ?? true));
        if (wasPresented)
        {
            _presenter.Present(frame.Snapshot, frame.LookDirection);
            if (_renderCommitSource is { } renderCommitSource &&
                _surfacePlacementTransaction is { } placementTransaction)
            {
                if (!_isDragging)
                {
                    placementTransaction.Stage(
                        renderCommitSource.PresentationVersion,
                        CreateSurfacePresentation(frame.Position));
                }
            }
            else if (!_isDragging)
            {
                // Presenters without compositor acknowledgements retain the
                // legacy synchronous placement contract.  The production
                // lizard presenter implements the versioned source on both
                // Windows and macOS.
                PlaceSurface(frame.Position);
            }
        }
        _spawnVisualTime = Math.Min(
            _runtime.Metrics.SpawnFadeDuration,
            _spawnVisualTime + frame.FrameDelta);
        _presenter.SetSpawnOpacity(SmoothStep(
            _spawnVisualTime / Math.Max(0.001f, _runtime.Metrics.SpawnFadeDuration)));
        SynchronizeInputRegion(force: false, now);
        var presentedCenter = PresentedSurfaceCenter(frame.Position);
        PublishDiagnosticSnapshot(
            frame,
            activeDisplay,
            wasPresented,
            hasPointer,
            pointerBlocked,
            pointerVector,
            presentedCenter);

        if (_diagnosticMode &&
            (now - _lastDiagnosticTicks) / (double)Stopwatch.Frequency >= 1d)
        {
            _lastDiagnosticTicks = now;
            WriteDiagnostic(
                $"frame world={frame.Position}, native={Position}, " +
                $"display={activeDisplay.Id}, work={activeDisplay.WorldWorkingArea}");
        }

        if (hasPointer &&
            (!anyMouseButtonPressed || _isDragging || _contextMenuOpen))
        {
            UpdateClickThrough(devicePointer);
        }
        else if (!hasPointer &&
                 !anyMouseButtonPressed &&
                 !_diagnosticMode &&
                 !_isDragging &&
                 !_contextMenuOpen)
        {
            SetClickThrough(true);
        }
    }

    private void RefreshTopology(bool force)
    {
        var screens = Screens.All;
        if (screens.Count == 0)
        {
            return;
        }

        var signature = string.Join(
            '|',
            screens.Select((screen, index) =>
                $"{DisplayId(screen, index)}:{screen.Bounds}:{screen.WorkingArea}:{screen.Scaling}"));
        if (!force && string.Equals(signature, _topologySignature, StringComparison.Ordinal))
        {
            return;
        }

        var descriptors = screens.Select((screen, index) => new DisplayDescriptor(
            DisplayId(screen, index),
            ToDeviceRect(screen.Bounds),
            ToDeviceRect(screen.WorkingArea),
            Math.Max(0.25d, screen.Scaling),
            screen.IsPrimary));
        var newTopology = new DisplayTopology(descriptors);
        _topology = newTopology;
        _topologySignature = signature;
        _lastPlacement = new SurfacePlacement(int.MinValue, int.MinValue, 0, 0);
        if (_diagnosticMode)
        {
            foreach (var display in newTopology.Displays)
            {
                WriteDiagnostic(
                    $"screen {display.Id}: device={display.DeviceBounds}, " +
                    $"workDevice={display.DeviceWorkingArea}, scale={display.Scale:F3}, " +
                    $"world={display.WorldBounds}, workWorld={display.WorldWorkingArea}");
            }
        }

        if (!_hasSimulationPosition || _displaySpace is null)
        {
            return;
        }

        var positionBeforeRebase = _runtime.Position;
        var presentedCenterBeforeRebase = PresentedSurfaceCenter(positionBeforeRebase);
        var estimatedPointerBeforeRebase =
            positionBeforeRebase + _dragOffsetWorld;
        var transition = _displaySpace.ReplaceTopology(
            newTopology,
            new WorldPoint(positionBeforeRebase.X, positionBeforeRebase.Y));
        var rebased = new Vector2(
            (float)transition.RebasedWorldPoint.X,
            (float)transition.RebasedWorldPoint.Y);
        var safeArea = SafeInset(
            ToWorldRect(transition.Current.WorldWorkingArea),
            _runtime.Metrics.FullRenderRadiusWorld);
        rebased = safeArea.Clamp(rebased);
        var rebasedPresentedCenter = safeArea.Clamp(
            DesktopPetSurfaceCoordinateMapper.RebaseCenter(
                transition.Previous,
                transition.Current,
                presentedCenterBeforeRebase));
        var rebaseDelta = rebased - _runtime.Position;
        _runtime.RebaseWorldPosition(rebaseDelta);
        if (_isDragging)
        {
            if (_platform.TryGetGlobalPointer(out var pointer))
            {
                var pointerWorld = _displaySpace.DeviceToWorld(pointer);
                _dragOffsetWorld = new Vector2(
                    (float)pointerWorld.X,
                    (float)pointerWorld.Y) - _runtime.Position;
            }
            else
            {
                // Preserve the physical grab point even if global pointer
                // sampling is briefly unavailable during a DPI/hot-plug
                // transition. Keeping the old world-space offset would jump
                // on the next sample when the two displays use different
                // scales or the rebased center had to be clamped.
                var estimatedPointerDevice = transition.Previous.WorldToDevice(
                    new WorldPoint(
                        estimatedPointerBeforeRebase.X,
                        estimatedPointerBeforeRebase.Y));
                var estimatedPointerAfter = transition.Current.DeviceToWorld(
                    estimatedPointerDevice);
                _dragOffsetWorld = new Vector2(
                    (float)estimatedPointerAfter.X,
                    (float)estimatedPointerAfter.Y) - _runtime.Position;
            }
        }
        // The visible pose can trail the simulation while a render fence is in
        // flight. Rebase each center independently so topology refresh cannot
        // teleport old backing pixels to the newer simulation position.
        ResetSurfacePlacement(rebasedPresentedCenter);
        SynchronizeInputRegion(force: true, _clock.ElapsedTicks);
    }

    private DesktopPetSurfacePresentation CreateSurfacePresentation(Vector2 center)
    {
        var displaySpace = _displaySpace ??
            throw new InvalidOperationException("No active display is available.");
        var placement = SurfacePlacement.FromCenter(
            displaySpace.Display,
            new WorldPoint(center.X, center.Y),
            _runtime.Metrics.CanvasSizeWorld);
        return new DesktopPetSurfacePresentation(
            center,
            placement,
            displaySpace.Display.Scale);
    }

    private void ResetSurfacePlacement(Vector2 center)
    {
        var presentation = CreateSurfacePresentation(center);
        PlaceSurface(presentation);
        if (_surfacePlacementTransaction is { } transaction)
        {
            if (transaction.HasCommittedPresentation)
            {
                transaction.ResetTo(presentation);
            }
            else
            {
                transaction.Initialize(
                    _renderCommitSource?.PresentationVersion ?? 0,
                    presentation);
            }
            if (_presenter is IDesktopPetPresentationHitTester hitTester)
            {
                hitTester.DiscardUncommittedPresentations(
                    transaction.LatestCommittedVersion);
            }
        }
    }

    private void PlaceSurface(Vector2 center)
    {
        PlaceSurface(CreateSurfacePresentation(center));
    }

    private void PlaceSurface(DesktopPetSurfacePresentation presentation)
    {
        if (presentation.Placement == _lastPlacement)
        {
            return;
        }

        _platform.PlaceSurface(
            this,
            presentation.Placement,
            presentation.DisplayScale);
        // Cache only an accepted native placement. A backend failure must not
        // make the host suppress the same safety-critical retry later.
        _lastPlacement = presentation.Placement;
    }

    private Vector2 PresentedSurfaceCenter(Vector2 fallback) =>
        _surfacePlacementTransaction is { HasCommittedPresentation: true } transaction
            ? transaction.Committed.Center
            : fallback;

    private bool HitTestPresentedSurface(Point viewPoint)
    {
        if (_surfacePlacementTransaction is
                { HasCommittedPresentation: true } transaction &&
            _presenter is IDesktopPetPresentationHitTester hitTester)
        {
            return hitTester.HitTestPresentation(
                transaction.LatestCommittedVersion,
                viewPoint);
        }

        return _presenter.HitTest(viewPoint);
    }

    private void UpdateClickThrough(DevicePoint pointerDevice)
    {
        if (_diagnosticMode || _isDragging || _contextMenuOpen)
        {
            SetClickThrough(false);
            return;
        }
        if (_spawnVisualTime < _runtime.Metrics.SpawnFadeDuration * 0.2f)
        {
            SetClickThrough(true);
            return;
        }

        if (_surfacePlacementTransaction is
            not { HasCommittedPresentation: true } transaction)
        {
            SetClickThrough(true);
            return;
        }

        var presented = transaction.Committed;
        var local = DesktopPetSurfaceCoordinateMapper.DeviceToView(
            presented,
            pointerDevice);
        SetClickThrough(!HitTestPresentedSurface(local));
    }

    private void SetClickThrough(bool value)
    {
        if (_diagnosticMode || _clickThrough == value)
        {
            return;
        }

        try
        {
            _platform.SetClickThrough(value);
            _clickThrough = value;
        }
        catch (NativeOverlaySafetyException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[InfiniteLizards] click-through update failed: {exception}");
        }
    }

    private void SynchronizeInputRegion(bool force, long nowTicks)
    {
        if (_platform is not IOverlayInputRegionBackend backend ||
            _presenter is not IDesktopPetInputRegionProvider provider ||
            _displaySpace is not { } displaySpace)
        {
            return;
        }

        var displayScale = displaySpace.Display.Scale;
        var version = _lastInputRegionVersion;
        var nativeBoundaryReached = false;
        try
        {
            version = provider.InputRegionVersion;
            if (!DesktopPetInputRegionRefreshPolicy.ShouldRefresh(
                    force,
                    _hasAttemptedInputRegion,
                    _lastInputRegionAttemptSucceeded,
                    version,
                    _lastInputRegionVersion,
                    displayScale,
                    _lastInputRegionDisplayScale,
                    nowTicks,
                    _lastInputRegionRefreshTicks,
                    provider.InputRegionRefreshRate,
                    Stopwatch.Frequency))
            {
                return;
            }

            var region = provider.CaptureInputRegion();
            var renderScale = CurrentRenderScale(displayScale);
            DesktopPetInputRegionInstallation installation;
            if (_inputRegionTransaction is { } transaction)
            {
                if (version > transaction.LatestSubmittedVersion)
                {
                    installation = transaction.Stage(
                        version,
                        region,
                        renderScale,
                        displayScale);
                }
                else if (version == transaction.LatestSubmittedVersion &&
                         transaction.HasInstallation)
                {
                    installation = transaction.Current;
                }
                else
                {
                    throw new InvalidOperationException(
                        "The presenter input-region version regressed.");
                }
            }
            else
            {
                installation = new DesktopPetInputRegionInstallation(
                    version,
                    [new DesktopPetRasterizedInputRegionPose(
                        region,
                        version,
                        isAcknowledged: false,
                        [renderScale, displayScale])]);
            }

            nativeBoundaryReached = true;
            backend.SetInputRegion(
                this,
                installation,
                displayScale);
            _lastInputRegionVersion = version;
            _lastInputRegionDisplayScale = displayScale;
            _lastInputRegionRefreshTicks = nowTicks;
            _hasAttemptedInputRegion = true;
            _lastInputRegionAttemptSucceeded = true;
        }
        catch (NativeOverlaySafetyException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (!nativeBoundaryReached)
            {
                // Capture/version failures make the transaction contents
                // unknowable. Native failures happen after a complete immutable
                // request was staged, so retain that request for bounded retry.
                _inputRegionTransaction?.Reset();
            }

            // Capture may fail before the backend sees the geometry, so always
            // revoke any older shape. A Clear failure means the full-window
            // input invariant is unknown and must terminate production.
            try
            {
                backend.ClearInputRegion(this);
            }
            catch (NativeOverlaySafetyException)
            {
                throw;
            }
            catch (Exception clearException)
            {
                throw new NativeOverlaySafetyException(
                    "The input-region update failed and the backend could not " +
                    "establish a fail-closed window.",
                    new AggregateException(exception, clearException));
            }
            _lastInputRegionVersion = version;
            _lastInputRegionDisplayScale = displayScale;
            _lastInputRegionRefreshTicks = nowTicks;
            _hasAttemptedInputRegion = true;
            _lastInputRegionAttemptSucceeded = false;
            Trace.WriteLine($"[InfiniteLizards] input-region update failed: {exception}");
        }
    }

    private double CurrentRenderScale(double fallbackDisplayScale)
    {
        var topLevel = TopLevel.GetTopLevel(_view);
        var renderScale = topLevel?.RenderScaling ?? fallbackDisplayScale;
        DesktopPetRasterScaleSet.RequireValid(renderScale, nameof(renderScale));
        return renderScale;
    }

    private void OnRenderCommitAcknowledged(
        object? sender,
        DesktopPetRenderCommitAcknowledgedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(
                () => OnRenderCommitAcknowledged(sender, e),
                DispatcherPriority.Normal);
            return;
        }
        if (_isClosed)
        {
            return;
        }

        if (_surfacePlacementTransaction is { } placementTransaction)
        {
            placementTransaction.TryAcknowledge(
                e.Sequence,
                e.Version,
                presentation => ApplyAcknowledgedSurface(
                    e.Version,
                    presentation));
        }

        if (_inputRegionTransaction is { } regionTransaction &&
            regionTransaction.TryAcknowledge(
                e.Sequence,
                e.Version,
                e.RenderScale,
                out _))
        {
            // The Windows region transaction is independently safe to narrow.
            // This callback is dispatched from the compositor fence to the UI
            // thread, never from Render or the render thread itself.
            SynchronizeInputRegion(force: true, _clock.ElapsedTicks);
        }
    }

    private void ApplyAcknowledgedSurface(
        long presentationVersion,
        DesktopPetSurfacePresentation presentation)
    {
        try
        {
            // Apply the exact placement captured beside this presenter
            // version. Never recalculate it from the now-newer simulation or
            // current display mapping.
            PlaceSurface(presentation);
            if (_presenter is IDesktopPetPresentationHitTester hitTester)
            {
                hitTester.CommitPresentation(presentationVersion);
            }
        }
        catch (NativeOverlaySafetyException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A successfully rendered pose has no second fence to wake a
            // retry. Continuing would leave native placement and committed
            // hit geometry permanently divergent, so fail-stop explicitly.
            throw new NativeOverlaySafetyException(
                $"The native surface rejected committed presenter pose " +
                $"v{presentationVersion}.",
                exception);
        }
    }

    private void OnWindowPropertyChanged(
        object? sender,
        AvaloniaPropertyChangedEventArgs e)
    {
        if (_isClosed ||
            !_preparedForShow ||
            _inputRegionTransaction is null ||
            !string.Equals(
                e.Property.Name,
                nameof(RenderScaling),
                StringComparison.Ordinal) ||
            _renderScaleSynchronizationQueued)
        {
            return;
        }

        _renderScaleSynchronizationQueued = true;
        // RenderScaling can change inside WM_DPICHANGED/SetWindowPos. Defer
        // USER32 work until the current native call unwinds, but use Send so
        // the conservative old/new-scale HRGN is installed before Avalonia's
        // queued Render job can rasterize at the new scale.
        Dispatcher.UIThread.Post(
            SynchronizeRenderScaleChange,
            DispatcherPriority.Send);
    }

    private void SynchronizeRenderScaleChange()
    {
        _renderScaleSynchronizationQueued = false;
        if (_isClosed || _displaySpace is null)
        {
            return;
        }

        SynchronizeInputRegion(force: true, _clock.ElapsedTicks);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_view);
        if (!point.Properties.IsLeftButtonPressed ||
            _isDragging ||
            !HitTestPresentedSurface(point.Position) ||
            _displaySpace is null ||
            !_platform.TryGetGlobalPointer(out var devicePointer))
        {
            return;
        }

        var pointer = _displaySpace.DeviceToWorld(devicePointer);
        var pointerWorld = new Vector2((float)pointer.X, (float)pointer.Y);
        var visibleCenter = PresentedSurfaceCenter(_runtime.Position);
        var alignmentDelta = visibleCenter - _runtime.Position;
        if (alignmentDelta != Vector2.Zero)
        {
            // Direct manipulation begins from what the user can actually see,
            // not from simulation frames still waiting on the compositor.
            _runtime.RebaseWorldPosition(alignmentDelta);
        }
        if (_surfacePlacementTransaction is { } transaction)
        {
            transaction.DiscardPending();
            if (_presenter is IDesktopPetPresentationHitTester hitTester)
            {
                hitTester.DiscardUncommittedPresentations(
                    transaction.LatestCommittedVersion);
            }
        }
        _dragOffsetWorld = pointerWorld - visibleCenter;
        _isDragging = true;
        SetClickThrough(false);
        _capturedPointer = e.Pointer;
        e.Pointer.Capture(_view);
        _runtime.BeginPrimaryInteraction(_presenter.ViewToModel(point.Position));
        RequestDebugPathReset();
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging)
        {
            if (!_diagnosticMode &&
                !_contextMenuOpen &&
                !_platform.IsAnyMouseButtonPressed)
            {
                SetClickThrough(!HitTestPresentedSurface(e.GetPosition(_view)));
            }
            return;
        }

        if (_displaySpace is null ||
            !_platform.TryGetGlobalPointer(out var devicePointer))
        {
            return;
        }

        DragToPointer(_displaySpace, devicePointer);
        e.Handled = true;
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (!_diagnosticMode &&
            !_isDragging &&
            !_contextMenuOpen &&
            !_platform.IsAnyMouseButtonPressed)
        {
            SetClickThrough(true);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging && e.InitialPressMouseButton == MouseButton.Left)
        {
            FinishDrag();
            e.Handled = true;
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDragging)
        {
            FinishDrag();
        }
    }

    private void FinishDrag()
    {
        _isDragging = false;
        RequestDebugPathReset();
        var capturedPointer = _capturedPointer;
        _capturedPointer = null;
        capturedPointer?.Capture(null!);

        var displaySpace = _displaySpace;
        if (displaySpace is null)
        {
            _runtime.EndPrimaryInteraction(_runtime.Position);
            return;
        }

        var display = displaySpace.Display;
        var releaseArea = SafeInset(
            ToWorldRect(display.WorldWorkingArea),
            _runtime.Metrics.FullRenderRadiusWorld);
        var settled = releaseArea.Clamp(_runtime.Position);
        _runtime.EndPrimaryInteraction(settled);
        ResetSurfacePlacement(settled);
        SetClickThrough(true);
    }

    private (ContextMenu Menu, MenuItem Pause) CreateContextMenu()
    {
        var pause = new MenuItem { Header = "暂停散步" };
        pause.Click += (_, _) =>
        {
            var paused = _runtime.TogglePaused();
            pause.Header = paused ? "继续散步" : "暂停散步";
        };

        var center = new MenuItem { Header = "回到当前屏幕中央" };
        center.Click += (_, _) =>
        {
            if (_displaySpace is null)
            {
                return;
            }
            var display = _displaySpace.Display;
            _runtime.MoveTo(new Vector2(
                (float)display.WorldWorkingArea.CenterX,
                (float)display.WorldWorkingArea.CenterY));
            RequestDebugPathReset();
        };

        var debugOverlay = new MenuItem
        {
            Header = "显示调试信息",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _debugOverlayEnabled
        };
        debugOverlay.Click += (_, _) =>
            SetDebugOverlayEnabled(debugOverlay.IsChecked);

        var editConfiguration = new MenuItem
        {
            Header = "编辑个体配置（重启后生效）",
            IsEnabled = !string.IsNullOrWhiteSpace(_configurationPath)
        };
        editConfiguration.Click += (_, _) => OpenConfigurationFile();

        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) =>
        {
            if (Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        };

        var menu = new ContextMenu
        {
            ItemsSource = new object[]
            {
                pause,
                center,
                debugOverlay,
                new Separator(),
                editConfiguration,
                new Separator(),
                exit
            }
        };
        menu.Opening += (_, _) =>
        {
            _contextMenuOpen = true;
            pause.Header = _runtime.IsPaused ? "继续散步" : "暂停散步";
            debugOverlay.IsChecked = _debugOverlayEnabled;
            SetClickThrough(false);
        };
        menu.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            SetClickThrough(true);
        };
        return (menu, pause);
    }

    public void SetDebugOverlayEnabled(bool enabled)
    {
        if (_debugOverlayEnabled == enabled)
        {
            return;
        }
        _debugOverlayEnabled = enabled;
        if (enabled)
        {
            _diagnosticTelemetry ??= new DesktopPetDiagnosticTelemetry(_debugSettings);
        }
        DebugOverlayEnabledChanged?.Invoke(this, enabled);
    }

    private void RequestDebugPathReset()
    {
        if (_debugOverlayEnabled)
        {
            DebugPathResetRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OpenConfigurationFile()
    {
        if (string.IsNullOrWhiteSpace(_configurationPath))
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(_configurationPath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[InfiniteLizards] cannot open configuration: {exception}");
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _frameTimer.Stop();
        _frameTimer.Tick -= OnFrameTimer;
        _view.PointerExited -= OnPointerExited;
        if (_renderCommitSource is not null)
        {
            _renderCommitSource.RenderCommitAcknowledged -= OnRenderCommitAcknowledged;
            PropertyChanged -= OnWindowPropertyChanged;
        }
        _surfacePlacementTransaction?.Close();
        _platform.Dispose();
    }

    private static string DisplayId(Screen screen, int index)
    {
        var handle = screen.TryGetPlatformHandle();
        if (handle is { Handle: not 0 })
        {
            return $"{handle.HandleDescriptor}:{handle.Handle}";
        }

        return string.IsNullOrWhiteSpace(screen.DisplayName)
            ? $"display:{screen.Bounds}:{index}"
            : $"{screen.DisplayName}:{screen.Bounds}";
    }

    private void DragToPointer(
        ActiveDisplaySpace displaySpace,
        DevicePoint pointerDevice)
    {
        var pointerBefore = displaySpace.DeviceToWorld(pointerDevice);
        var desiredBefore = new WorldPoint(
            pointerBefore.X - _dragOffsetWorld.X,
            pointerBefore.Y - _dragOffsetWorld.Y);
        var desiredDevice = displaySpace.WorldToDevice(desiredBefore);
        var transition = displaySpace.SwitchForDevicePoint(
            desiredDevice,
            desiredBefore);
        if (!transition.Changed)
        {
            _runtime.DragTo(new Vector2(
                (float)desiredBefore.X,
                (float)desiredBefore.Y));
            ResetSurfacePlacement(_runtime.Position);
            return;
        }

        _dragOffsetWorld = DisplaySpaceDragTransition.Apply(
            _runtime,
            displaySpace,
            transition,
            desiredBefore,
            pointerDevice);
        _lastPlacement = new SurfacePlacement(int.MinValue, int.MinValue, 0, 0);
        ResetSurfacePlacement(_runtime.Position);
    }

    private static DeviceRect ToDeviceRect(PixelRect value) => new(
        value.X,
        value.Y,
        value.Right,
        value.Bottom);

    private static WorldRect ToWorldRect(WorldRectD value) => new(
        (float)value.Left,
        (float)value.Top,
        (float)value.Right,
        (float)value.Bottom);

    private static WorldRect SafeInset(WorldRect rect, float inset)
    {
        var maximum = Math.Max(1f, Math.Min(rect.Width, rect.Height) * 0.5f - 1f);
        return rect.Inset(Math.Min(Math.Max(0f, inset), maximum));
    }

    private static SafetyArea CreateSafetyArea(WorldRect workArea, float requiredInset)
    {
        var available =
            float.IsFinite(requiredInset) &&
            requiredInset >= 0f &&
            workArea.Width >= requiredInset * 2f &&
            workArea.Height >= requiredInset * 2f;
        return new SafetyArea(
            available
                ? workArea.Inset(requiredInset)
                : SafeInset(workArea, Math.Max(0f, requiredInset)),
            available);
    }

    private static float SmoothStep(float value)
    {
        var t = Math.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private void WriteDiagnostic(string message)
    {
        if (!_diagnosticMode)
        {
            return;
        }

        var line = $"[InfiniteLizards] {message}";
        Trace.WriteLine(line);
        Console.Error.WriteLine(line);
    }

    private void PublishDiagnosticSnapshot(
        in DesktopPetFrame<TSnapshot> frame,
        MappedDisplay activeDisplay,
        bool wasPresented,
        bool pointerAvailable,
        bool pointerBlocked,
        Vector2 pointerPosition,
        Vector2 presentedCenter)
    {
        if (!_debugOverlayEnabled || _diagnosticTelemetry is null)
        {
            return;
        }

        var providerVersion = _presenter is IDesktopPetInputRegionProvider provider
            ? provider.InputRegionVersion
            : -1L;
        var regionBackendAvailable = _platform is IOverlayInputRegionBackend;
        var transaction = _inputRegionTransaction;
        var installation = transaction is { HasInstallation: true }
            ? transaction.Current
            : null;
        var acknowledged = installation?.Poses.Count(pose => pose.IsAcknowledged) ?? 0;
        var snapshot = _diagnosticTelemetry.Capture(
            frame.FrameDelta,
            _runtime.Timing.SimulationStep,
            frame.SimulationSteps,
            presentedCenter,
            activeDisplay.Id,
            activeDisplay.Scale,
            pointerAvailable,
            pointerBlocked,
            pointerPosition,
            pointerAvailable
                ? Vector2.Distance(pointerPosition, presentedCenter)
                : float.NaN,
            frame.IsPaused,
            _isDragging,
            wasPresented,
            providerVersion,
            regionBackendAvailable,
            _hasAttemptedInputRegion,
            _lastInputRegionAttemptSucceeded,
            transaction?.LatestSubmittedVersion ?? -1L,
            transaction?.PendingPoseCount ?? 0,
            acknowledged);
        DiagnosticSnapshotUpdated?.Invoke(this, snapshot);
    }
}
