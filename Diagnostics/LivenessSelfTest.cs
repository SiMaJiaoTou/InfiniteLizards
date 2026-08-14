using System.Numerics;
using DesktopLizard.AppRuntime;
using DesktopLizard.Core;
using DesktopLizard.Diagnostics.Framework;

namespace DesktopLizard.Diagnostics;

internal readonly record struct LivenessTestResult(
    bool Passed,
    int Samples,
    float LongestActiveFreeze,
    float LongestVisualFreeze,
    float LongestRestChain,
    float MinimumIdleTailRange,
    int WatchdogRecoveries,
    int NonFiniteSamples);

internal static class LivenessSelfTest
{
    private static LizardProfile DefaultProfile => LizardProfile.Default;

    private const string SamplesMetric = "samples";
    private const string ActiveFreezeMetric = "longest_active_freeze";
    private const string VisualFreezeMetric = "longest_visual_freeze";
    private const string RestChainMetric = "longest_rest_chain";
    private const string IdleTailRangeMetric = "minimum_idle_tail_range";
    private const string WatchdogRecoveriesMetric = "watchdog_recoveries";
    private const string NonFiniteSamplesMetric = "non_finite_samples";

    public static LivenessTestResult Run()
    {
        var report = RunReport();
        return new LivenessTestResult(
            report.Passed,
            report.GetMetric(SamplesMetric).AsInt32,
            report.GetMetric(ActiveFreezeMetric).AsSingle,
            report.GetMetric(VisualFreezeMetric).AsSingle,
            report.GetMetric(RestChainMetric).AsSingle,
            report.GetMetric(IdleTailRangeMetric).AsSingle,
            report.GetMetric(WatchdogRecoveriesMetric).AsInt32,
            report.GetMetric(NonFiniteSamplesMetric).AsInt32);
    }

