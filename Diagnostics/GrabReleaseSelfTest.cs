using System.Numerics;
using DesktopLizard.AppRuntime;
using DesktopLizard.Core;
using DesktopLizard.Diagnostics.Framework;

namespace DesktopLizard.Diagnostics;

internal readonly record struct GrabReleaseTestResult(
    bool Passed,
    int GrabbedStatesCovered,
    int GrabAttachmentsCovered,
    int ReleaseSequences,
    int InvalidTransitions,
    string MissingSourceStates,
    float MinimumHangDrop,
    float MinimumFootDrop,
    float MaximumConstraintError,
    float MaximumSpineError,
    float MaximumUpperLegError,
    float MaximumLowerLegError,
    float MaximumGrabError,
    float MinimumCanvasMargin,
    float MaximumReleasePoseJump,
    float MaximumReleaseSpineJump,
    float MaximumReleaseElbowJump,
    float MaximumReleaseFootJump,
    float MinimumSprintDistance,
    float MaximumSprintDistance,
    float MaximumSprintSpeed,
    int SprintQuadrants,
    int BoundaryViolations,
    int RegrabViolations,
    int NonFiniteSamples);

internal static class GrabReleaseSelfTest
{
    private static LizardProfile DefaultProfile => LizardProfile.Default;

    public static GrabReleaseTestResult Run()
    {
        var result = Measure();
        return result with { Passed = BuildReport(result).Passed };
    }

    public static DiagnosticReport RunReport() => BuildReport(Measure());

