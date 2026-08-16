using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using DesktopPet.Engine;

namespace InfiniteLizards.Desktop.Platform;

internal sealed class Win32OverlayWindowBackend :
    IOverlayWindowBackend,
    IOverlayInputRegionBackend
{
    private const int GwlExStyle = -20;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmMouseActivate = 0x0021;
    private const int HtTransparent = -1;
    private const int MaNoActivate = 3;
    private const int VkLeftButton = 0x01;
    private const int VkRightButton = 0x02;
    private const int VkMiddleButton = 0x04;
    private const int VkXButton1 = 0x05;
    private const int VkXButton2 = 0x06;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private Window? _windowOwner;
    private nint _window;
    private Win32Properties.CustomWindowStylesCallback? _windowStylesCallback;
    private Win32Properties.CustomWndProcHookCallback? _wndProcHookCallback;
    private Win32NativeAcceptanceTelemetry? _nativeAcceptanceTelemetry;
    private bool _nativeOverlayEnabled;
    private bool _hasVerifiedWindowHandle;
    private Win32InputRegionState _inputRegionState;
    private int _windowHandleEpoch;
    private DesktopPetInputRegionInstallation? _requestedInputRegion;
    private double _requestedDisplayScale = 1d;
    private bool _requestedClickThrough = true;
    private Win32InputRegionSignature? _installedInputRegionSignature;
    private SurfacePlacement? _lastPlacement;

    public bool IsAnyMouseButtonPressed =>
        IsKeyPressed(VkLeftButton) ||
        IsKeyPressed(VkRightButton) ||
        IsKeyPressed(VkMiddleButton) ||
        IsKeyPressed(VkXButton1) ||
        IsKeyPressed(VkXButton2);

    public void Attach(Window window, bool interactiveDiagnosticMode)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_windowOwner is not null)
        {
            throw new InvalidOperationException("The Windows overlay backend is already attached.");
        }

        _windowOwner = window;
        _ = TryRefreshWindowHandle();
        if (interactiveDiagnosticMode)
        {
            return;
        }
        if (nint.Size != 8)
        {
            ResetAttachment();
            throw new PlatformNotSupportedException(
                "Infinite Lizards supports 64-bit Windows hosts only.");
        }

        _nativeAcceptanceTelemetry = Win32NativeAcceptanceTelemetry.TryCreate();
        _windowStylesCallback = ApplyWindowStyles;
        _wndProcHookCallback = WindowProcedureHook;

        try
        {
            // Avalonia rebuilds GWL_EXSTYLE while showing a Window. Registering
            // these callbacks before the first Show keeps the overlay contract
            // intact through that rebuild and all later property changes.
            Win32Properties.AddWindowStylesCallback(window, _windowStylesCallback);
            Win32Properties.AddWndProcHookCallback(window, _wndProcHookCallback);
            _nativeOverlayEnabled = true;
            if (TryRefreshWindowHandle())
            {
                InstallFailClosedWindowRegion();
            }
        }
        catch
        {
            RemoveAvaloniaCallbacks();
            ResetAttachment();
            throw;
        }
    }

    public bool TryGetGlobalPointer(out DevicePoint point)
    {
        if (GetCursorPos(out var native))
        {
            point = new DevicePoint(native.X, native.Y);
            return true;
        }

        point = default;
        return false;
    }

    public void SetClickThrough(bool clickThrough)
    {
        _requestedClickThrough = clickThrough;
        if (!_nativeOverlayEnabled)
        {
            return;
        }

        if (!TryRefreshWindowHandleForRuntime("click-through update"))
        {
            return;
        }

        // SetWindowRgn defines the visible silhouette as well as the HWND shape,
        // so clearing it would erase rendering. EnableWindow is the independent
        // USER32 input switch: a disabled HWND keeps drawing but is skipped by
        // native hit testing. Its return value describes the previous state,
        // therefore ApplyNativeInputEnabled verifies the resulting state.
        if (!clickThrough &&
            _requestedInputRegion is not null &&
            _inputRegionState != Win32InputRegionState.Installed)
        {
            ApplyRequestedInputRegion(force: true);
        }
        else
        {
            ApplyNativeInputEnabled(ShouldEnableNativeInput);
        }
        ApplyNativeExtendedStyle(RequiresTransparentFallback);
    }

    public void SetInputRegion(
        Window window,
        DesktopPetInputRegionInstallation installation,
        double displayScale)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(installation);
        if (!ReferenceEquals(window, _windowOwner))
        {
            throw new InvalidOperationException(
                "The input region belongs to a different overlay window.");
        }
        if (!double.IsFinite(displayScale) || displayScale <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(displayScale));
        }

        _requestedInputRegion = installation;
        _requestedDisplayScale = displayScale;
        if (!_nativeOverlayEnabled || !TryRefreshWindowHandleForRuntime("input-region update"))
        {
            return;
        }

        ApplyRequestedInputRegion(force: false);
    }

    public void ClearInputRegion(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!ReferenceEquals(window, _windowOwner))
        {
            throw new InvalidOperationException(
                "The input region belongs to a different overlay window.");
        }

        _requestedInputRegion = null;
        _installedInputRegionSignature = null;
        if (!_nativeOverlayEnabled || !TryRefreshWindowHandleForRuntime("input-region clear"))
        {
            return;
        }

        InstallFailClosedWindowRegion();
    }

    public void PlaceSurface(
        Window window,
        SurfacePlacement placement,
        double displayScale)
    {
        if (!double.IsFinite(displayScale) || displayScale <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(displayScale));
        }

        OverlayWindowPlacement.ApplyManaged(window, placement, displayScale);
        _lastPlacement = placement;
        _requestedDisplayScale = displayScale;
        if (!TryRefreshWindowHandleForRuntime("surface placement"))
        {
            return;
        }

        var actualScale = window.RenderScaling;
        if (double.IsFinite(actualScale) &&
            double.IsFinite(displayScale) &&
            Math.Abs(actualScale - displayScale) > 0.001d)
        {
            Trace.WriteLine(
                "[InfiniteLizards] Win32 DPI transition is still settling after managed Position: " +
                $"targetScale={displayScale:F4}, hwndScale={actualScale:F4}. " +
                "Native client placement will be measured and retried after SetWindowPos.");
        }

        PlaceNativeSurface(placement);
        TryApplyRequestedInputRegionForLifecycle(force: false, "placement");
    }

    private void PlaceNativeSurface(SurfacePlacement placement)
    {
        // SurfacePlacement is the desired Skia/client rectangle in physical
        // desktop pixels. SetWindowPos accepts an outer-window rectangle, so
        // preserve the actual frame insets that Windows reports for this HWND.
        // A first SetWindowPos can itself finish a cross-monitor DPI change.
        // Re-measure once in that case so the final client rectangle is exact.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var insets = MeasureWindowInsets();
            var outer = Win32ClientRectPlacement.ToOuterWindow(placement, insets);
            if (!SetWindowPos(
                    _window,
                    nint.Zero,
                    outer.X,
                    outer.Y,
                    outer.Width,
                    outer.Height,
                    SwpNoZOrder | SwpNoActivate))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            if (ClientPlacementMatches(placement, out _))
            {
                return;
            }

            if (attempt == 0)
            {
                Trace.WriteLine(
                    "[InfiniteLizards] Win32 client rectangle changed during a DPI/frame transition; " +
                    "retrying with the post-move native insets.");
            }
        }

        VerifyClientPlacement(placement);
    }

    public void EnsureVisible()
    {
        if (!TryRefreshWindowHandle())
        {
            if (_nativeOverlayEnabled)
            {
                throw new NativeOverlaySafetyException(
                    "Avalonia did not expose a valid HWND after the production " +
                    "overlay opened, so a fail-closed window region cannot be verified.");
            }
            Trace.WriteLine(
                "[InfiniteLizards] Win32 HWND is still unavailable after Opened; " +
                "native overlay placement cannot be verified.");
            Debug.Fail("Avalonia must expose an HWND after the Window has opened.");
            return;
        }

        if (_lastPlacement is { } placement)
        {
            PlaceNativeSurface(placement);
        }

        if (_nativeOverlayEnabled)
        {
            if (_requestedInputRegion is null)
            {
                InstallFailClosedWindowRegion();
            }
            else
            {
                TryApplyRequestedInputRegionForLifecycle(force: true, "post-Show verification");
            }

            // This runs after Opened. It both repairs external style changes and
            // verifies that Avalonia's first-Show style rebuild kept our contract.
            ApplyNativeExtendedStyle(RequiresTransparentFallback);
            VerifyExtendedStyle(RequiresTransparentFallback);
            VerifyNativeInputEnabled(ShouldEnableNativeInput);
            _hasVerifiedWindowHandle = true;
            _nativeAcceptanceTelemetry?.MarkReady(_window);
        }
    }

    public void Dispose()
    {
        RemoveAvaloniaCallbacks();
        ResetAttachment();
    }

    private static bool IsKeyPressed(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private (uint style, uint exStyle) ApplyWindowStyles(uint style, uint exStyle) =>
        (
            Win32OverlayStyle.ComposeWindowStyle(style, ShouldEnableNativeInput),
            Win32OverlayStyle.ComposeExtendedStyle(exStyle, RequiresTransparentFallback));

    private nint WindowProcedureHook(
        nint window,
        uint message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (_nativeAcceptanceTelemetry is { } telemetry)
        {
            if (telemetry.TryHandleControl(
                    window,
                    message,
                    wParam,
                    lParam,
                    out var controlResult))
            {
                handled = true;
                return controlResult;
            }

            telemetry.ObserveWindowMessage(message, wParam);
        }

        if (message == WmNcHitTest && RequiresTransparentFallback)
        {
            handled = true;
            return new nint(HtTransparent);
        }

        if (message == WmMouseActivate)
        {
            handled = true;
            return new nint(MaNoActivate);
        }

        return nint.Zero;
    }

    private void ApplyNativeExtendedStyle(bool clickThrough)
    {
        var current = unchecked((uint)GetWindowLongPtrChecked(_window, GwlExStyle).ToInt64());
        var desired = Win32OverlayStyle.ComposeExtendedStyle(current, clickThrough);
        if (desired != current)
        {
            SetWindowLongPtrChecked(_window, GwlExStyle, new nint(desired));
            try
            {
                ApplyFrameChange();
            }
            catch (Exception frameChangeException)
            {
                try
                {
                    SetWindowLongPtrChecked(_window, GwlExStyle, new nint(current));
                    ApplyFrameChange();
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "Failed to apply the Win32 overlay style and to restore its previous value.",
                        frameChangeException,
                        rollbackException);
                }

                throw;
            }
        }
    }

    private void ApplyNativeInputEnabled(bool enabled)
    {
        VerifyCurrentWindowHandle("changing native input state");
        _ = EnableWindow(_window, enabled);
        VerifyCurrentWindowHandle("verifying native input state");
        if (IsWindowEnabled(_window) != enabled)
        {
            throw new NativeOverlaySafetyException(
                enabled
                    ? "USER32 did not enable the shaped overlay for pet interaction."
                    : "USER32 did not disable the shaped overlay for click-through input.");
        }
    }

    private void ApplyRequestedInputRegion(bool force)
    {
        if (!_nativeOverlayEnabled ||
            _window == nint.Zero ||
            _requestedInputRegion is not { } installation)
        {
            return;
        }

        var insets = MeasureWindowInsets();
        var rasterPlan = Win32InputRegionRasterPlan.Create(
            installation,
            _requestedDisplayScale,
            _windowOwner?.RenderScaling ?? double.NaN);
        var signature = new Win32InputRegionSignature(
            _windowHandleEpoch,
            installation.Revision,
            BitConverter.DoubleToInt64Bits(_requestedDisplayScale),
            rasterPlan.Signature,
            insets);
        if (!force &&
            _inputRegionState == Win32InputRegionState.Installed &&
            _installedInputRegionSignature == signature)
        {
            return;
        }

        nint ownedRegion = nint.Zero;
        try
        {
            ownedRegion = Win32InputRegionBuilder.Build(
                rasterPlan.Rasterizations,
                insets);
            Marshal.SetLastPInvokeError(0);
            if (!SetWindowRgn(_window, ownedRegion, redraw: true))
            {
                throw NativeFailure("SetWindowRgn");
            }

            // USER32 owns the HRGN after success; deleting it here would leave
            // the HWND with an invalid region handle.
            ownedRegion = nint.Zero;
            _inputRegionState = Win32InputRegionState.Installed;
            _installedInputRegionSignature = signature;
            ApplyNativeExtendedStyle(clickThrough: false);
            ApplyNativeInputEnabled(ShouldEnableNativeInput);
        }
        catch (Exception regionException)
        {
            Win32InputRegionBuilder.DeleteOwnedRegion(ownedRegion);
            try
            {
                InstallFailClosedWindowRegion();
            }
            catch (NativeOverlaySafetyException safetyException)
            {
                throw new NativeOverlaySafetyException(
                    "The shaped Win32 input region failed and an empty safety region " +
                    "could not be restored.",
                    new AggregateException(regionException, safetyException));
            }

            throw;
        }
    }

    private void TryApplyRequestedInputRegionForLifecycle(bool force, string stage)
    {
        try
        {
            ApplyRequestedInputRegion(force);
        }
        catch (NativeOverlaySafetyException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // ApplyRequestedInputRegion has already restored the empty HRGN.
            // Retain the immutable request so the next host tick can retry.
            Trace.WriteLine(
                $"[InfiniteLizards] Win32 input region failed during {stage}; " +
                $"the overlay remains fail-closed: {exception}");
        }
    }

    private void InstallFailClosedWindowRegion()
    {
        if (_window == nint.Zero)
        {
            throw new NativeOverlaySafetyException(
                "Cannot establish the Win32 fail-closed region without an HWND.");
        }

        nint emptyRegion;
        try
        {
            emptyRegion = Win32InputRegionBuilder.CreateEmptyRegion();
        }
        catch (Exception exception)
        {
            throw new NativeOverlaySafetyException(
                "GDI could not allocate the empty fail-closed window region.",
                exception);
        }
        try
        {
            Marshal.SetLastPInvokeError(0);
            if (!SetWindowRgn(_window, emptyRegion, redraw: true))
            {
                throw new NativeOverlaySafetyException(
                    "SetWindowRgn could not install the empty fail-closed region.",
                    NativeFailure("SetWindowRgn(empty)"));
            }

            // USER32 owns the empty HRGN after success.
            emptyRegion = nint.Zero;
            _inputRegionState = Win32InputRegionState.FailClosed;
            _installedInputRegionSignature = null;
            ApplyNativeInputEnabled(enabled: false);
            try
            {
                ApplyNativeExtendedStyle(clickThrough: true);
            }
            catch (Exception exception)
            {
                // The empty HRGN itself is sufficient to make the HWND both
                // invisible and inputless. Style is only a redundant fallback.
                Trace.WriteLine(
                    "[InfiniteLizards] empty Win32 region is installed, but the " +
                    $"transparent fallback style could not be refreshed: {exception}");
            }
        }
        finally
        {
            Win32InputRegionBuilder.DeleteOwnedRegion(emptyRegion);
        }
    }

    private static Exception NativeFailure(string operation)
    {
        var error = Marshal.GetLastPInvokeError();
        return error != 0
            ? new Win32Exception(error, $"{operation} failed.")
            : new InvalidOperationException($"{operation} failed without a Win32 error code.");
    }

    private Win32WindowInsets MeasureWindowInsets()
    {
        if (!GetWindowRect(_window, out var windowRect))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        if (!GetClientRect(_window, out var clientRect))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var clientOrigin = new NativePoint(clientRect.Left, clientRect.Top);
        if (!ClientToScreen(_window, ref clientOrigin))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return Win32ClientRectPlacement.MeasureInsets(
            new Win32PixelRect(
                windowRect.Left,
                windowRect.Top,
                windowRect.Right,
                windowRect.Bottom),
            new Win32PixelRect(
                clientOrigin.X,
                clientOrigin.Y,
                checked(clientOrigin.X + clientRect.Width),
                checked(clientOrigin.Y + clientRect.Height)));
    }

    private void VerifyClientPlacement(SurfacePlacement expected)
    {
        var matches = ClientPlacementMatches(expected, out var actual);
        if (!matches)
        {
            throw new NativeOverlaySafetyException(
                "Win32 client placement still mismatched after the DPI retry: " +
                $"expected=({expected.X},{expected.Y},{expected.Width},{expected.Height}), " +
                $"actual=({actual.X},{actual.Y},{actual.Width},{actual.Height}).");
        }
    }

    private bool ClientPlacementMatches(
        SurfacePlacement expected,
        out SurfacePlacement actual)
    {
        if (!GetClientRect(_window, out var clientRect))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var clientOrigin = new NativePoint(clientRect.Left, clientRect.Top);
        if (!ClientToScreen(_window, ref clientOrigin))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        actual = new SurfacePlacement(
            clientOrigin.X,
            clientOrigin.Y,
            clientRect.Width,
            clientRect.Height);
        return
            actual.X == expected.X &&
            actual.Y == expected.Y &&
            actual.Width == expected.Width &&
            actual.Height == expected.Height;
    }

    private void VerifyExtendedStyle(bool clickThrough)
    {
        var actual = unchecked((uint)GetWindowLongPtrChecked(_window, GwlExStyle).ToInt64());
        var required = Win32OverlayStyle.ToolWindow | Win32OverlayStyle.NoActivate;
        var matches =
            (actual & required) == required &&
            ((actual & Win32OverlayStyle.Transparent) != 0) == clickThrough;
        if (!matches)
        {
            throw new NativeOverlaySafetyException(
                "Win32 overlay styles did not survive the native Show transition: " +
                $"GWL_EXSTYLE=0x{actual:X8}, transparentFallback={clickThrough}.");
        }
    }

    private void VerifyNativeInputEnabled(bool expected)
    {
        VerifyCurrentWindowHandle("verifying post-Show native input state");
        var actual = IsWindowEnabled(_window);
        if (actual != expected)
        {
            throw new NativeOverlaySafetyException(
                "Win32 overlay input state did not survive the native Show transition: " +
                $"expectedEnabled={expected}, actualEnabled={actual}.");
        }
    }

    private void VerifyCurrentWindowHandle(string operation)
    {
        var candidate = _windowOwner?.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (_window == nint.Zero ||
            !IsWindow(_window) ||
            candidate != _window)
        {
            throw new NativeOverlaySafetyException(
                $"The Avalonia HWND changed while {operation}; native overlay state is unknown.");
        }
    }

    private void ApplyFrameChange()
    {
        if (!SetWindowPos(
                _window,
                nint.Zero,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private void RemoveAvaloniaCallbacks()
    {
        if (_windowOwner is null)
        {
            return;
        }

        if (_windowStylesCallback is not null)
        {
            Win32Properties.RemoveWindowStylesCallback(_windowOwner, _windowStylesCallback);
        }
        if (_wndProcHookCallback is not null)
        {
            Win32Properties.RemoveWndProcHookCallback(_windowOwner, _wndProcHookCallback);
        }
    }

    private void ResetAttachment()
    {
        _nativeAcceptanceTelemetry?.Disable();
        _windowOwner = null;
        _window = nint.Zero;
        _windowStylesCallback = null;
        _wndProcHookCallback = null;
        _nativeAcceptanceTelemetry = null;
        _nativeOverlayEnabled = false;
        _hasVerifiedWindowHandle = false;
        _inputRegionState = Win32InputRegionState.Unknown;
        _windowHandleEpoch = 0;
        _requestedInputRegion = null;
        _requestedDisplayScale = 1d;
        _requestedClickThrough = true;
        _installedInputRegionSignature = null;
        _lastPlacement = null;
    }

    private bool TryRefreshWindowHandle()
    {
        var candidate = _windowOwner?.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (candidate != nint.Zero && IsWindow(candidate))
        {
            if (candidate == _window)
            {
                return true;
            }

            // Always secure the owner-reported handle before considering an
            // older live HWND. Replacement is accepted during pre-Show setup;
            // after verification the new handle is made fail-closed and the
            // unsupported lifecycle transition terminates below.
            _window = candidate;
            _windowHandleEpoch++;
            ResetInputRegionHandleState();
            if (_nativeOverlayEnabled)
            {
                InstallFailClosedWindowRegion();
            }
            if (_hasVerifiedWindowHandle)
            {
                throw new NativeOverlaySafetyException(
                    "Avalonia replaced the verified HWND. The replacement was " +
                    "made fail-closed, but this pinned host does not continue " +
                    "after an unexpected native-window rebuild.");
            }
            return true;
        }

        if (_window != nint.Zero && IsWindow(_window))
        {
            return true;
        }

        _window = nint.Zero;
        ResetInputRegionHandleState();
        return false;
    }

    private bool TryRefreshWindowHandleForRuntime(string operation)
    {
        if (TryRefreshWindowHandle())
        {
            return true;
        }
        if (_nativeOverlayEnabled && _hasVerifiedWindowHandle)
        {
            throw new NativeOverlaySafetyException(
                $"The verified Win32 overlay HWND disappeared during {operation}.");
        }
        return false;
    }

    private bool RequiresTransparentFallback =>
        Win32InputRegionPolicy.RequiresTransparentFallback(_inputRegionState);

    private bool ShouldEnableNativeInput =>
        !_requestedClickThrough &&
        _inputRegionState == Win32InputRegionState.Installed;

    private void ResetInputRegionHandleState()
    {
        _inputRegionState = Win32InputRegionState.Unknown;
        _installedInputRegionSignature = null;
    }

    private readonly record struct Win32InputRegionSignature(
        int WindowHandleEpoch,
        long Revision,
        long DisplayScaleBits,
        string RasterScaleSignature,
        Win32WindowInsets Insets);

    private static nint GetWindowLongPtrChecked(nint window, int index)
    {
        Marshal.SetLastPInvokeError(0);
        var result = GetWindowLongPtr(window, index);
        var error = Marshal.GetLastPInvokeError();
        if (result == nint.Zero && error != 0)
        {
            throw new Win32Exception(error);
        }
        return result;
    }

    private static nint SetWindowLongPtrChecked(nint window, int index, nint value)
    {
        Marshal.SetLastPInvokeError(0);
        var result = SetWindowLongPtr(window, index, value);
        var error = Marshal.GetLastPInvokeError();
        if (result == nint.Zero && error != 0)
        {
            throw new Win32Exception(error);
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => checked(Right - Left);
        public readonly int Height => checked(Bottom - Top);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowRgn(
        nint window,
        nint region,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(
        nint window,
        [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint window);
}
