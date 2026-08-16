using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using InfiniteLizards.Desktop.Rendering;
using SkiaSharp;

/// <summary>
/// Raster-level complement to the gameplay DPI test. The gameplay test proves
/// that each display scale produces the same immutable pose; this test sends
/// that pose through the production <see cref="LizardView.Render"/> path and
/// checks the pixels emitted by Avalonia's shared Skia backend.
/// </summary>
internal static class RasterDpiInvarianceSelfTest
{
    private const double BaseDpi = 96d;
    private const int LogicalCellSize = 4;
    private const double ActiveAlphaThreshold = 0.01d;

    private static readonly double[] Scales = [1d, 1.25d, 1.5d, 2d];

    public static void Run()
    {
        EnsureSkiaInitialized();
        var poses = CreateProductionPoses(out var logicalSize);
        AssertTrue(logicalSize % LogicalCellSize == 0,
            $"the logical canvas {logicalSize} must divide into {LogicalCellSize}-DIP cells");

        foreach (var pose in poses)
        {
            var baseline = Render(pose.View, logicalSize, Scales[0]);
            var repeated = Render(pose.View, logicalSize, Scales[0]);
            AssertEqual(baseline.DeviceHash, repeated.DeviceHash,
                $"{pose.Name} must rasterize byte-for-byte deterministically");

            foreach (var scale in Scales.Skip(1))
            {
                var actual = Render(pose.View, logicalSize, scale);
                var comparison = Compare(baseline.Normalized, actual.Normalized);
                AssertTrue(comparison.Accepted,
                    $"{pose.Name} at {scale:0.##}x differs after physical-pixel " +
                    $"normalization: {comparison}");
            }
        }

        // Prove that the tolerances detect a meaningful logical rendering
        // regression. One normalized cell is exactly four DIPs on every scale.
        var negativeControlBaseline = Render(poses[0].View, logicalSize, Scales[0]);
        var shifted = negativeControlBaseline.Normalized.ShiftCells(1, 0);
        var negativeControl = Compare(negativeControlBaseline.Normalized, shifted);
        AssertTrue(!negativeControl.Accepted,
            $"the comparator accepted a {LogicalCellSize}-DIP translation: {negativeControl}");
    }

    private static void EnsureSkiaInitialized()
    {
        // Registration is intentionally idempotent. Another geometry test may
        // initialize Skia first, while this test must also remain standalone.
        SkiaPlatform.Initialize();
    }

