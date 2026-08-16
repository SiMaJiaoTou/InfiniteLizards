using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace InfiniteLizards.Windows.NativeAcceptance;

internal static class NativeAcceptanceRunner
{
    private const int WindowX = -48;
    private const int WindowWidth = 336;
    private const int WindowHeight = 240;
    internal static async Task RunAsync()
    {
        InteractiveWindowsSession.RequirePerMonitorV2Awareness();
        var mouse = new SafeInjectedMouse();

        Exception? failure = null;
        try
        {
            await RunCoreAsync(mouse);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (mouse.HasPendingAcceptedInput)
        {
            var unsafeState = new InvalidOperationException(
                "The injected left-button state could not be safely cleared while the " +
                "owned child HWNDs were alive. The original cursor position will not be restored.");
            failure = failure is null
                ? unsafeState
                : new AggregateException(failure, unsafeState);
            Console.Error.WriteLine(
                "CURSOR|restored=SKIPPED|reason=injected-left-button-state-uncleared");
        }
        else
        {
            try
            {
                mouse.RestoreOriginalCursor();
                Console.WriteLine("CURSOR|restored=PASS");
            }
            catch (Exception restoreException)
            {
                failure = failure is null
                    ? restoreException
                    : new AggregateException(failure, restoreException);
            }
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    private static async Task RunCoreAsync(SafeInjectedMouse mouse)
    {
        var stopwatch = Stopwatch.StartNew();
        Console.WriteLine(
            "CAPABILITY|kind=Win32-primitives-smoke|" +
            "proves=USER32,GDI32,cross-process-input,foreground|" +
            "doesNotProve=production-Desktop,production-Avalonia,DWM-composite," +
            "mixed-DPI-hardware");
        var primaryHeight = Win32Native.GetSystemMetrics(Win32Native.SmCyScreen);
        Require(primaryHeight > WindowHeight + 16,
            $"The primary display is too short for the {WindowHeight}px acceptance windows.");
        var windowY = Math.Clamp((primaryHeight - WindowHeight) / 3, 8, 120);
        var virtualRect = new Win32Native.Rect
        {
            Left = Win32Native.GetSystemMetrics(Win32Native.SmXVirtualScreen),
            Top = Win32Native.GetSystemMetrics(Win32Native.SmYVirtualScreen)
        };
        virtualRect.Right = checked(
            virtualRect.Left + Win32Native.GetSystemMetrics(Win32Native.SmCxVirtualScreen));
        virtualRect.Bottom = checked(
            virtualRect.Top + Win32Native.GetSystemMetrics(Win32Native.SmCyVirtualScreen));
        Console.WriteLine(string.Join(
            '|',
            "ENV",
            $"session={Process.GetCurrentProcess().SessionId}",
            $"processArch={RuntimeInformation.ProcessArchitecture}",
            "dpiAwareness=PMv2",
            $"systemDpi={Win32Native.GetDpiForSystem()}",
            $"monitors={Win32Native.GetSystemMetrics(Win32Native.SmCMonitors)}",
            $"primary={Win32Native.GetSystemMetrics(Win32Native.SmCxScreen)}x{primaryHeight}",
            $"virtual={virtualRect}"));

        await using var foregroundSampler = new ForegroundSampler();
        var initialForeground = Win32Native.GetForegroundWindow();
        var routeTag = CreateRouteTag();

        await using var probe = await NativeChildProcess.StartAsync(
            "probe", WindowX, windowY, WindowWidth, WindowHeight, routeTag);
        VerifyNativeWindowIdentity(probe);
        // Launch this acceptance from the currently foreground terminal for
        // the strongest setup oracle. Windows foreground-lock policy may still
        // deny a programmatic SetForegroundWindow; the first real SendInput
        // click below must then activate this ordinary probe window instead.
        var foregroundRequestAccepted = Win32Native.SetForegroundWindow(probe.Window);
        var probeForegroundEstablished = foregroundRequestAccepted &&
            await TryWaitUntilAsync(
                () => Win32Native.GetForegroundWindow() == probe.Window,
                TimeSpan.FromMilliseconds(500));
        Console.WriteLine(string.Join(
            '|',
            "FOREGROUND_SETUP",
            $"setForegroundReturn={foregroundRequestAccepted}",
            $"probeEstablished={probeForegroundEstablished}",
            probeForegroundEstablished
                ? "strengthening=PASS"
                : "strengthening=UNAVAILABLE_FOREGROUND_LOCK"));

        await using var overlay = await NativeChildProcess.StartAsync(
            "overlay", WindowX, windowY, WindowWidth, WindowHeight, routeTag);
        var inside = new Win32Native.Point(
            WindowX + (NativeWindowChild.RegionLeft + NativeWindowChild.RegionRight) / 2,
            windowY + (NativeWindowChild.RegionTop + NativeWindowChild.RegionBottom) / 2);
        var outside = new Win32Native.Point(WindowX + 280, inside.Y);
        Exception? liveWindowFailure = null;
        try
        {
            VerifyNativeWindowIdentity(overlay);
            Require(probe.Process.Id != overlay.Process.Id,
                "The probe and overlay must be hosted by different processes.");
            Require(probe.WindowThreadId != overlay.WindowThreadId,
                "The probe and overlay must be owned by different UI threads.");
            await WaitUntilAsync(
                () => Win32Native.IsWindowVisible(probe.Window) &&
                      Win32Native.IsWindowVisible(overlay.Window),
                "both child HWNDs to become visible");
            await Task.Delay(50);
            Require(Win32Native.GetForegroundWindow() != overlay.Window,
                "Creating the NOACTIVATE overlay made it foreground.");

            VerifyPlacementAndSignedCoordinates(probe, overlay, windowY, virtualRect);
            VerifyDpiContexts(probe, overlay);
            VerifyOverlayStyles(overlay.Window, expectedEnabled: true);
            VerifyRegionRoundTrip(overlay.Window);
            VerifyOverlayNeverActivated(overlay.Window, "creation");

            await VerifyResourcePlateauAsync(overlay);
            VerifyRegionRoundTrip(overlay.Window);

            await VerifyActualClickRouteAsync(
                "shaped-outside",
                outside,
                expectedWindow: probe,
                unexpectedWindow: overlay,
                mouse,
                routeTag);
            await WaitUntilAsync(
                () => Win32Native.GetForegroundWindow() == probe.Window,
                "the ordinary probe to become foreground after a real routed click");
            await VerifyActualClickRouteAsync(
                "shaped-inside-enabled",
                inside,
                expectedWindow: overlay,
                unexpectedWindow: probe,
                mouse,
                routeTag);

            // EnableWindow's return value is the previous state, not a success
            // indicator. USER32 state and WS_DISABLED are the authoritative readback.
            _ = Win32Native.EnableWindow(overlay.Window, false);
            await WaitUntilAsync(
                () => !Win32Native.IsWindowEnabled(overlay.Window) &&
                      (Win32Native.ReadWindowBits(overlay.Window, Win32Native.GwlStyle) &
                       Win32Native.WsDisabled) != 0,
                "EnableWindow(false) and WS_DISABLED readback");
            VerifyOverlayStyles(overlay.Window, expectedEnabled: false);
            VerifyRegionRoundTrip(overlay.Window);
            await VerifyActualClickRouteAsync(
                "shaped-inside-disabled",
                inside,
                expectedWindow: probe,
                unexpectedWindow: overlay,
                mouse,
                routeTag);

            _ = Win32Native.EnableWindow(overlay.Window, true);
            await WaitUntilAsync(
                () => Win32Native.IsWindowEnabled(overlay.Window) &&
                      (Win32Native.ReadWindowBits(overlay.Window, Win32Native.GwlStyle) &
                       Win32Native.WsDisabled) == 0,
                "EnableWindow(true) and WS_DISABLED removal");
            VerifyOverlayStyles(overlay.Window, expectedEnabled: true);
            await VerifyActualClickRouteAsync(
                "shaped-inside-reenabled",
                inside,
                expectedWindow: overlay,
                unexpectedWindow: probe,
                mouse,
                routeTag);

            Require(Win32Native.GetForegroundWindow() == probe.Window,
                "The overlay stole foreground during native input acceptance.");
            VerifyOverlayNeverActivated(overlay.Window, "final input route");

            await foregroundSampler.StopAsync();
            var samples = foregroundSampler.Samples;
            var overlayForegroundSamples = samples.Count(
                sample => sample.ProcessId == overlay.WindowProcessId);
            Require(overlayForegroundSamples == 0,
                $"The overlay was foreground in {overlayForegroundSamples} sampled interval(s).");
            var finalForeground = Win32Native.GetForegroundWindow();
            Console.WriteLine(string.Join(
                '|',
                "FOREGROUND",
                $"initialHwnd={FormatHandle(initialForeground)}",
                $"initialPid={GetWindowProcessId(initialForeground)}",
                $"probeHwnd={FormatHandle(probe.Window)}",
                $"overlayHwnd={FormatHandle(overlay.Window)}",
                $"samples={samples.Count}",
                $"intervalMs=10",
                $"overlaySamples={overlayForegroundSamples}",
                $"finalHwnd={FormatHandle(finalForeground)}",
                $"finalPid={GetWindowProcessId(finalForeground)}"));
            Console.WriteLine(string.Join(
                '|',
                "WINDOWS",
                $"probePid={probe.Process.Id}",
                $"probeTid={probe.WindowThreadId}",
                $"probeDpi={probe.Dpi}",
                $"overlayPid={overlay.Process.Id}",
                $"overlayTid={overlay.WindowThreadId}",
                $"overlayDpi={overlay.Dpi}"));
            Console.WriteLine($"DURATION|milliseconds={stopwatch.ElapsedMilliseconds}");
        }
        catch (Exception exception)
        {
            liveWindowFailure = exception;
        }

        // This is the final input-state fallback. It runs before either await-using
        // child is disposed, at a point outside the overlay region that must hit
        // the independently owned probe HWND.
        try
        {
            await mouse.ReleaseIfNeededAsync(probe, outside);
        }
        catch (Exception cleanupException)
        {
            const string reason = "primitives tagged LEFTUP cleanup was not proven";
            probe.PreserveForInputSafety(reason);
            overlay.PreserveForInputSafety(reason);
            liveWindowFailure = liveWindowFailure is null
                ? cleanupException
                : new AggregateException(liveWindowFailure, cleanupException);
        }

        if (liveWindowFailure is not null)
        {
            throw liveWindowFailure;
        }
    }

    private static void VerifyNativeWindowIdentity(NativeChildProcess child)
    {
        var threadId = Win32Native.GetWindowThreadProcessId(child.Window, out var processId);
        Require(threadId == child.WindowThreadId,
            $"{child.Kind} HWND thread changed after READY.");
        Require(processId == child.WindowProcessId && processId == checked((uint)child.Process.Id),
            $"{child.Kind} HWND is not owned by its independent child process.");
    }

    private static void VerifyPlacementAndSignedCoordinates(
        NativeChildProcess probe,
        NativeChildProcess overlay,
        int expectedY,
        Win32Native.Rect virtualRect)
    {
        foreach (var child in new[] { probe, overlay })
        {
            Require(Win32Native.GetWindowRect(child.Window, out var rect),
                $"GetWindowRect failed for {child.Kind}.");
            Require(
                rect.Left == WindowX && rect.Top == expectedY &&
                rect.Width == WindowWidth && rect.Height == WindowHeight,
                $"{child.Kind} signed window placement mismatch: {rect}.");

            var origin = new Win32Native.Point(0, 0);
            Require(Win32Native.ClientToScreen(child.Window, ref origin),
                $"ClientToScreen failed for {child.Kind}.");
            Require(origin.X == WindowX && origin.Y == expectedY,
                $"{child.Kind} lost the negative screen origin: {origin}.");
            Require(Win32Native.ScreenToClient(child.Window, ref origin),
                $"ScreenToClient failed for {child.Kind}.");
            Require(origin.X == 0 && origin.Y == 0,
                $"{child.Kind} signed screen/client coordinate roundtrip failed: {origin}.");
        }

        var negativePoint = new Win32Native.Point(WindowX + 32, expectedY + 120);
        var pointIsOnVirtualDesktop =
            negativePoint.X >= virtualRect.Left && negativePoint.X < virtualRect.Right &&
            negativePoint.Y >= virtualRect.Top && negativePoint.Y < virtualRect.Bottom;
        if (pointIsOnVirtualDesktop)
        {
            Require(Win32Native.WindowFromPoint(negativePoint) == probe.Window,
                "WindowFromPoint did not preserve an actual negative desktop coordinate.");
        }

        Console.WriteLine(string.Join(
            '|',
            "SIGNED_COORDINATES",
            $"windowOrigin=({WindowX},{expectedY})",
            "clientRoundTrip=PASS",
            pointIsOnVirtualDesktop
                ? $"negativeWindowFromPoint=PASS@{negativePoint}"
                : "negativeWindowFromPoint=NOT_APPLICABLE_OUTSIDE_VIRTUAL_DESKTOP"));
    }

    private static void VerifyDpiContexts(
        NativeChildProcess probe,
        NativeChildProcess overlay)
    {
        foreach (var child in new[] { probe, overlay })
        {
            var context = Win32Native.GetWindowDpiAwarenessContext(child.Window);
            Require(Win32Native.AreDpiAwarenessContextsEqual(
                    context,
                    Win32Native.DpiAwarenessContextPerMonitorAwareV2),
                $"{child.Kind} HWND is not per-monitor-v2 DPI aware.");
            var liveDpi = Win32Native.GetDpiForWindow(child.Window);
            Require(liveDpi > 0 && liveDpi == child.Dpi,
                $"{child.Kind} DPI readback changed: ready={child.Dpi}, live={liveDpi}.");
        }
    }

    private static void VerifyOverlayStyles(nint overlay, bool expectedEnabled)
    {
        var exStyle = Win32Native.ReadWindowBits(overlay, Win32Native.GwlExStyle);
        var style = Win32Native.ReadWindowBits(overlay, Win32Native.GwlStyle);
        var requiredExStyle = Win32Native.WsExNoActivate | Win32Native.WsExToolWindow;
        Require((exStyle & requiredExStyle) == requiredExStyle,
            $"Overlay GWL_EXSTYLE=0x{exStyle:X8} lacks NOACTIVATE/TOOLWINDOW.");
        Require(Win32Native.IsWindowEnabled(overlay) == expectedEnabled,
            $"Overlay IsWindowEnabled did not read back {expectedEnabled}.");
        Require(((style & Win32Native.WsDisabled) == 0) == expectedEnabled,
            $"Overlay GWL_STYLE=0x{style:X8} has an unexpected WS_DISABLED state.");
        Console.WriteLine(string.Join(
            '|',
            "STYLE",
            $"enabled={expectedEnabled}",
            $"style=0x{style:X8}",
            $"exStyle=0x{exStyle:X8}"));
    }

    private static void VerifyRegionRoundTrip(nint overlay)
    {
        var copiedRegion = Win32Native.CreateRectRgn(0, 0, 0, 0);
        if (copiedRegion == nint.Zero)
        {
            throw Win32Native.Failure("CreateRectRgn(region copy)");
        }

        try
        {
            var regionType = Win32Native.GetWindowRgn(overlay, copiedRegion);
            Require(regionType is 2 or 3,
                $"GetWindowRgn returned unexpected region type {regionType}.");
            Require(Win32Native.GetRgnBox(copiedRegion, out var bounds) is 2 or 3,
                "GetRgnBox did not return a non-empty region.");
            Require(
                bounds.Left == NativeWindowChild.RegionLeft &&
                bounds.Top == NativeWindowChild.RegionTop &&
                bounds.Right == NativeWindowChild.RegionRight &&
                bounds.Bottom == NativeWindowChild.RegionBottom,
                $"Window-region bounds do not match outer-HWND coordinates: {bounds}.");
            Require(Win32Native.PtInRegion(
                    copiedRegion,
                    (NativeWindowChild.RegionLeft + NativeWindowChild.RegionRight) / 2,
                    (NativeWindowChild.RegionTop + NativeWindowChild.RegionBottom) / 2),
                "The copied window region excludes its expected interior point.");
            Require(!Win32Native.PtInRegion(copiedRegion, 280, 120),
                "The copied window region includes its expected exterior point.");
        }
        finally
        {
            // GetWindowRgn copies into a caller-owned HRGN, unlike the HRGN
            // transferred to USER32 by a successful SetWindowRgn.
            Require(Win32Native.DeleteObject(copiedRegion),
                "DeleteObject failed for the caller-owned GetWindowRgn copy.");
        }
    }

    private static async Task VerifyResourcePlateauAsync(NativeChildProcess overlay)
    {
        StressRegions(overlay.Window, 32);
        await Task.Delay(50);
        var baseline = ReadGuiResources(overlay);
        StressRegions(overlay.Window, 256);
        await Task.Delay(50);
        var middle = ReadGuiResources(overlay);
        StressRegions(overlay.Window, 256);
        await Task.Delay(50);
        var final = ReadGuiResources(overlay);

        Require(middle.Gdi <= baseline.Gdi + 1 && final.Gdi <= baseline.Gdi + 1,
            $"GDI objects did not plateau: {baseline.Gdi}->{middle.Gdi}->{final.Gdi}.");
        Require(middle.User <= baseline.User + 1 && final.User <= baseline.User + 1,
            $"USER objects did not plateau: {baseline.User}->{middle.User}->{final.User}.");
        Console.WriteLine(string.Join(
            '|',
            "GUI_RESOURCES",
            "warmupRegions=32",
            "sample1Regions=256",
            "sample2Regions=256",
            $"gdi={baseline.Gdi},{middle.Gdi},{final.Gdi}",
            $"user={baseline.User},{middle.User},{final.User}",
            "plateau=PASS"));
    }

    private static void StressRegions(nint overlay, int iterations)
    {
        var result = Win32Native.SendAcceptanceMessage(
            overlay,
            Win32Native.WmAcceptanceStressRegions,
            new nint(iterations));
        Require(result != nint.Zero,
            $"The overlay failed to replace {iterations} USER32-owned regions.");
    }

    private static (uint Gdi, uint User) ReadGuiResources(NativeChildProcess child)
    {
        var gdi = Win32Native.GetGuiResources(
            child.Process.Handle,
            Win32Native.GrGdiObjects);
        var user = Win32Native.GetGuiResources(
            child.Process.Handle,
            Win32Native.GrUserObjects);
        Require(user > 0, "GetGuiResources returned no USER objects for the window child.");
        return (gdi, user);
    }

    private static async Task VerifyActualClickRouteAsync(
        string name,
        Win32Native.Point point,
        NativeChildProcess expectedWindow,
        NativeChildProcess unexpectedWindow,
        SafeInjectedMouse mouse,
        nuint routeTag)
    {
        ResetCounters(expectedWindow.Window);
        ResetCounters(unexpectedWindow.Window);

        var hit = Win32Native.WindowFromPoint(point);
        Require(hit == expectedWindow.Window,
            $"WindowFromPoint route {name} expected {FormatHandle(expectedWindow.Window)} " +
            $"but returned {FormatHandle(hit)}.");

        var inserted = await mouse.ClickAsync(
            point,
            expectedWindow.Window,
            expectedWindow.WindowProcessId,
            routeTag,
            () => expectedWindow.ReadCounter(Win32Native.CounterTaggedLeftDown) == 1 &&
                  expectedWindow.ReadCounter(Win32Native.CounterTaggedLeftUp) == 1,
            () => expectedWindow.ReadCounter(Win32Native.CounterSafetyReleaseUp));
        Require(inserted == 3,
            $"SendInput route {name} inserted {inserted}/3 MOVE/DOWN/UP events.");

        await Task.Delay(50);
        var expectedDown = ReadCounter(expectedWindow.Window, Win32Native.CounterLeftDown);
        var expectedUp = ReadCounter(expectedWindow.Window, Win32Native.CounterLeftUp);
        var unexpectedDown = ReadCounter(unexpectedWindow.Window, Win32Native.CounterLeftDown);
        var unexpectedUp = ReadCounter(unexpectedWindow.Window, Win32Native.CounterLeftUp);
        Require(expectedDown == 1 && expectedUp == 1,
            $"Route {name} expected exactly one down/up at {expectedWindow.Kind}, " +
            $"but observed down={expectedDown}, up={expectedUp}.");
        Require(unexpectedDown == 0 && unexpectedUp == 0,
            $"Route {name} leaked mouse messages to {unexpectedWindow.Kind}: " +
            $"down={unexpectedDown}, up={unexpectedUp}.");
        VerifyOverlayNeverActivated(
            expectedWindow.Kind == "overlay" ? expectedWindow.Window : unexpectedWindow.Window,
            name);
        Console.WriteLine(string.Join(
            '|',
            "ROUTE",
            $"name={name}",
            $"point={point}",
            $"windowFromPoint={expectedWindow.Kind}",
            $"sendInput={inserted}/3",
            $"expectedMessages={expectedDown},{expectedUp}",
            $"unexpectedMessages={unexpectedDown},{unexpectedUp}",
            "result=PASS"));
    }

    private static void VerifyOverlayNeverActivated(nint overlay, string stage)
    {
        var activate = ReadCounter(overlay, Win32Native.CounterActivate);
        var activateApp = ReadCounter(overlay, Win32Native.CounterActivateApp);
        var setFocus = ReadCounter(overlay, Win32Native.CounterSetFocus);
        Require(activate == 0 && activateApp == 0 && setFocus == 0,
            $"Overlay activation/focus contract failed during {stage}: " +
            $"WM_ACTIVATE={activate}, WM_ACTIVATEAPP={activateApp}, WM_SETFOCUS={setFocus}.");
    }

    private static void ResetCounters(nint window)
    {
        Require(Win32Native.SendAcceptanceMessage(
                window,
                Win32Native.WmAcceptanceResetCounters) != nint.Zero,
            "A child window could not reset its input counters.");
    }

    private static int ReadCounter(nint window, int counter) => checked((int)
        Win32Native.SendAcceptanceMessage(
            window,
            Win32Native.WmAcceptanceReadCounter,
            new nint(counter)).ToInt64());

    private static nuint CreateRouteTag()
    {
        Span<byte> bytes = stackalloc byte[8];
        ulong value;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            value = BitConverter.ToUInt64(bytes);
        }
        while (value == 0);
        return checked((nuint)value);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(3))
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(10);
        }

        throw new TimeoutException($"Timed out waiting for {description}.");
    }

