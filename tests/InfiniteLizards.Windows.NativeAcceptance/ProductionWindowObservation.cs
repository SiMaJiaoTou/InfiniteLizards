using System.Runtime.InteropServices;

namespace InfiniteLizards.Windows.NativeAcceptance;

internal sealed class ProductionWindowObservation : IDisposable
{
    private ProductionWindowObservation(
        nint window,
        Win32Native.Rect windowRectangle,
        Win32Native.Rect clientRectangle,
        nint region,
        int regionType,
        uint dpi)
    {
        Window = window;
        WindowRectangle = windowRectangle;
        ClientRectangle = clientRectangle;
        Region = region;
        RegionType = regionType;
        Dpi = dpi;
    }

    internal nint Window { get; }
    internal Win32Native.Rect WindowRectangle { get; }
    internal Win32Native.Rect ClientRectangle { get; }
    internal nint Region { get; private set; }
    internal int RegionType { get; }
    internal uint Dpi { get; }

    internal static ProductionWindowObservation Capture(nint window)
    {
        if (!Win32Native.IsWindow(window))
        {
            throw new InvalidOperationException("The production HWND is no longer alive.");
        }
        if (!Win32Native.GetWindowRect(window, out var windowRectangle))
        {
            throw Win32Native.Failure("GetWindowRect(production)");
        }
        if (!Win32Native.GetClientRect(window, out var localClient))
        {
            throw Win32Native.Failure("GetClientRect(production)");
        }
        var clientOrigin = new Win32Native.Point(localClient.Left, localClient.Top);
        if (!Win32Native.ClientToScreen(window, ref clientOrigin))
        {
            throw Win32Native.Failure("ClientToScreen(production)");
        }
        var clientRectangle = new Win32Native.Rect
        {
            Left = clientOrigin.X,
            Top = clientOrigin.Y,
            Right = checked(clientOrigin.X + localClient.Width),
            Bottom = checked(clientOrigin.Y + localClient.Height)
        };

        var region = Win32Native.CreateRectRgn(0, 0, 0, 0);
        if (region == nint.Zero)
        {
            throw Win32Native.Failure("CreateRectRgn(production copy)");
        }
        try
        {
            var regionType = Win32Native.GetWindowRgn(window, region);
            if (regionType is not (2 or 3))
            {
                throw new InvalidOperationException(
                    $"The production HWND has no non-empty shaped region (type={regionType}).");
            }
            var dpi = Win32Native.GetDpiForWindow(window);
            if (dpi == 0)
            {
                throw new InvalidOperationException("GetDpiForWindow returned zero.");
            }

            var result = new ProductionWindowObservation(
                window,
                windowRectangle,
                clientRectangle,
                region,
                regionType,
                dpi);
            region = nint.Zero;
            return result;
        }
        finally
        {
            if (region != nint.Zero)
            {
                _ = Win32Native.DeleteObject(region);
            }
        }
    }

    internal Win32Native.Point FindInteriorScreenPoint()
    {
        var localClient = LocalClientRectangle();
        var bestWidth = 0;
        var best = default(Win32Native.Point);
        for (var y = localClient.Top; y < localClient.Bottom; y++)
        {
            var runStart = -1;
            for (var x = localClient.Left; x <= localClient.Right; x++)
            {
                var inside = x < localClient.Right &&
                    Win32Native.PtInRegion(Region, x, y);
                if (inside && runStart < 0)
                {
                    runStart = x;
                }
                else if (!inside && runStart >= 0)
                {
                    var width = x - runStart;
                    if (width > bestWidth)
                    {
                        bestWidth = width;
                        best = new Win32Native.Point(
                            checked(WindowRectangle.Left + runStart + width / 2),
                            checked(WindowRectangle.Top + y));
                    }
                    runStart = -1;
                }
            }
        }

        if (bestWidth < 3)
        {
            throw new InvalidOperationException(
                "The production region has no stable three-pixel interior run.");
        }
        return best;
    }

    internal Win32Native.Point FindExteriorScreenPoint()
    {
        var client = LocalClientRectangle();
        var candidates = new[]
        {
            new Win32Native.Point(client.Left + 4, client.Top + 4),
            new Win32Native.Point(client.Right - 5, client.Top + 4),
            new Win32Native.Point(client.Left + 4, client.Bottom - 5),
            new Win32Native.Point(client.Right - 5, client.Bottom - 5)
        };
        foreach (var candidate in candidates)
        {
            if (!Win32Native.PtInRegion(Region, candidate.X, candidate.Y))
            {
                return new Win32Native.Point(
                    checked(WindowRectangle.Left + candidate.X),
                    checked(WindowRectangle.Top + candidate.Y));
            }
        }

        for (var y = client.Top; y < client.Bottom; y += 2)
        {
            for (var x = client.Left; x < client.Right; x += 2)
            {
                if (!Win32Native.PtInRegion(Region, x, y))
                {
                    return new Win32Native.Point(
                        checked(WindowRectangle.Left + x),
                        checked(WindowRectangle.Top + y));
                }
            }
        }

        throw new InvalidOperationException(
            "The production shaped region unexpectedly covers its full client rectangle.");
    }

