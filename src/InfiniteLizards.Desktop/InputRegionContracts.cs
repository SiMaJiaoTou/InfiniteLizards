using System.Collections.Immutable;
using Avalonia;

namespace InfiniteLizards.Desktop;

/// <summary>
/// Immutable, renderer-independent vector geometry describing the portion of
/// a desktop-pet surface that is visible or interactive. Coordinates are in
/// the presenter's view-space DIPs.
/// </summary>
internal readonly record struct DesktopPetBezierSegment
{
    private readonly bool _isInitialized;

    public DesktopPetBezierSegment(Point control1, Point control2, Point end)
    {
        DesktopPetInputRegionValidation.RequireFinite(control1, nameof(control1));
        DesktopPetInputRegionValidation.RequireFinite(control2, nameof(control2));
        DesktopPetInputRegionValidation.RequireFinite(end, nameof(end));
        Control1 = control1;
        Control2 = control2;
        End = end;
        _isInitialized = true;
    }

    public Point Control1 { get; }
    public Point Control2 { get; }
    public Point End { get; }

    internal void EnsureValid()
    {
        if (!_isInitialized)
        {
            throw new ArgumentException(
                "A default Bezier segment is not initialized vector geometry.");
        }
        DesktopPetInputRegionValidation.RequireFinite(Control1, nameof(Control1));
        DesktopPetInputRegionValidation.RequireFinite(Control2, nameof(Control2));
        DesktopPetInputRegionValidation.RequireFinite(End, nameof(End));
    }
}

internal static class DesktopPetVisibleInputSizing
{
    public static double MainLimbThickness(
        double hitLimbWidth,
        double hitTolerance,
        double visibleLimbWidth) =>
        Math.Max(
            RequireNonNegative(hitLimbWidth) + RequireNonNegative(hitTolerance) * 2d,
            RequireNonNegative(visibleLimbWidth));

    public static double MainFootRadius(
        double hitFootRadius,
        double hitTolerance,
        double visibleFootRadius) =>
        Math.Max(
            RequireNonNegative(hitFootRadius) + RequireNonNegative(hitTolerance),
            RequireNonNegative(visibleFootRadius));

    public static double MainEyeRadius(
        double hitEyeRadius,
        double hitTolerance,
        double visibleEyeRadius,
        double pupilOffset,
        double pupilRadius,
        double minimumPupilRadius) =>
        Math.Max(
            RequireNonNegative(hitEyeRadius) + RequireNonNegative(hitTolerance),
            Math.Max(
                RequireNonNegative(visibleEyeRadius),
                RequireNonNegative(pupilOffset) + Math.Max(
                    RequireNonNegative(pupilRadius),
                    RequireNonNegative(minimumPupilRadius))));

    private static double RequireNonNegative(double value)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        return value;
    }
}

internal readonly record struct DesktopPetBezierPath
{
    public DesktopPetBezierPath(
        Point start,
        ImmutableArray<DesktopPetBezierSegment> segments)
    {
        DesktopPetInputRegionValidation.RequireFinite(start, nameof(start));
        if (segments.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A Bezier path must contain at least one segment.",
                nameof(segments));
        }
        foreach (var segment in segments)
        {
            segment.EnsureValid();
        }

        Start = start;
        Segments = segments;
    }

    public Point Start { get; }
    public ImmutableArray<DesktopPetBezierSegment> Segments { get; }

    internal void EnsureValid()
    {
        DesktopPetInputRegionValidation.RequireFinite(Start, nameof(Start));
        if (Segments.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A Bezier path must contain at least one segment.");
        }
        foreach (var segment in Segments)
        {
            segment.EnsureValid();
        }
    }
}

internal readonly record struct DesktopPetStrokedBezierPath
{
    public DesktopPetStrokedBezierPath(DesktopPetBezierPath path, double thickness)
    {
        path.EnsureValid();
        if (!double.IsFinite(thickness) || thickness <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(thickness),
                "A stroked path must have a finite positive thickness.");
        }

        Path = path;
        Thickness = thickness;
    }

    public DesktopPetBezierPath Path { get; }
    public double Thickness { get; }

    internal void EnsureValid()
    {
        Path.EnsureValid();
        if (!double.IsFinite(Thickness) || Thickness <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Thickness),
                "A stroked path must have a finite positive thickness.");
        }
    }
}

internal readonly record struct DesktopPetRegionEllipse
{
    public DesktopPetRegionEllipse(Point center, double radiusX, double radiusY)
    {
        DesktopPetInputRegionValidation.RequireFinite(center, nameof(center));
        if (!double.IsFinite(radiusX) || radiusX <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radiusX),
                "An ellipse must have finite positive radii.");
        }
        if (!double.IsFinite(radiusY) || radiusY <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radiusY),
                "An ellipse must have finite positive radii.");
        }

        Center = center;
        RadiusX = radiusX;
        RadiusY = radiusY;
    }

    public Point Center { get; }
    public double RadiusX { get; }
    public double RadiusY { get; }

    internal void EnsureValid()
    {
        DesktopPetInputRegionValidation.RequireFinite(Center, nameof(Center));
        if (!double.IsFinite(RadiusX) || RadiusX <= 0d ||
            !double.IsFinite(RadiusY) || RadiusY <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RadiusX),
                "An ellipse must have finite positive radii.");
        }
    }
}

