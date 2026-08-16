namespace DesktopPet.Engine;

/// <summary>
/// A point in the host platform's global desktop coordinate space. Windows
/// supplies physical pixels; Avalonia.Native on macOS supplies Cocoa points.
/// <see cref="DisplayDescriptor.Scale"/> is the sole conversion from these
/// platform units into the engine's canonical 96-DIP world.
/// </summary>
public readonly record struct DevicePoint(double X, double Y);

public readonly record struct WorldPoint(double X, double Y);

/// <summary>
/// A rectangle in the same platform desktop units as <see cref="DevicePoint"/>.
/// The name distinguishes the host boundary from world space; it does not
/// imply that every platform reports raster/backing pixels.
/// </summary>
public readonly record struct DeviceRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
    public double CenterX => (Left + Right) * 0.5d;
    public double CenterY => (Top + Bottom) * 0.5d;

    public bool Contains(DevicePoint point) =>
        point.X >= Left && point.X < Right &&
        point.Y >= Top && point.Y < Bottom;
}

public readonly record struct WorldRectD(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
    public double CenterX => (Left + Right) * 0.5d;
    public double CenterY => (Top + Bottom) * 0.5d;

    public bool Contains(WorldPoint point) =>
        point.X >= Left && point.X < Right &&
        point.Y >= Top && point.Y < Bottom;
}

public sealed record DisplayDescriptor(
    string Id,
    DeviceRect Bounds,
    DeviceRect WorkingArea,
    double Scale,
    bool IsPrimary = false);

public sealed record MappedDisplay(
    string Id,
    DeviceRect DeviceBounds,
    DeviceRect DeviceWorkingArea,
    WorldRectD WorldBounds,
    WorldRectD WorldWorkingArea,
    double Scale,
    bool IsPrimary)
{
    public WorldPoint DeviceToWorld(DevicePoint point) => new(
        WorldBounds.Left + (point.X - DeviceBounds.Left) / Scale,
        WorldBounds.Top + (point.Y - DeviceBounds.Top) / Scale);

    public DevicePoint WorldToDevice(WorldPoint point) => new(
        DeviceBounds.Left + (point.X - WorldBounds.Left) * Scale,
        DeviceBounds.Top + (point.Y - WorldBounds.Top) * Scale);
}

/// <summary>
/// Describes per-display 96-DIP spaces and their device-space arrangement.
/// A single stateless affine map cannot keep every point on a mixed-DPI seam
/// continuous. Interactive cross-display movement therefore goes through
/// <see cref="ActiveDisplaySpace"/>, which rebases tracked state atomically.
/// </summary>
public sealed class DisplayTopology
{
    private readonly MappedDisplay[] _displays;

    public IReadOnlyList<MappedDisplay> Displays => _displays;
    public MappedDisplay Primary { get; }

    public DisplayTopology(IEnumerable<DisplayDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var source = descriptors
            .Where(IsValid)
            .OrderByDescending(value => value.IsPrimary)
            .ThenBy(value => value.Id, StringComparer.Ordinal)
            .ToList();
        if (source.Count == 0)
        {
            throw new ArgumentException("At least one valid display is required.", nameof(descriptors));
        }

        var duplicateId = source
            .GroupBy(value => value.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Display identifiers must be unique; '{duplicateId.Key}' was reported more than once.",
                nameof(descriptors));
        }

        if (source.Count(value => value.IsPrimary) > 1)
        {
            throw new ArgumentException(
                "At most one display may be marked as primary.",
                nameof(descriptors));
        }

        var primaryDescriptor = source.FirstOrDefault(value => value.IsPrimary) ?? source[0];
        var mapped = new List<MappedDisplay>(source.Count)
        {
            MapAt(primaryDescriptor, 0d, 0d)
        };
        source.Remove(primaryDescriptor);

        while (source.Count > 0)
        {
            var bestSource = source[0];
            var bestAnchor = mapped[0];
            var bestDistance = double.PositiveInfinity;
            foreach (var candidate in source)
            {
                foreach (var anchor in mapped)
                {
                    var distance = RectDistanceSquared(candidate.Bounds, anchor.DeviceBounds);
                    if (distance < bestDistance)
                    {
                        bestSource = candidate;
                        bestAnchor = anchor;
                        bestDistance = distance;
                    }
                }
            }

            var origin = PlaceRelative(bestSource, bestAnchor);
            mapped.Add(MapAt(bestSource, origin.X, origin.Y));
            source.Remove(bestSource);
        }

