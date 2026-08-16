using System.Globalization;
using System.Buffers.Binary;
using System.Numerics;

namespace InfiniteLizards.Windows.NativeAcceptance;

/// <summary>
/// Nonce-scoped, read-mostly protocol exposed by the real Win32 overlay only
/// when its controller is the live parent process in the same Windows session.
/// Registered messages avoid colliding with Avalonia or third-party WM_APP use.
/// </summary>
internal sealed class ProductionAcceptanceProtocol
{
    internal const string NonceEnvironment =
        "INFINITE_LIZARDS_WINDOWS_NATIVE_ACCEPTANCE_NONCE";
    internal const string ParentProcessEnvironment =
        "INFINITE_LIZARDS_WINDOWS_NATIVE_ACCEPTANCE_PARENT_PID";
    internal const string RouteTagEnvironment =
        "INFINITE_LIZARDS_WINDOWS_NATIVE_ACCEPTANCE_ROUTE_TAG";

    internal const int CommandPing = 1;
    internal const int CommandResetRoute = 2;
    internal const int CommandReadCounter = 3;

    internal const int CounterTaggedLeftDown = 1;
    internal const int CounterTaggedLeftUp = 2;
    internal const int CounterActivate = 3;
    internal const int CounterActivateApp = 4;
    internal const int CounterSetFocus = 5;
    internal const int CounterMouseActivate = 6;
    internal const int CounterSafetyReleaseUp = 7;

    internal const long PingMagic = 0x494C5A4450524F44;
    internal const string StableMessageName =
        "InfiniteLizards.Windows.NativeAcceptance.v1.Control";

    internal ProductionAcceptanceProtocol(Guid nonce, nuint routeTag)
    {
        if (nonce == Guid.Empty)
        {
            throw new ArgumentException("The production acceptance nonce cannot be empty.", nameof(nonce));
        }
        if (routeTag == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routeTag));
        }

        Nonce = nonce;
        Marker = ProductionAcceptanceMarker.Derive(nonce);
        RouteTag = routeTag;
        NonceText = nonce.ToString("N", CultureInfo.InvariantCulture).ToLowerInvariant();
        RouteTagText = ((ulong)routeTag).ToString("x16", CultureInfo.InvariantCulture);
        ControlToken = DeriveControlToken(nonce);
        MessageName = StableMessageName;
        Message = Win32Native.RegisterWindowMessage(MessageName);
        if (Message == 0)
        {
            throw Win32Native.Failure("RegisterWindowMessageW(production acceptance)");
        }
    }

    internal Guid Nonce { get; }
    internal ProductionAcceptanceMarker Marker { get; }
    internal nuint RouteTag { get; }
    internal string NonceText { get; }
    internal string RouteTagText { get; }
    internal ulong ControlToken { get; }
    internal string MessageName { get; }
    internal uint Message { get; }

    internal bool IsReadyWindow(nint window) =>
        Send(window, CommandPing).ToInt64() == PingMagic;

    internal void ResetRoute(nint window)
    {
        if (Send(window, CommandResetRoute) != new nint(1))
        {
            throw new InvalidOperationException(
                "The production overlay rejected its route-counter reset.");
        }
    }

    internal int ReadCounter(
        nint window,
        int counter,
        uint timeoutMilliseconds = 5_000)
    {
        var value = Send(
            window,
            CommandReadCounter,
            new nint(counter),
            timeoutMilliseconds).ToInt64();
        if (value < 0 || value > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"The production overlay returned invalid counter {counter}: {value}.");
        }

        return checked((int)value);
    }

    internal static ulong DeriveControlToken(Guid nonce)
    {
        if (nonce == Guid.Empty)
        {
            throw new ArgumentException("The production acceptance nonce cannot be empty.", nameof(nonce));
        }

        Span<byte> bytes = stackalloc byte[16];
        _ = nonce.TryWriteBytes(bytes);
        var first = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        var second = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
        var token = first ^ BitOperations.RotateLeft(second, 17) ^
            0x494C5A444354524CUL; // "ILZDCTRL"
        return token == 0 ? 0x494C5A444354524CUL : token;
    }

    internal static long PackPayload(int command, int parameter = 0)
    {
        if (command <= 0 || parameter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }
        return unchecked((long)(((ulong)(uint)parameter << 32) | (uint)command));
    }

    private nint Send(
        nint window,
        int command,
        nint parameter = default,
        uint timeoutMilliseconds = 5_000) =>
        Win32Native.SendAcceptanceMessage(
            window,
            Message,
            new nint(unchecked((long)ControlToken)),
            new nint(PackPayload(command, checked((int)parameter.ToInt64()))),
            timeoutMilliseconds);
}
