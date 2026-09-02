using Avalonia.Media;

namespace InfiniteLizards.Desktop.Management;

/// <summary>
/// A warm, toy-like (but not childish) palette for the breeding game UI.
/// Keeping the tokens here prevents individual pages from drifting back into
/// unrelated dark-panel styling as new breeding features are added.
/// </summary>
internal static class CuteGameUiTheme
{
    internal static readonly IBrush Canvas = Brush("#FFF8F1");
    internal static readonly IBrush Surface = Brush("#FFFFFC");
    internal static readonly IBrush SurfaceRaised = Brush("#FFFFFFFF");
    internal static readonly IBrush SurfaceMuted = Brush("#F8F3EF");

    internal static readonly IBrush Ink = Brush("#403B4A");
    internal static readonly IBrush InkSoft = Brush("#665F70");
    // Text accents deliberately stay darker than their decorative partners so
    // 10–12 px labels remain readable on cream and pastel surfaces.
    internal static readonly IBrush Muted = Brush("#706978");
    internal static readonly IBrush Line = Brush("#E9DED7");
    internal static readonly IBrush LineStrong = Brush("#DCCFC6");

    internal static readonly IBrush MintSoft = Brush("#DDF4E8");
    internal static readonly IBrush Mint = Brush("#A8DFC5");
    internal static readonly IBrush MintStrong = Brush("#489B78");
    internal static readonly IBrush MintDeep = Brush("#34765C");

    internal static readonly IBrush PeachSoft = Brush("#FFE6D8");
    internal static readonly IBrush Peach = Brush("#FFC6AD");
    internal static readonly IBrush PeachStrong = Brush("#A84437");

    internal static readonly IBrush LavenderSoft = Brush("#F0EBFC");
    internal static readonly IBrush Lavender = Brush("#D9CDF4");
    internal static readonly IBrush LavenderStrong = Brush("#65508F");

    internal static readonly IBrush HoneySoft = Brush("#FFF2C8");
    internal static readonly IBrush Honey = Brush("#F3C85F");
    internal static readonly IBrush HoneyDeep = Brush("#755713");

    internal static readonly IBrush SkySoft = Brush("#E2F1FB");
    internal static readonly IBrush DangerSoft = Brush("#FFE5E7");
    internal static readonly IBrush Danger = Brush("#BF5454");
    internal static readonly IBrush DangerDeep = Brush("#A83F4A");
    internal static readonly IBrush Disabled = Brush("#EEE9E5");

    internal static readonly FontFamily Font = new(
        "PingFang SC, Microsoft YaHei UI, Noto Sans CJK SC, sans-serif");

    private static SolidColorBrush Brush(string color) =>
        new(Color.Parse(color));
}