        _displays = mapped.ToArray();
        Primary = _displays.First(value => value.Id == primaryDescriptor.Id);
    }

    public MappedDisplay FindByDevicePoint(DevicePoint point) =>
        _displays.FirstOrDefault(value => value.DeviceBounds.Contains(point)) ??
        _displays.MinBy(value => DistanceSquared(point, value.DeviceBounds))!;

    public MappedDisplay FindByWorldPoint(WorldPoint point) =>
        _displays.FirstOrDefault(value => value.WorldBounds.Contains(point)) ??
        _displays.MinBy(value => DistanceSquared(point, value.WorldBounds))!;

    public MappedDisplay? FindById(string id) =>
        _displays.FirstOrDefault(value =>
            string.Equals(value.Id, id, StringComparison.Ordinal));

    public WorldPoint DeviceToWorld(DevicePoint point, MappedDisplay? display = null) =>
        (display ?? FindByDevicePoint(point)).DeviceToWorld(point);

    public WorldPoint DeviceToWorld(double x, double y, MappedDisplay? display = null) =>
        DeviceToWorld(new DevicePoint(x, y), display);

    public DevicePoint WorldToDevice(WorldPoint point, MappedDisplay? display = null) =>
        (display ?? FindByWorldPoint(point)).WorldToDevice(point);

    public DevicePoint WorldToDevice(double x, double y, MappedDisplay? display = null) =>
        WorldToDevice(new WorldPoint(x, y), display);

    private static bool IsValid(DisplayDescriptor? value) =>
        value is not null &&
        !string.IsNullOrWhiteSpace(value.Id) &&
        double.IsFinite(value.Scale) && value.Scale > 0d &&
        IsFinite(value.Bounds) &&
        IsFinite(value.WorkingArea) &&
        value.Bounds.Width > 0d && value.Bounds.Height > 0d &&
        value.WorkingArea.Width > 0d && value.WorkingArea.Height > 0d &&
        value.WorkingArea.Left >= value.Bounds.Left &&
        value.WorkingArea.Top >= value.Bounds.Top &&
        value.WorkingArea.Right <= value.Bounds.Right &&
        value.WorkingArea.Bottom <= value.Bounds.Bottom;

    private static bool IsFinite(DeviceRect value) =>
        double.IsFinite(value.Left) &&
        double.IsFinite(value.Top) &&
        double.IsFinite(value.Right) &&
        double.IsFinite(value.Bottom);

    private static MappedDisplay MapAt(DisplayDescriptor source, double worldLeft, double worldTop)
    {
        var bounds = new WorldRectD(
            worldLeft,
            worldTop,
            worldLeft + source.Bounds.Width / source.Scale,
            worldTop + source.Bounds.Height / source.Scale);
        var working = new WorldRectD(
            worldLeft + (source.WorkingArea.Left - source.Bounds.Left) / source.Scale,
            worldTop + (source.WorkingArea.Top - source.Bounds.Top) / source.Scale,
            worldLeft + (source.WorkingArea.Right - source.Bounds.Left) / source.Scale,
            worldTop + (source.WorkingArea.Bottom - source.Bounds.Top) / source.Scale);
        return new MappedDisplay(
            source.Id,
            source.Bounds,
            source.WorkingArea,
            bounds,
            working,
            source.Scale,
            source.IsPrimary);
    }

    private static WorldPoint PlaceRelative(DisplayDescriptor source, MappedDisplay anchor)
    {
        var target = source.Bounds;
        var reference = anchor.DeviceBounds;
        var horizontalOverlap = Math.Min(target.Right, reference.Right) -
                                Math.Max(target.Left, reference.Left);
        var verticalOverlap = Math.Min(target.Bottom, reference.Bottom) -
                              Math.Max(target.Top, reference.Top);
        var averageScale = (source.Scale + anchor.Scale) * 0.5d;

        if (target.Left >= reference.Right && verticalOverlap > 0d)
        {
            return new WorldPoint(
                anchor.WorldBounds.Right + (target.Left - reference.Right) / averageScale,
                anchor.WorldBounds.Top + (target.Top - reference.Top) / anchor.Scale);
        }
        if (target.Right <= reference.Left && verticalOverlap > 0d)
        {
            var right = anchor.WorldBounds.Left -
                        (reference.Left - target.Right) / averageScale;
            return new WorldPoint(
                right - target.Width / source.Scale,
                anchor.WorldBounds.Top + (target.Top - reference.Top) / anchor.Scale);
        }
        if (target.Top >= reference.Bottom && horizontalOverlap > 0d)
        {
            return new WorldPoint(
                anchor.WorldBounds.Left + (target.Left - reference.Left) / anchor.Scale,
                anchor.WorldBounds.Bottom + (target.Top - reference.Bottom) / averageScale);
        }
        if (target.Bottom <= reference.Top && horizontalOverlap > 0d)
        {
            var bottom = anchor.WorldBounds.Top -
                         (reference.Top - target.Bottom) / averageScale;
            return new WorldPoint(
                anchor.WorldBounds.Left + (target.Left - reference.Left) / anchor.Scale,
                bottom - target.Height / source.Scale);
        }

        var deltaX = target.CenterX - reference.CenterX;
        var deltaY = target.CenterY - reference.CenterY;
        if (Math.Abs(deltaX) >= Math.Abs(deltaY))
        {
            return deltaX >= 0d
                ? new WorldPoint(anchor.WorldBounds.Right, anchor.WorldBounds.Top)
                : new WorldPoint(
                    anchor.WorldBounds.Left - target.Width / source.Scale,
                    anchor.WorldBounds.Top);
        }
        return deltaY >= 0d
            ? new WorldPoint(anchor.WorldBounds.Left, anchor.WorldBounds.Bottom)
            : new WorldPoint(
                anchor.WorldBounds.Left,
                anchor.WorldBounds.Top - target.Height / source.Scale);
    }

    private static double RectDistanceSquared(DeviceRect a, DeviceRect b)
    {
        var dx = Math.Max(0d, Math.Max(a.Left - b.Right, b.Left - a.Right));
        var dy = Math.Max(0d, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom));
        return dx * dx + dy * dy;
    }

    private static double DistanceSquared(DevicePoint point, DeviceRect rect)
    {
        var dx = Math.Max(0d, Math.Max(rect.Left - point.X, point.X - rect.Right));
        var dy = Math.Max(0d, Math.Max(rect.Top - point.Y, point.Y - rect.Bottom));
        return dx * dx + dy * dy;
    }

    private static double DistanceSquared(WorldPoint point, WorldRectD rect)
    {
        var dx = Math.Max(0d, Math.Max(rect.Left - point.X, point.X - rect.Right));
        var dy = Math.Max(0d, Math.Max(rect.Top - point.Y, point.Y - rect.Bottom));
        return dx * dx + dy * dy;
    }
}

