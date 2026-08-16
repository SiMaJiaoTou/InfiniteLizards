using System.Numerics;
using DesktopPet.Engine;
using DesktopLizard.AppRuntime;
using DesktopLizard.Core;
using DesktopLizard.Diagnostics.Framework;

namespace DesktopLizard.Diagnostics;

internal readonly record struct SimulationSessionTestResult(
    bool Passed,
    int ComparedFrames,
    int StateMismatches,
    int TransitionMismatches,
    float MaximumPositionDifference,
    float MaximumHeadingDifference,
    float MaximumJointDifference,
    bool CatchUpClampMatched,
    bool FixedStepBounded,
    bool HighRefreshAccumulationMatched,
    bool LongTermTimeConserved,
    bool ResetClearedAccumulator,
    int NonFiniteSamples);

internal static class SimulationSessionSelfTest
{
    private static LizardProfile DefaultProfile => LizardProfile.Default;

    private const float Tolerance = 0.0001f;
    private const int ComparedDisplayFrames = 240;

    private readonly record struct Measurements(
        int StateMismatches,
        int TransitionMismatches,
        float MaximumPositionDifference,
        float MaximumHeadingDifference,
        float MaximumSpeedDifference,
        float MaximumJointDifference,
        bool SixtyHzMatched,
        bool CatchUpClampMatched,
        bool FixedStepBounded,
        bool HighRefreshAccumulationMatched,
        bool LongTermTimeConserved,
        bool ResetClearedAccumulator,
        int HighRefreshFirstStepCount,
        int HighRefreshSecondStepCount,
        int LongTermStepCount,
        double LongTermInputTime,
        double LongTermAccountedTime,
        double LongTermConservationError,
        int NonFiniteSamples,
        ScenarioRunSummary SixtyHzSummary,
        ScenarioRunSummary CatchUpSummary);

    public static SimulationSessionTestResult Run()
    {
        var measurements = Measure();
        var report = BuildReport(measurements);
        return new SimulationSessionTestResult(
            report.Passed,
            ComparedDisplayFrames,
            measurements.StateMismatches,
            measurements.TransitionMismatches,
            measurements.MaximumPositionDifference,
            measurements.MaximumHeadingDifference,
            measurements.MaximumJointDifference,
            measurements.CatchUpClampMatched,
            measurements.FixedStepBounded,
            measurements.HighRefreshAccumulationMatched,
            measurements.LongTermTimeConserved,
            measurements.ResetClearedAccumulator,
            measurements.NonFiniteSamples);
    }

    public static DiagnosticReport RunReport() => BuildReport(Measure());