    internal bool ContainsScreenPoint(Win32Native.Point point) =>
        Win32Native.PtInRegion(
            Region,
            checked(point.X - WindowRectangle.Left),
            checked(point.Y - WindowRectangle.Top));

    public void Dispose()
    {
        var region = Region;
        Region = nint.Zero;
        if (region != nint.Zero && !Win32Native.DeleteObject(region))
        {
            throw Win32Native.Failure("DeleteObject(production region copy)");
        }
    }

    private Win32Native.Rect LocalClientRectangle() => new()
    {
        Left = checked(ClientRectangle.Left - WindowRectangle.Left),
        Top = checked(ClientRectangle.Top - WindowRectangle.Top),
        Right = checked(ClientRectangle.Right - WindowRectangle.Left),
        Bottom = checked(ClientRectangle.Bottom - WindowRectangle.Top)
    };
}

internal sealed record ProductionCompositeObservation(
    int Width,
    int Height,
    uint BackgroundRgb,
    int RegionPixels,
    int ChangedRegionPixels,
    int ExteriorPixels,
    int ExpectedBackgroundExteriorPixels,
    int ContaminatedExteriorPixels,
    int MarkerBodyPixels,
    int DefaultEyeWhitePixels,
    int MarkerPupilPixels)
{
    internal double ExteriorContamination => ExteriorPixels == 0
        ? 1d
        : ContaminatedExteriorPixels / (double)ExteriorPixels;

    internal double ExpectedBackgroundCoverage => ExteriorPixels == 0
        ? 0d
        : ExpectedBackgroundExteriorPixels / (double)ExteriorPixels;

    internal bool IsLaunchMarkedLizardFrame(out string reason)
    {
        var requiredChanged = Math.Max(512, (int)Math.Ceiling(RegionPixels * 0.05d));
        var requiredBody = Math.Max(192, (int)Math.Ceiling(RegionPixels * 0.01d));
        var palettePixels = MarkerBodyPixels + DefaultEyeWhitePixels + MarkerPupilPixels;
        var requiredPalette = Math.Max(320, (int)Math.Ceiling(ChangedRegionPixels * 0.25d));
        var accepted =
            RegionPixels >= 512 &&
            ExteriorPixels >= 512 &&
            ExpectedBackgroundCoverage >= 0.985d &&
            ChangedRegionPixels >= requiredChanged &&
            MarkerBodyPixels >= requiredBody &&
            DefaultEyeWhitePixels >= 32 &&
            MarkerPupilPixels >= 48 &&
            palettePixels >= requiredPalette;
        reason =
            $"probeBackground={ExpectedBackgroundExteriorPixels}/{ExteriorPixels}, " +
            $"changed={ChangedRegionPixels}/{RegionPixels} (required {requiredChanged}), " +
            $"markerBody={MarkerBodyPixels} (required {requiredBody}), " +
            $"eyeWhite={DefaultEyeWhitePixels}, markerPupil={MarkerPupilPixels}, " +
            $"knownPalette={palettePixels} (required {requiredPalette})";
        return accepted;
    }
}

internal static class ProductionCompositeCapture
{
    internal static ProductionCompositeObservation CaptureAndAnalyze(
        ProductionWindowObservation window,
        ProductionAcceptanceMarker marker)
    {
        var rectangle = window.WindowRectangle;
        var pixels = CaptureScreen(rectangle);
        return AnalyzePixels(
            rectangle.Width,
            rectangle.Height,
            pixels,
            (x, y) => Win32Native.PtInRegion(window.Region, x, y),
            marker);
    }

    internal static ProductionCompositeObservation AnalyzePixels(
        int width,
        int height,
        IReadOnlyList<int> pixels,
        Func<int, int, bool> isRegionPixel,
        ProductionAcceptanceMarker marker)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(isRegionPixel);
        if (width <= 0 || height <= 0 || pixels.Count != checked(width * height))
        {
            throw new ArgumentException("Composite pixels do not match their declared dimensions.");
        }

        var expectedBackground = ColorRefToDibRgb(NativeWindowChild.ProductionProbeColor);
        var regionPixels = 0;
        var exteriorPixels = 0;
        var expectedBackgroundExterior = 0;
        var changedRegion = 0;
        var bodyPixels = 0;
        var eyeWhitePixels = 0;
        var pupilPixels = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var rgb = unchecked((uint)pixels[y * width + x]) & 0x00FFFFFFu;
                var inRegion = isRegionPixel(x, y);
                if (inRegion)
                {
                    regionPixels++;
                    if (ColorDistance(rgb, expectedBackground) > 12)
                    {
                        changedRegion++;
                    }
                    if (ColorDistance(rgb, marker.BodyDibRgb) <= 12)
                    {
                        bodyPixels++;
                    }
                    if (IsEyeWhite(rgb))
                    {
                        eyeWhitePixels++;
                    }
                    if (ColorDistance(rgb, marker.PupilDibRgb) <= 12)
                    {
                        pupilPixels++;
                    }
                    continue;
                }

