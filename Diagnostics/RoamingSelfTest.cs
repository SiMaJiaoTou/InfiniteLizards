using System.Numerics;
using DesktopLizard.Core;

namespace DesktopLizard.Diagnostics;

internal readonly record struct RoamingTestResult(
    bool Passed,
    int MovingSamples,
    float CurvedFraction,
    float TurnDensity,
    int Turnarounds,
    int BoundaryContacts,
    float MaximumAngularSpeed,
    float MaximumAngularAcceleration,
    int AutonomousStateCoverage,
    int RequiredAutonomousStateCount,
    int InteractionStateCoverage,
    int InvalidTransitions,
    int CompleteCycles,
    int DistinctSequences,
    int SCurveEpisodes,
    int ValidSCurveEpisodes,
    float MinimumSCurveLobeAngle,
    float MaximumSCurveHeadingBias,
    int MinimumSCurveReversals,
    float MinimumNormalSCurveTurnRadius,
    float MinimumFastSCurveTurnRadius,
    int NormalForwardEntries,
    int NormalSCurveEntries,
    int FastForwardEntries,
    int FastSCurveEntries,
    float MedianFastCrawlSpeed,
    float PeakFastCrawlSpeed,
    float TurnaroundActionRatio,
    float MedianForwardSpeed,
    float PeakSpeed,
    float MinimumCycleForwardDistance,
    float MinimumTurnaroundAngle,
    int RestDurationViolations,
    int BoundaryRecoveryViolations);

internal static class RoamingSelfTest
{
    private static BehaviorConfiguration DefaultBehavior => LizardProfile.Default.Behavior;

    private enum CycleStage
    {
        WaitingForForward,
        PreTurnForward,
        Turning,
        PostTurnForward
    }