public readonly record struct SurfacePlacement(int X, int Y, int Width, int Height)
{
    public static SurfacePlacement FromCenter(
        DisplayTopology topology,
        WorldPoint center,
        WorldSize sizeWorld)
    {
        ArgumentNullException.ThrowIfNull(topology);
        var display = topology.FindByWorldPoint(center);
        return FromCenter(display, center, sizeWorld);
    }

    public static SurfacePlacement FromCenter(
        MappedDisplay display,
        WorldPoint center,
        WorldSize sizeWorld) =>
        FromCenter(display, center, sizeWorld.Width, sizeWorld.Height);

    public static SurfacePlacement FromCenter(
        DisplayTopology topology,
        WorldPoint center,
        double widthWorld,
        double heightWorld)
    {
        ArgumentNullException.ThrowIfNull(topology);
        if (!double.IsFinite(widthWorld) || widthWorld <= 0d ||
            !double.IsFinite(heightWorld) || heightWorld <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(widthWorld));
        }

        var display = topology.FindByWorldPoint(center);
        return FromCenter(display, center, widthWorld, heightWorld);
    }

    public static SurfacePlacement FromCenter(
        MappedDisplay display,
        WorldPoint center,
        double widthWorld,
        double heightWorld)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (!double.IsFinite(widthWorld) || widthWorld <= 0d ||
            !double.IsFinite(heightWorld) || heightWorld <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(widthWorld));
        }

        var leftTop = display.WorldToDevice(new WorldPoint(
            center.X - widthWorld * 0.5d,
            center.Y - heightWorld * 0.5d));
        var rightBottom = display.WorldToDevice(new WorldPoint(
            center.X + widthWorld * 0.5d,
            center.Y + heightWorld * 0.5d));
        var left = checked((int)Math.Floor(Math.Min(leftTop.X, rightBottom.X)));
        var top = checked((int)Math.Floor(Math.Min(leftTop.Y, rightBottom.Y)));
        var right = checked((int)Math.Ceiling(Math.Max(leftTop.X, rightBottom.X)));
        var bottom = checked((int)Math.Ceiling(Math.Max(leftTop.Y, rightBottom.Y)));
        return new SurfacePlacement(left, top, right - left, bottom - top);
    }
}