    private static RasterPose[] CreateProductionPoses(out int logicalSize)
    {
        var gameplay = Assembly.Load("InfiniteLizards.Gameplay");
        var profileType = gameplay.GetType(
            "DesktopLizard.Core.LizardProfile",
            throwOnError: true)!;
        var profile = profileType.GetProperty(
                "Default",
                BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
        var appearance = profileType.GetProperty("Appearance")!.GetValue(profile)!;
        var renderCanvasSize = Convert.ToSingle(
            appearance.GetType().GetProperty("RenderCanvasSize")!.GetValue(appearance));
        var visualScale = Convert.ToSingle(
            appearance.GetType().GetProperty("VisualScale")!.GetValue(appearance));
        // Match LizardGameModule's single-precision metrics calculation.
        var logicalCanvas = (double)(renderCanvasSize * visualScale);
        logicalSize = checked((int)Math.Round(logicalCanvas));
        AssertNear(logicalSize, logicalCanvas, 0.000001d,
            "the production canvas must have an integral DIP extent for this raster matrix");

        var gameType = gameplay.GetType(
            "InfiniteLizards.Gameplay.LizardGameModule",
            throwOnError: true)!;
        var game = Activator.CreateInstance(
            gameType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [profile, 0x51A7E],
            culture: null)!;
        var frame = gameType.GetMethod(
                "CaptureSnapshot",
                BindingFlags.Instance | BindingFlags.Public)!
            .Invoke(game, null)!;
        var viewConstructor = typeof(LizardView).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single();
        var openEyes = (LizardView)viewConstructor.Invoke([profile, frame]);
        var partialBlinkFrame = CloneFrameWithBlink(frame, 0.45f);
        var partialBlink = (LizardView)viewConstructor.Invoke([profile, partialBlinkFrame]);
        typeof(LizardView).GetMethod(
                nameof(LizardView.Present),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(partialBlink, [partialBlinkFrame, new Vector2(-0.6f, 0.8f)]);
        var closedEyes = (LizardView)viewConstructor.Invoke(
            [profile, CloneFrameWithBlink(frame, 1f)]);

        var poses = new[]
        {
            new RasterPose("open-eyes pose", openEyes),
            new RasterPose("partial-blink redirected-look pose", partialBlink),
            new RasterPose("closed-eyes pose", closedEyes)
        };
        foreach (var pose in poses)
        {
            pose.View.Measure(new Size(logicalSize, logicalSize));
            pose.View.Arrange(new Rect(0, 0, logicalSize, logicalSize));
        }
        return poses;
    }

    private static object CloneFrameWithBlink(object frame, float blinkAmount)
    {
        var frameType = frame.GetType();
        var constructor = frameType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 7);
        return constructor.Invoke(
        [
            frameType.GetProperty("BodyOutline")!.GetValue(frame)!,
            frameType.GetProperty("Legs")!.GetValue(frame)!,
            frameType.GetProperty("HeadNose")!.GetValue(frame)!,
            frameType.GetProperty("NegativeEyeCenter")!.GetValue(frame)!,
            frameType.GetProperty("PositiveEyeCenter")!.GetValue(frame)!,
            frameType.GetProperty("Heading")!.GetValue(frame)!,
            blinkAmount
        ]);
    }

    private static RasterResult Render(Control view, int logicalSize, double scale)
    {
        var deviceSize = checked((int)Math.Round(logicalSize * scale));
        AssertNear(deviceSize, logicalSize * scale, 0.000001d,
            $"{scale:0.##}x canvas must map to integral device pixels");
        var pixelsPerCell = checked((int)Math.Round(LogicalCellSize * scale));
        AssertNear(pixelsPerCell, LogicalCellSize * scale, 0.000001d,
            $"{scale:0.##}x cell must map to integral device pixels");

        using var target = new RenderTargetBitmap(
            new PixelSize(deviceSize, deviceSize),
            new Avalonia.Vector(BaseDpi * scale, BaseDpi * scale));
        target.Render(view);

        using var encoded = new MemoryStream();
        target.Save(encoded);
        encoded.Position = 0;
        using var bitmap = SKBitmap.Decode(encoded) ??
            throw new InvalidOperationException("Skia could not decode its offscreen PNG.");
        AssertEqual(deviceSize, bitmap.Width, $"{scale:0.##}x bitmap width");
        AssertEqual(deviceSize, bitmap.Height, $"{scale:0.##}x bitmap height");

        var rawPixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];
        var normalizedSize = logicalSize / LogicalCellSize;
        var cells = new PremultipliedCell[normalizedSize * normalizedSize];
        for (var cellY = 0; cellY < normalizedSize; cellY++)
        {
            for (var cellX = 0; cellX < normalizedSize; cellX++)
            {
                double alpha = 0d;
                double red = 0d;
                double green = 0d;
                double blue = 0d;
                for (var offsetY = 0; offsetY < pixelsPerCell; offsetY++)
                {
                    var y = cellY * pixelsPerCell + offsetY;
                    for (var offsetX = 0; offsetX < pixelsPerCell; offsetX++)
                    {
                        var x = cellX * pixelsPerCell + offsetX;
                        var color = bitmap.GetPixel(x, y);
                        var rawIndex = checked((y * bitmap.Width + x) * 4);
                        rawPixels[rawIndex] = color.Red;
                        rawPixels[rawIndex + 1] = color.Green;
                        rawPixels[rawIndex + 2] = color.Blue;
                        rawPixels[rawIndex + 3] = color.Alpha;

                        var unitAlpha = color.Alpha / 255d;
                        alpha += unitAlpha;
                        red += color.Red / 255d * unitAlpha;
                        green += color.Green / 255d * unitAlpha;
                        blue += color.Blue / 255d * unitAlpha;
                    }
                }

                var sampleCount = pixelsPerCell * pixelsPerCell;
                cells[cellY * normalizedSize + cellX] = new PremultipliedCell(
                    alpha / sampleCount,
                    red / sampleCount,
                    green / sampleCount,
                    blue / sampleCount);
            }
        }

        return new RasterResult(
            Convert.ToHexString(SHA256.HashData(rawPixels)),
            new NormalizedRaster(normalizedSize, normalizedSize, cells));
    }

