using System.Numerics;
using Avalonia;
using DesktopPet.Engine;

namespace InfiniteLizards.Desktop;

internal static class DesktopPetSurfaceCoordinateMapper
{
    public static Vector2 RebaseCenter(
        MappedDisplay previous,
        MappedDisplay current,
        Vector2 center)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(center));
        }

        var device = previous.WorldToDevice(new WorldPoint(center.X, center.Y));
        var rebased = current.DeviceToWorld(device);
        return new Vector2((float)rebased.X, (float)rebased.Y);
    }

    public static Point DeviceToView(
        DesktopPetSurfacePresentation presentation,
        DevicePoint devicePoint)
    {
        if (!double.IsFinite(devicePoint.X) || !double.IsFinite(devicePoint.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(devicePoint));
        }

        return new Point(
            (devicePoint.X - presentation.Placement.X) /
                presentation.DisplayScale,
            (devicePoint.Y - presentation.Placement.Y) /
                presentation.DisplayScale);
    }
}

/// <summary>
/// The native surface placement that belongs to one presenter pose.  The
/// center is retained in canonical world units for pointer and diagnostic
/// calculations, while Placement/DisplayScale are the exact native request
/// captured when that pose was submitted.
/// </summary>
internal readonly record struct DesktopPetSurfacePresentation
{
    public DesktopPetSurfacePresentation(
        Vector2 center,
        SurfacePlacement placement,
        double displayScale)
    {
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(center));
        }
        if (placement.Width <= 0 || placement.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(placement));
        }
        if (!double.IsFinite(displayScale) || displayScale <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(displayScale));
        }

        Center = center;
        Placement = placement;
        DisplayScale = displayScale;
    }

    public Vector2 Center { get; }

    public SurfacePlacement Placement { get; }

    public double DisplayScale { get; }
}

/// <summary>
/// Binds presenter versions to native placements until Avalonia acknowledges
/// that the matching retained pose reached the compositor.  A newer
/// acknowledgement may retire coalesced intermediate versions, but neither a
/// late sequence nor a discarded generation can move the native window back.
/// </summary>
internal sealed class DesktopPetSurfacePlacementTransaction
{
    public const int DefaultMaximumPendingPresentations = 8;

    private readonly int _maximumPendingPresentations;
    private readonly SortedDictionary<long, DesktopPetSurfacePresentation> _pending = [];
    private DesktopPetSurfacePresentation? _committed;
    private long _latestSubmittedVersion = -1;
    private long _latestCommittedVersion = -1;
    private long _lastAcknowledgedSequence;
    private bool _isClosed;

    public DesktopPetSurfacePlacementTransaction(
        int maximumPendingPresentations = DefaultMaximumPendingPresentations)
    {
        if (maximumPendingPresentations <= 0 || maximumPendingPresentations > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPendingPresentations));
        }

        _maximumPendingPresentations = maximumPendingPresentations;
    }

    public bool CanSubmitPresentation =>
        !_isClosed && _pending.Count < _maximumPendingPresentations;

    public bool HasCommittedPresentation => _committed is not null;

    public int PendingPresentationCount => _pending.Count;

    public long LatestSubmittedVersion => _latestSubmittedVersion;

    public long LatestCommittedVersion => _latestCommittedVersion;

    public DesktopPetSurfacePresentation Committed => _committed ??
        throw new InvalidOperationException(
            "No native surface placement has been initialized.");

    /// <summary>
    /// Establishes an immediately accepted native placement for initial Show
    /// or a coordinate-space discontinuity.  Presenter version monotonicity is
    /// intentionally preserved, while all placements from the old coordinate
    /// generation are made ineligible for later acknowledgements.
    /// </summary>
    public void Initialize(
        long presentationVersion,
        DesktopPetSurfacePresentation presentation)
    {
        ThrowIfClosed();
        if (_committed is not null || presentationVersion < 0)
        {
            throw new InvalidOperationException(
                "The initial surface presentation is already established.");
        }

        _committed = presentation;
        _latestCommittedVersion = presentationVersion;
        _latestSubmittedVersion = presentationVersion;
    }

    public void ResetTo(DesktopPetSurfacePresentation presentation)
    {
        ThrowIfClosed();
        _pending.Clear();
        _committed = presentation;
    }

    /// <summary>
    /// Drops uncommitted automatic-motion poses before direct manipulation.
    /// The already accepted native placement remains the visible baseline.
    /// </summary>
    public void DiscardPending()
    {
        ThrowIfClosed();
        _pending.Clear();
    }

    public void Stage(long version, DesktopPetSurfacePresentation presentation)
    {
        ThrowIfClosed();
        if (version < 0 || version <= _latestSubmittedVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                "Presenter versions must increase monotonically.");
        }
        if (!CanSubmitPresentation)
        {
            throw new InvalidOperationException(
                "The compositor placement acknowledgement backlog is full.");
        }

        _pending.Add(version, presentation);
        _latestSubmittedVersion = version;
    }

    public bool TryAcknowledge(
        long sequence,
        long version,
        Action<DesktopPetSurfacePresentation> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }
        if (_isClosed || sequence <= _lastAcknowledgedSequence)
        {
            return false;
        }

        if (version <= _latestCommittedVersion)
        {
            // A newer fence attempt for an older retained pose is harmless,
            // but remembering its sequence prevents an even older completion
            // from being considered later.
            _lastAcknowledgedSequence = sequence;
            return false;
        }

        if (!_pending.TryGetValue(version, out var presentation))
        {
            // The version belonged to a topology/drag generation that was
            // explicitly discarded, or it was never submitted by this host.
            _lastAcknowledgedSequence = sequence;
            return false;
        }

        // Native placement is the fallible boundary.  Do not claim this pose
        // as visible or retire its retry state until that boundary accepts the
        // exact request.
        apply(presentation);
        _committed = presentation;
        _latestCommittedVersion = version;
        _lastAcknowledgedSequence = sequence;
        foreach (var retiredVersion in _pending.Keys
                     .TakeWhile(candidate => candidate <= version)
                     .ToArray())
        {
            _pending.Remove(retiredVersion);
        }
        return true;
    }

    public void Close()
    {
        _isClosed = true;
        _pending.Clear();
    }

    private void ThrowIfClosed()
    {
        if (_isClosed)
        {
            throw new ObjectDisposedException(
                nameof(DesktopPetSurfacePlacementTransaction));
        }
    }
}
