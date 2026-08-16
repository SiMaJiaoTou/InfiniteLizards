namespace DesktopPet.Engine;

/// <summary>
/// Display-rate-independent fixed-step accumulator. It is generic engine
/// infrastructure and contains no gameplay or platform knowledge.
/// </summary>
public sealed class FixedStepRunner
{
    public const int MaximumStepsPerAdvance = 2048;

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

        return float.IsFinite(frameDelta)
            ? Math.Clamp(frameDelta, 0f, maximumFrameCatchUp)
            : 0f;
    }

    public FixedStepAdvanceResult Advance(
        float frameDelta,
        float simulationStep,
        float maximumFrameCatchUp,
        Action<float, int> advanceStep)
    {
        ArgumentNullException.ThrowIfNull(advanceStep);
        if (!float.IsFinite(simulationStep) || simulationStep <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(simulationStep),
                "The fixed simulation step must be finite and positive.");
        }

        var acceptedFrameDelta = ClampFrameDelta(frameDelta, maximumFrameCatchUp);
        var pendingTime = _accumulatedTime + acceptedFrameDelta;
        var step = (double)simulationStep;
        var boundaryTolerance = Math.Max(1e-12, step * 1e-5);
        var requiredSteps = Math.Floor((pendingTime + boundaryTolerance) / step);
        if (!double.IsFinite(requiredSteps) ||
            requiredSteps > MaximumStepsPerAdvance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(simulationStep),
                $"One advance cannot execute more than {MaximumStepsPerAdvance} fixed steps.");
        }

        var stepCount = checked((int)requiredSteps);
        _accumulatedTime = pendingTime;
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
}

public readonly record struct FixedStepAdvanceResult(
    float FrameDelta,
    float SimulationDelta,
    int StepCount,
    double AccumulatedTime);
