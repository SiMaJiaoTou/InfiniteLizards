using System.ComponentModel;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;

namespace InfiniteLizards.Desktop.Platform;

internal enum Win32InputRegionState
{
    Unknown,
    FailClosed,
    Installed
}

internal static class Win32InputRegionPolicy
{
    public static bool RequiresTransparentFallback(Win32InputRegionState state) =>
        state != Win32InputRegionState.Installed;
}

/// <summary>
/// Resolves every physical raster scale that may still be present while a
/// per-monitor DPI transition is settling. An invalid live RenderScaling is a
/// safety failure: callers must retain/retry the request from a fail-closed
/// HWND instead of guessing a transform.
/// </summary>
internal static class Win32InputRegionScalePolicy
{
    public static ImmutableArray<double> Resolve(
        ImmutableArray<double> poseRasterScales,
        double targetDisplayScale,
        double currentRenderScale,
        bool includeLiveTransitionScales)
    {
        DesktopPetRasterScaleSet.RequireValid(
            targetDisplayScale,
            nameof(targetDisplayScale));
        DesktopPetRasterScaleSet.RequireValid(
            currentRenderScale,
            nameof(currentRenderScale));
        if (poseRasterScales.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "At least one submitted or acknowledged raster scale is required.",
                nameof(poseRasterScales));
        }

        return includeLiveTransitionScales
            ? DesktopPetRasterScaleSet.Distinct(
                poseRasterScales.Concat(
                    [targetDisplayScale, currentRenderScale]))
            : DesktopPetRasterScaleSet.Distinct(poseRasterScales);
    }

    public static string Signature(ImmutableArray<double> scales)
    {
        if (scales.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "At least one raster scale is required.",
                nameof(scales));
        }

        return string.Join(
            ',',
            scales.Select(scale => BitConverter.DoubleToInt64Bits(scale)
                .ToString("X16", CultureInfo.InvariantCulture)));
    }
}

internal sealed record Win32InputRegionRasterization(
    DesktopPetInputRegion Region,
    long PoseVersion,
    bool IsAcknowledged,
    double RasterScale);

/// <summary>
/// Pure pose-to-raster plan used by GDI. Historical poses retain only their
/// recorded scales. Every retained pose additionally covers the live
/// target/current scales: an older committed or in-flight drawing list can be
/// rerasterized by a target resize before the latest Present reaches Render.
/// Historical per-pose submission scales remain correlated and are never
/// projected onto unrelated poses.
/// </summary>
internal sealed record Win32InputRegionRasterPlan
{
    private Win32InputRegionRasterPlan(
        ImmutableArray<Win32InputRegionRasterization> rasterizations,
        string signature)
    {
        Rasterizations = rasterizations;
        Signature = signature;
    }

    public ImmutableArray<Win32InputRegionRasterization> Rasterizations { get; }

    public string Signature { get; }

    public static Win32InputRegionRasterPlan Create(
        DesktopPetInputRegionInstallation installation,
        double targetDisplayScale,
        double currentRenderScale)
    {
        ArgumentNullException.ThrowIfNull(installation);
        DesktopPetRasterScaleSet.RequireValid(
            targetDisplayScale,
            nameof(targetDisplayScale));
        DesktopPetRasterScaleSet.RequireValid(
            currentRenderScale,
            nameof(currentRenderScale));

        var rasterizations =
            ImmutableArray.CreateBuilder<Win32InputRegionRasterization>();
        var signatures = new string[installation.Poses.Length];
        for (var index = 0; index < installation.Poses.Length; index++)
        {
            var pose = installation.Poses[index];
            var scales = Win32InputRegionScalePolicy.Resolve(
                pose.RasterScales,
                targetDisplayScale,
                currentRenderScale,
                includeLiveTransitionScales: true);
            foreach (var scale in scales)
            {
                rasterizations.Add(new Win32InputRegionRasterization(
                    pose.Region,
                    pose.Version,
                    pose.IsAcknowledged,
                    scale));
            }

            signatures[index] = string.Concat(
                pose.Version.ToString("X16", CultureInfo.InvariantCulture),
                pose.IsAcknowledged ? ":A:" : ":P:",
                Win32InputRegionScalePolicy.Signature(scales));
        }

        return new Win32InputRegionRasterPlan(
            rasterizations.ToImmutable(),
            string.Join('|', signatures));
    }
}

internal readonly record struct Win32RegionPoint(int X, int Y);