/// <summary>
/// Owns the logical coordinate frame currently used by a desktop pet. Pointer
/// samples are interpreted in that frame until an explicit screen transition.
/// On transition, the tracked world point is rebased through device space so
/// its on-screen position cannot jump when the two displays use different DPI.
/// </summary>
public sealed class ActiveDisplaySpace
{
    public DisplayTopology Topology { get; private set; }
    public MappedDisplay Display { get; private set; }

    public ActiveDisplaySpace(DisplayTopology topology, MappedDisplay display)
    {
        Topology = topology ?? throw new ArgumentNullException(nameof(topology));
        ArgumentNullException.ThrowIfNull(display);
        Display = topology.FindById(display.Id) ??
                  throw new ArgumentException(
                      "The active display must belong to the supplied topology.",
                      nameof(display));
    }

    public ActiveDisplaySpace(DisplayTopology topology, DevicePoint initialDevicePoint)
        : this(topology, topology?.FindByDevicePoint(initialDevicePoint) ??
                         throw new ArgumentNullException(nameof(topology)))
    {
    }

    /// <summary>
    /// Maps through the active display even when the pointer is temporarily on
    /// another display. This keeps chase distances continuous at a DPI seam.
    /// </summary>
    public WorldPoint DeviceToWorld(DevicePoint point) => Display.DeviceToWorld(point);

    public DevicePoint WorldToDevice(WorldPoint point) => Display.WorldToDevice(point);

    public DisplaySpaceTransition SwitchForDevicePoint(
        DevicePoint devicePoint,
        WorldPoint trackedWorldPoint)
    {
        var target = Topology.FindByDevicePoint(devicePoint);
        if (string.Equals(target.Id, Display.Id, StringComparison.Ordinal))
        {
            return new DisplaySpaceTransition(
                false,
                Display,
                Display,
                trackedWorldPoint);
        }

        var previous = Display;
        var trackedDevicePoint = previous.WorldToDevice(trackedWorldPoint);
        Display = target;
        return new DisplaySpaceTransition(
            true,
            previous,
            target,
            target.DeviceToWorld(trackedDevicePoint));
    }

    public DisplaySpaceTransition ReplaceTopology(
        DisplayTopology topology,
        WorldPoint trackedWorldPoint)
    {
        ArgumentNullException.ThrowIfNull(topology);
        var previous = Display;
        var trackedDevicePoint = previous.WorldToDevice(trackedWorldPoint);
        var replacement = topology.FindById(previous.Id) ?? topology.Primary;
        Topology = topology;
        Display = replacement;
        return new DisplaySpaceTransition(
            !string.Equals(previous.Id, replacement.Id, StringComparison.Ordinal) ||
            Math.Abs(previous.Scale - replacement.Scale) > 0.000001d ||
            previous.DeviceBounds != replacement.DeviceBounds,
            previous,
            replacement,
            replacement.DeviceToWorld(trackedDevicePoint));
    }
}

public readonly record struct DisplaySpaceTransition(
    bool Changed,
    MappedDisplay Previous,
    MappedDisplay Current,
    WorldPoint RebasedWorldPoint);
