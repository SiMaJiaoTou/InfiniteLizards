using System.Collections.Immutable;

namespace InfiniteLizards.Desktop;

/// <summary>
/// A pose and only the raster scales at which that pose may still exist in the
/// compositor backing surface. Keeping this association prevents the native
/// region from producing a geometry-by-scale Cartesian product.
/// </summary>
internal sealed record DesktopPetRasterizedInputRegionPose
{
    public DesktopPetRasterizedInputRegionPose(
        DesktopPetInputRegion region,
        long version,
        bool isAcknowledged,
        ImmutableArray<double> rasterScales)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }
        if (rasterScales.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "At least one possible raster scale is required.",
                nameof(rasterScales));
        }
        foreach (var scale in rasterScales)
        {
            DesktopPetRasterScaleSet.RequireValid(scale, nameof(rasterScales));
        }

        Region = region;
        Version = version;
        IsAcknowledged = isAcknowledged;
        RasterScales = DesktopPetRasterScaleSet.Distinct(rasterScales);
    }

    public DesktopPetInputRegion Region { get; }

    public long Version { get; }

    public bool IsAcknowledged { get; }

    public ImmutableArray<double> RasterScales { get; }
}

/// <summary>
/// Immutable native-shape request. Poses is ordered by presenter version and
/// contains at most one acknowledged pose followed by unacknowledged poses.
/// </summary>
internal sealed record DesktopPetInputRegionInstallation
{
    public DesktopPetInputRegionInstallation(
        long revision,
        ImmutableArray<DesktopPetRasterizedInputRegionPose> poses)
    {
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }
        if (poses.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "At least one rasterized input-region pose is required.",
                nameof(poses));
        }

        var previousVersion = -1L;
        var acknowledgedCount = 0;
        for (var index = 0; index < poses.Length; index++)
        {
            var pose = poses[index] ?? throw new ArgumentException(
                "Rasterized pose entries cannot be null.",
                nameof(poses));
            if (pose.Version <= previousVersion)
            {
                throw new ArgumentException(
                    "Rasterized poses must have strictly increasing versions.",
                    nameof(poses));
            }
            if (pose.IsAcknowledged)
            {
                acknowledgedCount++;
                if (index != 0 || acknowledgedCount > 1)
                {
                    throw new ArgumentException(
                        "Only the oldest retained pose may be acknowledged.",
                        nameof(poses));
                }
            }
            previousVersion = pose.Version;
        }

        Revision = revision;
        Poses = poses;
    }

    public long Revision { get; }

    public ImmutableArray<DesktopPetRasterizedInputRegionPose> Poses { get; }
}

/// <summary>
/// A bounded two-phase transaction between retained-scene submission and the
/// native HWND shape. Before a compositor acknowledgement, Current is the
/// union of the last known rasterized pose and every pose that could still be
/// rendered. Acknowledging a newer pose atomically retires older geometry.
/// </summary>
internal sealed class DesktopPetInputRegionTransaction
{
    public const int DefaultMaximumPendingPoses = 8;

    private readonly int _maximumPendingPoses;
    private readonly SortedDictionary<long, PendingPose> _pending = [];
    private PendingPose? _committed;
    private OrphanAcknowledgement? _orphanAcknowledgement;
    private long _latestSubmittedVersion = -1;
    private long _lastAcknowledgedSequence;
    private long _revision;