    private static async Task<bool> TryWaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            if (condition())
            {
                return true;
            }
            await Task.Delay(10);
        }

        return condition();
    }

    private static uint GetWindowProcessId(nint window)
    {
        if (window == nint.Zero)
        {
            return 0;
        }
        _ = Win32Native.GetWindowThreadProcessId(window, out var processId);
        return processId;
    }

    private static string FormatHandle(nint window) =>
        $"0x{unchecked((ulong)window.ToInt64()):X}";

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    internal sealed class ForegroundSampler : IAsyncDisposable
    {
        private readonly ConcurrentQueue<ForegroundSample> _samples = new();
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _samplingTask;
        private int _stopped;

        internal ForegroundSampler()
        {
            _samplingTask = Task.Run(SampleAsync);
        }

        internal IReadOnlyList<ForegroundSample> Samples => _samples.ToArray();

        internal async Task StopAsync()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
            {
                return;
            }

            _cancellation.Cancel();
            try
            {
                await _samplingTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _cancellation.Dispose();
        }

        private async Task SampleAsync()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                var window = Win32Native.GetForegroundWindow();
                _samples.Enqueue(new ForegroundSample(
                    Stopwatch.GetTimestamp(),
                    window,
                    GetWindowProcessId(window)));
                await Task.Delay(10, _cancellation.Token);
            }
        }
    }

    internal readonly record struct ForegroundSample(
        long Timestamp,
        nint Window,
        uint ProcessId);
}
