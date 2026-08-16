using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace InfiniteLizards.Windows.NativeAcceptance;

internal static class NativeWindowChild
{
    // COLORREF stores bytes as 0x00BBGGRR. A deliberately muted, uncommon
    // background lets the production acceptance distinguish real DWM/Skia
    // pixels from the independently owned window below the pet.
    internal const uint ProductionProbeColor = 0x003F2F1F;
    internal const int RegionLeft = 96;
    internal const int RegionTop = 54;
    internal const int RegionRight = 224;
    internal const int RegionBottom = 186;

    private static readonly Win32Native.WindowProcedure WindowProcedure = WndProc;
    private static WindowKind _kind;
    private static int _leftDown;
    private static int _leftUp;
    private static int _mouseActivate;
    private static int _activate;
    private static int _enable;
    private static int _activateApp;
    private static int _setFocus;
    private static int _killFocus;
    private static int _safetyReleaseUp;
    private static int _taggedLeftDown;
    private static int _taggedLeftUp;
    private static nuint _routeTag;

    internal static int Run(
        string kindText,
        string xText,
        string yText,
        string widthText,
        string heightText,
        string? routeTagText)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("The native window child requires Windows.");
            }

            var kind = kindText switch
            {
                "probe" => WindowKind.Probe,
                "production-probe" => WindowKind.ProductionProbe,
                "overlay" => WindowKind.Overlay,
                _ => throw new ArgumentException($"Unknown child window kind: {kindText}.")
            };
            var x = ParseCoordinate(xText, nameof(xText));
            var y = ParseCoordinate(yText, nameof(yText));
            var width = ParsePositive(widthText, nameof(widthText));
            var height = ParsePositive(heightText, nameof(heightText));
            _routeTag = ParseRouteTag(routeTagText);

            InteractiveWindowsSession.RequirePerMonitorV2Awareness();
            return RunMessageLoop(kind, x, y, width, height);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"CHILD_FAIL|{kindText}|{exception}");
            return 1;
        }
    }

    private static int RunMessageLoop(
        WindowKind kind,
        int x,
        int y,
        int width,
        int height)
    {
        ResetCounters();
        _kind = kind;
        var instance = Win32Native.GetModuleHandle(null);
        if (instance == nint.Zero)
        {
            throw Win32Native.Failure("GetModuleHandleW");
        }

        var className = string.Concat(
            "InfiniteLizards.NativeAcceptance.",
            kind,
            ".",
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        var ownedBackground = kind == WindowKind.ProductionProbe
            ? Win32Native.CreateSolidBrush(ProductionProbeColor)
            : nint.Zero;
        if (kind == WindowKind.ProductionProbe && ownedBackground == nint.Zero)
        {
            throw Win32Native.Failure("CreateSolidBrush(production probe)");
        }
        var windowClass = new Win32Native.WindowClassEx
        {
            Size = checked((uint)Marshal.SizeOf<Win32Native.WindowClassEx>()),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedure),
            Instance = instance,
            // COLOR_WINDOW + 1 is system-owned. The production probe instead
            // owns a deterministic solid brush and releases it only after its
            // class is unregistered.
            Background = ownedBackground != nint.Zero ? ownedBackground : new nint(6),
            ClassName = className
        };
        if (Win32Native.RegisterClassEx(ref windowClass) == 0)
        {
            if (ownedBackground != nint.Zero)
            {
                _ = Win32Native.DeleteObject(ownedBackground);
            }
            throw Win32Native.Failure("RegisterClassExW");
        }

        nint window = nint.Zero;
        try
        {
            window = Win32Native.CreateWindowEx(
                kind == WindowKind.Overlay
                    ? Win32Native.WsExToolWindow | Win32Native.WsExNoActivate
                    : 0,
                className,
                $"Infinite Lizards native acceptance {kind}",
                Win32Native.WsPopup,
                x,
                y,
                width,
                height,
                nint.Zero,
                nint.Zero,
                instance,
                nint.Zero);
            if (window == nint.Zero)
            {
                throw Win32Native.Failure("CreateWindowExW");
            }

            if (kind == WindowKind.Overlay && !InstallOverlayRegion(window, redraw: true))
            {
                throw Win32Native.Failure("SetWindowRgn(initial)");
            }

            if (!Win32Native.SetWindowPos(
                    window,
                    kind == WindowKind.Overlay
                        ? Win32Native.HwndTopmost
                        : Win32Native.HwndTop,
                    x,
                    y,
                    width,
                    height,
                    Win32Native.SwpNoActivate | Win32Native.SwpShowWindow))
            {
                throw Win32Native.Failure("SetWindowPos(HWND_TOPMOST)");
            }

            var threadId = Win32Native.GetWindowThreadProcessId(window, out var processId);
            Console.WriteLine(string.Join(
                '|',
                "READY",
                KindName(kind),
                window.ToInt64().ToString(CultureInfo.InvariantCulture),
                threadId.ToString(CultureInfo.InvariantCulture),
                processId.ToString(CultureInfo.InvariantCulture),
                Win32Native.GetDpiForWindow(window).ToString(CultureInfo.InvariantCulture)));
            Console.Out.Flush();

            while (true)
            {
                var result = Win32Native.GetMessage(out var message, nint.Zero, 0, 0);
                if (result == -1)
                {
                    throw Win32Native.Failure("GetMessageW");
                }
                if (result == 0)
                {
                    break;
                }

                _ = Win32Native.TranslateMessage(ref message);
                _ = Win32Native.DispatchMessage(ref message);
            }

            window = nint.Zero;
            return 0;
        }
        finally
        {
            if (window != nint.Zero && Win32Native.IsWindow(window))
            {
                _ = Win32Native.DestroyWindow(window);
            }

            _ = Win32Native.UnregisterClass(className, instance);
            if (ownedBackground != nint.Zero)
            {
                _ = Win32Native.DeleteObject(ownedBackground);
            }
            GC.KeepAlive(WindowProcedure);
        }
    }

    private static nint WndProc(nint window, uint message, nint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case Win32Native.WmAcceptanceStressRegions:
                    return StressRegions(window, checked((int)wParam.ToInt64()))
                        ? new nint(1)
                        : nint.Zero;
                case Win32Native.WmAcceptanceResetCounters:
                    ResetCounters();
                    return new nint(1);
                case Win32Native.WmAcceptanceReadCounter:
                    return new nint(ReadCounter(checked((int)wParam.ToInt64())));
                case Win32Native.WmMouseActivate:
                    Interlocked.Increment(ref _mouseActivate);
                    if (_kind == WindowKind.Overlay)
                    {
                        return new nint(Win32Native.MaNoActivate);
                    }
                    break;
                case Win32Native.WmLButtonDown:
                    Interlocked.Increment(ref _leftDown);
                    if (_routeTag != 0 &&
                        unchecked((nuint)Win32Native.GetMessageExtraInfo().ToInt64()) == _routeTag)
                    {
                        Interlocked.Increment(ref _taggedLeftDown);
                    }
                    break;
                case Win32Native.WmLButtonUp:
                    Interlocked.Increment(ref _leftUp);
                    var extraInfo = unchecked(
                        (nuint)Win32Native.GetMessageExtraInfo().ToInt64());
                    if (_routeTag != 0 && extraInfo == _routeTag)
                    {
                        Interlocked.Increment(ref _taggedLeftUp);
                    }
                    if (extraInfo == Win32Native.SafetyReleaseExtraInfo)
                    {
                        Interlocked.Increment(ref _safetyReleaseUp);
                    }
                    break;
                case Win32Native.WmActivate:
                    if ((wParam.ToInt64() & 0xFFFF) != 0)
                    {
                        Interlocked.Increment(ref _activate);
                    }
                    break;
                case Win32Native.WmActivateApp:
                    if (wParam != nint.Zero)
                    {
                        Interlocked.Increment(ref _activateApp);
                    }
                    break;
                case Win32Native.WmSetFocus:
                    Interlocked.Increment(ref _setFocus);
                    break;
                case Win32Native.WmKillFocus:
                    Interlocked.Increment(ref _killFocus);
                    break;
                case Win32Native.WmEnable:
                    Interlocked.Increment(ref _enable);
                    break;
                case Win32Native.WmClose:
                    _ = Win32Native.DestroyWindow(window);
                    return nint.Zero;
                case Win32Native.WmDestroy:
                    Win32Native.PostQuitMessage(0);
                    return nint.Zero;
            }
        }
        catch
        {
            // Exceptions may not cross the unmanaged WndProc boundary. A zero
            // custom-message result makes the controller fail the acceptance.
            if (message is >= Win32Native.WmApp)
            {
                return nint.Zero;
            }
        }

        return Win32Native.DefWindowProc(window, message, wParam, lParam);
    }

    private static bool StressRegions(nint window, int iterations)
    {
        if (iterations <= 0 || iterations > 10_000)
        {
            return false;
        }

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            if (!InstallOverlayRegion(window, redraw: true))
            {
                return false;
            }
        }

        return true;
    }

    private static bool InstallOverlayRegion(nint window, bool redraw)
    {
        var ownedRegion = Win32Native.CreateRectRgn(
            RegionLeft,
            RegionTop,
            RegionRight,
            RegionBottom);
        if (ownedRegion == nint.Zero)
        {
            return false;
        }

        if (Win32Native.SetWindowRgn(window, ownedRegion, redraw) == 0)
        {
            // SetWindowRgn failed, so ownership never transferred.
            _ = Win32Native.DeleteObject(ownedRegion);
            return false;
        }

        // On success USER32 owns this exact HRGN. It must not be deleted by
        // this process; replacing/destroying the window releases it.
        return true;
    }

    private static void ResetCounters()
    {
        Interlocked.Exchange(ref _leftDown, 0);
        Interlocked.Exchange(ref _leftUp, 0);
        Interlocked.Exchange(ref _mouseActivate, 0);
        Interlocked.Exchange(ref _activate, 0);
        Interlocked.Exchange(ref _enable, 0);
        Interlocked.Exchange(ref _activateApp, 0);
        Interlocked.Exchange(ref _setFocus, 0);
        Interlocked.Exchange(ref _killFocus, 0);
        Interlocked.Exchange(ref _safetyReleaseUp, 0);
        Interlocked.Exchange(ref _taggedLeftDown, 0);
        Interlocked.Exchange(ref _taggedLeftUp, 0);
    }

    private static int ReadCounter(int counter) => counter switch
    {
        Win32Native.CounterLeftDown => Volatile.Read(ref _leftDown),
        Win32Native.CounterLeftUp => Volatile.Read(ref _leftUp),
        Win32Native.CounterMouseActivate => Volatile.Read(ref _mouseActivate),
        Win32Native.CounterActivate => Volatile.Read(ref _activate),
        Win32Native.CounterEnable => Volatile.Read(ref _enable),
        Win32Native.CounterActivateApp => Volatile.Read(ref _activateApp),
        Win32Native.CounterSetFocus => Volatile.Read(ref _setFocus),
        Win32Native.CounterKillFocus => Volatile.Read(ref _killFocus),
        Win32Native.CounterSafetyReleaseUp => Volatile.Read(ref _safetyReleaseUp),
        Win32Native.CounterTaggedLeftDown => Volatile.Read(ref _taggedLeftDown),
        Win32Native.CounterTaggedLeftUp => Volatile.Read(ref _taggedLeftUp),
        _ => -1
    };

    private static nuint ParseRouteTag(string? value)
    {
        if (value is null)
        {
            return 0;
        }
        if (!ulong.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed == 0 ||
            (nuint.Size == 4 && parsed > uint.MaxValue))
        {
            throw new ArgumentException($"Invalid route tag: {value}.");
        }

        return checked((nuint)parsed);
    }

    private static int ParseCoordinate(string value, string parameterName) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ArgumentException($"Invalid {parameterName}: {value}.");

    private static int ParsePositive(string value, string parameterName)
    {
        var parsed = ParseCoordinate(value, parameterName);
        return parsed > 0
            ? parsed
            : throw new ArgumentOutOfRangeException(parameterName);
    }

    private static string KindName(WindowKind kind) => kind switch
    {
        WindowKind.Probe => "probe",
        WindowKind.ProductionProbe => "production-probe",
        WindowKind.Overlay => "overlay",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private enum WindowKind
    {
        Probe,
        ProductionProbe,
        Overlay
    }
}