    private static Measurements Measure()
    {
        var sixtyHzProbe = new StepCountingProbe();
        var sixtyHz = ScenarioRunner.RunFixedFrame(1f / 60f, null, sixtyHzProbe);
        var catchUpProbe = new StepCountingProbe();
        var catchUp = ScenarioRunner.RunFixedFrame(1f, null, catchUpProbe);

        var area = new FloatRect(-2000f, -1500f, 2000f, 1500f);
        var singleFrame = CreateSession(73129, area);
        var splitFrame = CreateSession(73129, area);
        var stateMismatches = 0;
        var transitionMismatches = 0;
        var maximumPositionDifference = 0f;
        var maximumHeadingDifference = 0f;
        var maximumSpeedDifference = 0f;
        var maximumJointDifference = 0f;
        var nonFiniteSamples = 0;
        var singleOutput = default(PetSimulationFrameOutput);
        var splitOutput = default(PetSimulationFrameOutput);

        for (var frame = 0; frame < ComparedDisplayFrames; frame++)
        {
            singleOutput = singleFrame.Advance(Input(1f / 60f, area));
            splitFrame.Advance(Input(1f / 120f, area));
            splitOutput = splitFrame.Advance(Input(1f / 120f, area));
            stateMismatches += singleFrame.BehaviorForDiagnostics.State == splitFrame.BehaviorForDiagnostics.State ? 0 : 1;
            transitionMismatches += singleFrame.BehaviorForDiagnostics.TransitionSerial == splitFrame.BehaviorForDiagnostics.TransitionSerial ? 0 : 1;
            maximumPositionDifference = Math.Max(
                maximumPositionDifference,
                Vector2.Distance(singleFrame.BehaviorForDiagnostics.Position, splitFrame.BehaviorForDiagnostics.Position));
            maximumHeadingDifference = Math.Max(
                maximumHeadingDifference,
                MathF.Abs(MathEx.DeltaAngle(singleFrame.BehaviorForDiagnostics.Heading, splitFrame.BehaviorForDiagnostics.Heading)));
            maximumSpeedDifference = Math.Max(
                maximumSpeedDifference,
                MathF.Abs(singleFrame.BehaviorForDiagnostics.Speed - splitFrame.BehaviorForDiagnostics.Speed));
            maximumJointDifference = Math.Max(
                maximumJointDifference,
                PoseError(singleFrame.LizardForDiagnostics, splitFrame.LizardForDiagnostics));
            nonFiniteSamples += IsFinite(singleFrame) && IsFinite(splitFrame) ? 0 : 1;
        }

        var sixtyHzMatched =
            MathF.Abs(singleOutput.FrameDelta - 1f / 60f) <= Tolerance &&
            MathF.Abs(splitOutput.FrameDelta - 1f / 120f) <= Tolerance &&
            stateMismatches == 0 &&
            transitionMismatches == 0 &&
            maximumPositionDifference <= Tolerance &&
            maximumHeadingDifference <= Tolerance &&
            maximumSpeedDifference <= Tolerance &&
            maximumJointDifference <= Tolerance;

        var clampedFrame = CreateSession(91003, area);
        var explicitQuarterSecond = CreateSession(91003, area);
        var clampedOutput = clampedFrame.Advance(Input(1f, area));
        var explicitOutput = explicitQuarterSecond.Advance(Input(0.25f, area));
        var catchUpPositionError = Vector2.Distance(
            clampedFrame.BehaviorForDiagnostics.Position,
            explicitQuarterSecond.BehaviorForDiagnostics.Position);
        var catchUpHeadingError = MathF.Abs(MathEx.DeltaAngle(
            clampedFrame.BehaviorForDiagnostics.Heading,
            explicitQuarterSecond.BehaviorForDiagnostics.Heading));
        var catchUpSpeedError = MathF.Abs(
            clampedFrame.BehaviorForDiagnostics.Speed - explicitQuarterSecond.BehaviorForDiagnostics.Speed);
        var catchUpPoseError = PoseError(clampedFrame.LizardForDiagnostics, explicitQuarterSecond.LizardForDiagnostics);
        maximumPositionDifference = Math.Max(maximumPositionDifference, catchUpPositionError);
        maximumHeadingDifference = Math.Max(maximumHeadingDifference, catchUpHeadingError);
        maximumSpeedDifference = Math.Max(maximumSpeedDifference, catchUpSpeedError);
        maximumJointDifference = Math.Max(maximumJointDifference, catchUpPoseError);
        nonFiniteSamples += IsFinite(clampedFrame) && IsFinite(explicitQuarterSecond) ? 0 : 1;
        var catchUpClampMatched =
            MathF.Abs(clampedOutput.FrameDelta - 0.25f) <= Tolerance &&
            MathF.Abs(explicitOutput.FrameDelta - 0.25f) <= Tolerance &&
            clampedOutput.SimulationSteps == 30 &&
            explicitOutput.SimulationSteps == 30 &&
            MathF.Abs(clampedOutput.SimulationDelta - 0.25f) <= Tolerance &&
            MathF.Abs(explicitOutput.SimulationDelta - 0.25f) <= Tolerance &&
            clampedFrame.BehaviorForDiagnostics.State == explicitQuarterSecond.BehaviorForDiagnostics.State &&
            clampedFrame.BehaviorForDiagnostics.TransitionSerial == explicitQuarterSecond.BehaviorForDiagnostics.TransitionSerial &&
            catchUpPositionError <= Tolerance &&
            catchUpHeadingError <= Tolerance &&
            catchUpSpeedError <= Tolerance &&
            catchUpPoseError <= Tolerance;
        var fixedStepBounded =
            sixtyHz.StepCount == 2 &&
            sixtyHzProbe.Count == 2 &&
            MathF.Abs(sixtyHz.SimulatedTime - 1f / 60f) <= Tolerance &&
            sixtyHz.MaximumStep <= 1f / 120f + Tolerance &&
            catchUp.StepCount == 30 &&
            catchUpProbe.Count == 30 &&
            MathF.Abs(catchUp.SimulatedTime - 0.25f) <= Tolerance &&
            catchUp.MaximumStep <= 1f / 120f + Tolerance;

        var simulationStep = 1f / DefaultProfile.Runtime.SimulationRate;
        const float highRefreshFrame = 1f / 240f;
        var highRefreshRunner = new FixedStepRunner();
        var highRefreshCallbacks = 0;
        var highRefreshFirst = highRefreshRunner.Advance(
            highRefreshFrame,
            simulationStep,
            DefaultProfile.Runtime.MaximumFrameCatchUp,
            (_, _) => highRefreshCallbacks++);
        var highRefreshSecond = highRefreshRunner.Advance(
            highRefreshFrame,
            simulationStep,
            DefaultProfile.Runtime.MaximumFrameCatchUp,
            (_, _) => highRefreshCallbacks++);

        var highRefreshSession = CreateSession(61987, area);
        var highRefreshSessionFirst = highRefreshSession.Advance(Input(highRefreshFrame, area));
        var highRefreshSessionSecond = highRefreshSession.Advance(Input(highRefreshFrame, area));
        var highRefreshAccumulationMatched =
            highRefreshFirst.StepCount == 0 &&
            highRefreshSecond.StepCount == 1 &&
            highRefreshCallbacks == 1 &&
            Math.Abs(highRefreshFirst.AccumulatedTime - highRefreshFrame) <= 0.0000001 &&
            Math.Abs(highRefreshSecond.AccumulatedTime) <= 0.0000001 &&
            highRefreshSessionFirst.SimulationSteps == 0 &&
            highRefreshSessionFirst.SimulationDelta == 0f &&
            highRefreshSessionSecond.SimulationSteps == 1 &&
            MathF.Abs(highRefreshSessionSecond.SimulationDelta - simulationStep) <= Tolerance;

        // Use an odd display-frame count so the invariant checks both the
        // completed steps and the half-step remainder after a long run.
        const int longTermDisplayFrames = 24_001;
        var longTermRunner = new FixedStepRunner();
        var longTermStepCount = 0;
        for (var frame = 0; frame < longTermDisplayFrames; frame++)
        {
            longTermRunner.Advance(
                highRefreshFrame,
                simulationStep,
                DefaultProfile.Runtime.MaximumFrameCatchUp,
                (_, _) => longTermStepCount++);
        }
        var longTermInputTime = longTermDisplayFrames * (double)highRefreshFrame;
        var longTermAccountedTime =
            longTermStepCount * (double)simulationStep +
            longTermRunner.AccumulatedTime;
        var longTermConservationError = Math.Abs(longTermInputTime - longTermAccountedTime);
        var longTermTimeConserved =
            longTermStepCount == longTermDisplayFrames / 2 &&
            Math.Abs(longTermRunner.AccumulatedTime - highRefreshFrame) <= 0.0000001 &&
            longTermConservationError <= 0.0000001;

        var resetRunner = new FixedStepRunner();
        resetRunner.Advance(
            highRefreshFrame,
            simulationStep,
            DefaultProfile.Runtime.MaximumFrameCatchUp,
            (_, _) => { });
        resetRunner.Reset();
        var resetRunnerFirst = resetRunner.Advance(
            highRefreshFrame,
            simulationStep,
            DefaultProfile.Runtime.MaximumFrameCatchUp,
            (_, _) => { });

        var resetSession = CreateSession(77143, area);
        resetSession.Advance(Input(highRefreshFrame, area));
        resetSession.Reset(area.Center, -0.18f);
        var resetSessionFirst = resetSession.Advance(Input(highRefreshFrame, area));
        var resetSessionSecond = resetSession.Advance(Input(highRefreshFrame, area));
        var resetClearedAccumulator =
            resetRunnerFirst.StepCount == 0 &&
            Math.Abs(resetRunnerFirst.AccumulatedTime - highRefreshFrame) <= 0.0000001 &&
            resetSessionFirst.SimulationSteps == 0 &&
            resetSessionSecond.SimulationSteps == 1;

        fixedStepBounded =
            fixedStepBounded &&
            highRefreshAccumulationMatched &&
            longTermTimeConserved &&
            resetClearedAccumulator;

        return new Measurements(
            stateMismatches,
            transitionMismatches,
            maximumPositionDifference,
            maximumHeadingDifference,
            maximumSpeedDifference,
            maximumJointDifference,
            sixtyHzMatched,
            catchUpClampMatched,
            fixedStepBounded,
            highRefreshAccumulationMatched,
            longTermTimeConserved,
            resetClearedAccumulator,
            highRefreshFirst.StepCount,
            highRefreshSecond.StepCount,
            longTermStepCount,
            longTermInputTime,
            longTermAccountedTime,
            longTermConservationError,
            nonFiniteSamples,
            sixtyHz,
            catchUp);
    }