    private static GrabReleaseTestResult Measure()
    {
        const float dt = 1f / 120f;
        var area = new FloatRect(110f, 110f, 1170f, 610f);
        var invalidTransitions = 0;
        var missingSourceStates = new List<string>();
        var releaseSequences = 0;
        var minimumSprintDistance = float.MaxValue;
        var maximumSprintDistance = 0f;
        var maximumSprintSpeed = 0f;
        var quadrants = new HashSet<int>();
        var boundaryViolations = 0;
        var regrabViolations = 0;
        var nonFinite = 0;

        var autonomousStates = new[]
        {
            RoamingState.Spawn,
            RoamingState.Idle,
            RoamingState.Observe,
            RoamingState.ForwardCrawl,
            RoamingState.FastForwardCrawl,
            RoamingState.CurveCrawl,
            RoamingState.SCurveCrawl,
            RoamingState.FastSCurveCrawl,
            RoamingState.TurnAround,
            RoamingState.MouseChase,
            RoamingState.EdgeTurn,
            RoamingState.EscapeSprint
        };
        var covered = 0;
        for (var index = 0; index < autonomousStates.Length; index++)
        {
            BehaviorController? behavior = null;
            for (var attempt = 0; attempt < 24; attempt++)
            {
                // Transition probabilities are intentionally tunable. Search
                // a deterministic seed pool instead of coupling source-state
                // coverage to one particular random sequence.
                var seed = 18001 + index * 113 + attempt * 7919;
                var candidate = new BehaviorController(seed);
                candidate.Reset(area.Center, 0f);
                PrepareState(candidate, autonomousStates[index], area, dt);
                if (candidate.State == autonomousStates[index])
                {
                    behavior = candidate;
                    break;
                }
            }

            if (behavior is null)
            {
                invalidTransitions++;
                missingSourceStates.Add(autonomousStates[index].ToString());
                continue;
            }

            behavior.BeginGrab();
            if (behavior.State == RoamingState.Grabbed) covered++;
            else invalidTransitions++;
            behavior.EndGrab(behavior.Position);
            if (behavior.State != RoamingState.ReleaseSettle) invalidTransitions++;
        }

        var minimumHangDrop = float.MaxValue;
        var minimumFootDrop = float.MaxValue;
        var maximumConstraintError = 0f;
        var maximumSpineError = 0f;
        var maximumUpperLegError = 0f;
        var maximumLowerLegError = 0f;
        var maximumGrabError = 0f;
        var minimumCanvasMargin = float.MaxValue;
        var maximumReleasePoseJump = 0f;
        var maximumReleaseSpineJump = 0f;
        var maximumReleaseElbowJump = 0f;
        var maximumReleaseFootJump = 0f;
        for (var seed = 0; seed < 24; seed++)
        {
            var session = new PetSimulationSession(29011 + seed * 71, DefaultProfile);
            session.Reset(area.Center, 0f);
            var behavior = session.BehaviorForDiagnostics;
            var lizard = session.LizardForDiagnostics;
            var grabPoint = lizard.Spine.Joints[seed % lizard.Spine.Joints.Count];
            session.BeginGrab(grabPoint);
            ScenarioRunner.RunSteps(StepCount(1.50f, dt), dt, step =>
            {
                if (step.StepIndex < 0.55f / dt)
                {
                    var sway = new Vector2(
                        MathF.Sin(step.StepIndex * 0.045f) * 0.8f,
                        MathF.Cos(step.StepIndex * 0.027f) * 0.18f);
                    session.DragTo(behavior.Position + sway);
                }
                session.Advance(SessionInput(step.DeltaTime, area, isDragging: true));
                CountFinite(lizard, behavior, ref nonFinite);
                MeasureDanglingPose(
                    lizard,
                    ref minimumCanvasMargin,
                    ref maximumSpineError,
                    ref maximumUpperLegError,
                    ref maximumLowerLegError,
                    ref maximumGrabError);
            });
            var centerOfMassY = lizard.Spine.Joints.Average(point => point.Y);
            minimumHangDrop = Math.Min(minimumHangDrop, centerOfMassY - grabPoint.Y);
            foreach (var leg in lizard.Legs)
            {
                minimumFootDrop = Math.Min(minimumFootDrop, leg.Foot.Y - leg.Shoulder.Y);
            }

            var releasePosition = behavior.Position;
            var preReleaseSpine = lizard.Spine.Joints.ToArray();
            var preReleaseElbows = lizard.Legs.Select(leg => leg.Elbow).ToArray();
            var preReleaseFeet = lizard.Legs.Select(leg => leg.Foot).ToArray();
            session.EndGrab(releasePosition);
            if (behavior.State != RoamingState.ReleaseSettle) invalidTransitions++;
            var transitionSerial = behavior.TransitionSerial;
            session.EndGrab(releasePosition);
            if (behavior.TransitionSerial != transitionSerial || behavior.State != RoamingState.ReleaseSettle)
            {
                invalidTransitions++;
            }
            session.Advance(SessionInput(dt, area));
            for (var i = 0; i < lizard.Spine.Joints.Count; i++)
            {
                var jump = Vector2.Distance(preReleaseSpine[i], lizard.Spine.Joints[i]);
                maximumReleaseSpineJump = Math.Max(maximumReleaseSpineJump, jump);
                maximumReleasePoseJump = Math.Max(maximumReleasePoseJump, jump);
            }
            for (var i = 0; i < lizard.Legs.Count; i++)
            {
                var elbowJump = Vector2.Distance(preReleaseElbows[i], lizard.Legs[i].Elbow);
                var footJump = Vector2.Distance(preReleaseFeet[i], lizard.Legs[i].Foot);
                maximumReleaseElbowJump = Math.Max(maximumReleaseElbowJump, elbowJump);
                maximumReleaseFootJump = Math.Max(maximumReleaseFootJump, footJump);
                maximumReleasePoseJump = Math.Max(maximumReleasePoseJump, elbowJump);
                maximumReleasePoseJump = Math.Max(maximumReleasePoseJump, footJump);
            }
            var sprintStarted = false;
            var sprintStart = releasePosition;
            var sprintEnd = releasePosition;
            var sprintTravel = 0f;
            var previousSprintPosition = releasePosition;
            ScenarioRunner.RunUntil(StepCount(3f, dt) + 1, dt, step =>
            {
                session.Advance(SessionInput(step.DeltaTime, area));
                if (behavior.State == RoamingState.EscapeSprint)
                {
                    if (!sprintStarted)
                    {
                        sprintStarted = true;
                        sprintStart = behavior.Position;
                        releaseSequences++;
                        previousSprintPosition = behavior.Position;
                    }
                    sprintTravel += Vector2.Distance(previousSprintPosition, behavior.Position);
                    previousSprintPosition = behavior.Position;
                    sprintEnd = behavior.Position;
                    maximumSprintSpeed = Math.Max(maximumSprintSpeed, behavior.Speed);
                }
                if (!area.Contains(behavior.Position)) boundaryViolations++;
                if (!float.IsFinite(behavior.Position.X) || !float.IsFinite(behavior.Position.Y)) nonFinite++;
                return step.ElapsedTime < 3f &&
                       !(sprintStarted && behavior.State == RoamingState.ForwardCrawl);
            });

            if (!sprintStarted)
            {
                invalidTransitions++;
                continue;
            }
            var sprintDelta = sprintEnd - sprintStart;
            var distance = sprintTravel;
            minimumSprintDistance = Math.Min(minimumSprintDistance, distance);
            maximumSprintDistance = Math.Max(maximumSprintDistance, distance);
            if (sprintDelta.LengthSquared() > 1f)
            {
                quadrants.Add((sprintDelta.X >= 0f ? 1 : 0) | (sprintDelta.Y >= 0f ? 2 : 0));
            }
        }

        var grabAttachmentsCovered = RunAttachmentCoverage(
            ref nonFinite,
            ref minimumCanvasMargin,
            ref maximumSpineError,
            ref maximumUpperLegError,
            ref maximumLowerLegError,
            ref maximumGrabError);

        RunDanglingStress(
            ref nonFinite,
            ref minimumCanvasMargin,
            ref maximumSpineError,
            ref maximumUpperLegError,
            ref maximumLowerLegError,
            ref maximumGrabError);

        maximumConstraintError = Math.Max(
            maximumSpineError,
            Math.Max(maximumUpperLegError, maximumLowerLegError));


        var regrabBehavior = new BehaviorController(77731);
        regrabBehavior.Reset(area.Center, 0f);
        regrabBehavior.BeginGrab();
        regrabBehavior.EndGrab(regrabBehavior.Position);
        ScenarioRunner.RunSteps(
            StepCount(0.55f, dt),
            dt,
            step => regrabBehavior.Update(step.DeltaTime, area));
        if (regrabBehavior.State != RoamingState.EscapeSprint) regrabViolations++;
        regrabBehavior.BeginGrab();
        if (regrabBehavior.State != RoamingState.Grabbed) regrabViolations++;
        ScenarioRunner.RunSteps(
            StepCount(0.45f, dt),
            dt,
            step => regrabBehavior.Update(step.DeltaTime, area));
        if (regrabBehavior.State != RoamingState.Grabbed) regrabViolations++;
        regrabBehavior.EndGrab(regrabBehavior.Position);
        if (regrabBehavior.State != RoamingState.ReleaseSettle) regrabViolations++;

        // Pausing while held and then releasing must terminate the interaction
        // in Idle. It must neither throw an illegal-transition exception nor
        // leave a deferred release/sprint action that fires after resume.
        var pausedGrabBehavior = new BehaviorController(81173);
        pausedGrabBehavior.Reset(area.Center, 0f);
        pausedGrabBehavior.BeginGrab();
        pausedGrabBehavior.IsPaused = true;
        pausedGrabBehavior.EndGrab(pausedGrabBehavior.Position);
        if (pausedGrabBehavior.State != RoamingState.Idle ||
            pausedGrabBehavior.Speed != 0f ||
            pausedGrabBehavior.DropProgress != 0f)
        {
            regrabViolations++;
        }
        ScenarioRunner.RunUntil(StepCount(0.35f, dt), dt, step =>
        {
            pausedGrabBehavior.Update(step.DeltaTime, area);
            if (pausedGrabBehavior.State != RoamingState.Idle || pausedGrabBehavior.Speed != 0f)
            {
                regrabViolations++;
                return false;
            }
            return true;
        });
        pausedGrabBehavior.IsPaused = false;
        ScenarioRunner.RunUntil(StepCount(2.75f, dt), dt, step =>
        {
            pausedGrabBehavior.Update(step.DeltaTime, area);
            if (pausedGrabBehavior.State is RoamingState.ReleaseSettle or RoamingState.EscapeSprint)
            {
                regrabViolations++;
                return false;
            }
            return true;
        });

        // DragTo may be followed by MouseUp before the next render/simulation
        // tick. The release must use that latest position exactly once.
        var immediateReleaseSession = new PetSimulationSession(82721, DefaultProfile);
        immediateReleaseSession.Reset(area.Center, 0f);
        var immediateReleaseBehavior = immediateReleaseSession.BehaviorForDiagnostics;
        var immediateReleaseLizard = immediateReleaseSession.LizardForDiagnostics;
        immediateReleaseSession.BeginGrab(immediateReleaseLizard.Spine.Joints[6]);
        immediateReleaseSession.Advance(SessionInput(dt, area, isDragging: true));
        var immediateSpine = immediateReleaseLizard.Spine.Joints.ToArray();
        var immediateElbows = immediateReleaseLizard.Legs.Select(leg => leg.Elbow).ToArray();
        var immediateFeet = immediateReleaseLizard.Legs.Select(leg => leg.Foot).ToArray();
        var immediateReleasePosition = area.Center + new Vector2(137f, -91f);
        immediateReleaseSession.DragTo(immediateReleasePosition);
        immediateReleaseSession.EndGrab(immediateReleasePosition);
        if (immediateReleaseBehavior.State != RoamingState.ReleaseSettle ||
            Vector2.Distance(immediateReleaseBehavior.Position, immediateReleasePosition) > 0.001f)
        {
            regrabViolations++;
        }
        immediateReleaseSession.Advance(SessionInput(dt, area));
        for (var i = 0; i < immediateSpine.Length; i++)
        {
            maximumReleaseSpineJump = Math.Max(
                maximumReleaseSpineJump,
                Vector2.Distance(immediateSpine[i], immediateReleaseLizard.Spine.Joints[i]));
        }
        for (var i = 0; i < immediateElbows.Length; i++)
        {
            maximumReleaseElbowJump = Math.Max(
                maximumReleaseElbowJump,
                Vector2.Distance(immediateElbows[i], immediateReleaseLizard.Legs[i].Elbow));
            maximumReleaseFootJump = Math.Max(
                maximumReleaseFootJump,
                Vector2.Distance(immediateFeet[i], immediateReleaseLizard.Legs[i].Foot));
        }
        maximumReleasePoseJump = Math.Max(
            maximumReleasePoseJump,
            Math.Max(maximumReleaseSpineJump, Math.Max(maximumReleaseElbowJump, maximumReleaseFootJump)));

        // Pause also has to clear already accumulated sprint velocity in the
        // same tick, not merely brake it over subsequent Idle frames.
        var pausedSprintBehavior = new BehaviorController(83939);
        pausedSprintBehavior.Reset(area.Center, 0f);
        pausedSprintBehavior.BeginGrab();
        pausedSprintBehavior.EndGrab(pausedSprintBehavior.Position);
        ScenarioRunner.RunSteps(
            StepCount(0.55f, dt),
            dt,
            step => pausedSprintBehavior.Update(step.DeltaTime, area));
        if (pausedSprintBehavior.State != RoamingState.EscapeSprint || pausedSprintBehavior.Speed <= 1f)
        {
            regrabViolations++;
        }
        pausedSprintBehavior.IsPaused = true;
        ScenarioRunner.RunSteps(
            1,
            dt,
            step => pausedSprintBehavior.Update(step.DeltaTime, area));
        if (pausedSprintBehavior.State != RoamingState.Idle ||
            pausedSprintBehavior.Speed != 0f ||
            pausedSprintBehavior.TurnVelocity != 0f ||
            pausedSprintBehavior.DropProgress != 0f)
        {
            regrabViolations++;
        }

        if (!float.IsFinite(minimumSprintDistance)) minimumSprintDistance = 0f;
        if (!float.IsFinite(minimumHangDrop)) minimumHangDrop = 0f;
        if (!float.IsFinite(minimumFootDrop)) minimumFootDrop = 0f;
        var passed =
            covered == autonomousStates.Length &&
            grabAttachmentsCovered == 13 &&
            releaseSequences >= 23 &&
            invalidTransitions == 0 &&
            minimumHangDrop >= 28f &&
            minimumFootDrop >= 8f &&
            maximumConstraintError <= 1.5f &&
            maximumGrabError <= 1.5f &&
            minimumCanvasMargin >= 8f &&
            maximumReleasePoseJump <= 8f &&
            minimumSprintDistance >= 70f &&
            maximumSprintDistance <= 185f &&
            maximumSprintSpeed is >= 78f and <= 98f &&
            quadrants.Count >= 3 &&
            boundaryViolations == 0 &&
            regrabViolations == 0 &&
            nonFinite == 0;

        return new GrabReleaseTestResult(
            passed,
            covered,
            grabAttachmentsCovered,
            releaseSequences,
            invalidTransitions,
            string.Join(", ", missingSourceStates),
            minimumHangDrop,
            minimumFootDrop,
            maximumConstraintError,
            maximumSpineError,
            maximumUpperLegError,
            maximumLowerLegError,
            maximumGrabError,
            minimumCanvasMargin,
            maximumReleasePoseJump,
            maximumReleaseSpineJump,
            maximumReleaseElbowJump,
            maximumReleaseFootJump,
            minimumSprintDistance,
            maximumSprintDistance,
            maximumSprintSpeed,
            quadrants.Count,
            boundaryViolations,
            regrabViolations,
            nonFinite);
    }

