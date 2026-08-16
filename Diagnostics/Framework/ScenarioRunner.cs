using DesktopPet.Engine;
using DesktopLizard.Core;

namespace DesktopLizard.Diagnostics.Framework;

internal readonly record struct ScenarioStep(
    int StepIndex,
    int SubstepIndex,
    float DeltaTime,
    float ElapsedTime);

internal interface IScenarioProbe
{
    void Observe(in ScenarioStep step);
}

internal readonly record struct ScenarioRunSummary(
    int StepCount,
    float SimulatedTime,
    float MaximumStep);

/// <summary>
/// Runs deterministic diagnostic steps and notifies optional streaming probes.
/// It intentionally keeps no model-specific state so behavior, pose and full
/// application-session scenarios can share the same execution contract.
/// </summary>
internal static class ScenarioRunner
{
    public static ScenarioRunSummary RunSteps(
        int stepCount,
        float stepDelta,
        Action<ScenarioStep> advanceStep,
        params IScenarioProbe[] probes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stepCount);
        ArgumentNullException.ThrowIfNull(advanceStep);
        probes ??= [];

        var elapsed = 0f;
        for (var stepIndex = 0; stepIndex < stepCount; stepIndex++)
        {
            elapsed += stepDelta;
            var step = new ScenarioStep(stepIndex, 0, stepDelta, elapsed);
            advanceStep(step);
            Notify(probes, in step);
        }

        return new ScenarioRunSummary(
            stepCount,
            elapsed,
            stepCount > 0 ? stepDelta : 0f);
    }

    /// <summary>
    /// Runs at most <paramref name="maximumStepCount"/> deterministic steps,
    /// stopping after the first step whose callback returns <see langword="false"/>.
    /// This keeps state-dependent diagnostic loops on the same stepping contract
    /// without forcing a scenario to know its exact duration in advance.
    /// </summary>
    public static ScenarioRunSummary RunUntil(
        int maximumStepCount,
        float stepDelta,
        Func<ScenarioStep, bool> advanceStep,
        params IScenarioProbe[] probes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumStepCount);
        ArgumentNullException.ThrowIfNull(advanceStep);
        probes ??= [];

        var elapsed = 0f;
        var completedSteps = 0;
        for (var stepIndex = 0; stepIndex < maximumStepCount; stepIndex++)
        {
            elapsed += stepDelta;
            var step = new ScenarioStep(stepIndex, 0, stepDelta, elapsed);
            var shouldContinue = advanceStep(step);
            Notify(probes, in step);
            completedSteps++;
            if (!shouldContinue)
            {
                break;
            }
        }

        return new ScenarioRunSummary(
            completedSteps,
            elapsed,
            completedSteps > 0 ? stepDelta : 0f);
    }

    public static ScenarioRunSummary RunFixedFrame(
        float frameDelta,
        Action<ScenarioStep>? advanceStep = null,
        params IScenarioProbe[] probes)
    {
        probes ??= [];
        var runtime = LizardProfile.Default.Runtime;
        var simulationStep = 1f / runtime.SimulationRate;
        var clampedFrameDelta = FixedStepRunner.ClampFrameDelta(
            frameDelta,
            runtime.MaximumFrameCatchUp);
        var stepCount = 0;
        var elapsed = 0f;
        var maximumStep = 0f;

        var runner = new FixedStepRunner();
        runner.Advance(
            clampedFrameDelta,
            simulationStep,
            runtime.MaximumFrameCatchUp,
            (stepDelta, _) =>
        {
            elapsed += stepDelta;
            maximumStep = Math.Max(maximumStep, stepDelta);
            var stepIndex = stepCount++;
            var step = new ScenarioStep(stepIndex, stepIndex, stepDelta, elapsed);
            advanceStep?.Invoke(step);
            Notify(probes, in step);
        });

        return new ScenarioRunSummary(stepCount, elapsed, maximumStep);
    }

    private static void Notify(IScenarioProbe[] probes, in ScenarioStep step)
    {
        foreach (var probe in probes)
        {
            probe.Observe(in step);
        }
    }
}
