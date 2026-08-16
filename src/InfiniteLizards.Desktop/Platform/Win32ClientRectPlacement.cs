using DesktopPet.Engine;

namespace InfiniteLizards.Desktop.Platform;

internal readonly record struct Win32PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => checked(Right - Left);
    public int Height => checked(Bottom - Top);
}

internal readonly record struct Win32WindowInsets(int Left, int Top, int Right, int Bottom);

internal readonly record struct Win32OuterWindowPlacement(int X, int Y, int Width, int Height);

internal static class Win32ClientRectPlacement
{
    public static Win32WindowInsets MeasureInsets(
        Win32PixelRect outerWindow,
        Win32PixelRect clientScreen)
    {
        if (outerWindow.Width < 0 || outerWindow.Height < 0 ||
            clientScreen.Width < 0 || clientScreen.Height < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outerWindow),
                "Window rectangles must have non-negative dimensions.");
        }

        var insets = new Win32WindowInsets(
            checked(clientScreen.Left - outerWindow.Left),
            checked(clientScreen.Top - outerWindow.Top),
            checked(outerWindow.Right - clientScreen.Right),
            checked(outerWindow.Bottom - clientScreen.Bottom));
        ValidateInsets(insets);
        return insets;
    }

    public static Win32OuterWindowPlacement ToOuterWindow(
        SurfacePlacement client,
        Win32WindowInsets insets)
    {
        if (client.Width <= 0 || client.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(client),
                "The desired client rectangle must have positive dimensions.");
        }
        ValidateInsets(insets);

        return new Win32OuterWindowPlacement(
            checked(client.X - insets.Left),
            checked(client.Y - insets.Top),
            checked(client.Width + insets.Left + insets.Right),
            checked(client.Height + insets.Top + insets.Bottom));
    }

    private static void ValidateInsets(Win32WindowInsets insets)
    {
        if (insets.Left < 0 || insets.Top < 0 || insets.Right < 0 || insets.Bottom < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(insets),
                "The client rectangle must be enclosed by the outer window rectangle.");
        }
    }
}

internal static class Win32OverlayStyle
{
    internal const uint Disabled = 0x08000000U;
    internal const uint Transparent = 0x00000020U;
    internal const uint ToolWindow = 0x00000080U;
    internal const uint NoActivate = 0x08000000U;

    public static uint ComposeWindowStyle(uint existing, bool inputEnabled) =>
        inputEnabled
            ? existing & ~Disabled
            : existing | Disabled;

    public static uint ComposeExtendedStyle(uint existing, bool clickThrough)
    {
        var overlay = existing | ToolWindow | NoActivate;
        return clickThrough
            ? overlay | Transparent
            : overlay & ~Transparent;
    }
}