    private static DiagnosticReport BuildReport(GrabReleaseTestResult value) =>
        new DiagnosticReportBuilder(nameof(GrabReleaseSelfTest))
            .AddMetric("grabbed_states_covered", value.GrabbedStatesCovered)
            .AddMetric("grab_attachments_covered", value.GrabAttachmentsCovered)
            .AddMetric("release_sequences", value.ReleaseSequences)
            .AddMetric("invalid_transitions", value.InvalidTransitions)
            .AddMetric("minimum_hang_drop", value.MinimumHangDrop, "model px")
            .AddMetric("minimum_foot_drop", value.MinimumFootDrop, "model px")
            .AddMetric("maximum_constraint_error", value.MaximumConstraintError, "model px")
            .AddMetric("maximum_spine_error", value.MaximumSpineError, "model px")
            .AddMetric("maximum_upper_leg_error", value.MaximumUpperLegError, "model px")
            .AddMetric("maximum_lower_leg_error", value.MaximumLowerLegError, "model px")
            .AddMetric("maximum_grab_error", value.MaximumGrabError, "model px")
            .AddMetric("minimum_canvas_margin", value.MinimumCanvasMargin, "model px")
            .AddMetric("maximum_release_pose_jump", value.MaximumReleasePoseJump, "model px")
            .AddMetric("maximum_release_spine_jump", value.MaximumReleaseSpineJump, "model px")
            .AddMetric("maximum_release_elbow_jump", value.MaximumReleaseElbowJump, "model px")
            .AddMetric("maximum_release_foot_jump", value.MaximumReleaseFootJump, "model px")
            .AddMetric("minimum_sprint_distance", value.MinimumSprintDistance, "px")
            .AddMetric("maximum_sprint_distance", value.MaximumSprintDistance, "px")
            .AddMetric("maximum_sprint_speed", value.MaximumSprintSpeed, "px/s")
            .AddMetric("sprint_quadrants", value.SprintQuadrants)
            .AddMetric("boundary_violations", value.BoundaryViolations)
            .AddMetric("regrab_violations", value.RegrabViolations)
            .AddMetric("non_finite_samples", value.NonFiniteSamples)
            .AddCheck(
                "source state coverage",
                value.GrabbedStatesCovered == 12,
                "12 states",
                $"{value.GrabbedStatesCovered}; missing {value.MissingSourceStates}")
            .AddCheck(
                "attachment coverage",
                value.GrabAttachmentsCovered == 13,
                "13",
                value.GrabAttachmentsCovered.ToString())
            .AddCheck("release sequences", value.ReleaseSequences >= 23, ">= 23", value.ReleaseSequences.ToString())
            .AddCheck(
                "transition validity",
                value.InvalidTransitions == 0,
                "0 invalid transitions",
                value.InvalidTransitions.ToString())
            .AddCheck("body hang drop", value.MinimumHangDrop >= 28f, ">= 28 model px", $"{value.MinimumHangDrop:F2}")
            .AddCheck("foot hang drop", value.MinimumFootDrop >= 8f, ">= 8 model px", $"{value.MinimumFootDrop:F2}")
            .AddCheck(
                "limb constraints",
                value.MaximumConstraintError <= 1.5f,
                "<= 1.5 model px",
                $"{value.MaximumConstraintError:F3}")
            .AddCheck(
                "grab attachment",
                value.MaximumGrabError <= 1.5f,
                "<= 1.5 model px",
                $"{value.MaximumGrabError:F3}")
            .AddCheck(
                "canvas margin",
                value.MinimumCanvasMargin >= 8f,
                ">= 8 model px",
                $"{value.MinimumCanvasMargin:F2}")
            .AddCheck(
                "release pose continuity",
                value.MaximumReleasePoseJump <= 8f,
                "<= 8 model px",
                $"{value.MaximumReleasePoseJump:F2}")
            .AddCheck(
                "minimum sprint distance",
                value.MinimumSprintDistance >= 70f,
                ">= 70 px",
                $"{value.MinimumSprintDistance:F2}")
            .AddCheck(
                "maximum sprint distance",
                value.MaximumSprintDistance <= 185f,
                "<= 185 px",
                $"{value.MaximumSprintDistance:F2}")
            .AddCheck(
                "sprint speed",
                value.MaximumSprintSpeed is >= 78f and <= 98f,
                "78-98 px/s",
                $"{value.MaximumSprintSpeed:F2}")
            .AddCheck(
                "sprint direction coverage",
                value.SprintQuadrants >= 3,
                ">= 3 quadrants",
                value.SprintQuadrants.ToString())
            .AddCheck(
                "boundary safety",
                value.BoundaryViolations == 0,
                "0 violations",
                value.BoundaryViolations.ToString())
            .AddCheck("regrab safety", value.RegrabViolations == 0, "0 violations", value.RegrabViolations.ToString())
            .AddCheck(
                "finite pose",
                value.NonFiniteSamples == 0,
                "0 non-finite samples",
                value.NonFiniteSamples.ToString())
            .Build();