internal readonly record struct Win32RegionBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => checked(Right - Left);
    public int Height => checked(Bottom - Top);
}

/// <summary>
/// Pure DIP-to-window coordinate conversion used by the native region builder.
/// The client inset is in physical pixels and shifts client-relative geometry
/// into the outer HWND coordinate space expected by SetWindowRgn.
/// </summary>
internal readonly record struct Win32InputRegionTransform
{
    public const int AntialiasDilationPixels = 1;

    public Win32InputRegionTransform(double displayScale, Win32WindowInsets insets)
    {
        if (!double.IsFinite(displayScale) || displayScale <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(displayScale),
                "Display scale must be finite and positive.");
        }
        if (insets.Left < 0 || insets.Top < 0 ||
            insets.Right < 0 || insets.Bottom < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(insets),
                "Window insets must be non-negative.");
        }

        DisplayScale = displayScale;
        Insets = insets;
    }

    public double DisplayScale { get; }
    public Win32WindowInsets Insets { get; }

    public void EnsureValid()
    {
        DesktopPetRasterScaleSet.RequireValid(DisplayScale, nameof(DisplayScale));
        if (Insets.Left < 0 || Insets.Top < 0 ||
            Insets.Right < 0 || Insets.Bottom < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Insets),
                "Window insets must be non-negative.");
        }
    }

    public Win32RegionPoint Point(Point value) => new(
        RoundCoordinate(value.X, Insets.Left),
        RoundCoordinate(value.Y, Insets.Top));

    public Win32RegionBounds EllipseBounds(DesktopPetRegionEllipse ellipse)
    {
        ellipse.EnsureValid();
        var left = FloorCoordinate(ellipse.Center.X - ellipse.RadiusX, Insets.Left);
        var top = FloorCoordinate(ellipse.Center.Y - ellipse.RadiusY, Insets.Top);
        var right = CeilingCoordinate(ellipse.Center.X + ellipse.RadiusX, Insets.Left);
        var bottom = CeilingCoordinate(ellipse.Center.Y + ellipse.RadiusY, Insets.Top);
        if (right <= left || bottom <= top)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ellipse),
                "The transformed ellipse must occupy at least one device pixel.");
        }
        return new Win32RegionBounds(left, top, right, bottom);
    }

    public int StrokeWidth(double thickness)
    {
        if (!double.IsFinite(thickness) || thickness <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(thickness));
        }

        return checked(Math.Max(1, (int)Math.Ceiling(thickness * DisplayScale)));
    }

    private int RoundCoordinate(double value, int offset) =>
        CheckedInt(Math.Round(value * DisplayScale + offset, MidpointRounding.AwayFromZero));

    private int FloorCoordinate(double value, int offset) =>
        CheckedInt(Math.Floor(value * DisplayScale + offset));

    private int CeilingCoordinate(double value, int offset) =>
        CheckedInt(Math.Ceiling(value * DisplayScale + offset));

    private static int CheckedInt(double value)
    {
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
        {
            throw new OverflowException("Vector-region coordinates exceed Win32 integer limits.");
        }
        return (int)value;
    }
}

/// <summary>
/// Owns every temporary GDI object it creates. The returned HRGN belongs to
/// the caller until a successful SetWindowRgn transfers it to USER32.
/// </summary>
internal static class Win32InputRegionBuilder
{
    private const uint PsSolid = 0x00000000;
    private const uint PsEndCapRound = 0x00000000;
    private const uint PsJoinRound = 0x00000000;
    private const uint PsGeometric = 0x00010000;
    private const uint BsSolid = 0;
    private const int Winding = 2;
    private const int Error = 0;
    private const int NullRegion = 1;
    private const int RgnOr = 2;
    private const int RgnCopy = 5;

    public static nint Build(
        DesktopPetInputRegion region,
        Win32InputRegionTransform transform) => Build(
        ImmutableArray.Create(new BuildItem(region, transform)));

    public static nint Build(
        ImmutableArray<Win32InputRegionRasterization> rasterizations,
        Win32WindowInsets insets)
    {
        if (rasterizations.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "At least one pose rasterization is required.",
                nameof(rasterizations));
        }
        var items = ImmutableArray.CreateBuilder<BuildItem>(rasterizations.Length);
        foreach (var rasterization in rasterizations)
        {
            ArgumentNullException.ThrowIfNull(rasterization);
            ArgumentNullException.ThrowIfNull(rasterization.Region);
            DesktopPetRasterScaleSet.RequireValid(
                rasterization.RasterScale,
                nameof(rasterizations));
            items.Add(new BuildItem(
                rasterization.Region,
                new Win32InputRegionTransform(rasterization.RasterScale, insets)));
        }

