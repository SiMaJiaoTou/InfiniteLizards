using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace InfiniteLizards.Desktop.Management;

/// <summary>
/// Stable semantic order of the generated 4x4 icon atlas. Never use numeric
/// indexes at call sites: the enum is the contract with the generated asset.
/// </summary>
internal enum BreedingIcon
{
    Collection,
    Breeding,
    Egg,
    Hatching,
    Buy,
    Sell,
    Genetics,
    Details,
    Color,
    Temperament,
    Limbs,
    Fins,
    Whiskers,
    Tail,
    Speed,
    Home,

    // Utility atlas. Keep these values after the original sixteen entries so
    // existing call sites and the generated primary-atlas ordering stay
    // binary compatible.
    Coin,
    MarketStall,
    Desktop,
    Close,
    Pause,
    Play,
    Center,
    Settings,
    Debug,
    Exit,
    Add,
    Remove,
    Check,
    Warning,
    Autosave,
    Refresh
}

internal enum BreedingAtlasKind
{
    Primary,
    Utility
}

internal readonly record struct BreedingAtlasCell(
    BreedingAtlasKind Atlas,
    int TileIndex,
    string AssetUri);

internal sealed class BreedingAtlasIcon : Control
{
    private const int Columns = 4;
    private const int Rows = 4;
    private const int TilesPerAtlas = Columns * Rows;
    private const string PrimaryAtlasUri =
        "avares://InfiniteLizards.Desktop/Assets/UI/breeding-icons-atlas.png";
    private const string UtilityAtlasUri =
        "avares://InfiniteLizards.Desktop/Assets/UI/cute-utility-icons-atlas.png";
    private static readonly Lazy<Bitmap> PrimaryAtlas = new(
        () => LoadAtlas(PrimaryAtlasUri));
    private static readonly Lazy<Bitmap> UtilityAtlas = new(
        () => LoadAtlas(UtilityAtlasUri));

    public static readonly StyledProperty<BreedingIcon> IconProperty =
        AvaloniaProperty.Register<BreedingAtlasIcon, BreedingIcon>(nameof(Icon));

    public BreedingIcon Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    static BreedingAtlasIcon()
    {
        AffectsRender<BreedingAtlasIcon>(IconProperty);
    }

    public BreedingAtlasIcon()
    {
        Width = 28d;
        Height = 28d;
        IsHitTestVisible = false;
        Focusable = false;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var cell = ResolveCell(Icon);
        var atlas = cell.Atlas == BreedingAtlasKind.Primary
            ? PrimaryAtlas.Value
            : UtilityAtlas.Value;
        var source = CalculateSourceRect(atlas.PixelSize, cell.TileIndex);
        var extent = Math.Min(Bounds.Width, Bounds.Height);
        if (!double.IsFinite(extent) || extent <= 0d)
        {
            return;
        }

        var carrier = new Rect(
            (Bounds.Width - extent) * 0.5d,
            (Bounds.Height - extent) * 0.5d,
            extent,
            extent);
        DrawSoftCarrier(context, carrier, Icon);

        // The generated art already has a transparent breathing margin. A
        // very small inset reveals the soft carrier without sacrificing icon
        // readability in compact navigation and context-menu buttons.
        var destination = carrier.Deflate(extent * 0.045d);
        context.DrawImage(atlas, source, destination);
    }

    internal static BreedingAtlasCell ResolveCell(BreedingIcon icon)
    {
        var normalized = Math.Clamp(
            (int)icon,
            (int)BreedingIcon.Collection,
            (int)BreedingIcon.Refresh);
        if (normalized < TilesPerAtlas)
        {
            return new BreedingAtlasCell(
                BreedingAtlasKind.Primary,
                normalized,
                PrimaryAtlasUri);
        }

        return new BreedingAtlasCell(
            BreedingAtlasKind.Utility,
            normalized - TilesPerAtlas,
            UtilityAtlasUri);
    }

    internal static Rect CalculateSourceRect(PixelSize pixelSize, int tileIndex)
    {
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelSize),
                "Atlas pixel dimensions must be positive.");
        }

        tileIndex = Math.Clamp(tileIndex, 0, TilesPerAtlas - 1);
        var column = tileIndex % Columns;
        var row = tileIndex / Columns;
        // The generated atlases are 1254px square, which is not evenly
        // divisible by four. Snapping every boundary to a real pixel avoids
        // sampling a neighbour while still covering the complete bitmap.
        var sourceLeft = Math.Round(column * pixelSize.Width / (double)Columns);
        var sourceTop = Math.Round(row * pixelSize.Height / (double)Rows);
        var sourceRight = Math.Round((column + 1) * pixelSize.Width / (double)Columns);
        var sourceBottom = Math.Round((row + 1) * pixelSize.Height / (double)Rows);
        return new Rect(
            sourceLeft,
            sourceTop,
            sourceRight - sourceLeft,
            sourceBottom - sourceTop);
    }

    private static void DrawSoftCarrier(
        DrawingContext context,
        Rect bounds,
        BreedingIcon icon)
    {
        var radius = bounds.Width * 0.32d;
        var lineWidth = Math.Max(0.75d, bounds.Width * 0.025d);
        var (fill, line) = CarrierPalette(icon);
        var shadow = new Rect(
            bounds.X,
            bounds.Y + bounds.Height * 0.035d,
            bounds.Width,
            bounds.Height * 0.965d);
        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(30, 96, 66, 52)),
            null,
            new RoundedRect(shadow, radius));
        context.DrawRectangle(
            new SolidColorBrush(fill),
            new Pen(new SolidColorBrush(line), lineWidth),
            new RoundedRect(bounds.Deflate(lineWidth * 0.5d), radius));
        context.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(92, 255, 255, 255)),
            null,
            new Point(
                bounds.X + bounds.Width * 0.31d,
                bounds.Y + bounds.Height * 0.24d),
            bounds.Width * 0.18d,
            bounds.Height * 0.095d);
    }

    private static (Color Fill, Color Line) CarrierPalette(BreedingIcon icon) => icon switch
    {
        BreedingIcon.Breeding or BreedingIcon.Buy or BreedingIcon.Sell or
        BreedingIcon.Close or BreedingIcon.Remove or BreedingIcon.Exit or
        BreedingIcon.Warning =>
            (Color.FromRgb(255, 237, 226), Color.FromRgb(242, 188, 163)),
        BreedingIcon.Egg or BreedingIcon.Hatching or BreedingIcon.Genetics or
        BreedingIcon.Settings or BreedingIcon.Debug or BreedingIcon.Autosave =>
            (Color.FromRgb(242, 238, 252), Color.FromRgb(211, 199, 240)),
        BreedingIcon.Collection or BreedingIcon.Details or BreedingIcon.Desktop or
        BreedingIcon.Center or BreedingIcon.Play or BreedingIcon.Pause =>
            (Color.FromRgb(231, 245, 252), Color.FromRgb(185, 217, 234)),
        _ =>
            (Color.FromRgb(232, 247, 239), Color.FromRgb(166, 218, 193))
    };

    private static Bitmap LoadAtlas(string assetUri)
    {
        using var stream = AssetLoader.Open(new Uri(assetUri));
        return new Bitmap(stream);
    }
}