    private static PetSimulationFrameInput SessionInput(
        float frameDelta,
        FloatRect area,
        bool isDragging = false) => new(
        frameDelta,
        area,
        default,
        isDragging,
        DefaultProfile.Appearance.VisualScale);

    private static int StepCount(float duration, float dt) =>
        (int)MathF.Ceiling(duration / dt);

    private static void PrepareState(BehaviorController behavior, RoamingState target, FloatRect area, float dt)
    {
        if (target == RoamingState.Spawn) return;
        if (target == RoamingState.EdgeTurn)
        {
            behavior.Reset(new Vector2(area.Left + 6f, area.Center.Y), MathF.PI);
        }
        if (behavior.State == target)
        {
            return;
        }

        ScenarioRunner.RunUntil(StepCount(90f, dt), dt, step =>
        {
            if (target == RoamingState.MouseChase)
            {
                behavior.Update(
                    step.DeltaTime,
                    area,
                    new PointerObservation(behavior.Position + new Vector2(220f, 0f), true));
            }
            else
            {
                behavior.Update(step.DeltaTime, area);
                if (target == RoamingState.EscapeSprint && step.StepIndex == 80)
                {
                    behavior.BeginGrab();
                    behavior.EndGrab(behavior.Position);
                }
            }
            return behavior.State != target;
        });
    }

