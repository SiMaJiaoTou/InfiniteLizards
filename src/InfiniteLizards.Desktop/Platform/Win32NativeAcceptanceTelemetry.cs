using System.Diagnostics;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace InfiniteLizards.Desktop.Platform;

/// <summary>
/// Private, launch-scoped protocol used by the external Windows production
/// acceptance controller. Nothing is registered unless all three environment
/// values are valid and the declared controller is this process's real,
/// same-session parent.
/// </summary>
internal static class Win32NativeAcceptanceProtocol
{
    internal const string NonceEnvironmentVariable =
        "INFINITE_LIZARDS_WINDOWS_NATIVE_ACCEPTANCE_NONCE";
    internal const string ParentProcessIdEnvironmentVariable =
        "INFINITE_LIZARDS_WINDOWS_NATIVE_ACCEPTANCE_PARENT_PID";
    internal const string RouteTagEnvironmentVariable =
        "INFINITE_LIZARDS_WINDOWS_NATIVE_ACCEPTANCE_ROUTE_TAG";

    internal const ulong PingMagic = 0x494C5A4450524F44UL; // "ILZDPROD"

    internal const int PingCommand = 1;
    internal const int ResetRouteCommand = 2;
    internal const int ReadCounterCommand = 3;

    internal const int TaggedLeftDownCounter = 1;
    internal const int TaggedLeftUpCounter = 2;
    internal const int ActivateCounter = 3;
    internal const int ActivateAppCounter = 4;
    internal const int SetFocusCounter = 5;
    internal const int MouseActivateCounter = 6;
    internal const int SafetyReleaseUpCounter = 7;

    internal const ulong SafetyReleaseExtraInfo = 0x494C5A4453414645UL;
    internal const string StableControlMessageName =
        "InfiniteLizards.Windows.NativeAcceptance.v1.Control";

    internal static string ControlMessageName(string canonicalNonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalNonce);
        return StableControlMessageName;
    }

    internal static ulong DeriveControlToken(string canonicalNonce)
    {
        if (!Guid.TryParseExact(canonicalNonce, "N", out var nonce) ||
            nonce == Guid.Empty)
        {
            throw new ArgumentException("A non-empty canonical GUID-N nonce is required.",
                nameof(canonicalNonce));
        }

        Span<byte> bytes = stackalloc byte[16];
        _ = nonce.TryWriteBytes(bytes);
        var first = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        var second = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
        var token = first ^ BitOperations.RotateLeft(second, 17) ^
            0x494C5A444354524CUL;
        return token == 0 ? 0x494C5A444354524CUL : token;
    }

    internal static int PayloadCommand(nint payload) =>
        unchecked((int)(uint)(unchecked((ulong)payload.ToInt64()) & uint.MaxValue));

    internal static int PayloadParameter(nint payload) =>
        unchecked((int)(uint)(unchecked((ulong)payload.ToInt64()) >> 32));

    internal static bool TryReadEnvironment(
        out Win32NativeAcceptanceConfiguration configuration)
    {
        var nonce = Environment.GetEnvironmentVariable(NonceEnvironmentVariable);
        var parentProcessId = Environment.GetEnvironmentVariable(
            ParentProcessIdEnvironmentVariable);
        var routeTag = Environment.GetEnvironmentVariable(RouteTagEnvironmentVariable);
        return TryParse(nonce, parentProcessId, routeTag, out configuration);
    }

    internal static bool TryParse(
        string? nonce,
        string? parentProcessId,
        string? routeTag,
        out Win32NativeAcceptanceConfiguration configuration)
    {
        configuration = default;
        if (!Guid.TryParseExact(nonce, "N", out var parsedNonce) ||
            parsedNonce == Guid.Empty ||
            !int.TryParse(
                parentProcessId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsedParentProcessId) ||
            parsedParentProcessId <= 0 ||
            routeTag is not { Length: 16 } ||
            !ulong.TryParse(
                routeTag,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var parsedRouteTag) ||
            parsedRouteTag == 0)
        {
            return false;
        }

        configuration = new Win32NativeAcceptanceConfiguration(
            parsedNonce.ToString("N"),
            parsedParentProcessId,
            parsedRouteTag);
        return true;
    }
}