    private static DiagnosticReport BuildReport(Measurements value) =>
        new DiagnosticReportBuilder(nameof(SimulationSessionSelfTest))
            .AddMetric("compared_display_frames", ComparedDisplayFrames)
            .AddMetric("state_mismatches", value.StateMismatches)
            .AddMetric("transition_mismatches", value.TransitionMismatches)
            .AddMetric("maximum_position_difference", value.MaximumPositionDifference, "px")
            .AddMetric("maximum_heading_difference", value.MaximumHeadingDifference, "rad")
            .AddMetric("maximum_speed_difference", value.MaximumSpeedDifference, "px/s")
            .AddMetric("maximum_joint_difference", value.MaximumJointDifference, "model px")
            .AddMetric("sixty_hz_step_count", value.SixtyHzSummary.StepCount)
            .AddMetric("sixty_hz_simulated_time", value.SixtyHzSummary.SimulatedTime, "s")
            .AddMetric("catch_up_step_count", value.CatchUpSummary.StepCount)
            .AddMetric("catch_up_simulated_time", value.CatchUpSummary.SimulatedTime, "s")
            .AddMetric("high_refresh_first_step_count", value.HighRefreshFirstStepCount)
            .AddMetric("high_refresh_second_step_count", value.HighRefreshSecondStepCount)
            .AddMetric("long_term_step_count", value.LongTermStepCount)
            .AddMetric("long_term_input_time", value.LongTermInputTime, "s")
            .AddMetric("long_term_accounted_time", value.LongTermAccountedTime, "s")
            .AddMetric("long_term_conservation_error", value.LongTermConservationError, "s")
            .AddMetric("non_finite_samples", value.NonFiniteSamples)
            .AddCheck(
                "fixed steps bounded",
                value.FixedStepBounded,
                "1/60 -> 2 steps; catch-up -> 30 steps/0.25 s",
                $"{value.SixtyHzSummary.StepCount} and {value.CatchUpSummary.StepCount} steps")
            .AddCheck(
                "session 60 Hz equivalence",
                value.SixtyHzMatched,
                "one 1/60 frame equals two 1/120 frames",
                $"position {value.MaximumPositionDifference:F6}, heading {value.MaximumHeadingDifference:F6}, joint {value.MaximumJointDifference:F6}")
            .AddCheck(
                "240 Hz accumulation",
                value.HighRefreshAccumulationMatched,
                "first frame 0 steps; second frame 1 fixed step",
                $"{value.HighRefreshFirstStepCount} then {value.HighRefreshSecondStepCount} steps")
            .AddCheck(
                "long-term time conservation",
                value.LongTermTimeConserved,
                "simulated time plus remainder equals accepted display time",
                $"error {value.LongTermConservationError:E3} s")
            .AddCheck(
                "reset clears accumulator",
                value.ResetClearedAccumulator,
                "runner and session restart with zero pending time",
                value.ResetClearedAccumulator ? "cleared" : "stale remainder observed")
            .AddCheck(
                "session catch-up equivalence",
                value.CatchUpClampMatched,
                "1 s input equals explicit 0.25 s input",
                value.CatchUpClampMatched ? "matched" : "mismatch")
            .AddCheck(
                "finite session state",
                value.NonFiniteSamples == 0,
                "0 non-finite samples",
                value.NonFiniteSamples.ToString())
            .AddChild(DebugPanelPlacementSelfTest.RunReport())
            .AddChild(DebugStateControlSelfTest.RunReport())
            .AddChild(LostGripFallSelfTest.RunReport())
            .Build();

