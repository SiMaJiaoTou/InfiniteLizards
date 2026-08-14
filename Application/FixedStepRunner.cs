namespace DesktopLizard.AppRuntime;

/// <summary>
/// Accumulates display-frame time and advances a model only in complete fixed
/// simulation steps. A runner belongs to one simulation timeline; callers must
/// not share an instance between independent sessions.
/// </summary>
internal sealed class FixedStepRunner
{
    private double _accumulatedTime;

    public double AccumulatedTime => _accumulatedTime;

    public static float ClampFrameDelta(float frameDelta, float maximumFrameCatchUp)
    {
        if (!float.IsFinite(maximumFrameCatchUp) || maximumFrameCatchUp <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFrameCatchUp),
                "The frame catch-up limit must be finite and positive.");
        }

        // A non-finite display timestamp must not contaminate the persistent
        // accumulator. Treat it as a frame that supplied no elapsed time.
        return float.IsFinite(frameDelta)
            ? Math.Clamp(frameDelta, 0f, maximumFrameCatchUp)
            : 0f;
    }

    /// <summary>
    /// Adds one display-frame delta to this timeline and invokes
    /// <paramref name="advanceStep"/> once per complete simulation step. The
    /// callback's second argument is the number of steps still to be consumed,
    /// including the current one; this lets frame-scoped external input be
    /// spread deterministically across all substeps.
    /// </summary>
    public FixedStepAdvanceResult Advance(
        float frameDelta,
        float simulationStep,
        float maximumFrameCatchUp,
        Action<float, int> advanceStep)
    {
        ArgumentNullException.ThrowIfNull(advanceStep);
        ValidateSimulationStep(simulationStep);

        var acceptedFrameDelta = ClampFrameDelta(frameDelta, maximumFrameCatchUp);
        _accumulatedTime += acceptedFrameDelta;

        var step = (double)simulationStep;
        // Float configuration values such as 1/120 cannot represent 0.25 / 30
        // exactly. This scale-relative tolerance recognizes that boundary
        // without allowing a materially early step.
        var boundaryTolerance = Math.Max(1e-12, step * 1e-5);
        var stepCount = (int)Math.Floor((_accumulatedTime + boundaryTolerance) / step);
        var simulatedTime = 0d;

        for (var stepIndex = 0; stepIndex < stepCount; stepIndex++)
        {
            _accumulatedTime -= step;
            if (_accumulatedTime < 0d && _accumulatedTime >= -boundaryTolerance)
            {
                _accumulatedTime = 0d;
            }

            advanceStep(simulationStep, stepCount - stepIndex);
            simulatedTime += step;
        }

        return new FixedStepAdvanceResult(
            acceptedFrameDelta,
            (float)simulatedTime,
            stepCount,
            _accumulatedTime);
    }

    public void Reset() => _accumulatedTime = 0d;

    private static void ValidateSimulationStep(float simulationStep)
    {
        if (!float.IsFinite(simulationStep) || simulationStep <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(simulationStep),
                "The fixed simulation step must be finite and positive.");
        }
    }
}

internal readonly record struct FixedStepAdvanceResult(
    float FrameDelta,
    float SimulationDelta,
    int StepCount,
    double AccumulatedTime);