        return Build(items.MoveToImmutable());
    }

    private static nint Build(ImmutableArray<BuildItem> items)
    {
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item.Region);
            item.Transform.EnsureValid();
        }

        var deviceContext = CreateCompatibleDC(nint.Zero);
        if (deviceContext == nint.Zero)
        {
            throw NativeFailure("CreateCompatibleDC");
        }

        nint aggregate = nint.Zero;
        try
        {
            if (SetPolyFillMode(deviceContext, Winding) == 0)
            {
                throw NativeFailure("SetPolyFillMode");
            }

            aggregate = CreateEmptyRegion();
            foreach (var item in items)
            {
                var region = item.Region;
                var transform = item.Transform;
                foreach (var fill in region.Fills)
                {
                    UnionOwned(
                        aggregate,
                        BuildPathRegion(
                            deviceContext,
                            fill,
                            transform,
                            closeFigure: true,
                            0));
                }
                foreach (var stroke in region.Strokes)
                {
                    UnionOwned(
                        aggregate,
                        BuildPathRegion(
                            deviceContext,
                            stroke.Path,
                            transform,
                            closeFigure: false,
                            transform.StrokeWidth(stroke.Thickness)));
                }
                foreach (var ellipse in region.Ellipses)
                {
                    var bounds = transform.EllipseBounds(ellipse);
                    var component = CreateEllipticRgn(
                        bounds.Left,
                        bounds.Top,
                        bounds.Right,
                        bounds.Bottom);
                    if (component == nint.Zero)
                    {
                        throw NativeFailure("CreateEllipticRgn");
                    }
                    UnionOwned(aggregate, component);
                }
            }

            var regionType = GetRgnBox(aggregate, out _);
            if (regionType == Error)
            {
                throw NativeFailure("GetRgnBox");
            }
            if (regionType == NullRegion)
            {
                throw new InvalidOperationException(
                    "The desktop-pet input geometry produced an empty native region.");
            }

            var dilated = Dilate(aggregate, Win32InputRegionTransform.AntialiasDilationPixels);
            DeleteGdiObject(aggregate);
            aggregate = nint.Zero;
            return dilated;
        }
        catch
        {
            if (aggregate != nint.Zero)
            {
                DeleteGdiObject(aggregate);
            }
            throw;
        }
        finally
        {
            var deleted = DeleteDC(deviceContext);
            Debug.Assert(deleted, "The temporary GDI device context must be released.");
        }
    }

    private readonly record struct BuildItem(
        DesktopPetInputRegion Region,
        Win32InputRegionTransform Transform);

    public static void DeleteOwnedRegion(nint region)
    {
        if (region != nint.Zero)
        {
            DeleteGdiObject(region);
        }
    }

    private static nint BuildPathRegion(
        nint deviceContext,
        DesktopPetBezierPath path,
        Win32InputRegionTransform transform,
        bool closeFigure,
        int strokeWidth)
    {
        path.EnsureValid();
        nint pen = nint.Zero;
        nint previousPen = nint.Zero;
        var pathOpen = false;
        try
        {
            if (strokeWidth > 0)
            {
                var brush = new LogBrush
                {
                    Style = BsSolid,
                    Color = 0,
                    Hatch = 0
                };
                pen = ExtCreatePen(
                    PsGeometric | PsSolid | PsEndCapRound | PsJoinRound,
                    checked((uint)strokeWidth),
                    ref brush,
                    0,
                    null);
                if (pen == nint.Zero)
                {
                    throw NativeFailure("ExtCreatePen");
                }
                previousPen = SelectObject(deviceContext, pen);
                if (previousPen == nint.Zero || previousPen == new nint(-1))
                {
                    throw NativeFailure("SelectObject");
                }
            }

            if (!BeginPath(deviceContext))
            {
                throw NativeFailure("BeginPath");
            }
            pathOpen = true;
            var start = transform.Point(path.Start);
            if (!MoveToEx(deviceContext, start.X, start.Y, nint.Zero))
            {
                throw NativeFailure("MoveToEx");
            }

            var nativePoints = new NativePoint[checked(path.Segments.Length * 3)];
            var pointIndex = 0;
            foreach (var segment in path.Segments)
            {
                nativePoints[pointIndex++] = ToNative(transform.Point(segment.Control1));
                nativePoints[pointIndex++] = ToNative(transform.Point(segment.Control2));
                nativePoints[pointIndex++] = ToNative(transform.Point(segment.End));
            }
            if (!PolyBezierTo(deviceContext, nativePoints, checked((uint)nativePoints.Length)))
            {
                throw NativeFailure("PolyBezierTo");
            }
            if (closeFigure && !CloseFigure(deviceContext))
            {
                throw NativeFailure("CloseFigure");
            }
            if (!EndPath(deviceContext))
            {
                throw NativeFailure("EndPath");
            }
            pathOpen = false;
            if (strokeWidth > 0 && !WidenPath(deviceContext))
            {
                throw NativeFailure("WidenPath");
            }

            var region = PathToRegion(deviceContext);
            if (region == nint.Zero)
            {
                throw NativeFailure("PathToRegion");
            }
            return region;
        }
        finally
        {
            if (pathOpen)
            {
                AbortPath(deviceContext);
            }
            if (previousPen != nint.Zero && previousPen != new nint(-1))
            {
                var restoreResult = SelectObject(deviceContext, previousPen);
                Debug.Assert(
                    restoreResult != nint.Zero && restoreResult != new nint(-1),
                    "The original GDI pen must be restored before deletion.");
            }
            if (pen != nint.Zero)
            {
                DeleteGdiObject(pen);
            }
        }
    }

    private static nint Dilate(nint source, int radius)
    {
        var result = CreateEmptyRegion();
        try
        {
            for (var y = -radius; y <= radius; y++)
            {
                for (var x = -radius; x <= radius; x++)
                {
                    var shifted = CreateEmptyRegion();
                    try
                    {
                        CheckRegionResult(CombineRgn(shifted, source, nint.Zero, RgnCopy),
                            "CombineRgn(RGN_COPY)");
                        CheckRegionResult(OffsetRgn(shifted, x, y), "OffsetRgn");
                        CheckRegionResult(CombineRgn(result, result, shifted, RgnOr),
                            "CombineRgn(RGN_OR)");
                    }
                    finally
                    {
                        DeleteGdiObject(shifted);
                    }
                }
            }

            var owned = result;
            result = nint.Zero;
            return owned;
        }
        finally
        {
            if (result != nint.Zero)
            {
                DeleteGdiObject(result);
            }
        }
    }

    private static void UnionOwned(nint destination, nint component)
    {
        try
        {
            CheckRegionResult(
                CombineRgn(destination, destination, component, RgnOr),
                "CombineRgn(RGN_OR)");
        }
        finally
        {
            DeleteGdiObject(component);
        }
    }

    internal static nint CreateEmptyRegion()
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        return region != nint.Zero
            ? region
            : throw NativeFailure("CreateRectRgn");
    }

    private static void CheckRegionResult(int result, string operation)
    {
        if (result == Error)
        {
            throw NativeFailure(operation);
        }
    }

    private static NativePoint ToNative(Win32RegionPoint point) => new(point.X, point.Y);

    private static void DeleteGdiObject(nint value)
    {
        if (value != nint.Zero)
        {
            var deleted = DeleteObject(value);
            Debug.Assert(deleted, "The owned GDI object must be released.");
        }
    }

    private static Exception NativeFailure(string operation)
    {
        var error = Marshal.GetLastPInvokeError();
        return error != 0
            ? new Win32Exception(error, $"{operation} failed.")
            : new InvalidOperationException($"{operation} failed without a Win32 error code.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LogBrush
    {
        public uint Style;
        public uint Color;
        public nuint Hatch;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int SetPolyFillMode(nint deviceContext, int mode);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BeginPath(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPath(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AbortPath(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseFigure(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveToEx(
        nint deviceContext,
        int x,
        int y,
        nint previousPoint);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PolyBezierTo(
        nint deviceContext,
        [In] NativePoint[] points,
        uint pointCount);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WidenPath(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint PathToRegion(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint ExtCreatePen(
        uint penStyle,
        uint width,
        ref LogBrush brush,
        uint styleCount,
        [In] uint[]? style);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint SelectObject(nint deviceContext, nint value);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateEllipticRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int CombineRgn(
        nint destination,
        nint source1,
        nint source2,
        int mode);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int OffsetRgn(nint region, int x, int y);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetRgnBox(nint region, out NativeRect bounds);
}