    public DesktopPetInputRegionTransaction(
        int maximumPendingPoses = DefaultMaximumPendingPoses)
    {
        if (maximumPendingPoses <= 0 || maximumPendingPoses > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPendingPoses));
        }

        _maximumPendingPoses = maximumPendingPoses;
    }

    public bool CanSubmitPose => _pending.Count < _maximumPendingPoses;

    public bool HasInstallation => _committed is not null || _pending.Count > 0;

    public long LatestSubmittedVersion => _latestSubmittedVersion;

    public int PendingPoseCount => _pending.Count;

    public DesktopPetInputRegionInstallation Stage(
        long version,
        DesktopPetInputRegion region,
        double submissionRenderScale,
        double targetDisplayScale)
    {
        ArgumentNullException.ThrowIfNull(region);
        DesktopPetRasterScaleSet.RequireValid(
            submissionRenderScale,
            nameof(submissionRenderScale));
        DesktopPetRasterScaleSet.RequireValid(
            targetDisplayScale,
            nameof(targetDisplayScale));
        if (version < 0 || version <= _latestSubmittedVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                "Presenter pose versions must increase monotonically.");
        }
        if (!CanSubmitPose)
        {
            throw new InvalidOperationException(
                "The compositor acknowledgement backlog is full.");
        }

        var possibleScales = DesktopPetRasterScaleSet.Distinct(
            [submissionRenderScale, targetDisplayScale]);
        if (_orphanAcknowledgement is { } orphan)
        {
            if (orphan.Version > version)
            {
                throw new InvalidOperationException(
                    "A newer compositor pose was acknowledged before an older " +
                    "presenter version could be staged.");
            }
            if (orphan.Version == version)
            {
                _committed = new PendingPose(
                    version,
                    region,
                    ImmutableArray.Create(orphan.RenderScale));
                _orphanAcknowledgement = null;
                _latestSubmittedVersion = version;
                _revision = checked(_revision + 1);
                return BuildInstallation();
            }

            _orphanAcknowledgement = null;
        }

        _pending.Add(version, new PendingPose(version, region, possibleScales));
        _latestSubmittedVersion = version;
        _revision = checked(_revision + 1);
        return BuildInstallation();
    }

    public bool TryAcknowledge(
        long sequence,
        long version,
        double renderScale,
        out DesktopPetInputRegionInstallation installation)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }
        DesktopPetRasterScaleSet.RequireValid(renderScale, nameof(renderScale));
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        if (sequence <= _lastAcknowledgedSequence)
        {
            installation = HasInstallation
                ? BuildInstallation()
                : null!;
            return false;
        }

        if (_pending.TryGetValue(version, out var acknowledged))
        {
            _committed = acknowledged with
            {
                RasterScales = ImmutableArray.Create(renderScale)
            };
            foreach (var retiredVersion in _pending.Keys
                         .TakeWhile(candidate => candidate <= version)
                         .ToArray())
            {
                _pending.Remove(retiredVersion);
            }
            _lastAcknowledgedSequence = sequence;
            _revision = checked(_revision + 1);
            installation = BuildInstallation();
            return true;
        }

        if (_committed is { } committed && committed.Version == version)
        {
            if (committed.RasterScales.Length == 1 &&
                BitConverter.DoubleToInt64Bits(committed.RasterScales[0]) ==
                BitConverter.DoubleToInt64Bits(renderScale))
            {
                _lastAcknowledgedSequence = sequence;
                installation = BuildInstallation();
                return false;
            }

            _committed = committed with
            {
                RasterScales = ImmutableArray.Create(renderScale)
            };
            _lastAcknowledgedSequence = sequence;
            _revision = checked(_revision + 1);
            installation = BuildInstallation();
            return true;
        }

        // Capture can fail after Present/Render but before Stage. Preserve the
        // highest such acknowledgement so a same-version retry can immediately
        // become committed; the fence tracker correctly suppresses a duplicate
        // marker for an already-rendered token.
        if (!HasInstallation)
        {
            _orphanAcknowledgement = new OrphanAcknowledgement(
                sequence,
                version,
                renderScale);
            _lastAcknowledgedSequence = sequence;
            installation = null!;
            return false;
        }

        // A skipped retained-scene pose can never be acknowledged, while an
        // old acknowledgement may arrive after a newer compositor batch. Both
        // cases are harmless and must not regress the committed shape.
        if (_committed is { } current && version < current.Version)
        {
            _lastAcknowledgedSequence = sequence;
        }
        installation = HasInstallation
            ? BuildInstallation()
            : null!;
        return false;
    }

    public DesktopPetInputRegionInstallation Current => HasInstallation
        ? BuildInstallation()
        : throw new InvalidOperationException("No input-region pose has been staged.");

    public void Reset()
    {
        if (_committed is { } committed &&
            _lastAcknowledgedSequence > 0 &&
            committed.RasterScales.Length == 1)
        {
            _orphanAcknowledgement = new OrphanAcknowledgement(
                _lastAcknowledgedSequence,
                committed.Version,
                committed.RasterScales[0]);
        }
        _pending.Clear();
        _committed = null;
        _latestSubmittedVersion = -1;
        _revision = 0;
    }

    private DesktopPetInputRegionInstallation BuildInstallation()
    {
        var poses = ImmutableArray.CreateBuilder<DesktopPetRasterizedInputRegionPose>(
            _pending.Count + (_committed is null ? 0 : 1));
        if (_committed is { } committed)
        {
            poses.Add(ToInstallationPose(committed, isAcknowledged: true));
        }
        foreach (var pending in _pending.Values)
        {
            poses.Add(ToInstallationPose(pending, isAcknowledged: false));
        }
        if (poses.Count == 0)
        {
            throw new InvalidOperationException("No input-region pose has been staged.");
        }

        return new DesktopPetInputRegionInstallation(
            _revision,
            poses.MoveToImmutable());
    }

    private static DesktopPetRasterizedInputRegionPose ToInstallationPose(
        PendingPose pose,
        bool isAcknowledged) => new(
        pose.Region,
        pose.Version,
        isAcknowledged,
        pose.RasterScales);

    private sealed record PendingPose(
        long Version,
        DesktopPetInputRegion Region,
        ImmutableArray<double> RasterScales);

    private sealed record OrphanAcknowledgement(
        long Sequence,
        long Version,
        double RenderScale);
}

