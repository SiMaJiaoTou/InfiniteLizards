using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace InfiniteLizards.Windows.NativeAcceptance;

internal static class ProductionDesktopAcceptanceRunner
{
    private const int ExpectedCanvasDip = 360;

    internal static async Task RunAsync(
        string executablePath,
        bool requireMixedDpi)
    {
        InteractiveWindowsSession.RequirePerMonitorV2Awareness();
        var monitors = ProductionMonitor.Enumerate();
        Require(monitors.Count > 0, "No desktop monitor is available.");
        var mouse = new SafeInjectedMouse();
        var results = new List<MonitorResult>();
        Exception? failure = null;
        try
        {
            foreach (var monitor in monitors)
            {
                results.Add(await RunMonitorAsync(executablePath, monitor, mouse));
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (mouse.HasPendingAcceptedInput)
        {
            var pending = new InvalidOperationException(
                "Production acceptance ended with an unacknowledged injected mouse sequence; " +
                "the original cursor position was intentionally not restored.");
            failure = failure is null
                ? pending
                : new AggregateException(failure, pending);
        }
        else
        {
            try
            {
                mouse.RestoreOriginalCursor();
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

        var distinctDpi = results.Select(result => result.Dpi).Distinct().ToArray();
        if (requireMixedDpi && distinctDpi.Length < 2)
        {
            throw new InvalidOperationException(
                "--require-mixed-dpi was requested, but the interactive desktop " +
                "does not expose at least two distinct monitor DPIs.");
        }
        var widthSpread = results.Max(result => result.LogicalWidth) -
            results.Min(result => result.LogicalWidth);
        var heightSpread = results.Max(result => result.LogicalHeight) -
            results.Min(result => result.LogicalHeight);
        Require(widthSpread <= 1.1d && heightSpread <= 1.1d,
            $"Production client logical size changed across monitors: " +
            $"widthSpread={widthSpread:F4}, heightSpread={heightSpread:F4}.");

        Console.WriteLine(string.Join(
            '|',
            "PRODUCTION_SUMMARY",
            $"monitors={results.Count}",
            $"distinctDpi={string.Join(',', distinctDpi)}",
            $"mixedDpi={(distinctDpi.Length > 1 ? "PASS" : "NOT_PRESENT")}",
            $"logicalWidthSpread={widthSpread:F4}",
            $"logicalHeightSpread={heightSpread:F4}",
            "productionDesktop=PASS"));
    }

    private static async Task<MonitorResult> RunMonitorAsync(
        string executablePath,
        ProductionMonitor monitor,
        SafeInjectedMouse mouse)
    {
        Require(monitor.WorkArea.Width >= ExpectedCanvasDip + 32 &&
                monitor.WorkArea.Height >= ExpectedCanvasDip + 32,
            $"Monitor work area {monitor.WorkArea} is too small for production acceptance.");
        var routeTag = CreateRouteTag();
        var protocol = new ProductionAcceptanceProtocol(Guid.NewGuid(), routeTag);
        var probe = await NativeChildProcess.StartAsync(
            "production-probe",
            monitor.WorkArea.Left,
            monitor.WorkArea.Top,
            monitor.WorkArea.Width,
            monitor.WorkArea.Height,
            routeTag);
        ProductionDesktopProcess? production = null;
        var safetyPoint = default(Win32Native.Point);
        var hasSafetyPoint = false;
        Exception? failure = null;
        MonitorResult? result = null;
        try
        {
            // This guard starts immediately after the probe becomes live. The
            // fallback foreground click can therefore never unwind through a
            // normal probe disposal while its DOWN prefix may be pending.
            safetyPoint = FindProbePoint(probe, monitor.WorkArea, nearCenter: false);
            hasSafetyPoint = true;
            await EstablishProbeForegroundAsync(probe, mouse, routeTag, safetyPoint);
            Require(Win32Native.SetCursorPos(
                    monitor.WorkAreaCenter.X,
                    monitor.WorkAreaCenter.Y),
                "Could not place the cursor on the target monitor before production launch.");

            await using var sampler = new NativeAcceptanceRunner.ForegroundSampler();
            production = await ProductionDesktopProcess.StartAsync(
                executablePath,
                protocol);
            var surface = production.Window;
            VerifySurfaceIdentity(surface, production.Process.Id, protocol);
            await SafeInjectedMouse.WaitUntilAsync(
                () => TryCaptureObservation(surface, out _),
                "a non-empty production shaped region",
                TimeSpan.FromSeconds(8));
            // The production backend's readiness ping proves EnsureVisible;
            // allow the real spawn fade and DWM composition to reach a stable
            // screen image before sampling pixels.
            await Task.Delay(1200);

            using var observation = ProductionWindowObservation.Capture(surface);
            var logicalWidth = observation.ClientRectangle.Width * 96d / observation.Dpi;
            var logicalHeight = observation.ClientRectangle.Height * 96d / observation.Dpi;
            VerifyProductionWindow(
                production,
                protocol,
                monitor,
                observation,
                logicalWidth,
                logicalHeight);
            var composite = await WaitForCompositeAsync(surface, protocol.Marker);

            var exterior = observation.FindExteriorScreenPoint();
            var interior = observation.FindInteriorScreenPoint();
            Require(!observation.ContainsScreenPoint(exterior),
                "The selected exterior point is inside the production region.");
            Require(observation.ContainsScreenPoint(interior),
                "The selected interior point is outside the production region.");

            await VerifyExteriorRouteAsync(
                production,
                protocol,
                probe,
                mouse,
                exterior,
                routeTag);
            await VerifyInteriorRouteAsync(
                production,
                protocol,
                probe,
                mouse,
                interior,
                routeTag);

            await sampler.StopAsync();
            var foregroundSamples = sampler.Samples;
            var productionForegroundSamples = foregroundSamples.Count(
                sample => sample.ProcessId == checked((uint)production.Process.Id));
            Require(productionForegroundSamples == 0,
                $"The production pet became foreground in {productionForegroundSamples} sample(s).");
            Require(Win32Native.GetForegroundWindow() == probe.Window,
                "The production pet changed the final foreground HWND.");
            VerifyNoActivation(protocol, surface);

            var resources = await ObserveResourcePlateauAsync(production.Process);
            result = new MonitorResult(
                observation.Dpi,
                logicalWidth,
                logicalHeight);
            Console.WriteLine(string.Join(
                '|',
                "PRODUCTION_MONITOR",
                $"monitor=0x{unchecked((ulong)monitor.Handle.ToInt64()):X}",
                $"primary={monitor.IsPrimary}",
                $"bounds={monitor.Bounds}",
                $"work={monitor.WorkArea}",
                $"surface=0x{unchecked((ulong)surface.ToInt64()):X}",
                $"dpi={observation.Dpi}",
                $"client={observation.ClientRectangle}",
                $"logical={logicalWidth:F4}x{logicalHeight:F4}",
                $"regionType={observation.RegionType}",
                $"compositeChanged={composite.ChangedRegionPixels}",
                $"probeBackground={composite.ExpectedBackgroundExteriorPixels}/" +
                    composite.ExteriorPixels,
                $"launchMarker=0x{protocol.Marker.BodyDibRgb:X6}," +
                    $"0x{protocol.Marker.PupilDibRgb:X6}",
                $"markerPalette={composite.MarkerBodyPixels}," +
                    $"{composite.DefaultEyeWhitePixels},{composite.MarkerPupilPixels}",
                $"compositeOutside={composite.ContaminatedExteriorPixels}/" +
                    composite.ExteriorPixels,
                $"foregroundSamples={foregroundSamples.Count}",
                $"productionForegroundSamples={productionForegroundSamples}",
                $"gdi={resources.FirstGdi},{resources.SecondGdi},{resources.FinalGdi}",
                $"user={resources.FirstUser},{resources.SecondUser},{resources.FinalUser}",
                "status=PASS"));
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (mouse.HasPendingAcceptedInput)
        {
            try
            {
                Require(hasSafetyPoint,
                    "Injected input became pending before a safety point was established.");
                await mouse.ReleaseIfNeededAsync(probe, safetyPoint);
            }
            catch (Exception cleanupException)
            {
                const string reason = "tagged LEFTUP cleanup was not proven";
                // DisposeAsync is intentionally converted to a no-op. Keeping
                // both possible capture recipients alive avoids rerouting a
                // late queued UP to an unrelated user's window. Structured
                // PID/HWND records define the later operator termination path.
                probe.PreserveForInputSafety(reason);
                production?.PreserveForInputSafety(reason);
                failure = failure is null
                    ? cleanupException
                    : new AggregateException(failure, cleanupException);
            }
        }

        failure = await DisposeAndCombineAsync(production, failure);
        failure = await DisposeAndCombineAsync(probe, failure);

        if (failure is not null)
        {
            throw failure;
        }
        return result ?? throw new InvalidOperationException(
            "Production monitor acceptance completed without a result.");
    }

    private static async Task<Exception?> DisposeAndCombineAsync(
        IAsyncDisposable? disposable,
        Exception? failure)
    {
        if (disposable is null)
        {
            return failure;
        }

        try
        {
            await disposable.DisposeAsync();
            return failure;
        }
        catch (Exception disposeException)
        {
            return failure is null
                ? disposeException
                : new AggregateException(failure, disposeException);
        }
    }

    private static async Task EstablishProbeForegroundAsync(
        NativeChildProcess probe,
        SafeInjectedMouse mouse,
        nuint routeTag,
        Win32Native.Point point)
    {
        _ = Win32Native.SetForegroundWindow(probe.Window);
        if (await IsEventuallyAsync(
                () => Win32Native.GetForegroundWindow() == probe.Window,
                TimeSpan.FromMilliseconds(500)))
        {
            return;
        }

        probe.ResetCounters();
        var inserted = await mouse.ClickAsync(
            point,
            probe.Window,
            probe.WindowProcessId,
            routeTag,
            () => probe.ReadCounter(Win32Native.CounterTaggedLeftDown) == 1 &&
                  probe.ReadCounter(Win32Native.CounterTaggedLeftUp) == 1,
            () => probe.ReadCounter(Win32Native.CounterSafetyReleaseUp));
        Require(inserted == 3,
            $"The foreground-establishing MOVE/DOWN/UP inserted {inserted}/3 events.");
        await SafeInjectedMouse.WaitUntilAsync(
            () => Win32Native.GetForegroundWindow() == probe.Window,
            "the independently owned probe to become foreground");
        probe.ResetCounters();
    }

    private static void VerifySurfaceIdentity(
        nint surface,
        int expectedProcessId,
        ProductionAcceptanceProtocol protocol)
    {
        var threadId = Win32Native.GetWindowThreadProcessId(surface, out var processId);
        Require(threadId != 0 && processId == checked((uint)expectedProcessId),
            "The nonce-scoped production HWND belongs to the wrong process.");
        Require(protocol.IsReadyWindow(surface),
            "The production HWND stopped answering its nonce-scoped readiness ping.");
        Require(Win32Native.IsWindowVisible(surface),
            "The production HWND is not visible after readiness.");
    }

    private static void VerifyProductionWindow(
        ProductionDesktopProcess production,
        ProductionAcceptanceProtocol protocol,
        ProductionMonitor monitor,
        ProductionWindowObservation observation,
        double logicalWidth,
        double logicalHeight)
    {
        var surface = production.Window;
        VerifySurfaceIdentity(surface, production.Process.Id, protocol);
        var exStyle = Win32Native.ReadWindowBits(surface, Win32Native.GwlExStyle);
        var required = Win32Native.WsExToolWindow |
            Win32Native.WsExNoActivate |
            Win32Native.WsExTopmost;
        Require((exStyle & required) == required,
            $"Production GWL_EXSTYLE=0x{exStyle:X8} lacks TOOLWINDOW/NOACTIVATE/TOPMOST.");
        var dpiContext = Win32Native.GetWindowDpiAwarenessContext(surface);
        Require(Win32Native.AreDpiAwarenessContextsEqual(
                dpiContext,
                Win32Native.DpiAwarenessContextPerMonitorAwareV2),
            "The production HWND is not per-monitor-v2 DPI aware.");
        Require(Win32Native.MonitorFromWindow(
                surface,
                Win32Native.MonitorDefaultToNearest) == monitor.Handle,
            "The production HWND did not spawn on the target monitor selected by the cursor.");
        Require(Math.Abs(logicalWidth - ExpectedCanvasDip) <= 1.1d &&
                Math.Abs(logicalHeight - ExpectedCanvasDip) <= 1.1d,
            $"Production client is not {ExpectedCanvasDip} logical DIP: " +
            $"{logicalWidth:F4}x{logicalHeight:F4}.");
    }

    private static async Task<ProductionCompositeObservation> WaitForCompositeAsync(
        nint surface,
        ProductionAcceptanceMarker marker)
    {
        var timeout = Stopwatch.StartNew();
        ProductionCompositeObservation? latest = null;
        Exception? latestFailure = null;
        while (timeout.Elapsed < TimeSpan.FromSeconds(6))
        {
            try
            {
                using var observation = ProductionWindowObservation.Capture(surface);
                latest = ProductionCompositeCapture.CaptureAndAnalyze(observation, marker);
                if (latest.IsLaunchMarkedLizardFrame(out _))
                {
                    return latest;
                }
            }
            catch (Exception exception)
            {
                latestFailure = exception;
            }
            await Task.Delay(50);
        }

        throw new InvalidOperationException(
            latest is null
                ? "DWM composite sampling never produced a usable production frame."
                : "DWM composite did not show real pet pixels clipped to the production region: " +
                  (latest.IsLaunchMarkedLizardFrame(out var reason) ?
                      "unexpected oracle state." : reason),
            latestFailure);
    }

    private static async Task VerifyExteriorRouteAsync(
        ProductionDesktopProcess production,
        ProductionAcceptanceProtocol protocol,
        NativeChildProcess probe,
        SafeInjectedMouse mouse,
        Win32Native.Point exterior,
        nuint routeTag)
    {
        Require(Win32Native.SetCursorPos(exterior.X, exterior.Y),
            "Could not move to the production-region exterior.");
        await SafeInjectedMouse.WaitUntilAsync(
            () => !Win32Native.IsWindowEnabled(production.Window) &&
                  (Win32Native.ReadWindowBits(
                       production.Window,
                       Win32Native.GwlStyle) & Win32Native.WsDisabled) != 0 &&
                  Win32Native.WindowFromPoint(exterior) == probe.Window,
            "production click-through disable and exterior probe routing");
        protocol.ResetRoute(production.Window);
        probe.ResetCounters();
        var inserted = await mouse.ClickAsync(
            exterior,
            probe.Window,
            probe.WindowProcessId,
            routeTag,
            () => probe.ReadCounter(Win32Native.CounterTaggedLeftDown) == 1 &&
                  probe.ReadCounter(Win32Native.CounterTaggedLeftUp) == 1,
            () => probe.ReadCounter(Win32Native.CounterSafetyReleaseUp));
        Require(inserted == 3,
            $"The exterior MOVE/DOWN/UP route inserted {inserted}/3 events.");
        Require(protocol.ReadCounter(
                    production.Window,
                    ProductionAcceptanceProtocol.CounterTaggedLeftDown) == 0 &&
                protocol.ReadCounter(
                    production.Window,
                    ProductionAcceptanceProtocol.CounterTaggedLeftUp) == 0,
            "The disabled production overlay received the exterior tagged click.");
    }

    private static async Task VerifyInteriorRouteAsync(
        ProductionDesktopProcess production,
        ProductionAcceptanceProtocol protocol,
        NativeChildProcess probe,
        SafeInjectedMouse mouse,
        Win32Native.Point interior,
        nuint routeTag)
    {
        Require(Win32Native.SetCursorPos(interior.X, interior.Y),
            "Could not move to the production-region interior.");
        await SafeInjectedMouse.WaitUntilAsync(
            () => Win32Native.IsWindowEnabled(production.Window) &&
                  (Win32Native.ReadWindowBits(
                       production.Window,
                       Win32Native.GwlStyle) & Win32Native.WsDisabled) == 0 &&
                  Win32Native.WindowFromPoint(interior) == production.Window,
            "production interaction enable and interior surface routing");
        protocol.ResetRoute(production.Window);
        probe.ResetCounters();
        var inserted = await mouse.ClickAsync(
            interior,
            production.Window,
            checked((uint)production.Process.Id),
            routeTag,
            () => protocol.ReadCounter(
                      production.Window,
                      ProductionAcceptanceProtocol.CounterTaggedLeftDown) == 1 &&
                  protocol.ReadCounter(
                      production.Window,
                      ProductionAcceptanceProtocol.CounterTaggedLeftUp) == 1,
            () => protocol.ReadCounter(
                production.Window,
                ProductionAcceptanceProtocol.CounterSafetyReleaseUp,
                timeoutMilliseconds: 100));
        Require(inserted == 3,
            $"The interior MOVE/DOWN/UP route inserted {inserted}/3 events.");
        Require(probe.ReadCounter(Win32Native.CounterTaggedLeftDown) == 0 &&
                probe.ReadCounter(Win32Native.CounterTaggedLeftUp) == 0,
            "The production interior click leaked to the independent probe.");
    }

    private static void VerifyNoActivation(
        ProductionAcceptanceProtocol protocol,
        nint surface)
    {
        var activate = protocol.ReadCounter(
            surface,
            ProductionAcceptanceProtocol.CounterActivate);
        var activateApp = protocol.ReadCounter(
            surface,
            ProductionAcceptanceProtocol.CounterActivateApp);
        var setFocus = protocol.ReadCounter(
            surface,
            ProductionAcceptanceProtocol.CounterSetFocus);
        Require(activate == 0 && activateApp == 0 && setFocus == 0,
            $"Production activation contract failed: WM_ACTIVATE={activate}, " +
            $"WM_ACTIVATEAPP={activateApp}, WM_SETFOCUS={setFocus}.");
    }

    private static async Task<ResourceObservation> ObserveResourcePlateauAsync(
        Process production)
    {
        await Task.Delay(250);
        var first = ReadResources(production);
        await Task.Delay(1000);
        var second = ReadResources(production);
        await Task.Delay(1000);
        var final = ReadResources(production);
        Require(final.Gdi <= second.Gdi + 2,
            $"Production GDI objects did not plateau: {first.Gdi}->{second.Gdi}->{final.Gdi}.");
        Require(final.User <= second.User + 2,
            $"Production USER objects did not plateau: {first.User}->{second.User}->{final.User}.");
        return new ResourceObservation(
            first.Gdi,
            second.Gdi,
            final.Gdi,
            first.User,
            second.User,
            final.User);
    }

    private static (uint Gdi, uint User) ReadResources(Process production)
    {
        var gdi = Win32Native.GetGuiResources(
            production.Handle,
            Win32Native.GrGdiObjects);
        var user = Win32Native.GetGuiResources(
            production.Handle,
            Win32Native.GrUserObjects);
        Require(user > 0, "GetGuiResources returned no USER objects for production Desktop.");
        return (gdi, user);
    }

    private static Win32Native.Point FindProbePoint(
        NativeChildProcess probe,
        Win32Native.Rect workArea,
        bool nearCenter)
    {
        var startX = nearCenter ? workArea.Left + workArea.Width / 2 : workArea.Left + 16;
        var startY = nearCenter ? workArea.Top + workArea.Height / 2 : workArea.Top + 16;
        for (var radius = 0; radius < Math.Max(workArea.Width, workArea.Height); radius += 16)
        {
            for (var y = Math.Max(workArea.Top + 2, startY - radius);
                 y < Math.Min(workArea.Bottom - 2, startY + radius + 1);
                 y += 16)
            {
                for (var x = Math.Max(workArea.Left + 2, startX - radius);
                     x < Math.Min(workArea.Right - 2, startX + radius + 1);
                     x += 16)
                {
                    var point = new Win32Native.Point(x, y);
                    if (Win32Native.WindowFromPoint(point) == probe.Window)
                    {
                        return point;
                    }
                }
            }
        }

        throw new InvalidOperationException(
            "No controller-owned safety point is visible in the target monitor work area.");
    }

    private static nuint CreateRouteTag()
    {
        Span<byte> bytes = stackalloc byte[8];
        do
        {
            RandomNumberGenerator.Fill(bytes);
        }
        while (BitConverter.ToUInt64(bytes) == 0);
        return checked((nuint)BitConverter.ToUInt64(bytes));
    }

    private static bool TryCaptureObservation(
        nint surface,
        out ProductionWindowObservation? observation)
    {
        try
        {
            observation = ProductionWindowObservation.Capture(surface);
            observation.Dispose();
            observation = null;
            return true;
        }
        catch
        {
            observation = null;
            return false;
        }
    }

    private static async Task<bool> IsEventuallyAsync(
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record MonitorResult(
        uint Dpi,
        double LogicalWidth,
        double LogicalHeight);

    private sealed record ResourceObservation(
        uint FirstGdi,
        uint SecondGdi,
        uint FinalGdi,
        uint FirstUser,
        uint SecondUser,
        uint FinalUser);
}
