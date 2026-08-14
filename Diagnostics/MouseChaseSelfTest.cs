using System.Numerics;
using DesktopLizard.Core;
using DesktopLizard.Diagnostics.Framework;

namespace DesktopLizard.Diagnostics;

internal readonly record struct MouseChaseTestResult(
    bool Passed,
    int ChaseEntries,
    float TriggerDelay,
    float SustainedChaseDuration,
    float DistanceClosed,
    float MaximumChaseSpeed,
    bool SustainedWhileNear,
    bool ReturnedAfterLeave,
    bool RearmedAfterLeave,
    bool InteractionPriorityPassed,
    int NearDistanceTriggers,
    bool OutsideNavigationPointerTriggered,
    bool SixtyHzSubstepPassed,
    bool SCurveInterruptionPassed,
    bool FastForwardInterruptionPassed,
    bool FastSCurveInterruptionPassed,
    int BoundaryViolations,
    int NonFiniteSamples);

internal static class MouseChaseSelfTest
{
    private static BehaviorConfiguration DefaultBehavior => LizardProfile.Default.Behavior;

    private readonly record struct Measurements(
        bool ShortExposurePassed,
        int ChaseEntries,
        float TriggerDelay,
        float SustainedChaseDuration,
        float DistanceClosed,
        float MaximumChaseSpeed,
        bool SustainedWhileNear,
        bool ReturnedAfterLeave,
        bool RearmedAfterLeave,
        bool InteractionPriorityPassed,
        int NearDistanceTriggers,
        bool OutsideNavigationPointerTriggered,
        bool SixtyHzSubstepPassed,
        bool SCurveInterruptionPassed,
        bool FastForwardInterruptionPassed,
        bool FastSCurveInterruptionPassed,
        int BoundaryViolations,
        int NonFiniteSamples);

    public static MouseChaseTestResult Run()
    {
        var measurements = Measure();
        var report = BuildReport(measurements);
        return new MouseChaseTestResult(
            report.Passed,
            measurements.ChaseEntries,
            measurements.TriggerDelay,
            measurements.SustainedChaseDuration,
            measurements.DistanceClosed,
            measurements.MaximumChaseSpeed,
            measurements.SustainedWhileNear,
            measurements.ReturnedAfterLeave,
            measurements.RearmedAfterLeave,
            measurements.InteractionPriorityPassed,
            measurements.NearDistanceTriggers,
            measurements.OutsideNavigationPointerTriggered,
            measurements.SixtyHzSubstepPassed,
            measurements.SCurveInterruptionPassed,
            measurements.FastForwardInterruptionPassed,
            measurements.FastSCurveInterruptionPassed,
            measurements.BoundaryViolations,
            measurements.NonFiniteSamples);
    }

    public static DiagnosticReport RunReport() => BuildReport(Measure());