internal readonly record struct Win32NativeAcceptanceConfiguration(
    string CanonicalNonce,
    int ParentProcessId,
    ulong RouteTag);

/// <summary>
/// Dormant production-window telemetry. The external controller still makes
/// all placement, DPI, region, hit-routing and composite assertions through
/// independent Win32 readback. This tap only proves that tagged SendInput
/// packets reached the real Avalonia HWND and that it never activated.
/// </summary>
internal sealed class Win32NativeAcceptanceTelemetry
{
    private const uint WmActivate = 0x0006;
    private const uint WmSetFocus = 0x0007;
    private const uint WmActivateApp = 0x001C;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmLeftButtonDown = 0x0201;
    private const uint WmLeftButtonUp = 0x0202;

    private const uint Th32csSnapProcess = 0x00000002;
    private static readonly nint InvalidHandleValue = new(-1);

    private readonly uint _controlMessage;
    private readonly nuint _controlToken;
    private readonly nuint _routeTag;
    private nint _readyWindow;
    private long _taggedLeftDown;
    private long _taggedLeftUp;
    private long _activate;
    private long _activateApp;
    private long _setFocus;
    private long _mouseActivate;
    private long _safetyReleaseUp;

    private Win32NativeAcceptanceTelemetry(
        uint controlMessage,
        nuint controlToken,
        nuint routeTag)
    {
        _controlMessage = controlMessage;
        _controlToken = controlToken;
        _routeTag = routeTag;
    }

    internal static Win32NativeAcceptanceTelemetry? TryCreate()
    {
        if (!OperatingSystem.IsWindows() ||
            nint.Size != 8 ||
            !Win32NativeAcceptanceProtocol.TryReadEnvironment(out var configuration) ||
            !IsLiveActualParent(configuration.ParentProcessId))
        {
            return null;
        }

        var message = RegisterWindowMessage(
            Win32NativeAcceptanceProtocol.ControlMessageName(
                configuration.CanonicalNonce));
        if (message == 0)
        {
            Trace.WriteLine(
                "[InfiniteLizards] Windows production-acceptance telemetry " +
                $"could not register its private control message; error={Marshal.GetLastPInvokeError()}.");
            return null;
        }

        return new Win32NativeAcceptanceTelemetry(
            message,
            unchecked((nuint)Win32NativeAcceptanceProtocol.DeriveControlToken(
                configuration.CanonicalNonce)),
            unchecked((nuint)configuration.RouteTag));
    }

    internal void MarkReady(nint window)
    {
        if (window != nint.Zero)
        {
            Volatile.Write(ref _readyWindow, window);
        }
    }

    internal void Disable() => Volatile.Write(ref _readyWindow, nint.Zero);

    internal bool TryHandleControl(
        nint window,
        uint message,
        nint wParam,
        nint lParam,
        out nint result)
    {
        result = nint.Zero;
        if (message != _controlMessage ||
            window == nint.Zero ||
            Volatile.Read(ref _readyWindow) != window ||
            unchecked((nuint)wParam.ToInt64()) != _controlToken)
        {
            return false;
        }

        var command = Win32NativeAcceptanceProtocol.PayloadCommand(lParam);
        var parameter = Win32NativeAcceptanceProtocol.PayloadParameter(lParam);
        switch (command)
        {
            case Win32NativeAcceptanceProtocol.PingCommand:
                result = new nint(unchecked((long)Win32NativeAcceptanceProtocol.PingMagic));
                break;
            case Win32NativeAcceptanceProtocol.ResetRouteCommand:
                Interlocked.Exchange(ref _taggedLeftDown, 0);
                Interlocked.Exchange(ref _taggedLeftUp, 0);
                result = new nint(1);
                break;
            case Win32NativeAcceptanceProtocol.ReadCounterCommand:
                result = new nint(ReadCounter(parameter));
                break;
            default:
                result = new nint(-1);
                break;
        }

        return true;
    }

