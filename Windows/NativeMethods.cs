using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DesktopLizard.Windows;

internal static class NativeMethods
{
    public const int GwlExStyle = -20;
    public const long WsExTransparent = 0x00000020L;
    public const long WsExToolWindow = 0x00000080L;
    public const long WsExNoActivate = 0x08000000L;

    public const int WmNcHitTest = 0x0084;
    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;
    public const int HtTransparent = -1;

    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const int VkLeftButton = 0x01;
    private const int VkRightButton = 0x02;
    private const int VkMiddleButton = 0x04;
    private const int VkXButton1 = 0x05;
    private const int VkXButton2 = 0x06;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    internal static extern bool ScreenToClient(nint window, ref Point point);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr64(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint window, int index, nint newValue);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint window);

    public static Vector2 CursorPosition
    {
        get
        {
            if (!GetCursorPos(out var point))
            {
                return Vector2.Zero;
            }

            return new Vector2(point.X, point.Y);
        }
    }

    public static bool TryGetCursorPosition(out Vector2 position)
    {
        if (!GetCursorPos(out var point))
        {
            position = default;
            return false;
        }

        position = new Vector2(point.X, point.Y);
        return true;
    }

    public static bool IsAnyMouseButtonPressed =>
        IsKeyDown(VkLeftButton) ||
        IsKeyDown(VkRightButton) ||
        IsKeyDown(VkMiddleButton) ||
        IsKeyDown(VkXButton1) ||
        IsKeyDown(VkXButton2);

    public static Rect GetWorkingArea(Vector2 screenPoint)
    {
        var point = new Point((int)MathF.Round(screenPoint.X), (int)MathF.Round(screenPoint.Y));
        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return info.Work;
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public static void ApplyPetWindowStyles(nint window, bool clickThrough)
    {
        var style = GetWindowLongPtr64(window, GwlExStyle).ToInt64();
        style |= WsExToolWindow | WsExNoActivate;
        style = clickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr64(window, GwlExStyle, new nint(style));
        SetWindowPos(window, nint.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    public static void SetClickThrough(nint window, bool clickThrough)
    {
        var style = GetWindowLongPtr64(window, GwlExStyle).ToInt64();
        var hasStyle = (style & WsExTransparent) != 0;
        if (hasStyle == clickThrough)
        {
            return;
        }

        style = clickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr64(window, GwlExStyle, new nint(style));
        SetWindowPos(window, nint.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    public static void SetTopmostCenter(nint window, Vector2 center, int width = 0, int height = 0)
    {
        if (!GetWindowRect(window, out var rect))
        {
            return;
        }

        var targetWidth = width > 0 ? width : rect.Width;
        var targetHeight = height > 0 ? height : rect.Height;
        var x = (int)MathF.Round(center.X - targetWidth * 0.5f);
        var y = (int)MathF.Round(center.Y - targetHeight * 0.5f);
        var sameSize = rect.Width == targetWidth && rect.Height == targetHeight;
        if (rect.Left == x && rect.Top == y && sameSize)
        {
            return;
        }

        // The WPF window is already Topmost. Avoid forcing a Z-order/show
        // transaction on every animation frame and move only when a pixel
        // coordinate actually changes.
        var flags = SwpNoZOrder | SwpNoActivate;
        if (sameSize)
        {
            flags |= SwpNoSize;
        }
        SetWindowPos(window, nint.Zero, x, y, targetWidth, targetHeight, flags);
    }

    public static System.Windows.Point ScreenPointToDip(nint window, Vector2 screenPoint)
    {
        var point = new Point((int)MathF.Round(screenPoint.X), (int)MathF.Round(screenPoint.Y));
        if (!ScreenToClient(window, ref point))
        {
            return new System.Windows.Point(double.NegativeInfinity, double.NegativeInfinity);
        }

        var dpi = Math.Max(96u, GetDpiForWindow(window));
        var scale = dpi / 96d;
        return new System.Windows.Point(point.X / scale, point.Y / scale);
    }
}