    private static Measurements Measure()
    {
        const float dt = 1f / 120f;
        var openArea = new FloatRect(-2000f, -1500f, 2000f, 1500f);
        var chaseEntries = 0;
        var finiteSamples = new FiniteSampleCounter();
        var behavior = new BehaviorController(73129);
        behavior.Reset(Vector2.Zero, 0f);
        Advance(behavior, openArea, 0.65f, default, dt, finiteSamples);

        // A drive-by shorter than the attention confirmation must not steal the
        // normal state machine.
        var pointerPosition = behavior.Position + new Vector2(220f, 0f);
        Advance(
            behavior,
            openArea,
            DefaultBehavior.Pointer.AttentionDuration - 0.06f,
            new PointerObservation(pointerPosition, true),
            dt,
            finiteSamples);
        var shortExposurePassed = behavior.State != RoamingState.MouseChase;
        Advance(behavior, openArea, 0.05f, default, dt, finiteSamples);

        var triggerRun = behavior.State == RoamingState.MouseChase
            ? default
            : ScenarioRunner.RunUntil(StepCount(0.65f, dt) + 1, dt, step =>
        {
            behavior.Update(step.DeltaTime, openArea, new PointerObservation(pointerPosition, true));
            finiteSamples.Observe(behavior);
            return step.ElapsedTime < 0.65f && behavior.State != RoamingState.MouseChase;
        });
        var triggerDelay = triggerRun.SimulatedTime;

        if (behavior.State == RoamingState.MouseChase)
        {
            chaseEntries++;
        }

        var initialDistance = Vector2.Distance(behavior.Position, pointerPosition);
        var minimumDistance = initialDistance;
        var maximumChaseSpeed = 0f;
        // Stay well beyond the removed 3.2-4.8 second timeout. A stationary
        // nearby cursor should keep MouseChase active even after the animal
        // reaches its stopping distance.
        var sustainedRun = behavior.State != RoamingState.MouseChase
            ? default
            : ScenarioRunner.RunUntil(StepCount(10f, dt) + 1, dt, step =>
        {
            behavior.Update(step.DeltaTime, openArea, new PointerObservation(pointerPosition, true));
            minimumDistance = Math.Min(minimumDistance, Vector2.Distance(behavior.Position, pointerPosition));
            maximumChaseSpeed = Math.Max(maximumChaseSpeed, behavior.Speed);
            finiteSamples.Observe(behavior);
            return step.ElapsedTime < 10f && behavior.State == RoamingState.MouseChase;
        });
        var sustainedChaseDuration = sustainedRun.SimulatedTime;

        var sustainedWhileNear =
            behavior.State == RoamingState.MouseChase &&
            sustainedChaseDuration >= 9.99f;
        var distanceClosed = initialDistance - minimumDistance;

        // Leaving the hysteresis radius for the grace period returns control
        // to the autonomous FSM.
        var farPointer = behavior.Position + new Vector2(500f, 0f);
        Advance(
            behavior,
            openArea,
            0.60f,
            new PointerObservation(farPointer, true),
            dt,
            finiteSamples);
        var returnedAfterLeave = behavior.State == RoamingState.ForwardCrawl;

        // Wait out the deliberate post-chase cooldown while keeping the
        // pointer unavailable. The leave latch was already cleared by the
        // confirmed far sample above, so this cannot accidentally trigger a
        // second chase while the autonomous animal moves around.
        Advance(behavior, openArea, 8.10f, default, dt, finiteSamples);
        if (behavior.State != RoamingState.MouseChase)
        {
            ScenarioRunner.RunUntil(StepCount(0.65f, dt) + 1, dt, step =>
            {
                var followingPointer = behavior.Position + new Vector2(220f, 0f);
                behavior.Update(step.DeltaTime, openArea, new PointerObservation(followingPointer, true));
                finiteSamples.Observe(behavior);
                return step.ElapsedTime < 0.65f && behavior.State != RoamingState.MouseChase;
            });
        }
        var rearmedAfterLeave = behavior.State == RoamingState.MouseChase;
        if (rearmedAfterLeave) chaseEntries++;

        var interactionPriorityPassed = RunInteractionPriorityChecks(openArea, dt, finiteSamples);
        var nearField = RunNearFieldChecks(openArea, finiteSamples);
        var interruptionArea = new FloatRect(-20_000f, -20_000f, 20_000f, 20_000f);
        var sCurveInterruptionPassed = RunStateInterruptionCheck(
            RoamingState.SCurveCrawl,
            interruptionArea,
            dt,
            17,
            finiteSamples);
        var fastForwardInterruptionPassed = RunStateInterruptionCheck(
            RoamingState.FastForwardCrawl,
            interruptionArea,
            dt,
            31001,
            finiteSamples);
        var fastSCurveInterruptionPassed = RunStateInterruptionCheck(
            RoamingState.FastSCurveCrawl,
            interruptionArea,
            dt,
            47001,
            finiteSamples);
        var boundaryViolations = RunBoundaryChecks(dt, finiteSamples);

        return new Measurements(
            shortExposurePassed,
            chaseEntries,
            triggerDelay,
            sustainedChaseDuration,
            distanceClosed,
            maximumChaseSpeed,
            sustainedWhileNear,
            returnedAfterLeave,
            rearmedAfterLeave,
            interactionPriorityPassed,
            nearField.TriggerCount,
            nearField.OutsideNavigationTriggered,
            nearField.SixtyHzSubstepPassed,
            sCurveInterruptionPassed,
            fastForwardInterruptionPassed,
            fastSCurveInterruptionPassed,
            boundaryViolations,
            finiteSamples.Count);
    }