    internal void ObserveWindowMessage(uint message, nint wParam)
    {
        switch (message)
        {
            case WmLeftButtonDown:
                if (HasRouteTag())
                {
                    Interlocked.Increment(ref _taggedLeftDown);
                }
                break;
            case WmLeftButtonUp:
                var extraInfo = unchecked((nuint)GetMessageExtraInfo().ToInt64());
                if (extraInfo == _routeTag)
                {
                    Interlocked.Increment(ref _taggedLeftUp);
                }
                if (extraInfo == unchecked((nuint)
                        Win32NativeAcceptanceProtocol.SafetyReleaseExtraInfo))
                {
                    Interlocked.Increment(ref _safetyReleaseUp);
                }
                break;
            case WmActivate:
                if ((wParam.ToInt64() & 0xFFFFL) != 0)
                {
                    Interlocked.Increment(ref _activate);
                }
                break;
            case WmActivateApp:
                if (wParam != nint.Zero)
                {
                    Interlocked.Increment(ref _activateApp);
                }
                break;
            case WmSetFocus:
                Interlocked.Increment(ref _setFocus);
                break;
            case WmMouseActivate:
                Interlocked.Increment(ref _mouseActivate);
                break;
        }
    }

    private bool HasRouteTag() =>
        unchecked((nuint)GetMessageExtraInfo().ToInt64()) == _routeTag;

    private long ReadCounter(long counter) => counter switch
    {
        Win32NativeAcceptanceProtocol.TaggedLeftDownCounter =>
            Volatile.Read(ref _taggedLeftDown),
        Win32NativeAcceptanceProtocol.TaggedLeftUpCounter =>
            Volatile.Read(ref _taggedLeftUp),
        Win32NativeAcceptanceProtocol.ActivateCounter => Volatile.Read(ref _activate),
        Win32NativeAcceptanceProtocol.ActivateAppCounter => Volatile.Read(ref _activateApp),
        Win32NativeAcceptanceProtocol.SetFocusCounter => Volatile.Read(ref _setFocus),
        Win32NativeAcceptanceProtocol.MouseActivateCounter =>
            Volatile.Read(ref _mouseActivate),
        Win32NativeAcceptanceProtocol.SafetyReleaseUpCounter =>
            Volatile.Read(ref _safetyReleaseUp),
        _ => -1
    };

    private static bool IsLiveActualParent(int expectedParentProcessId)
    {
        try
        {
            if (ReadActualParentProcessId() != expectedParentProcessId)
            {
                return false;
            }

            using var current = Process.GetCurrentProcess();
            using var parent = Process.GetProcessById(expectedParentProcessId);
            return !parent.HasExited && parent.SessionId == current.SessionId;
        }
        catch
        {
            return false;
        }
    }

    private static int ReadActualParentProcessId()
    {
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == nint.Zero || snapshot == InvalidHandleValue)
        {
            return 0;
        }

        try
        {
            var entry = new ProcessEntry32
            {
                Size = checked((uint)Marshal.SizeOf<ProcessEntry32>()),
                ExecutableFile = string.Empty
            };
            if (!Process32First(snapshot, ref entry))
            {
                return 0;
            }

            do
            {
                if (entry.ProcessId == checked((uint)Environment.ProcessId))
                {
                    return entry.ParentProcessId <= int.MaxValue
                        ? checked((int)entry.ParentProcessId)
                        : 0;
                }
            }
            while (Process32Next(snapshot, ref entry));

            return 0;
        }
        finally
        {
            _ = CloseHandle(snapshot);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        internal uint Size;
        internal uint Usage;
        internal uint ProcessId;
        internal nuint DefaultHeapId;
        internal uint ModuleId;
        internal uint Threads;
        internal uint ParentProcessId;
        internal int BasePriority;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string ExecutableFile;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern nint GetMessageExtraInfo();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
