using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InfiniteLizards.Windows.NativeAcceptance;

/// <summary>
/// Keeps every injected button sequence scoped to controller-owned HWNDs.
/// MOVE/DOWN/UP is one atomic SendInput request, and a request which may have
/// accepted DOWN remains recorded until an explicitly tagged UP is observed.
/// </summary>
internal sealed class SafeInjectedMouse
{
    private const int VkLeftButton = 0x01;
    private const int VkRightButton = 0x02;
    private const int VkMiddleButton = 0x04;
    private const int VkXButton1 = 0x05;
    private const int VkXButton2 = 0x06;
    private const int EmergencyReleaseAttempts = 3;

    private readonly Win32Native.Point _originalCursor;
    private PendingRoute? _pendingRoute;

    internal SafeInjectedMouse()
    {
        foreach (var button in new[]
                 {
                     VkLeftButton, VkRightButton, VkMiddleButton,
                     VkXButton1, VkXButton2
                 })
        {
            Require(
                (Win32Native.GetAsyncKeyState(button) & 0x8000) == 0,
                "A physical mouse button is down; release it before production acceptance.");
        }
        if (!Win32Native.GetCursorPos(out _originalCursor))
        {
            throw Win32Native.Failure("GetCursorPos(production original)");
        }
    }

    internal bool HasPendingAcceptedInput =>
        Volatile.Read(ref _pendingRoute) is not null;

    /// <summary>
    /// Returns the exact SendInput prefix length. With the ordered
    /// MOVE/DOWN/UP batch, 0/1 means DOWN was not accepted, 2 means DOWN may
    /// be pending, and only 3 is eligible for route acknowledgement.
    /// </summary>
    internal async Task<uint> ClickAsync(
        Win32Native.Point point,
        nint expectedWindow,
        uint expectedProcessId,
        nuint routeTag,
        Func<bool> acknowledged,
        Func<int> readSafetyReleaseCounter)
    {
        ArgumentNullException.ThrowIfNull(acknowledged);
        ArgumentNullException.ThrowIfNull(readSafetyReleaseCounter);
        Require(routeTag != 0, "The route tag cannot be zero.");
        Require(!HasPendingAcceptedInput,
            "A second injected route cannot start while LEFTDOWN may still be pending.");
        ValidateOwnedPoint(point, expectedWindow, expectedProcessId, "route");
        var targetSafetyReleaseBaseline = readSafetyReleaseCounter();

        var (absoluteX, absoluteY) = NormalizeVirtualDesktopPoint(point);
        var inputs = new[]
        {
            AbsoluteMoveInput(absoluteX, absoluteY, routeTag),
            MouseInput(Win32Native.MouseEventLeftDown, routeTag),
            MouseInput(Win32Native.MouseEventLeftUp, routeTag)
        };
        var pending = new PendingRoute(
            point,
            expectedWindow,
            expectedProcessId,
            readSafetyReleaseCounter,
            targetSafetyReleaseBaseline);
        Volatile.Write(ref _pendingRoute, pending);
        Marshal.SetLastPInvokeError(0);
        var inserted = Win32Native.SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<Win32Native.Input>());

        if (!MayHaveAcceptedLeftDown(inserted))
        {
            ClearPending(pending);
            return inserted;
        }
        if (inserted != inputs.Length)
        {
            // MOVE+DOWN is the only partial prefix which can leave the system
            // button state down. The caller's finally block owns recovery.
            return inserted;
        }

        var completed = await TryWaitUntilAsync(
            () => acknowledged() && IsLeftButtonUp(),
            TimeSpan.FromSeconds(3));
        if (!completed)
        {
            throw new InvalidOperationException(
                "The owned target did not acknowledge the complete tagged " +
                $"MOVE/DOWN/UP route after SendInput inserted {inserted}/{inputs.Length} events.");
        }

