using Avalonia;
using Avalonia.Controls;
using DesktopPet.Engine;

namespace InfiniteLizards.Desktop.Platform;

/// <summary>
/// Safe fallback for unsupported desktop platforms. It deliberately keeps the
/// window interactive because true click-through requires a native contract.
/// </summary>
internal sealed class AvaloniaOverlayWindowBackend : IOverlayWindowBackend
{
    public bool IsAnyMouseButtonPressed => false;

    public void Attach(Window window, bool interactiveDiagnosticMode)
    {
    }

    public bool TryGetGlobalPointer(out DevicePoint point)
    {
        point = default;
        return false;
    }

    public void SetClickThrough(bool clickThrough)
    {
    }

    public void PlaceSurface(
        Window window,
        SurfacePlacement placement,
        double displayScale) =>
        OverlayWindowPlacement.ApplyManaged(window, placement, displayScale);

    public void EnsureVisible()
    {
    }

    public void Dispose()
    {
    }
}