    public static RoamingTestResult Run()
    {
        const float dt = 1f / 120f;
        var area = new FloatRect(110f, 110f, 1170f, 610f);
        var movingSamples = 0;
        var curvedSamples = 0;
        var turnarounds = 0;
        var boundaryContacts = 0;
        var distance = 0f;
        var absoluteTurn = 0f;
        var maximumAngularSpeed = 0f;
        var maximumAngularAcceleration = 0f;
        var invalidTransitions = 0;
        var completeCycles = 0;
        var sCurveEpisodes = 0;
        var validSCurveEpisodes = 0;
        var minimumSCurveLobeAngle = float.MaxValue;
        var maximumSCurveHeadingBias = 0f;
        var minimumSCurveReversals = int.MaxValue;
        var minimumNormalSCurveTurnRadius = float.MaxValue;
        var minimumFastSCurveTurnRadius = float.MaxValue;
        var normalForwardEntries = 0;
        var normalSCurveEntries = 0;
        var fastForwardEntries = 0;
        var fastSCurveEntries = 0;
        var steeringActionEntries = 0;
        var turnaroundEntries = 0;
        var restDurationViolations = 0;
        var minimumCycleForwardDistance = float.MaxValue;
        var minimumTurnaroundAngle = float.MaxValue;
        var peakSpeed = 0f;
        var forwardSpeeds = new List<float>();
        var fastCrawlSpeeds = new List<float>();
        var autonomousCoverage = new HashSet<RoamingState>();
        var sequenceSignatures = new HashSet<string>();

        foreach (var seed in new[] { 17, 41, 83, 127, 211, 307, 419, 557 })
        {
            var behavior = new BehaviorController(seed);
            behavior.Reset(area.Center, -0.18f);
            var previousPosition = behavior.Position;
            var previousHeading = behavior.Heading;
            var previousAngularSpeed = 0f;
            var previousState = behavior.State;
            var stateEntryReason = behavior.LastTransitionReason;
            var stateDwell = 0f;
            var stateDistance = 0f;
            var turnaroundAngle = 0f;
            var sCurvePositiveTurn = 0f;
            var sCurveNegativeTurn = 0f;
            var sCurveReversals = 0;
            var sCurveTurnSign = 0;
            var sCurveExpectedCycles = 0;
            var sCurveMinimumRadius = float.MaxValue;
            var cyclePreTurnDistance = 0f;
            var cyclePostTurnDistance = 0f;
            var cycleStage = CycleStage.WaitingForForward;
            var sequence = new List<string>();

            autonomousCoverage.Add(previousState);
            // Multi-wave S actions and the configured long-rest tail can each
            // legitimately occupy many seconds. A longer deterministic window
            // keeps rare fast-state coverage meaningful without increasing
            // their production probabilities.
            for (var frame = 0; frame < 180f / dt; frame++)
            {
                behavior.Update(dt, area);
                var headingDelta = MathEx.DeltaAngle(previousHeading, behavior.Heading);
                var angularSpeed = headingDelta / dt;
                var angularAcceleration = (angularSpeed - previousAngularSpeed) / dt;
                var frameDistance = Vector2.Distance(previousPosition, behavior.Position);

                stateDwell += dt;
                stateDistance += frameDistance;
                if (previousState == RoamingState.TurnAround)
                {
                    turnaroundAngle += MathF.Abs(headingDelta);
                }
                else if (previousState.IsSCurveCrawl())
                {
                    if (headingDelta >= 0f) sCurvePositiveTurn += headingDelta;
                    else sCurveNegativeTurn -= headingDelta;

                    if (MathF.Abs(angularSpeed) >= 0.035f)
                    {
                        var sign = Math.Sign(angularSpeed);
                        if (sCurveTurnSign != 0 && sign != sCurveTurnSign)
                        {
                            sCurveReversals++;
                        }
                        sCurveTurnSign = sign;
                    }
                    if (stateDwell >= 0.45f &&
                        frameDistance > 0.01f &&
                        MathF.Abs(headingDelta) > 0.000001f)
                    {
                        var radius = frameDistance / MathF.Abs(headingDelta);
                        sCurveMinimumRadius = Math.Min(sCurveMinimumRadius, radius);
                        if (previousState == RoamingState.FastSCurveCrawl)
                        {
                            minimumFastSCurveTurnRadius = Math.Min(
                                minimumFastSCurveTurnRadius,
                                radius);
                        }
                        else
                        {
                            minimumNormalSCurveTurnRadius = Math.Min(
                                minimumNormalSCurveTurnRadius,
                                radius);
                        }
                    }
                }

                maximumAngularSpeed = Math.Max(maximumAngularSpeed, MathF.Abs(angularSpeed));
                maximumAngularAcceleration = Math.Max(maximumAngularAcceleration, MathF.Abs(angularAcceleration));
                autonomousCoverage.Add(behavior.State);

                if (behavior.State.IsLocomoting() && behavior.Speed >= 8f)
                {
                    movingSamples++;
                    distance += frameDistance;
                    absoluteTurn += MathF.Abs(headingDelta);
                    if (frameDistance > 0.001f && MathF.Abs(headingDelta) / frameDistance >= 0.003f)
                    {
                        curvedSamples++;
                    }
                }

                if (behavior.State != previousState)
                {
                    if (!RoamingStateMachine.IsLegal(
                            previousState,
                            behavior.State,
                            behavior.LastTransitionReason))
                    {
                        invalidTransitions++;
                    }
                    if (IsDwellViolation(
                            previousState,
                            behavior.State,
                            stateEntryReason,
                            stateDwell))
                    {
                        restDurationViolations++;
                    }

                    if (IsProgressiveCrawl(previousState) && stateDwell >= 1.35f)
                    {
                        if (cycleStage == CycleStage.PreTurnForward)
                        {
                            cyclePreTurnDistance += stateDistance;
                        }
                        else if (cycleStage == CycleStage.PostTurnForward)
                        {
                            cyclePostTurnDistance += stateDistance;
                        }
                    }

                    if (previousState == RoamingState.TurnAround)
                    {
                        if (behavior.State == RoamingState.ForwardCrawl && turnaroundAngle >= 2.55f)
                        {
                            turnarounds++;
                            minimumTurnaroundAngle = Math.Min(minimumTurnaroundAngle, turnaroundAngle);
                            if (cycleStage == CycleStage.Turning)
                            {
                                cycleStage = CycleStage.PostTurnForward;
                            }
                        }
                        else if (behavior.State == RoamingState.EdgeTurn)
                        {
                            cycleStage = CycleStage.WaitingForForward;
                            cyclePreTurnDistance = 0f;
                            cyclePostTurnDistance = 0f;
                        }
                    }

                    if (previousState.IsSCurveCrawl() &&
                        behavior.State != RoamingState.EdgeTurn)
                    {
                        var minimumLobe = Math.Min(sCurvePositiveTurn, sCurveNegativeTurn);
                        var headingBias = MathF.Abs(sCurvePositiveTurn - sCurveNegativeTurn);
                        sCurveEpisodes++;
                        minimumSCurveLobeAngle = Math.Min(minimumSCurveLobeAngle, minimumLobe);
                        maximumSCurveHeadingBias = Math.Max(maximumSCurveHeadingBias, headingBias);
                        minimumSCurveReversals = Math.Min(minimumSCurveReversals, sCurveReversals);
                        if (previousState == RoamingState.FastSCurveCrawl)
                        {
                            minimumFastSCurveTurnRadius = Math.Min(
                                minimumFastSCurveTurnRadius,
                                sCurveMinimumRadius);
                        }
                        else
                        {
                            minimumNormalSCurveTurnRadius = Math.Min(
                                minimumNormalSCurveTurnRadius,
                                sCurveMinimumRadius);
                        }
                        if (minimumLobe >= 0.36f &&
                            headingBias <= 0.25f &&
                            sCurveExpectedCycles >= 2 &&
                            sCurveReversals >= sCurveExpectedCycles &&
                            float.IsFinite(sCurveMinimumRadius) &&
                            stateDistance >= 70f)
                        {
                            validSCurveEpisodes++;
                        }
                    }

                    if (behavior.State is RoamingState.CurveCrawl or
                        RoamingState.SCurveCrawl or
                        RoamingState.FastSCurveCrawl or
                        RoamingState.TurnAround)
                    {
                        steeringActionEntries++;
                        if (behavior.State == RoamingState.TurnAround)
                        {
                            turnaroundEntries++;
                        }
                    }

                    switch (behavior.State)
                    {
                        case RoamingState.ForwardCrawl:
                            normalForwardEntries++;
                            break;
                        case RoamingState.SCurveCrawl:
                            normalSCurveEntries++;
                            break;
                        case RoamingState.FastForwardCrawl:
                            fastForwardEntries++;
                            break;
                        case RoamingState.FastSCurveCrawl:
                            fastSCurveEntries++;
                            break;
                    }

                    if ((behavior.State is RoamingState.ForwardCrawl or RoamingState.FastForwardCrawl) &&
                        cycleStage == CycleStage.WaitingForForward)
                    {
                        cycleStage = CycleStage.PreTurnForward;
                        cyclePreTurnDistance = 0f;
                        cyclePostTurnDistance = 0f;
                    }
                    else if (behavior.State == RoamingState.TurnAround && cycleStage == CycleStage.PreTurnForward)
                    {
                        cycleStage = CycleStage.Turning;
                    }

                    if (behavior.State.IsResting())
                    {
                        if (cycleStage == CycleStage.PostTurnForward &&
                            cyclePreTurnDistance >= 45f &&
                            cyclePostTurnDistance >= 24f)
                        {
                            completeCycles++;
                            minimumCycleForwardDistance = Math.Min(
                                minimumCycleForwardDistance,
                                cyclePreTurnDistance + cyclePostTurnDistance);
                        }
                        cycleStage = CycleStage.WaitingForForward;
                        cyclePreTurnDistance = 0f;
                        cyclePostTurnDistance = 0f;
                    }

                    if (sequence.Count < 18)
                    {
                        sequence.Add(behavior.State.ToString());
                    }
                    stateDwell = 0f;
                    stateDistance = 0f;
                    turnaroundAngle = 0f;
                    sCurvePositiveTurn = 0f;
                    sCurveNegativeTurn = 0f;
                    sCurveReversals = 0;
                    sCurveTurnSign = 0;
                    sCurveMinimumRadius = float.MaxValue;
                    sCurveExpectedCycles = behavior.State.IsSCurveCrawl()
                        ? behavior.CaptureDebugSnapshot(area).SCurveCycleCount
                        : 0;
                    stateEntryReason = behavior.LastTransitionReason;
                }

                if (behavior.State == RoamingState.ForwardCrawl && stateDwell >= 0.60f)
                {
                    forwardSpeeds.Add(behavior.Speed);
                    peakSpeed = Math.Max(peakSpeed, behavior.Speed);
                }
                if (behavior.State.IsFastCrawl() && stateDwell >= 0.45f)
                {
                    fastCrawlSpeeds.Add(behavior.Speed);
                }

                if (!area.Contains(behavior.Position) ||
                    behavior.Position.X <= area.Left + 0.01f || behavior.Position.X >= area.Right - 0.01f ||
                    behavior.Position.Y <= area.Top + 0.01f || behavior.Position.Y >= area.Bottom - 0.01f)
                {
                    boundaryContacts++;
                }

                previousPosition = behavior.Position;
                previousHeading = behavior.Heading;
                previousAngularSpeed = angularSpeed;
                previousState = behavior.State;
            }
            sequenceSignatures.Add(string.Join(">", sequence));
        }

        var boundaryRecoveryViolations = RunBoundaryRecoveryChecks(area, dt);
        restDurationViolations += RunRestDurationMappingChecks();
        var transitionPolicyValid = RunAutonomousTransitionPolicyChecks();
        // EdgeTurn is a safety state, not a state that must be reached by a
        // centered random walk. Its dedicated four-edge scenarios below are
        // the deterministic coverage proof.
        if (boundaryRecoveryViolations == 0)
        {
            autonomousCoverage.Add(RoamingState.EdgeTurn);
        }
        var interactionCoverage = RunInteractionChecks(area, dt, out var interactionViolations);
        invalidTransitions += interactionViolations;
        forwardSpeeds.Sort();
        fastCrawlSpeeds.Sort();
        var medianForwardSpeed = forwardSpeeds.Count > 0
            ? forwardSpeeds[forwardSpeeds.Count / 2]
            : 0f;
        var medianFastCrawlSpeed = fastCrawlSpeeds.Count > 0
            ? fastCrawlSpeeds[fastCrawlSpeeds.Count / 2]
            : 0f;
        var peakFastCrawlSpeed = fastCrawlSpeeds.Count > 0
            ? fastCrawlSpeeds[^1]
            : 0f;
        var curvedFraction = movingSamples > 0 ? curvedSamples / (float)movingSamples : 0f;
        var turnDensity = distance > 0.001f ? absoluteTurn / distance : 0f;
        if (!float.IsFinite(minimumCycleForwardDistance)) minimumCycleForwardDistance = 0f;
        if (!float.IsFinite(minimumTurnaroundAngle)) minimumTurnaroundAngle = 0f;
        if (!float.IsFinite(minimumSCurveLobeAngle)) minimumSCurveLobeAngle = 0f;
        if (minimumSCurveReversals == int.MaxValue) minimumSCurveReversals = 0;
        if (minimumNormalSCurveTurnRadius == float.MaxValue ||
            !float.IsFinite(minimumNormalSCurveTurnRadius)) minimumNormalSCurveTurnRadius = 0f;
        if (minimumFastSCurveTurnRadius == float.MaxValue ||
            !float.IsFinite(minimumFastSCurveTurnRadius)) minimumFastSCurveTurnRadius = 0f;
        var turnaroundActionRatio = steeringActionEntries > 0
            ? turnaroundEntries / (float)steeringActionEntries
            : 1f;

        var requiredAutonomous = new[]
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
            RoamingState.EdgeTurn,
            RoamingState.LostGripFall
        };
        var passed =
            movingSamples >= 35_000 &&
            curvedFraction >= 0.18f &&
            turnDensity is >= 0.003f and <= 0.05f &&
            turnarounds is >= 4 and <= 20 &&
            boundaryContacts == 0 &&
            maximumAngularSpeed <= 1.15f &&
            maximumAngularAcceleration <= 150f &&
            requiredAutonomous.All(autonomousCoverage.Contains) &&
            interactionCoverage == 3 &&
            invalidTransitions == 0 &&
            completeCycles >= 3 &&
            sequenceSignatures.Count >= 4 &&
            sCurveEpisodes >= 12 &&
            validSCurveEpisodes == sCurveEpisodes &&
            minimumSCurveLobeAngle >= 0.40f &&
            maximumSCurveHeadingBias <= 0.20f &&
            minimumSCurveReversals >= 2 &&
            minimumNormalSCurveTurnRadius >= 75f &&
            minimumFastSCurveTurnRadius >= 180f &&
            fastForwardEntries >= 2 &&
            fastSCurveEntries >= 2 &&
            fastForwardEntries < normalForwardEntries &&
            fastSCurveEntries < normalSCurveEntries &&
            medianFastCrawlSpeed >= 95.0f &&
            peakFastCrawlSpeed >= 95.5f &&
            peakFastCrawlSpeed <= DefaultBehavior.Speed.MaximumCrawl + 0.05f &&
            turnaroundActionRatio is >= 0.03f and <= 0.22f &&
            medianForwardSpeed is >= 23f and <= 25.5f &&
            peakSpeed >= 24.5f &&
            peakSpeed <= DefaultBehavior.Speed.ReferenceMaximumCrawl + 0.05f &&
            minimumCycleForwardDistance >= 90f &&
            minimumTurnaroundAngle >= 2.55f &&
            restDurationViolations == 0 &&
            transitionPolicyValid &&
            boundaryRecoveryViolations == 0;

