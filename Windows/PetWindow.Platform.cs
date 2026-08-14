using System.Numerics;
using System.Windows.Media;
using DesktopLizard.Core;
using Microsoft.Win32;

namespace DesktopLizard.Windows;

internal sealed partial class PetWindow
{
    private void UpdateClickThrough()
    {
        if (NativeMethods.TryGetCursorPosition(out var cursor))
        {
            UpdateClickThrough(cursor);
        }
    }

    private void UpdateClickThrough(Vector2 cursor)
    {
        if (_handle == nint.Zero || _diagnosticMode)
        {
            return;
        }

        if (_isDragging || _contextMenuOpen)
        {
            SetClickThrough(false);
            return;
        }

        if (cursor == _lastHitTestCursor &&
            _lastWindowX == _lastHitTestWindowX &&
            _lastWindowY == _lastHitTestWindowY &&
            _view.HitGeometryVersion == _lastHitGeometryVersion)
        {
            return;
        }

        _lastHitTestCursor = cursor;
        _lastHitTestWindowX = _lastWindowX;
        _lastHitTestWindowY = _lastWindowY;
        _lastHitGeometryVersion = _view.HitGeometryVersion;
        var point = NativeMethods.ScreenPointToDip(_handle, cursor);
        SetClickThrough(!_view.IsPointOnLizard(point));
    }

    private void SetClickThrough(bool value)
    {
        if (_handle == nint.Zero || _clickThrough == value)
        {
            return;
        }

        _clickThrough = value;
        NativeMethods.SetClickThrough(_handle, value);
    }

    private nint WindowHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmMouseActivate && !_diagnosticMode)
        {
            handled = true;
            return NativeMethods.MaNoActivate;
        }

        if (message == NativeMethods.WmNcHitTest && !_isDragging && !_contextMenuOpen)
        {
            var screenX = unchecked((short)(long)lParam);
            var screenY = unchecked((short)((long)lParam >> 16));
            var point = NativeMethods.ScreenPointToDip(hwnd, new Vector2(screenX, screenY));
            if (!_view.IsPointOnLizard(point))
            {
                handled = true;
                return NativeMethods.HtTransparent;
            }
        }

        return nint.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => _displaySettingsDirty = true);

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() => _displaySettingsDirty = true);

    private void OnClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _renderFallbackTimer.Stop();
        _renderFallbackTimer.Tick -= OnRenderFallbackTick;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _debugPanelWindow.ActionRequested -= OnDebugActionRequested;
        _debugPanelWindow.Close();
    }

    private static FloatRect ToFloatRect(NativeMethods.Rect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private static FloatRect SafeInset(FloatRect rect, float inset)
    {
        var maximumInset = Math.Max(1f, Math.Min(rect.Width, rect.Height) * 0.5f - 1f);
        return rect.Inset(Math.Min(inset, maximumInset));
    }

    private static LostGripSafetyContext CreateLostGripSafety(
        FloatRect workArea,
        float requiredInset)
    {
        if (float.IsFinite(requiredInset) &&
            requiredInset >= 0f &&
            workArea.Width >= requiredInset * 2f &&
            workArea.Height >= requiredInset * 2f)
        {
            return new LostGripSafetyContext(
                workArea.Inset(requiredInset),
                IsAvailable: true);
        }

        // Keep a usable fallback for an active fall after a display/taskbar
        // shrink, but mark it unavailable so a new event can never start.
        return new LostGripSafetyContext(
            SafeInset(workArea, Math.Max(0f, requiredInset)),
            IsAvailable: false);
    }

    private void SetPetWindowCenter(Vector2 center)
    {
        var size = Math.Max(1, (int)MathF.Round(_windowSizePixels));
        var x = (int)MathF.Round(center.X - size * 0.5f);
        var y = (int)MathF.Round(center.Y - size * 0.5f);
        if (x == _lastWindowX && y == _lastWindowY)
        {
            return;
        }

        _lastWindowX = x;
        _lastWindowY = y;
        NativeMethods.SetTopmostCenter(_handle, center, size, size);
    }

    private void EnsurePhysicalWindowSize(float dpiScale)
    {
        dpiScale = Math.Max(1f, dpiScale);
        if (MathF.Abs(dpiScale - _lastLayoutDpiScale) <= 0.001f)
        {
            return;
        }

        _lastLayoutDpiScale = dpiScale;
        var windowSizeDip = _windowSizePixels / dpiScale;
        // WPF sizes are DIPs while the native positioning contract is physical
        // pixels. Keep both aligned so a DPI layout pass cannot silently grow
        // the pet HWND after SetWindowPos and invalidate sidecar avoidance.
        MinWidth = 0d;
        MinHeight = 0d;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        Width = windowSizeDip;
        Height = windowSizeDip;
        MinWidth = windowSizeDip;
        MinHeight = windowSizeDip;
        MaxWidth = windowSizeDip;
        MaxHeight = windowSizeDip;
        _lastWindowX = int.MinValue;
        _lastWindowY = int.MinValue;
    }
}