    private static DiagnosticReport BuildReport(Measurements value) =>
        new DiagnosticReportBuilder(nameof(MouseChaseSelfTest))
            .AddMetric("chase_entries", value.ChaseEntries)
            .AddMetric("trigger_delay", value.TriggerDelay, "s")
            .AddMetric("sustained_chase_duration", value.SustainedChaseDuration, "s")
            .AddMetric("distance_closed", value.DistanceClosed, "px")
            .AddMetric("maximum_chase_speed", value.MaximumChaseSpeed, "px/s")
            .AddMetric("near_distance_triggers", value.NearDistanceTriggers)
            .AddMetric("boundary_violations", value.BoundaryViolations)
            .AddMetric("non_finite_samples", value.NonFiniteSamples)
            .AddCheck(
                "short pointer exposure ignored",
                value.ShortExposurePassed,
                "no chase before attention duration",
                value.ShortExposurePassed ? "ignored" : "chase entered")
            .AddCheck(
                "pointer trigger delay",
                value.TriggerDelay is >= 0.10f and <= 0.18f,
                "0.10-0.18 s",
                $"{value.TriggerDelay:F3} s")
            .AddCheck("sustained while near", value.SustainedWhileNear, "true", value.SustainedWhileNear.ToString())
            .AddCheck("distance closed", value.DistanceClosed >= 120f, ">= 120 px", $"{value.DistanceClosed:F2} px")
            .AddCheck(
                "chase speed bounded",
                value.MaximumChaseSpeed <= DefaultBehavior.Speed.MaximumCrawl + 0.01f,
                $"<= {DefaultBehavior.Speed.MaximumCrawl + 0.01f:F2} px/s",
                $"{value.MaximumChaseSpeed:F2} px/s")
            .AddCheck("returned after leave", value.ReturnedAfterLeave, "true", value.ReturnedAfterLeave.ToString())
            .AddCheck("rearmed after leave", value.RearmedAfterLeave, "true", value.RearmedAfterLeave.ToString())
            .AddCheck(
                "interaction priority",
                value.InteractionPriorityPassed,
                "true",
                value.InteractionPriorityPassed.ToString())
            .AddCheck(
                "near-distance triggers",
                value.NearDistanceTriggers == 4,
                "4",
                value.NearDistanceTriggers.ToString())
            .AddCheck(
                "outside-navigation pointer trigger",
                value.OutsideNavigationPointerTriggered,
                "true",
                value.OutsideNavigationPointerTriggered.ToString())
            .AddCheck("60 Hz substeps", value.SixtyHzSubstepPassed, "true", value.SixtyHzSubstepPassed.ToString())
            .AddCheck(
                "S-curve interruption",
                value.SCurveInterruptionPassed,
                "true",
                value.SCurveInterruptionPassed.ToString())
            .AddCheck(
                "fast-forward interruption",
                value.FastForwardInterruptionPassed,
                "true",
                value.FastForwardInterruptionPassed.ToString())
            .AddCheck(
                "fast S-curve interruption",
                value.FastSCurveInterruptionPassed,
                "true",
                value.FastSCurveInterruptionPassed.ToString())
            .AddCheck(
                "boundary safety",
                value.BoundaryViolations == 0,
                "0 violations",
                value.BoundaryViolations.ToString())
            .AddCheck(
                "finite motion",
                value.NonFiniteSamples == 0,
                "0 non-finite samples",
                value.NonFiniteSamples.ToString())
            .Build();

    private readonly record struct NearFieldResult(
        int TriggerCount,
        bool OutsideNavigationTriggered,
        bool SixtyHzSubstepPassed);