                exteriorPixels++;
                if (ColorDistance(rgb, expectedBackground) <= 12)
                {
                    expectedBackgroundExterior++;
                }
            }
        }
        if (regionPixels < 64 || exteriorPixels < 64)
        {
            throw new InvalidOperationException(
                "The production region does not leave measurable interior and exterior pixels.");
        }

        return new ProductionCompositeObservation(
            width,
            height,
            expectedBackground,
            regionPixels,
            changedRegion,
            exteriorPixels,
            expectedBackgroundExterior,
            exteriorPixels - expectedBackgroundExterior,
            bodyPixels,
            eyeWhitePixels,
            pupilPixels);
    }

    internal static uint ColorRefToDibRgb(uint colorRef) =>
        ((colorRef & 0x000000FFu) << 16) |
        (colorRef & 0x0000FF00u) |
        ((colorRef & 0x00FF0000u) >> 16);

    private static bool IsEyeWhite(uint rgb) =>
        Red(rgb) >= 238 && Green(rgb) >= 238 && Blue(rgb) >= 238;

    private static int Red(uint rgb) => (int)((rgb >> 16) & 0xFF);
    private static int Green(uint rgb) => (int)((rgb >> 8) & 0xFF);
    private static int Blue(uint rgb) => (int)(rgb & 0xFF);

    private static int ColorDistance(uint left, uint right)
    {
        var leftBlue = (int)(left & 0xFF);
        var leftGreen = (int)((left >> 8) & 0xFF);
        var leftRed = (int)((left >> 16) & 0xFF);
        var rightBlue = (int)(right & 0xFF);
        var rightGreen = (int)((right >> 8) & 0xFF);
        var rightRed = (int)((right >> 16) & 0xFF);
        return Math.Abs(leftRed - rightRed) +
            Math.Abs(leftGreen - rightGreen) +
            Math.Abs(leftBlue - rightBlue);
    }

    private static int[] CaptureScreen(Win32Native.Rect rectangle)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rectangle));
        }

        var screen = Win32Native.GetDC(nint.Zero);
        if (screen == nint.Zero)
        {
            throw Win32Native.Failure("GetDC(screen)");
        }
        nint memory = nint.Zero;
        nint bitmap = nint.Zero;
        nint previous = nint.Zero;
        try
        {
            memory = Win32Native.CreateCompatibleDC(screen);
            if (memory == nint.Zero)
            {
                throw Win32Native.Failure("CreateCompatibleDC(screen)");
            }
            var bitmapInfo = new Win32Native.BitmapInfo
            {
                Header = new Win32Native.BitmapInfoHeader
                {
                    Size = checked((uint)Marshal.SizeOf<Win32Native.BitmapInfoHeader>()),
                    Width = rectangle.Width,
                    Height = checked(-rectangle.Height),
                    Planes = 1,
                    BitCount = 32,
                    Compression = Win32Native.BiRgb
                }
            };
            bitmap = Win32Native.CreateDIBSection(
                screen,
                ref bitmapInfo,
                Win32Native.DibRgbColors,
                out var bits,
                nint.Zero,
                0);
            if (bitmap == nint.Zero || bits == nint.Zero)
            {
                throw Win32Native.Failure("CreateDIBSection(screen capture)");
            }
            previous = Win32Native.SelectObject(memory, bitmap);
            if (previous == nint.Zero || previous == new nint(-1))
            {
                throw Win32Native.Failure("SelectObject(screen capture)");
            }
            if (!Win32Native.BitBlt(
                    memory,
                    0,
                    0,
                    rectangle.Width,
                    rectangle.Height,
                    screen,
                    rectangle.Left,
                    rectangle.Top,
                    Win32Native.SrcCopy | Win32Native.CaptureBlt))
            {
                throw Win32Native.Failure("BitBlt(DWM composite)");
            }

            var pixels = new int[checked(rectangle.Width * rectangle.Height)];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            if (memory != nint.Zero && previous != nint.Zero && previous != new nint(-1))
            {
                _ = Win32Native.SelectObject(memory, previous);
            }
            if (bitmap != nint.Zero)
            {
                _ = Win32Native.DeleteObject(bitmap);
            }
            if (memory != nint.Zero)
            {
                _ = Win32Native.DeleteDC(memory);
            }
            _ = Win32Native.ReleaseDC(nint.Zero, screen);
        }
    }
}
