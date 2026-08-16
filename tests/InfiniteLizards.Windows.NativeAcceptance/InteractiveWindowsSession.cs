using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace InfiniteLizards.Windows.NativeAcceptance;

internal static class InteractiveWindowsSession
{
    internal static string? GetSkipReason()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "This acceptance executable requires Windows USER32/GDI32.";
        }

        if (!Environment.Is64BitProcess)
        {
            return "The production overlay and this acceptance executable require a 64-bit process.";
        }

        if (!Environment.UserInteractive)
        {
            return "Environment.UserInteractive is false.";
        }

        var windowStation = Win32Native.GetProcessWindowStation();
        if (windowStation == nint.Zero)
        {
            return $"GetProcessWindowStation failed with Win32 error {Marshal.GetLastPInvokeError()}.";
        }

        if (!Win32Native.GetUserObjectInformation(
                windowStation,
                Win32Native.UoiFlags,
                out var stationFlags,
                checked((uint)Marshal.SizeOf<Win32Native.UserObjectFlags>()),
                out _))
        {
            return $"The process window station could not be inspected (Win32 error " +
                   $"{Marshal.GetLastPInvokeError()}).";
        }

        if ((stationFlags.Flags & Win32Native.WsfVisible) == 0)
        {
            return "The process is attached to a non-visible window station.";
        }

        var inputDesktop = Win32Native.OpenInputDesktop(
            0,
            false,
            Win32Native.DesktopReadObjects | Win32Native.DesktopSwitchDesktop);
        if (inputDesktop == nint.Zero)
        {
            return $"The active input desktop is unavailable (Win32 error " +
                   $"{Marshal.GetLastPInvokeError()}); the session may be locked or secure.";
        }

        try
        {
            var currentDesktop = Win32Native.GetThreadDesktop(
                Win32Native.GetCurrentThreadId());
            if (currentDesktop == nint.Zero)
            {
                return $"GetThreadDesktop failed with Win32 error " +
                       $"{Marshal.GetLastPInvokeError()}.";
            }

            string inputName;
            string currentName;
            try
            {
                inputName = ReadObjectName(inputDesktop);
                currentName = ReadObjectName(currentDesktop);
            }
            catch (Exception exception)
            {
                return "The input/current desktop names could not be verified: " +
                       exception.Message;
            }
            if (!string.Equals(inputName, currentName, StringComparison.OrdinalIgnoreCase))
            {
                return $"The process desktop '{currentName}' is not the active input desktop " +
                       $"'{inputName}'; the session may be locked or displaying a secure desktop.";
            }
        }
        finally
        {
            _ = Win32Native.CloseDesktop(inputDesktop);
        }

        var sessionId = Process.GetCurrentProcess().SessionId;
        if (!Win32Native.WTSQuerySessionInformation(
                nint.Zero,
                sessionId,
                Win32Native.WtsConnectState,
                out var stateBuffer,
                out var stateBytes))
        {
            return $"WTS session {sessionId} could not be inspected (Win32 error " +
                   $"{Marshal.GetLastPInvokeError()}).";
        }

        try
        {
            if (stateBuffer == nint.Zero || stateBytes < sizeof(int))
            {
                return $"WTS session {sessionId} returned an invalid connection-state payload.";
            }

            var state = Marshal.ReadInt32(stateBuffer);
            return state == Win32Native.WtsActive
                ? null
                : $"WTS session {sessionId} is not active (state={state}).";
        }
        finally
        {
            Win32Native.WTSFreeMemory(stateBuffer);
        }
    }

    internal static void RequirePerMonitorV2Awareness()
    {
        Marshal.SetLastPInvokeError(0);
        _ = Win32Native.SetProcessDpiAwarenessContext(
            Win32Native.DpiAwarenessContextPerMonitorAwareV2);
        if (!Win32Native.AreDpiAwarenessContextsEqual(
                Win32Native.GetThreadDpiAwarenessContext(),
                Win32Native.DpiAwarenessContextPerMonitorAwareV2))
        {
            throw new InvalidOperationException(
                "The native acceptance process is not per-monitor-v2 DPI aware; " +
                $"SetProcessDpiAwarenessContext error={Marshal.GetLastPInvokeError()}.");
        }
    }

    private static string ReadObjectName(nint value)
    {
        var buffer = new StringBuilder(256);
        if (!Win32Native.GetUserObjectName(
                value,
                Win32Native.UoiName,
                buffer,
                checked((uint)(buffer.Capacity * sizeof(char))),
                out _))
        {
            throw Win32Native.Failure("GetUserObjectInformationW(UOI_NAME)");
        }

        return buffer.ToString();
    }
}