    private static NearFieldResult RunNearFieldChecks(
        FloatRect openArea,
        FiniteSampleCounter finiteSamples)
    {
        const float simulationStep = 1f / 120f;
        const float displayStep = 1f / 60f;
        var triggerCount = 0;
        var sixtyHzSubstepPassed = true;

        foreach (var distance in new[] { 40f, 80f, 140f, 220f })
        {
            var behavior = new BehaviorController(44000 + (int)distance);
            behavior.Reset(Vector2.Zero, 0f);
            Advance(behavior, openArea, 0.65f, default, simulationStep, finiteSamples);
            var pointer = behavior.Position + new Vector2(distance, 0f);
            if (behavior.State != RoamingState.MouseChase)
            {
                ScenarioRunner.RunUntil(
                    StepCount(0.30f, displayStep) + 1,
                    displayStep,
                    displayFrame =>
                {
                    // Mirror PetSimulationSession/PetWindow: one physical
                    // cursor sample is reused by every 120 Hz simulation
                    // slice of a 60 Hz rendered frame.
                    ScenarioRunner.RunFixedFrame(displayFrame.DeltaTime, substep =>
                    {
                        behavior.Update(
                            substep.DeltaTime,
                            openArea,
                            new PointerObservation(pointer, true));
                        finiteSamples.Observe(behavior);
                    });
                    return displayFrame.ElapsedTime < 0.30f &&
                           behavior.State != RoamingState.MouseChase;
                });
            }

            if (behavior.State == RoamingState.MouseChase)
            {
                triggerCount++;
            }
            else
            {
                sixtyHzSubstepPassed = false;
            }
        }

        // The navigation rectangle is already inset by the pet's visual
        // radius. A cursor may be outside that center-safe rectangle while it
        // is visibly beside a pet near the monitor edge; target clamping, not
        // trigger rejection, must keep the subsequent chase safe.
        var edgeArea = new FloatRect(110f, 110f, 1170f, 610f);
        var edgeBehavior = new BehaviorController(44881);
        edgeBehavior.Reset(new Vector2(edgeArea.Left + 82f, edgeArea.Center.Y), MathF.PI);
        Advance(edgeBehavior, edgeArea, 0.65f, default, simulationStep, finiteSamples);
        var outsidePointer = new Vector2(edgeArea.Left - 20f, edgeBehavior.Position.Y);
        if (edgeBehavior.State != RoamingState.MouseChase)
        {
            ScenarioRunner.RunUntil(
                StepCount(0.30f, simulationStep) + 1,
                simulationStep,
                step =>
                {
                    edgeBehavior.Update(
                        step.DeltaTime,
                        edgeArea,
                        new PointerObservation(outsidePointer, true));
                    finiteSamples.Observe(edgeBehavior);
                    return step.ElapsedTime < 0.30f &&
                           edgeBehavior.State != RoamingState.MouseChase;
                });
        }

        return new NearFieldResult(
            triggerCount,
            edgeBehavior.State == RoamingState.MouseChase,
            sixtyHzSubstepPassed);
    }

    private static bool RunInteractionPriorityChecks(
        FloatRect area,
        float dt,
        FiniteSampleCounter finiteSamples)
    {
        var behavior = new BehaviorController(88117);
        behavior.Reset(Vector2.Zero, 0f);
        Advance(behavior, area, 0.65f, default, dt, finiteSamples);
        var pointer = behavior.Position + new Vector2(220f, 0f);

        Advance(
            behavior,
            area,
            0.75f,
            new PointerObservation(pointer, true, IsInteractionBlocked: true),
            dt,
            finiteSamples);
        if (behavior.State == RoamingState.MouseChase)
        {
            return false;
        }

        if (behavior.State != RoamingState.MouseChase)
        {
            ScenarioRunner.RunUntil(StepCount(0.5f, dt) + 1, dt, step =>
            {
                behavior.Update(step.DeltaTime, area, new PointerObservation(pointer, true));
                return step.ElapsedTime < 0.5f && behavior.State != RoamingState.MouseChase;
            });
        }
        if (behavior.State != RoamingState.MouseChase)
        {
            return false;
        }

        behavior.BeginGrab();
        if (behavior.State != RoamingState.Grabbed)
        {
            return false;
        }
        Advance(behavior, area, 0.20f, new PointerObservation(pointer, true), dt, finiteSamples);
        if (behavior.State != RoamingState.Grabbed)
        {
            return false;
        }

        behavior.EndGrab(behavior.Position);
        Advance(behavior, area, 0.12f, new PointerObservation(pointer, true), dt, finiteSamples);
        if (behavior.State != RoamingState.ReleaseSettle)
        {
            return false;
        }
        Advance(behavior, area, 0.30f, new PointerObservation(pointer, true), dt, finiteSamples);
        if (behavior.State != RoamingState.EscapeSprint)
        {
            return false;
        }

        var paused = new BehaviorController(91003) { IsPaused = true };
        paused.Reset(Vector2.Zero, 0f);
        Advance(
            paused,
            area,
            1f,
            new PointerObservation(new Vector2(220f, 0f), true),
            dt,
            finiteSamples);
        return paused.State == RoamingState.Idle && paused.Speed < 0.1f;
    }