        ClearPending(pending);
        return inserted;
    }

    /// <summary>
    /// Retries a tagged absolute MOVE+UP batch only over live controller-owned
    /// HWNDs. Mouse capture may route UP to the original production/probe
    /// target, so either target's explicit safety counter is accepted, but the
    /// global async button state must also read up.
    /// </summary>
    internal async Task ReleaseIfNeededAsync(
        NativeChildProcess safetyProbe,
        Win32Native.Point safetyPoint)
    {
        ArgumentNullException.ThrowIfNull(safetyProbe);
        var pending = Volatile.Read(ref _pendingRoute);
        if (pending is null)
        {
            return;
        }

        ValidateOwnedPoint(
            safetyPoint,
            safetyProbe.Window,
            safetyProbe.WindowProcessId,
            "emergency release");
        var (absoluteX, absoluteY) = NormalizeVirtualDesktopPoint(safetyPoint);
        Exception? latestFailure = null;
        for (var attempt = 1; attempt <= EmergencyReleaseAttempts; attempt++)
        {
            try
            {
                var probeBefore = safetyProbe.ReadCounter(
                    Win32Native.CounterSafetyReleaseUp);
                var release = new[]
                {
                    AbsoluteMoveInput(
                        absoluteX,
                        absoluteY,
                        Win32Native.SafetyReleaseExtraInfo),
                    MouseInput(
                        Win32Native.MouseEventLeftUp,
                        Win32Native.SafetyReleaseExtraInfo)
                };
                Marshal.SetLastPInvokeError(0);
                var inserted = Win32Native.SendInput(
                    checked((uint)release.Length),
                    release,
                    Marshal.SizeOf<Win32Native.Input>());
                if (inserted != release.Length)
                {
                    latestFailure = new InvalidOperationException(
                        $"Emergency MOVE/LEFTUP attempt {attempt} inserted " +
                        $"{inserted}/{release.Length} events; " +
                        $"Win32 error={Marshal.GetLastPInvokeError()}.");
                    continue;
                }

                var released = await TryWaitUntilAsync(
                    () => IsLeftButtonUp() &&
                          (safetyProbe.ReadCounter(
                               Win32Native.CounterSafetyReleaseUp) > probeBefore ||
                           TryReadIncrementedTargetCounter(pending)),
                    TimeSpan.FromSeconds(2));
                if (released)
                {
                    ClearPending(pending);
                    return;
                }

                latestFailure = new InvalidOperationException(
                    $"Emergency MOVE/LEFTUP attempt {attempt} had no explicit " +
                    "tag acknowledgement from the production target or safety probe.");
            }
            catch (Exception exception)
            {
                latestFailure = exception;
            }
        }

        throw new InvalidOperationException(
            $"Could not prove emergency LEFTUP cleanup after " +
            $"{EmergencyReleaseAttempts} bounded attempts. The safety HWNDs must remain alive. " +
            $"Pending target=0x{unchecked((ulong)pending.TargetWindow.ToInt64()):X}, " +
            $"point={pending.TargetPoint}.",
            latestFailure);
    }

    internal void RestoreOriginalCursor()
    {
        Require(!HasPendingAcceptedInput,
            "The original cursor cannot be restored while injected input may still be pending.");
        if (!Win32Native.SetCursorPos(_originalCursor.X, _originalCursor.Y))
        {
            throw Win32Native.Failure("SetCursorPos(production restore)");
        }
    }

    internal static bool MayHaveAcceptedLeftDown(uint insertedPrefixLength) =>
        insertedPrefixLength >= 2;

    internal static (int X, int Y) NormalizeVirtualDesktopPoint(
        Win32Native.Point point,
        int virtualLeft,
        int virtualTop,
        int virtualWidth,
        int virtualHeight) =>
        (
            NormalizeAbsoluteCoordinate(point.X, virtualLeft, virtualWidth),
            NormalizeAbsoluteCoordinate(point.Y, virtualTop, virtualHeight)
        );

    internal static async Task WaitUntilAsync(
        Func<bool> condition,
        string description,
        TimeSpan? timeout = null)
    {
        if (!await TryWaitUntilAsync(
                condition,
                timeout ?? TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException($"Timed out waiting for {description}.");
        }
    }

    private static (int X, int Y) NormalizeVirtualDesktopPoint(
        Win32Native.Point point)
    {
        var left = Win32Native.GetSystemMetrics(Win32Native.SmXVirtualScreen);
        var top = Win32Native.GetSystemMetrics(Win32Native.SmYVirtualScreen);
        var width = Win32Native.GetSystemMetrics(Win32Native.SmCxVirtualScreen);
        var height = Win32Native.GetSystemMetrics(Win32Native.SmCyVirtualScreen);
        return NormalizeVirtualDesktopPoint(point, left, top, width, height);
    }

    private static int NormalizeAbsoluteCoordinate(
        int coordinate,
        int origin,
        int extent)
    {
        if (extent <= 1)
        {
            throw new InvalidOperationException(
                $"Virtual-desktop extent must exceed one pixel (actual {extent}).");
        }
        var offset = (long)coordinate - origin;
        if (offset < 0 || offset >= extent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coordinate),
                $"Coordinate {coordinate} lies outside [{origin}, {origin + (long)extent}).");
        }

        // MOUSEEVENTF_ABSOLUTE maps the inclusive [0,65535] interval onto the
        // inclusive first/last virtual-desktop pixel when VIRTUALDESK is set.
        return checked((int)((offset * 65_535L + (extent - 1L) / 2L) /
            (extent - 1L)));
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

    private static bool IsLeftButtonUp() =>
        (Win32Native.GetAsyncKeyState(VkLeftButton) & 0x8000) == 0;

    private static bool TryReadIncrementedTargetCounter(PendingRoute pending)
    {
        try
        {
            return pending.ReadSafetyReleaseCounter() >
                pending.TargetSafetyReleaseBaseline;
        }
        catch
        {
            // A dead/crashed/hung original target must never prevent sending
            // cleanup to the independent live safety probe. It only removes
            // one of the two acceptable explicit acknowledgement sources.
            return false;
        }
    }

    private static void ValidateOwnedPoint(
        Win32Native.Point point,
        nint expectedWindow,
        uint expectedProcessId,
        string stage)
    {
        ValidateOwnedWindow(expectedWindow, expectedProcessId, stage);
        var hit = Win32Native.WindowFromPoint(point);
        _ = Win32Native.GetWindowThreadProcessId(hit, out var hitProcessId);
        Require(hit == expectedWindow && hitProcessId == expectedProcessId,
            $"The {stage} point {point} is not owned by the expected HWND/PID.");
    }

    private static void ValidateOwnedWindow(
        nint expectedWindow,
        uint expectedProcessId,
        string stage)
    {
        Require(Win32Native.IsWindow(expectedWindow),
            $"The {stage} target HWND is no longer alive.");
        _ = Win32Native.GetWindowThreadProcessId(expectedWindow, out var ownerProcessId);
        Require(ownerProcessId == expectedProcessId,
            $"The {stage} target HWND is no longer owned by the expected PID.");
    }

    private static Win32Native.Input AbsoluteMoveInput(
        int normalizedX,
        int normalizedY,
        nuint extraInfo) => new()
    {
        Type = Win32Native.InputMouse,
        Value = new Win32Native.InputUnion
        {
            Mouse = new Win32Native.MouseInput
            {
                DeltaX = normalizedX,
                DeltaY = normalizedY,
                Flags = Win32Native.MouseEventMove |
                    Win32Native.MouseEventAbsolute |
                    Win32Native.MouseEventVirtualDesk,
                ExtraInfo = extraInfo
            }
        }
    };

    private static Win32Native.Input MouseInput(uint flags, nuint extraInfo) => new()
    {
        Type = Win32Native.InputMouse,
        Value = new Win32Native.InputUnion
        {
            Mouse = new Win32Native.MouseInput
            {
                Flags = flags,
                ExtraInfo = extraInfo
            }
        }
    };

    private void ClearPending(PendingRoute pending) =>
        _ = Interlocked.CompareExchange(ref _pendingRoute, null, pending);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record PendingRoute(
        Win32Native.Point TargetPoint,
        nint TargetWindow,
        uint TargetProcessId,
        Func<int> ReadSafetyReleaseCounter,
        int TargetSafetyReleaseBaseline);
}
