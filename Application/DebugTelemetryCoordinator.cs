using System.Collections.Immutable;
using System.Numerics;
using DesktopLizard.Core;

namespace DesktopLizard.AppRuntime;

internal readonly record struct DebugTelemetryFrameInput(
    float FrameDelta,
    float DpiScale,
    float ModelToViewScale,
    PointerObservation Pointer);

/// <summary>
/// Collects render-independent debug telemetry and produces the immutable
/// frame consumed by the overlay and its side panel. Window visibility,
/// placement and click-through policy deliberately remain in PetWindow.
/// </summary>
internal sealed class DebugTelemetryCoordinator
{
    private readonly RuntimeConfiguration _runtime;
    private readonly IndividualTraits _individual;
    private readonly float _canvasCenter;
    private readonly Queue<Vector2> _worldPath;
    private float _pathSampleTimer;
    private float _framesPerSecond;
    private long _frameId;

    public DebugTelemetryCoordinator(LizardProfile? profile = null)
    {
        var resolved = profile ?? LizardProfile.Default;
        _runtime = resolved.Runtime;
        _individual = resolved.Traits;
        _canvasCenter = resolved.Appearance.RenderCanvasSize * 0.5f;
        _worldPath = new Queue<Vector2>(_runtime.DebugPathCapacity);
    }

    public DebugFrameSnapshot Capture(
        DebugTelemetryFrameInput input,
        in BehaviorDebugSnapshot behavior,
        LizardDebugSnapshot lizard)
    {
        var behaviorPosition = behavior.Position;
        if (input.FrameDelta > 0.000001f)
        {
            var instantaneousFps = Math.Min(_runtime.DebugMaximumFps, 1f / input.FrameDelta);
            _framesPerSecond = _framesPerSecond <= 0f
                ? instantaneousFps
                : MathEx.Lerp(
                    _framesPerSecond,
                    instantaneousFps,
                    MathEx.ExpLerpFactor(_runtime.DebugFpsResponse, input.FrameDelta));
        }

        _pathSampleTimer += input.FrameDelta;
        var shouldSample = _worldPath.Count == 0;
        if (!shouldSample && _pathSampleTimer >= _runtime.DebugPathSampleInterval)
        {
            shouldSample = Vector2.DistanceSquared(_worldPath.Last(), behaviorPosition) >=
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
            input.DpiScale * input.ModelToViewScale);
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
            WorldToModel(input.Pointer.IsAvailable ? input.Pointer.Position : behaviorPosition),
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
}