    private static bool RunStateInterruptionCheck(
        RoamingState targetState,
        FloatRect area,
        float dt,
        int seedBase,
        FiniteSampleCounter finiteSamples)
    {
        // Fast actions are intentionally rare. Search a fixed seed pool rather
        // than increasing their production probability merely to make a test
        // convenient. Every run still explores the same deterministic paths.
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var behavior = new BehaviorController(seedBase + attempt * 7919);
            behavior.Reset(area.Center, -0.18f);
            if (behavior.State != targetState)
            {
                ScenarioRunner.RunUntil(StepCount(90f, dt), dt, step =>
                {
                    behavior.Update(step.DeltaTime, area);
                    finiteSamples.Observe(behavior);
                    return behavior.State != targetState;
                });
            }
            if (behavior.State != targetState)
            {
                continue;
            }

            var chaseDuration = DefaultBehavior.Pointer.AttentionDuration + 0.05f;
            if (behavior.State != RoamingState.MouseChase)
            {
                ScenarioRunner.RunUntil(StepCount(chaseDuration, dt) + 1, dt, step =>
                {
                    var pointer = behavior.Position + MathEx.FromAngle(behavior.Heading) * 220f;
                    behavior.Update(step.DeltaTime, area, new PointerObservation(pointer, true));
                    finiteSamples.Observe(behavior);
                    return step.ElapsedTime < chaseDuration &&
                           behavior.State != RoamingState.MouseChase;
                });
            }

            if (behavior.State != RoamingState.MouseChase ||
                behavior.LastTransitionReason != StateTransitionReason.Pointer)
            {
                return false;
            }

            ScenarioRunner.RunSteps(1, dt, step =>
            {
                behavior.Update(
                    step.DeltaTime,
                    area,
                    new PointerObservation(
                        behavior.Position + MathEx.FromAngle(behavior.Heading) * 220f,
                        true,
                        IsInteractionBlocked: true));
                finiteSamples.Observe(behavior);
            });
            return behavior.State == RoamingState.ForwardCrawl;
        }

        return false;
    }

    private static int RunBoundaryChecks(float dt, FiniteSampleCounter finiteSamples)
    {
        var area = new FloatRect(110f, 110f, 1170f, 610f);
        var starts = new[]
        {
            (new Vector2(area.Left + 70f, area.Center.Y), new Vector2(150f, 0f)),
            (new Vector2(area.Right - 70f, area.Center.Y), new Vector2(-150f, 0f)),
            (new Vector2(area.Center.X, area.Top + 70f), new Vector2(0f, 150f)),
            (new Vector2(area.Center.X, area.Bottom - 70f), new Vector2(0f, -150f))
        };
        var violations = 0;
        for (var index = 0; index < starts.Length; index++)
        {
            var behavior = new BehaviorController(12001 + index * 97);
            behavior.Reset(starts[index].Item1, 0f);
            Advance(behavior, area, 0.55f, default, dt, finiteSamples);
            ScenarioRunner.RunUntil(StepCount(6f, dt), dt, step =>
            {
                var pointer = behavior.Position + starts[index].Item2;
                behavior.Update(step.DeltaTime, area, new PointerObservation(pointer, true));
                if (!area.Contains(behavior.Position) ||
                    behavior.Position.X <= area.Left + 0.01f ||
                    behavior.Position.X >= area.Right - 0.01f ||
                    behavior.Position.Y <= area.Top + 0.01f ||
                    behavior.Position.Y >= area.Bottom - 0.01f)
                {
                    violations++;
                    return false;
                }
                finiteSamples.Observe(behavior);
                return true;
            });
        }
        return violations;
    }

    private static void Advance(
        BehaviorController behavior,
        FloatRect area,
        float duration,
        PointerObservation pointer,
        float dt,
        FiniteSampleCounter finiteSamples)
    {
        ScenarioRunner.RunSteps(StepCount(duration, dt), dt, step =>
        {
            behavior.Update(step.DeltaTime, area, pointer);
            finiteSamples.Observe(behavior);
        });
    }

    private static int StepCount(float duration, float dt) =>
        (int)MathF.Ceiling(duration / dt);

    private sealed class FiniteSampleCounter
    {
        public int Count { get; private set; }

        public void Observe(BehaviorController behavior)
        {
            if (!float.IsFinite(behavior.Position.X) ||
                !float.IsFinite(behavior.Position.Y) ||
                !float.IsFinite(behavior.Heading) ||
                !float.IsFinite(behavior.Speed))
            {
                Count++;
            }
        }
    }
}