internal static class DesktopPetRasterScaleSet
{
    public static ImmutableArray<double> Distinct(IEnumerable<double> scales)
    {
        ArgumentNullException.ThrowIfNull(scales);
        var seen = new HashSet<long>();
        var result = ImmutableArray.CreateBuilder<double>();
        foreach (var scale in scales)
        {
            RequireValid(scale, nameof(scales));
            if (seen.Add(BitConverter.DoubleToInt64Bits(scale)))
            {
                result.Add(scale);
            }
        }

        if (result.Count == 0)
        {
            throw new ArgumentException(
                "At least one finite positive scale is required.",
                nameof(scales));
        }
        return result.ToImmutable();
    }

    public static void RequireValid(double scale, string parameterName)
    {
        if (!double.IsFinite(scale) || scale <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Raster scales must be finite and positive.");
        }
    }
}

internal readonly record struct DesktopPetRenderedPoseToken(
    long Version,
    double RenderScale)
{
    public DesktopPetRenderedPoseToken EnsureValid()
    {
        if (Version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Version));
        }
        DesktopPetRasterScaleSet.RequireValid(RenderScale, nameof(RenderScale));
        return this;
    }
}

/// <summary>
/// Coalesces Render callbacks before a Loaded-priority compositor marker. A
/// queued callback always fences the latest Render that actually completed,
/// never the older pose that happened to enqueue the dispatcher operation.
/// </summary>
internal sealed class DesktopPetRenderFenceCoalescer
{
    private DesktopPetRenderedPoseToken? _latestRendered;
    private bool _dispatchPending;

    public bool ObserveRender(DesktopPetRenderedPoseToken token)
    {
        token.EnsureValid();
        if (_latestRendered is { } latest && token.Version < latest.Version)
        {
            throw new InvalidOperationException(
                "Rendered presenter versions cannot regress.");
        }

        _latestRendered = token;
        if (_dispatchPending)
        {
            return false;
        }

        _dispatchPending = true;
        return true;
    }

    public DesktopPetRenderedPoseToken TakeLatestForFence()
    {
        if (!_dispatchPending || _latestRendered is not { } latest)
        {
            throw new InvalidOperationException(
                "No rendered pose is waiting for a compositor fence.");
        }

        _dispatchPending = false;
        return latest;
    }

    public void CancelPendingDispatch() => _dispatchPending = false;
}

internal sealed class DesktopPetRenderFenceAttemptTracker
{
    private DesktopPetRenderedPoseToken? _inFlight;
    private DesktopPetRenderedPoseToken? _deferredLatest;
    private long _sequence;

    public int InFlightCount => _inFlight is null ? 0 : 1;

    public bool HasDeferredToken => _deferredLatest is not null;

    public bool TryBegin(
        DesktopPetRenderedPoseToken token,
        out long sequence)
    {
        token.EnsureValid();
        if (_inFlight is { } inFlight)
        {
            if (token.Version < inFlight.Version)
            {
                throw new InvalidOperationException(
                    "Rendered presenter versions cannot regress behind an in-flight fence.");
            }

            if (token == inFlight)
            {
                // Every Render observation is authoritative. A same-version
                // DPI round-trip A -> B -> A means B is no longer the latest
                // retained raster state and must not be fenced afterward.
                _deferredLatest = null;
            }
            else
            {
                _deferredLatest = token;
            }

            sequence = 0;
            return false;
        }

        _inFlight = token;
        sequence = checked(++_sequence);
        return true;
    }

    /// <summary>
    /// Releases the single in-flight marker and returns only the newest Render
    /// observed while it was pending. The caller can begin that token after it
    /// has published success or handled failure, keeping compositor tasks
    /// strictly bounded to one.
    /// </summary>
    public bool TryFinish(
        DesktopPetRenderedPoseToken token,
        long sequence,
        out DesktopPetRenderedPoseToken? deferredLatest)
    {
        token.EnsureValid();
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }
        if (sequence != _sequence || _inFlight != token)
        {
            deferredLatest = null;
            return false;
        }

        _inFlight = null;
        deferredLatest = _deferredLatest;
        _deferredLatest = null;
        return true;
    }
}

internal static class DesktopPetPresentationSubmissionPolicy
{
    public static bool CanSubmit(
        DesktopPetInputRegionTransaction? transaction,
        bool hasAttemptedInputRegion,
        bool lastInputRegionAttemptSucceeded) =>
        (!hasAttemptedInputRegion || lastInputRegionAttemptSucceeded) &&
        (transaction?.CanSubmitPose ?? true);
}

internal sealed class DesktopPetRenderCommitNotifier
{
    private EventHandler<DesktopPetRenderCommitAcknowledgedEventArgs>? _handlers;

    public event EventHandler<DesktopPetRenderCommitAcknowledgedEventArgs>?
        RenderCommitAcknowledged
    {
        add => _handlers += value;
        remove => _handlers -= value;
    }

    public bool HasSubscribers => _handlers is not null;

    public void Publish(long sequence, DesktopPetRenderedPoseToken token)
    {
        token.EnsureValid();
        _handlers?.Invoke(
            this,
            new DesktopPetRenderCommitAcknowledgedEventArgs(
                sequence,
                token.Version,
                token.RenderScale));
    }
}