    public static DiagnosticReport RunReport()
    {
        const float dt = 1f / 120f;
        var area = new FloatRect(110f, 110f, 1170f, 610f);
        var longestActiveFreeze = 0f;
        var longestVisualFreeze = 0f;
        var longestRestChain = 0f;
        var watchdogRecoveries = 0;
        var nonFiniteSamples = 0;
        var totalSamples = 0;

        var seeds = new[] { 24, 74, 109 }
            .Concat(Enumerable.Range(1, 16).Select(value => value * 7919));
        foreach (var seed in seeds)
        {
            var session = new PetSimulationSession(seed, DefaultProfile);
            session.Reset(area.Center, -0.18f);
            var behavior = session.BehaviorForDiagnostics;
            var lizard = session.LizardForDiagnostics;
            var lastPosition = behavior.Position;
            var lastHeading = behavior.Heading;
            var visualScale = session.Profile.Appearance.VisualScale;
            var visualAnchor = lizard.GetVisualBodyPoint(lizard.Spine.Joints.Count - 1, 1f) * visualScale;
            var lastStepSerial = lizard.Legs.Sum(leg => leg.StepSerial);
            var activeFreeze = 0f;
            var visualFreeze = 0f;
            var restChain = 0f;
            var lastState = behavior.State;

            var stepCount = (int)MathF.Ceiling(180f / dt);
            ScenarioRunner.RunSteps(stepCount, dt, step =>
            {
                session.Advance(new PetSimulationFrameInput(
                    step.DeltaTime,
                    area,
                    default,
                    IsDragging: false,
                    ModelToScreenScale: visualScale));
                var screenDelta = behavior.Position - lastPosition;

                var eye = lizard.GetEyeCenter(1);
                var visualTail = lizard.GetVisualBodyPoint(lizard.Spine.Joints.Count - 1, 1f) * visualScale;
                var stepSerial = lizard.Legs.Sum(leg => leg.StepSerial);
                var positionDelta = screenDelta.Length();
                var headingDelta = MathF.Abs(MathEx.DeltaAngle(lastHeading, behavior.Heading));
                var active = behavior.State.IsLocomoting();

                activeFreeze = active && positionDelta < 0.001f && headingDelta < 0.00001f && stepSerial == lastStepSerial
                    ? activeFreeze + step.DeltaTime
                    : 0f;
                var perceptibleVisualChange =
                    Vector2.Distance(visualTail, visualAnchor) >= 0.15f ||
                    lizard.BlinkAmount >= 0.5f ||
                    stepSerial != lastStepSerial;
                if (perceptibleVisualChange)
                {
                    visualFreeze = 0f;
                    visualAnchor = visualTail;
                }
                else
                {
                    visualFreeze += step.DeltaTime;
                }

                var resting = behavior.State.IsResting();
                restChain = resting && positionDelta < 0.001f && headingDelta < 0.00001f
                    ? restChain + step.DeltaTime
                    : 0f;
                longestActiveFreeze = Math.Max(longestActiveFreeze, activeFreeze);
                longestVisualFreeze = Math.Max(longestVisualFreeze, visualFreeze);
                longestRestChain = Math.Max(longestRestChain, restChain);

                if (lastState is RoamingState.ForwardCrawl or RoamingState.FastForwardCrawl or RoamingState.CurveCrawl or RoamingState.SCurveCrawl or RoamingState.FastSCurveCrawl or RoamingState.TurnAround &&
                    behavior.State == RoamingState.EdgeTurn && positionDelta < 0.02f)
                {
                    watchdogRecoveries++;
                }

                if (!float.IsFinite(behavior.Position.X) || !float.IsFinite(behavior.Position.Y) ||
                    !float.IsFinite(behavior.Heading) || !float.IsFinite(eye.X) || !float.IsFinite(eye.Y))
                {
                    nonFiniteSamples++;
                }

                lastPosition = behavior.Position;
                lastHeading = behavior.Heading;
                lastStepSerial = stepSerial;
                lastState = behavior.State;
                totalSamples++;
            });
        }

        var minimumIdleTailRange = MeasureIdleTailRange(dt);

        return new DiagnosticReportBuilder(nameof(LivenessSelfTest))
            .AddMetric(SamplesMetric, totalSamples)
            .AddMetric(ActiveFreezeMetric, longestActiveFreeze, "s")
            .AddMetric(VisualFreezeMetric, longestVisualFreeze, "s")
            .AddMetric(RestChainMetric, longestRestChain, "s")
            .AddMetric(IdleTailRangeMetric, minimumIdleTailRange, "px")
            .AddMetric(WatchdogRecoveriesMetric, watchdogRecoveries)
            .AddMetric(NonFiniteSamplesMetric, nonFiniteSamples)
            .AddCheck(
                "finite motion",
                nonFiniteSamples == 0,
                "0 non-finite samples",
                nonFiniteSamples.ToString())
            .AddCheck(
                "active motion freeze",
                longestActiveFreeze < 0.65f,
                "< 0.65 s",
                $"{longestActiveFreeze:F3} s")
            // The render-only idle tail eases through its extrema, where a
            // 0.15 px perceptual threshold can legitimately take just under
            // one second to cross. Active motion has its own stricter check.
            .AddCheck(
                "visual freeze",
                longestVisualFreeze < 1.00f,
                "< 1.00 s",
                $"{longestVisualFreeze:F3} s")
            // Resting is allowed to use a deliberate long tail. Only the
            // legitimate state dwell allowance reaches the configured cap.
            .AddCheck(
                "rest chain",
                longestRestChain <= DefaultProfile.Behavior.Rest.MaximumDuration + 0.05f,
                $"<= {DefaultProfile.Behavior.Rest.MaximumDuration + 0.05f:F2} s",
                $"{longestRestChain:F3} s")
            .AddCheck(
                "idle tail range",
                minimumIdleTailRange >= 0.30f,
                ">= 0.30 px",
                $"{minimumIdleTailRange:F3} px")
            .AddChild(SimulationSessionSelfTest.RunReport())
            .Build();
    }

    private static float MeasureIdleTailRange(float dt)
    {
        var lizard = new ProceduralLizard(DefaultProfile);
        var emotion = EmotionBlend.Normalize(new EmotionBlend(0.5f, 0.2f, 0.2f, 0.1f));
        var minimumRange = float.MaxValue;
        var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        var windowTime = 0f;

        var stepCount = (int)MathF.Ceiling(
            (DefaultProfile.Behavior.Rest.MaximumDuration + 1f) / dt);
        ScenarioRunner.RunSteps(stepCount, dt, step =>
        {
            lizard.Update(
                step.DeltaTime,
                new LizardAnimationInput(
                    0f,
                    0f,
                    LizardPoseMode.Rest,
                    emotion,
                    0f,
                    Vector2.Zero));
            if (step.StepIndex * dt < 1f)
            {
                return;
            }

            var tail = lizard.GetVisualBodyPoint(lizard.Spine.Joints.Count - 1, 1f) *
                       lizard.Profile.Appearance.VisualScale;
            minimum = Vector2.Min(minimum, tail);
            maximum = Vector2.Max(maximum, tail);
            windowTime += step.DeltaTime;
            if (windowTime + 0.0001f < 1f)
            {
                return;
            }

            minimumRange = Math.Min(minimumRange, Vector2.Distance(minimum, maximum));
            minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            windowTime = 0f;
        });

        return minimumRange;
    }
}