        return new RoamingTestResult(
            passed,
            movingSamples,
            curvedFraction,
            turnDensity,
            turnarounds,
            boundaryContacts,
            maximumAngularSpeed,
            maximumAngularAcceleration,
            requiredAutonomous.Count(autonomousCoverage.Contains),
            requiredAutonomous.Length,
            interactionCoverage,
            invalidTransitions,
            completeCycles,
            sequenceSignatures.Count,
            sCurveEpisodes,
            validSCurveEpisodes,
            minimumSCurveLobeAngle,
            maximumSCurveHeadingBias,
            minimumSCurveReversals,
            minimumNormalSCurveTurnRadius,
            minimumFastSCurveTurnRadius,
            normalForwardEntries,
            normalSCurveEntries,
            fastForwardEntries,
            fastSCurveEntries,
            medianFastCrawlSpeed,
            peakFastCrawlSpeed,
            turnaroundActionRatio,
            medianForwardSpeed,
            peakSpeed,
            minimumCycleForwardDistance,
            minimumTurnaroundAngle,
            restDurationViolations,
            boundaryRecoveryViolations);
    }

    private static int RunBoundaryRecoveryChecks(FloatRect area, float dt)
    {
        var cases = new[]
        {
            (new Vector2(area.Left + 6f, area.Center.Y), MathF.PI),
            (new Vector2(area.Right - 6f, area.Center.Y), 0f),
            (new Vector2(area.Center.X, area.Top + 6f), -MathF.PI * 0.5f),
            (new Vector2(area.Center.X, area.Bottom - 6f), MathF.PI * 0.5f)
        };
        var violations = 0;
        for (var index = 0; index < cases.Length; index++)
        {
            var behavior = new BehaviorController(9001 + index * 101);
            behavior.Reset(cases[index].Item1, cases[index].Item2);
            var enteredAvoidance = false;
            var recoveredToForward = false;
            for (var frame = 0; frame < 8f / dt; frame++)
            {
                behavior.Update(dt, area);
                enteredAvoidance |= behavior.State == RoamingState.EdgeTurn;
                recoveredToForward |= enteredAvoidance && behavior.State == RoamingState.ForwardCrawl;
                if (!area.Contains(behavior.Position))
                {
                    violations++;
                    break;
                }
            }
            if (!enteredAvoidance || !recoveredToForward) violations++;
        }
        return violations;
    }

    private static int RunInteractionChecks(FloatRect area, float dt, out int violations)
    {
        violations = 0;
        var covered = new HashSet<RoamingState>();
        var behavior = new BehaviorController(55119);
        behavior.Reset(area.Center, 0f);
        behavior.BeginGrab();
        covered.Add(behavior.State);
        if (behavior.State != RoamingState.Grabbed) violations++;
        var dropPoint = area.Center + new Vector2(30f, 20f);
        behavior.DragTo(dropPoint);
        behavior.EndGrab(dropPoint);
        covered.Add(behavior.State);
        if (behavior.State != RoamingState.ReleaseSettle) violations++;
        for (var frame = 0; frame < 0.12f / dt; frame++) behavior.Update(dt, area);
        for (var frame = 0; frame < 0.35f / dt; frame++)
        {
            behavior.Update(dt, area);
            covered.Add(behavior.State);
        }
        if (behavior.State != RoamingState.EscapeSprint) violations++;
        return covered.Count;
    }

    private static bool IsDwellViolation(
        RoamingState state,
        RoamingState destination,
        StateTransitionReason entryReason,
        float duration)
    {
        var interruptedByEdge = destination == RoamingState.EdgeTurn;
        return state switch
        {
            RoamingState.Spawn => duration is < 0.44f or > 0.56f,
            RoamingState.Idle =>
                entryReason != StateTransitionReason.FallComplete &&
                (duration < 0.38f || duration > DefaultBehavior.Rest.MaximumDuration + 0.05f),
            RoamingState.Observe => duration is < 0.50f or > 1.08f,
            RoamingState.ForwardCrawl => !interruptedByEdge && duration is < 1.75f or > 8.20f,
            RoamingState.FastForwardCrawl =>
                !interruptedByEdge &&
                (duration < DefaultBehavior.FastForward.MinimumDuration - 0.05f ||
                 duration > DefaultBehavior.FastForward.MaximumDuration + 0.05f),
            RoamingState.CurveCrawl => !interruptedByEdge && duration is < 1.35f or > 2.65f,
            RoamingState.SCurveCrawl =>
                !interruptedByEdge &&
                (duration < DefaultBehavior.SCurve.Normal.MinimumDuration(DefaultBehavior.SCurve.SettleDuration) - 0.05f ||
                 duration > DefaultBehavior.SCurve.Normal.MaximumDuration(DefaultBehavior.SCurve.SettleDuration) + 0.05f),
            RoamingState.FastSCurveCrawl =>
                !interruptedByEdge &&
                (duration < DefaultBehavior.SCurve.Fast.MinimumDuration(DefaultBehavior.SCurve.SettleDuration) - 0.05f ||
                 duration > DefaultBehavior.SCurve.Fast.MaximumDuration(DefaultBehavior.SCurve.SettleDuration) + 0.05f),
            RoamingState.TurnAround => !interruptedByEdge && duration is < 3.80f or > 5.65f,
            RoamingState.EdgeTurn => duration > 4.25f,
            _ => false
        };
    }

    private static bool IsProgressiveCrawl(RoamingState state) => state is
        RoamingState.ForwardCrawl or
        RoamingState.FastForwardCrawl or
        RoamingState.CurveCrawl or
        RoamingState.SCurveCrawl or
        RoamingState.FastSCurveCrawl;

    private static int RunRestDurationMappingChecks()
    {
        var rest = DefaultBehavior.Rest;
        var violations = 0;
        var previous = float.NegativeInfinity;
        for (var index = 0; index <= 1_000; index++)
        {
            var duration = RestDurationDistribution.Sample(index / 1_000f, rest);
            if (!float.IsFinite(duration) ||
                duration < rest.MinimumDuration - 0.0001f ||
                duration > rest.MaximumDuration + 0.0001f ||
                duration + 0.0001f < previous)
            {
                violations++;
                break;
            }
            previous = duration;
        }

        var totalWeight = rest.Bands.Sum(band => band.Weight);
        if (MathF.Abs(totalWeight - 1f) > 0.0001f ||
            MathF.Abs(RestDurationDistribution.Sample(0f, rest) - rest.MinimumDuration) > 0.0001f ||
            MathF.Abs(RestDurationDistribution.Sample(1f, rest) - rest.MaximumDuration) > 0.0001f)
        {
            violations++;
        }

        var cumulativeWeight = 0f;
        foreach (var band in rest.Bands)
        {
            cumulativeWeight += band.Weight;
            if (MathF.Abs(
                    RestDurationDistribution.Sample(cumulativeWeight, rest) -
                    band.MaximumDuration) > 0.0001f)
            {
                violations++;
                break;
            }
        }

        return violations;
    }

    private static bool RunAutonomousTransitionPolicyChecks()
    {
        var matrix = DefaultBehavior.TransitionMatrix;
        if (!AutonomousTransitionPolicy.ValidateConfiguration(matrix, out _))
        {
            return false;
        }

        var extensionSample = FindConfiguredSample(
            matrix.AfterForward,
            AutonomousAction.ForwardExtension);
        var defaultsPreserved =
            RowMatchesConfiguredWeights(
                matrix.AfterForward,
                sample => AutonomousTransitionPolicy.ChooseAfterForward(sample, false)) &&
            RowMatchesConfiguredWeights(
                matrix.AfterCurve,
                AutonomousTransitionPolicy.ChooseAfterCurve) &&
            RowMatchesConfiguredWeights(
                matrix.AfterSCurve,
                AutonomousTransitionPolicy.ChooseAfterSCurve) &&
            RowMatchesConfiguredWeights(
                matrix.AfterFast,
                AutonomousTransitionPolicy.ChooseAfterFast) &&
            extensionSample.HasValue &&
            AutonomousTransitionPolicy.ChooseAfterForward(extensionSample.Value, true) ==
                AutonomousAction.SCurve;

        // Prove that transition rows are actual per-profile data, not merely a
        // serialized facade over hard-coded thresholds. Each extreme row has
        // one deterministic outcome for every possible unit sample.
        var forcedMatrix = new TransitionMatrixConfiguration
        {
            AfterForward = ForcedRow(AutonomousAction.ForwardExtension),
            AfterCurve = ForcedRow(AutonomousAction.TurnAround),
            AfterSCurve = ForcedRow(AutonomousAction.FastSCurve),
            AfterFast = ForcedRow(AutonomousAction.Curve)
        };
        var injectedProfile = LizardProfile.Default with
        {
            Behavior = LizardProfile.Default.Behavior with
            {
                TransitionMatrix = forcedMatrix
            }
        };
        var injectedController = new BehaviorController(77891, injectedProfile);
        var forcedMatrixWorks =
            AutonomousTransitionPolicy.ValidateConfiguration(forcedMatrix, out _) &&
            ReferenceEquals(injectedController.Configuration.TransitionMatrix, forcedMatrix) &&
            AutonomousTransitionPolicy.ChooseAfterForward(0f, false, forcedMatrix) ==
                AutonomousAction.ForwardExtension &&
            AutonomousTransitionPolicy.ChooseAfterForward(0.999999f, true, forcedMatrix) ==
                AutonomousAction.SCurve &&
            AutonomousTransitionPolicy.ChooseAfterCurve(0.73f, forcedMatrix) ==
                AutonomousAction.TurnAround &&
            AutonomousTransitionPolicy.ChooseAfterSCurve(0.51f, forcedMatrix) ==
                AutonomousAction.FastSCurve &&
            AutonomousTransitionPolicy.ChooseAfterFast(1f, forcedMatrix) ==
                AutonomousAction.Curve;

        return defaultsPreserved && forcedMatrixWorks;
    }

    private static bool RowMatchesConfiguredWeights(
        TransitionRowConfiguration row,
        Func<float, AutonomousAction> choose)
    {
        var cumulativeWeight = 0f;
        foreach (var entry in row.Entries)
        {
            if (entry.Weight > 0f)
            {
                var sample = cumulativeWeight + entry.Weight * 0.5f;
                if (choose(sample) != entry.Action)
                {
                    return false;
                }
            }
            cumulativeWeight += entry.Weight;
        }
        return true;
    }

    private static float? FindConfiguredSample(
        TransitionRowConfiguration row,
        AutonomousAction action)
    {
        var cumulativeWeight = 0f;
        foreach (var entry in row.Entries)
        {
            if (entry.Action == action && entry.Weight > 0f)
            {
                return cumulativeWeight + entry.Weight * 0.5f;
            }
            cumulativeWeight += entry.Weight;
        }
        return null;
    }

    private static TransitionRowConfiguration ForcedRow(AutonomousAction action) => new()
    {
        Entries = [new WeightedTransitionConfiguration(action, 1f)]
    };
}
