using System.Collections.Immutable;
using System.Numerics;
using DesktopLizard.Core;

namespace DesktopLizard.AppRuntime;

internal readonly record struct LegacyDebugTelemetryFrameInput(
    float FrameDelta,
    float DpiScale,
    float ModelToScreenScale,
    PointerObservation Pointer);

/// <summary>
/// Compatibility-only WPF telemetry. Display cadence, DPI and conversion to
/// the legacy renderer's model canvas remain outside the gameplay assembly.
/// </summary>
internal sealed class LegacyDebugTelemetryCoordinator
{
    private readonly RuntimeConfiguration _runtime;
    private readonly IndividualTraits _individual;
    private readonly float _canvasCenter;
    private readonly Queue<Vector2> _worldPath;
    private float _pathSampleTimer;
    private float _framesPerSecond;
    private long _frameId;

    public LegacyDebugTelemetryCoordinator(LizardProfile? profile = null)
    {
        var resolved = profile ?? LizardProfile.Default;
        _runtime = resolved.Runtime;
        _individual = resolved.Traits;
        _canvasCenter = resolved.Appearance.RenderCanvasSize * 0.5f;
        _worldPath = new Queue<Vector2>(_runtime.DebugPathCapacity);
    }

    public DebugFrameSnapshot Capture(
        LegacyDebugTelemetryFrameInput input,
        in BehaviorDebugSnapshot behavior,
        LizardDebugSnapshot lizard)
    {
        var behaviorPosition = behavior.Position;
        if (input.FrameDelta > 0.000001f)
        {
            var instantaneousFps = Math.Min(
                _runtime.DebugMaximumFps,
                1f / input.FrameDelta);
            _framesPerSecond = _framesPerSecond <= 0f
                ? instantaneousFps
                : MathEx.Lerp(
                    _framesPerSecond,
                    instantaneousFps,
                    MathEx.ExpLerpFactor(
                        _runtime.DebugFpsResponse,
                        input.FrameDelta));
        }

        _pathSampleTimer += input.FrameDelta;
        var shouldSample = _worldPath.Count == 0;
        if (!shouldSample &&
            _pathSampleTimer >= _runtime.DebugPathSampleInterval)
        {
            shouldSample =
                Vector2.DistanceSquared(_worldPath.Last(), behaviorPosition) >=
                _runtime.DebugPathMinimumDistance *
                _runtime.DebugPathMinimumDistance;
        }
        if (shouldSample)
        {
            _pathSampleTimer = 0f;
            _worldPath.Enqueue(behaviorPosition);
            while (_worldPath.Count > _runtime.DebugPathCapacity)
            {
                _worldPath.Dequeue();
            }
        }

        var worldToModelScale = Math.Max(
            0.0001f,
            input.ModelToScreenScale);
        Vector2 WorldToModel(Vector2 worldPoint)
        {
            if (!float.IsFinite(worldPoint.X) || !float.IsFinite(worldPoint.Y))
            {
                return new Vector2(_canvasCenter);
            }
            return new Vector2(_canvasCenter) +
                   (worldPoint - behaviorPosition) / worldToModelScale;
        }

        var trail = new Vector2[_worldPath.Count];
        var trailIndex = 0;
        foreach (var point in _worldPath)
        {
            trail[trailIndex++] = WorldToModel(point);
        }

        var pointerDistance = input.Pointer.IsAvailable
            ? Vector2.Distance(behaviorPosition, input.Pointer.Position)
            : float.NaN;
        return new DebugFrameSnapshot(
            ++_frameId,
            _individual,
            behavior,
            lizard,
            new Vector2(_canvasCenter),
            trail.ToImmutableArray(),
            WorldToModel(behavior.Target),
            WorldToModel(behavior.LookTarget),
            WorldToModel(
                input.Pointer.IsAvailable
                    ? input.Pointer.Position
                    : behaviorPosition),
            input.Pointer.IsAvailable,
            input.Pointer.IsInteractionBlocked,
            pointerDistance,
            _framesPerSecond,
            input.DpiScale);
    }

    public void ClearPath()
    {
        _worldPath.Clear();
        _pathSampleTimer = 0f;
    }

    public void TranslateWorld(Vector2 delta)
    {
        if (_worldPath.Count == 0 || delta == Vector2.Zero)
        {
            return;
        }

        var translated = new Vector2[_worldPath.Count];
        var index = 0;
        foreach (var point in _worldPath)
        {
            var value = point + delta;
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(delta),
                    "The translated legacy debug path must remain finite.");
            }
            translated[index++] = value;
        }

        _worldPath.Clear();
        foreach (var point in translated)
        {
            _worldPath.Enqueue(point);
        }
    }
}
