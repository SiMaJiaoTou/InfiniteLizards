using System.Runtime.InteropServices;

namespace InfiniteLizards.Windows.NativeAcceptance;

internal sealed record ProductionMonitor(
    nint Handle,
    Win32Native.Rect Bounds,
    Win32Native.Rect WorkArea,
    bool IsPrimary)
{
    internal Win32Native.Point WorkAreaCenter => new(
        checked(WorkArea.Left + WorkArea.Width / 2),
        checked(WorkArea.Top + WorkArea.Height / 2));

    internal static IReadOnlyList<ProductionMonitor> Enumerate()
    {
        var monitors = new List<ProductionMonitor>();
        Exception? callbackFailure = null;
        Win32Native.MonitorEnumerationProcedure callback =
            (nint monitor,
                nint deviceContext,
                ref Win32Native.Rect monitorRectangle,
                nint parameter) =>
            {
                try
                {
                    var information = new Win32Native.MonitorInfo
                    {
                        Size = checked((uint)Marshal.SizeOf<Win32Native.MonitorInfo>())
                    };
                    if (!Win32Native.GetMonitorInfo(monitor, ref information))
                    {
                        throw Win32Native.Failure("GetMonitorInfoW");
                    }

                    monitors.Add(new ProductionMonitor(
                        monitor,
                        information.Monitor,
                        information.Work,
                        (information.Flags & Win32Native.MonitorInfoPrimary) != 0));
                    return true;
                }
                catch (Exception exception)
                {
                    callbackFailure = exception;
                    return false;
                }
            };

        var enumerated = Win32Native.EnumDisplayMonitors(
                nint.Zero,
                nint.Zero,
                callback,
                nint.Zero);
        GC.KeepAlive(callback);
        if (callbackFailure is not null)
        {
            throw new InvalidOperationException(
                "Monitor enumeration failed inside its native callback.",
                callbackFailure);
        }
        if (!enumerated)
        {
            throw Win32Native.Failure("EnumDisplayMonitors");
        }

        return monitors
            .OrderByDescending(monitor => monitor.IsPrimary)
            .ThenBy(monitor => monitor.Bounds.Left)
            .ThenBy(monitor => monitor.Bounds.Top)
            .ToArray();
    }
}
