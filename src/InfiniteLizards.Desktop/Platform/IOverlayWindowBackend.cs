using Avalonia;
using Avalonia.Controls;
using DesktopPet.Engine;

namespace InfiniteLizards.Desktop.Platform;

/// <summary>
/// Thin native-window boundary used by the shared Avalonia host. Implementations
/// may know HWND or NSWindow; the engine and gameplay assemblies never do.
/// </summary>
internal interface IOverlayWindowBackend : IDisposable
{
    void Attach(Window window, bool interactiveDiagnosticMode);

    bool TryGetGlobalPointer(out DevicePoint point);

    bool IsAnyMouseButtonPressed { get; }

    void SetClickThrough(bool clickThrough);

    void PlaceSurface(Window window, SurfacePlacement placement, double displayScale);

    void EnsureVisible();
}

/// <summary>
/// Optional native capability for a true shaped input surface. Backends that
/// do not expose it retain the normal presenter hit-testing path.
/// </summary>
internal interface IOverlayInputRegionBackend
{
    void SetInputRegion(
        Window window,
        DesktopPetInputRegionInstallation installation,
        double displayScale);

    void ClearInputRegion(Window window);
}

/// <summary>
/// The native backend could not establish or restore its fail-closed input
/// invariant. A production overlay must not be shown after this exception.
/// </summary>
internal sealed class NativeOverlaySafetyException : Exception
{
    public NativeOverlaySafetyException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal static class OverlayWindowBackendFactory
{
    public static IOverlayWindowBackend Create() => OperatingSystem.IsWindows()
        ? new Win32OverlayWindowBackend()
        : OperatingSystem.IsMacOS()
            ? new MacOsOverlayWindowBackend()
            : new AvaloniaOverlayWindowBackend();
}

internal static class OverlayWindowPlacement
{
    public static void ApplyManaged(
        Window window,
        SurfacePlacement placement,
        double displayScale)
    {
        ArgumentNullException.ThrowIfNull(window);
        var scale = double.IsFinite(displayScale) && displayScale > 0d
            ? displayScale
            : 1d;
        window.Width = placement.Width / scale;
        window.Height = placement.Height / scale;
        window.Position = new PixelPoint(placement.X, placement.Y);
    }
}