    private static PetSimulationSession CreateSession(int seed, FloatRect area)
    {
        var session = new PetSimulationSession(seed, DefaultProfile);
        session.Reset(area.Center, -0.18f);
        return session;
    }

    private static PetSimulationFrameInput Input(float frameDelta, FloatRect area) => new(
        frameDelta,
        area,
        default,
        IsDragging: false,
        ModelToScreenScale: DefaultProfile.Appearance.VisualScale);

    private static float PoseError(ProceduralLizard left, ProceduralLizard right)
    {
        var maximum = MathF.Abs(left.BlinkAmount - right.BlinkAmount);
        for (var index = 0; index < left.Spine.Joints.Count; index++)
        {
            maximum = Math.Max(maximum, Vector2.Distance(left.Spine.Joints[index], right.Spine.Joints[index]));
        }
        for (var index = 0; index < left.Legs.Count; index++)
        {
            maximum = Math.Max(maximum, Vector2.Distance(left.Legs[index].Elbow, right.Legs[index].Elbow));
            maximum = Math.Max(maximum, Vector2.Distance(left.Legs[index].Foot, right.Legs[index].Foot));
        }
        return maximum;
    }

    private static bool IsFinite(PetSimulationSession session) =>
        float.IsFinite(session.BehaviorForDiagnostics.Position.X) &&
        float.IsFinite(session.BehaviorForDiagnostics.Position.Y) &&
        float.IsFinite(session.BehaviorForDiagnostics.Heading) &&
        float.IsFinite(session.BehaviorForDiagnostics.Speed) &&
        session.LizardForDiagnostics.Spine.Joints.All(point => float.IsFinite(point.X) && float.IsFinite(point.Y)) &&
        session.LizardForDiagnostics.Legs.All(leg =>
            float.IsFinite(leg.Elbow.X) && float.IsFinite(leg.Elbow.Y) &&
            float.IsFinite(leg.Foot.X) && float.IsFinite(leg.Foot.Y));

    private sealed class StepCountingProbe : IScenarioProbe
    {
        public int Count { get; private set; }

        public void Observe(in ScenarioStep step) => Count++;
    }
}