    private static void CountFinite(ProceduralLizard lizard, BehaviorController? behavior, ref int count)
    {
        if (behavior is not null &&
            (!float.IsFinite(behavior.Position.X) || !float.IsFinite(behavior.Position.Y))) count++;
        foreach (var point in lizard.Spine.Joints)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) count++;
        }
        foreach (var leg in lizard.Legs)
        {
            if (!float.IsFinite(leg.Elbow.X) || !float.IsFinite(leg.Foot.X) ||
                !float.IsFinite(leg.Elbow.Y) || !float.IsFinite(leg.Foot.Y)) count++;
        }
    }

    private static void RunDanglingStress(
        ref int nonFinite,
        ref float minimumCanvasMargin,
        ref float maximumSpineError,
        ref float maximumUpperLegError,
        ref float maximumLowerLegError,
        ref float maximumGrabError)
    {
        const float dt = 1f / 120f;
        var lizard = new ProceduralLizard(DefaultProfile);
        var mood = EmotionBlend.Normalize(new EmotionBlend(0.25f, 0.35f, 0.30f, 0.10f));
        lizard.BeginGrab(lizard.Spine.Joints[0]);
        var measuredNonFinite = nonFinite;
        var measuredCanvasMargin = minimumCanvasMargin;
        var measuredSpineError = maximumSpineError;
        var measuredUpperLegError = maximumUpperLegError;
        var measuredLowerLegError = maximumLowerLegError;
        var measuredGrabError = maximumGrabError;
        ScenarioRunner.RunSteps(StepCount(30f, dt), dt, step =>
        {
            var frame = step.StepIndex;
            var impulse = frame % 420 == 0
                ? new Vector2(frame % 840 == 0 ? 76f : -76f, -18f)
                : new Vector2(MathF.Sin(frame * 0.071f) * 1.2f, MathF.Cos(frame * 0.043f) * 0.35f);
            lizard.Update(
                step.DeltaTime,
                new LizardAnimationInput(
                    0f,
                    0f,
                    LizardPoseMode.Grabbed,
                    mood,
                    0f,
                    impulse));
            CountFinite(lizard, null, ref measuredNonFinite);
            MeasureDanglingPose(
                lizard,
                ref measuredCanvasMargin,
                ref measuredSpineError,
                ref measuredUpperLegError,
                ref measuredLowerLegError,
                ref measuredGrabError);
        });
        nonFinite = measuredNonFinite;
        minimumCanvasMargin = measuredCanvasMargin;
        maximumSpineError = measuredSpineError;
        maximumUpperLegError = measuredUpperLegError;
        maximumLowerLegError = measuredLowerLegError;
        maximumGrabError = measuredGrabError;
    }

    private static int RunAttachmentCoverage(
        ref int nonFinite,
        ref float minimumCanvasMargin,
        ref float maximumSpineError,
        ref float maximumUpperLegError,
        ref float maximumLowerLegError,
        ref float maximumGrabError)
    {
        const float dt = 1f / 120f;
        var covered = 0;
        var measuredNonFinite = nonFinite;
        var measuredCanvasMargin = minimumCanvasMargin;
        var measuredSpineError = maximumSpineError;
        var measuredUpperLegError = maximumUpperLegError;
        var measuredLowerLegError = maximumLowerLegError;
        var measuredGrabError = maximumGrabError;
        for (var scenario = 0; scenario < 13; scenario++)
        {
            var lizard = new ProceduralLizard(DefaultProfile);
            Vector2 grabPoint;
            if (scenario == 0)
            {
                // Body surface rather than its centerline.
                grabPoint = lizard.GetBodyPoint(2, 1f);
            }
            else
            {
                var leg = lizard.Legs[(scenario - 1) / 3];
                grabPoint = ((scenario - 1) % 3) switch
                {
                    0 => Vector2.Lerp(leg.Shoulder, leg.Elbow, 0.5f),
                    1 => Vector2.Lerp(leg.Elbow, leg.Foot, 0.5f),
                    _ => leg.Foot
                };
            }

            lizard.BeginGrab(grabPoint);
            var valid = true;
            ScenarioRunner.RunSteps(StepCount(1.1f, dt), dt, step =>
            {
                var frame = step.StepIndex;
                var screenDelta = frame == 36
                    ? new Vector2(52f, -21f)
                    : new Vector2(MathF.Sin(frame * 0.083f) * 0.85f, MathF.Cos(frame * 0.057f) * 0.3f);
                lizard.Update(
                    step.DeltaTime,
                    new LizardAnimationInput(
                        0f,
                        0f,
                        LizardPoseMode.Grabbed,
                        EmotionBlend.Normalize(new EmotionBlend(0.2f, 0.3f, 0.4f, 0.1f)),
                        0f,
                        screenDelta));
                CountFinite(lizard, null, ref measuredNonFinite);
                MeasureDanglingPose(
                    lizard,
                    ref measuredCanvasMargin,
                    ref measuredSpineError,
                    ref measuredUpperLegError,
                    ref measuredLowerLegError,
                    ref measuredGrabError);
                valid &= lizard.DanglingGrabError <= 1.5f &&
                         lizard.DanglingConstraintError <= 1.5f;
            });
            if (valid)
            {
                covered++;
            }
        }

        nonFinite = measuredNonFinite;
        minimumCanvasMargin = measuredCanvasMargin;
        maximumSpineError = measuredSpineError;
        maximumUpperLegError = measuredUpperLegError;
        maximumLowerLegError = measuredLowerLegError;
        maximumGrabError = measuredGrabError;

        return covered;
    }

    private static void MeasureDanglingPose(
        ProceduralLizard lizard,
        ref float minimumCanvasMargin,
        ref float maximumSpineError,
        ref float maximumUpperLegError,
        ref float maximumLowerLegError,
        ref float maximumGrabError)
    {
        foreach (var point in lizard.Spine.Joints)
        {
            minimumCanvasMargin = Math.Min(
                minimumCanvasMargin,
                CanvasMargin(point, 28f, lizard.Profile.Appearance.RenderCanvasSize));
        }
        maximumSpineError = Math.Max(maximumSpineError, lizard.DanglingSpineError);
        maximumUpperLegError = Math.Max(maximumUpperLegError, lizard.DanglingUpperLegError);
        maximumLowerLegError = Math.Max(maximumLowerLegError, lizard.DanglingLowerLegError);
        foreach (var leg in lizard.Legs)
        {
            minimumCanvasMargin = Math.Min(
                minimumCanvasMargin,
                CanvasMargin(leg.Elbow, 18f, lizard.Profile.Appearance.RenderCanvasSize));
            minimumCanvasMargin = Math.Min(
                minimumCanvasMargin,
                CanvasMargin(leg.Foot, 22f, lizard.Profile.Appearance.RenderCanvasSize));
        }
        maximumGrabError = Math.Max(maximumGrabError, lizard.DanglingGrabError);
    }

    private static float CanvasMargin(Vector2 point, float padding, float canvasSize) => Math.Min(
        Math.Min(point.X - padding, canvasSize - point.X - padding),
        Math.Min(point.Y - padding, canvasSize - point.Y - padding));
}