    private static RasterComparison Compare(
        NormalizedRaster expected,
        NormalizedRaster actual)
    {
        AssertEqual(expected.Width, actual.Width, "normalized width");
        AssertEqual(expected.Height, actual.Height, "normalized height");

        var expectedBounds = expected.ActiveBounds(ActiveAlphaThreshold);
        var actualBounds = actual.ActiveBounds(ActiveAlphaThreshold);
        var unionBounds = CellBounds.Union(expectedBounds, actualBounds);
        AssertTrue(!unionBounds.IsEmpty, "the production renderer emitted no visible pixels");

        var intersectionCount = 0;
        var unionCount = 0;
        double alphaAbsoluteError = 0d;
        double colorAbsoluteError = 0d;
        double expectedCoverage = 0d;
        double actualCoverage = 0d;
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var expectedCell = expected[x, y];
                var actualCell = actual[x, y];
                expectedCoverage += expectedCell.Alpha;
                actualCoverage += actualCell.Alpha;

                var expectedActive = expectedCell.Alpha > ActiveAlphaThreshold;
                var actualActive = actualCell.Alpha > ActiveAlphaThreshold;
                if (expectedActive || actualActive)
                {
                    unionCount++;
                    if (expectedActive && actualActive)
                    {
                        intersectionCount++;
                    }
                    alphaAbsoluteError += Math.Abs(expectedCell.Alpha - actualCell.Alpha);
                    colorAbsoluteError +=
                        Math.Abs(expectedCell.Red - actualCell.Red) +
                        Math.Abs(expectedCell.Green - actualCell.Green) +
                        Math.Abs(expectedCell.Blue - actualCell.Blue);
                }
            }
        }

        var coverageRelativeError = Math.Abs(expectedCoverage - actualCoverage) /
            Math.Max(expectedCoverage, 0.000001d);
        var activeIntersectionOverUnion = unionCount == 0
            ? 1d
            : intersectionCount / (double)unionCount;
        var alphaMeanAbsoluteError = alphaAbsoluteError / unionCount;
        var colorMeanAbsoluteError = colorAbsoluteError / (unionCount * 3d);
        var boundsEdgeError = expectedBounds.EdgeDistance(actualBounds);

        // These are deliberately per-active-extent gates. A whole-canvas
        // average would let transparent desktop pixels hide a renderer drift.
        var accepted =
            boundsEdgeError <= 1 &&
            coverageRelativeError <= 0.005d &&
            activeIntersectionOverUnion >= 0.98d &&
            alphaMeanAbsoluteError <= 0.006d &&
            colorMeanAbsoluteError <= 0.005d;
        return new RasterComparison(
            accepted,
            boundsEdgeError,
            coverageRelativeError,
            activeIntersectionOverUnion,
            alphaMeanAbsoluteError,
            colorMeanAbsoluteError);
    }

    private readonly record struct RasterResult(
        string DeviceHash,
        NormalizedRaster Normalized);

    private readonly record struct RasterPose(string Name, Control View);

    private sealed class NormalizedRaster
    {
        private readonly PremultipliedCell[] _cells;

        public NormalizedRaster(int width, int height, PremultipliedCell[] cells)
        {
            if (width <= 0 || height <= 0 || cells.Length != width * height)
            {
                throw new ArgumentException("Invalid normalized raster dimensions.");
            }

            Width = width;
            Height = height;
            _cells = cells;
        }

        public int Width { get; }
        public int Height { get; }
        public PremultipliedCell this[int x, int y] => _cells[y * Width + x];

        public CellBounds ActiveBounds(double alphaThreshold)
        {
            var left = Width;
            var top = Height;
            var right = -1;
            var bottom = -1;
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    if (this[x, y].Alpha <= alphaThreshold)
                    {
                        continue;
                    }

                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }

            return right < left
                ? CellBounds.Empty
                : new CellBounds(left, top, right, bottom);
        }

        public NormalizedRaster ShiftCells(int deltaX, int deltaY)
        {
            var shifted = new PremultipliedCell[_cells.Length];
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var targetX = x + deltaX;
                    var targetY = y + deltaY;
                    if (targetX >= 0 && targetX < Width &&
                        targetY >= 0 && targetY < Height)
                    {
                        shifted[targetY * Width + targetX] = this[x, y];
                    }
                }
            }

            return new NormalizedRaster(Width, Height, shifted);
        }
    }

    private readonly record struct PremultipliedCell(
        double Alpha,
        double Red,
        double Green,
        double Blue);

    private readonly record struct CellBounds(int Left, int Top, int Right, int Bottom)
    {
        public static CellBounds Empty => new(0, 0, -1, -1);
        public bool IsEmpty => Right < Left || Bottom < Top;

        public int EdgeDistance(CellBounds other)
        {
            if (IsEmpty || other.IsEmpty)
            {
                return IsEmpty == other.IsEmpty ? 0 : int.MaxValue;
            }

            return Math.Max(
                Math.Max(Math.Abs(Left - other.Left), Math.Abs(Top - other.Top)),
                Math.Max(Math.Abs(Right - other.Right), Math.Abs(Bottom - other.Bottom)));
        }

        public static CellBounds Union(CellBounds left, CellBounds right)
        {
            if (left.IsEmpty)
            {
                return right;
            }
            if (right.IsEmpty)
            {
                return left;
            }

            return new CellBounds(
                Math.Min(left.Left, right.Left),
                Math.Min(left.Top, right.Top),
                Math.Max(left.Right, right.Right),
                Math.Max(left.Bottom, right.Bottom));
        }
    }

    private readonly record struct RasterComparison(
        bool Accepted,
        int BoundsEdgeError,
        double CoverageRelativeError,
        double ActiveIntersectionOverUnion,
        double AlphaMeanAbsoluteError,
        double ColorMeanAbsoluteError)
    {
        public override string ToString() =>
            $"bounds={BoundsEdgeError} cells, " +
            $"coverage={CoverageRelativeError:P3}, " +
            $"active-IoU={ActiveIntersectionOverUnion:P3}, " +
            $"alpha-MAE={AlphaMeanAbsoluteError:F6}, " +
            $"premul-color-MAE={ColorMeanAbsoluteError:F6}";
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
        where T : IEquatable<T>
    {
        if (!expected.Equals(actual))
        {
            throw new InvalidOperationException(
                $"{message}: expected {expected}, actual {actual}");
        }
    }

    private static void AssertNear(
        double expected,
        double actual,
        double tolerance,
        string message)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"{message}: expected {expected:R}, actual {actual:R}");
        }
    }
}