internal sealed record DesktopPetInputRegion
{
    public DesktopPetInputRegion(
        ImmutableArray<DesktopPetBezierPath> fills,
        ImmutableArray<DesktopPetStrokedBezierPath> strokes,
        ImmutableArray<DesktopPetRegionEllipse> ellipses)
    {
        if (fills.IsDefault)
        {
            throw new ArgumentException("The fill collection must be initialized.", nameof(fills));
        }
        if (strokes.IsDefault)
        {
            throw new ArgumentException("The stroke collection must be initialized.", nameof(strokes));
        }
        if (ellipses.IsDefault)
        {
            throw new ArgumentException("The ellipse collection must be initialized.", nameof(ellipses));
        }
        if (fills.IsEmpty && strokes.IsEmpty && ellipses.IsEmpty)
        {
            throw new ArgumentException("An input region must contain visible geometry.");
        }
        foreach (var fill in fills)
        {
            fill.EnsureValid();
        }
        foreach (var stroke in strokes)
        {
            stroke.EnsureValid();
        }
        foreach (var ellipse in ellipses)
        {
            ellipse.EnsureValid();
        }

        Fills = fills;
        Strokes = strokes;
        Ellipses = ellipses;
    }

    public ImmutableArray<DesktopPetBezierPath> Fills { get; }
    public ImmutableArray<DesktopPetStrokedBezierPath> Strokes { get; }
    public ImmutableArray<DesktopPetRegionEllipse> Ellipses { get; }
}

/// <summary>
/// Optional presenter capability. The host remains generic over the gameplay
/// snapshot and only observes this stable desktop-side geometry contract.
/// </summary>
internal interface IDesktopPetInputRegionProvider
{
    long InputRegionVersion { get; }

    double InputRegionRefreshRate { get; }

    DesktopPetInputRegion CaptureInputRegion();
}

/// <summary>
/// Optional render-thread acknowledgement emitted only after the Avalonia
/// composition batch containing a presenter pose has been rasterized. The
/// event is delivered on the UI dispatcher; native window code must never run
/// from the renderer or compositor thread.
/// </summary>
internal interface IDesktopPetRenderCommitSource
{
    /// <summary>
    /// The presenter version currently stored in the retained view.  Hosts
    /// read this immediately after a successful Present call so non-render
    /// state can be bound to the same version later carried by the compositor
    /// acknowledgement.
    /// </summary>
    long PresentationVersion { get; }

    event EventHandler<DesktopPetRenderCommitAcknowledgedEventArgs>? RenderCommitAcknowledged;
}

internal sealed class DesktopPetRenderCommitAcknowledgedEventArgs : EventArgs
{
    public DesktopPetRenderCommitAcknowledgedEventArgs(
        long sequence,
        long version,
        double renderScale)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }
        if (!double.IsFinite(renderScale) || renderScale <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(renderScale));
        }

        Sequence = sequence;
        Version = version;
        RenderScale = renderScale;
    }

    public long Sequence { get; }

    public long Version { get; }

    public double RenderScale { get; }
}

internal static class DesktopPetInputRegionValidation
{
    public static void RequireFinite(Point point, string parameterName)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Vector-region coordinates must be finite.");
        }
    }
}

internal static class DesktopPetInputRegionRefreshPolicy
{
    public static bool ShouldRefresh(
        bool force,
        bool hasAttempted,
        bool lastAttemptSucceeded,
        long version,
        long lastVersion,
        double displayScale,
        double lastDisplayScale,
        long nowTicks,
        long lastRefreshTicks,
        double refreshRate,
        long frequency)
    {
        if (force || !hasAttempted)
        {
            return true;
        }
        if (!double.IsFinite(displayScale) || displayScale <= 0d ||
            !double.IsFinite(lastDisplayScale) || lastDisplayScale <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(displayScale));
        }
        if (BitConverter.DoubleToInt64Bits(displayScale) !=
            BitConverter.DoubleToInt64Bits(lastDisplayScale))
        {
            return true;
        }
        if (lastAttemptSucceeded)
        {
            // A successfully installed pose can advance immediately so visual
            // clipping follows each immutable render snapshot.
            return version != lastVersion;
        }
        if (!double.IsFinite(refreshRate) || refreshRate <= 0d || frequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshRate));
        }

        // Once native installation has failed, every later pose is still a
        // retry of that failed boundary. Throttle before considering version
        // changes so a 120 Hz animation cannot become a 120 Hz USER32 failure
        // loop. Very small positive rates can overflow the double quotient;
        // saturate instead of relying on an implementation-defined cast.
        var interval = Math.Ceiling(frequency / refreshRate);
        var minimumInterval = !double.IsFinite(interval) || interval >= long.MaxValue
            ? long.MaxValue
            : Math.Max(1L, (long)interval);
        if (nowTicks < 0 || lastRefreshTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nowTicks));
        }
        if (nowTicks < lastRefreshTicks)
        {
            return false;
        }

        return nowTicks - lastRefreshTicks >= minimumInterval;
    }
}
