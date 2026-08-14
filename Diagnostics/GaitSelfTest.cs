using System.Numerics;
using DesktopLizard.Core;

namespace DesktopLizard.Diagnostics;

internal readonly record struct GaitTestResult(
    bool Passed,
    int StepTransitions,
    int PairSequenceViolations,
    int MaximumSwingingLegs,
    int PlantedSamples,
    float MaximumPlantedDrift,
    float MaximumPlantedDriftSpeed,
    int MaximumPlantedDriftLeg,
    float MaximumReachOverflow,
    int ReachProjections,
    int StepTargetClamps,
    int SwingClamps,
    float MaximumReachCorrection,
    int MaximumReachCorrectionFrame,
    float MaximumStepGap,
    float MaximumHighSpeedStepGap,
    float AverageHighSpeedStepGap,
    float MinimumAllFeetSupport,
    float AverageHighSpeedAllFeetSupport,
    float AverageReferencePairGap,
    float AverageReferenceAllFeetSupport,
    float AverageVisualPairStagger,
    float MaximumVisualPairStagger,
    float AverageReferenceWorldStride,
    float AverageHighSpeedWorldStride,
    float AverageCurvePairSpanRatio,
    float CurveOuterInnerSpanRatio,
    float TightTurnPlantedDrift,
    int TightTurnReachCorrections,
    string TightTurnCorrectionBreakdown,
    float AverageHighSpeedStepSpan,
    float AverageHighSpeedStepHeight,
    int FastSCurveCasesPassed,
    int FastSCurveCaseCount,
    int FastSCurveStepTransitions,
    int FastSCurvePairSequenceViolations,
    int FastSCurveMaximumSwingingLegs,
    float FastSCurveMaximumPlantedDrift,
    int FastSCurveReachProjections,
    int FastSCurveStepTargetClamps,
    int FastSCurveSwingClamps,
    float FastSCurveMinimumPeakSpeed,
    float FastSCurveMinimumLobeAngle,
    float FastSCurveMaximumHeadingBias,
    int FastSCurveNonFiniteSamples,
    string FastSCurveFailureDetails,
    bool ProfileSmokePassed,
    bool ProfileSmokeApplied,
    int ProfileSmokeStepTransitions,
    int ProfileSmokeNonFiniteSamples,
    float ProfileSmokeMaximumConstraintError,
    float ProfileSmokeMaximumGrabError,
    string ProfileSmokeFailureDetail);

internal static class GaitSelfTest
{
    private static LizardProfile DefaultProfile => LizardProfile.Default;
    private static BehaviorConfiguration DefaultBehavior => DefaultProfile.Behavior;

    private readonly record struct TightTurnResult(
        float PairSpanRatio,
        float OuterInnerSpanRatio,
        float MaximumPlantedDrift,
        int ReachCorrections,
        string CorrectionBreakdown);

    private readonly record struct FastSCurveStressResult(
        int CasesPassed,
        int CaseCount,
        int StepTransitions,
        int PairSequenceViolations,
        int MaximumSwingingLegs,
        float MaximumPlantedDrift,
        int ReachProjections,
        int StepTargetClamps,
        int SwingClamps,
        float MinimumPeakSpeed,
        float MinimumLobeAngle,
        float MaximumHeadingBias,
        int NonFiniteSamples,
        string FailureDetails);

    public static GaitTestResult Run()
    {
        var lizard = new ProceduralLizard(DefaultProfile);
        var speed = DefaultBehavior.Speed;
        var mood = EmotionBlend.Normalize(new EmotionBlend(0.3f, 0.3f, 0.3f, 0.1f));
        var lastFeet = lizard.Legs.Select(leg => leg.Foot).ToArray();
        var lastStepping = new bool[lizard.Legs.Count];
        var lastStepSerial = new int[lizard.Legs.Count];
        var plantedSamples = 0;
        var plantedDriftMaximum = 0f;
        var plantedDriftSpeed = 0f;
        var plantedDriftLeg = -1;
        var pairSequenceViolations = 0;
        var maximumSwingingLegs = 0;
        var lastStartedPair = -1;
        var stepTransitions = 0;
        var maximumReachOverflow = 0f;
        var maximumReachCorrection = 0f;
        var maximumReachCorrectionFrame = -1;
        var lastStepFrame = -1;
        var maximumStepGap = 0f;
        var maximumHighSpeedStepGap = 0f;
        var highSpeedGapTotal = 0f;
        var highSpeedGapSamples = 0;
        var minimumAllFeetSupport = float.MaxValue;
        var highSpeedSupportTotal = 0f;
        var highSpeedSupportSamples = 0;
        var referenceGapTotal = 0f;
        var referenceGapSamples = 0;
        var referenceSupportTotal = 0f;
        var referenceSupportSamples = 0;
        var allFeetDownStartFrame = -1;
        var previousSwingingLegs = 0;
        var highSpeedSpanTotal = 0f;
        var highSpeedHeightTotal = 0f;
        var highSpeedStepSamples = 0;
        var previousStartWasHighSpeed = false;
        var previousStartWasReference = false;
        var visualPair = -1;
        var visualPairIsReference = false;
        var visualPairStartFrames = new[] { -1, -1 };
        var visualStaggerTotal = 0f;
        var visualStaggerMaximum = 0f;
        var visualStaggerSamples = 0;
        var worldOffset = Vector2.Zero;
        var lastWorldLandings = new Vector2?[lizard.Legs.Count];
        var referenceWorldStrideTotal = 0f;
        var referenceWorldStrideSamples = 0;
        var highSpeedWorldStrideTotal = 0f;
        var highSpeedWorldStrideSamples = 0;
        var curvePairRatioTotal = 0f;
        var curvePairRatioSamples = 0;
        var curveOuterSpanTotal = 0f;
        var curveOuterSpanSamples = 0;
        var curveInnerSpanTotal = 0f;
        var curveInnerSpanSamples = 0;
        var previousCommandHeading = 0f;
        const float simulationRate = 120f;
        const float dt = 1f / simulationRate;

        for (var frame = 0; frame < 4800; frame++)
        {
            var targetDisplaySpeed = frame switch
            {
                < 1200 => speed.ReferenceMinimumCrawl,
                < 2400 => speed.ReferenceMaximumCrawl,
                < 3600 => 72f,
                _ => speed.MaximumCrawl
            };
            var previousDisplaySpeed = frame switch
            {
                < 1200 => speed.ReferenceMinimumCrawl,
                < 2400 => speed.ReferenceMinimumCrawl,
                < 3600 => speed.ReferenceMaximumCrawl,
                _ => 72f
            };
            var segmentFrame = frame % 1200;
            var speedBlend = frame < 1200
                ? 1f
                : SmoothStep(MathEx.Clamp01(segmentFrame / 120f));
            var displaySpeed = MathEx.Lerp(previousDisplaySpeed, targetDisplaySpeed, speedBlend);
            // Match each speed band to a path the behavior state machine can request:
            // tight turns happen at crawl speed, curves slow down, and the 96.2 px/s
            // ceiling is used by the two explicit fast states and reactive bursts.
            var targetHeadingAmplitude = frame switch
            {
                < 1200 => 0.055f,
                < 2400 => 0.40f,
                < 3600 => 0.12f,
                _ => 0.04f
            };
            var previousHeadingAmplitude = frame switch
            {
                < 1200 => 0.055f,
                < 2400 => 0.055f,
                < 3600 => 0.40f,
                _ => 0.12f
            };
            var headingAmplitude = MathEx.Lerp(previousHeadingAmplitude, targetHeadingAmplitude, speedBlend);
            var heading = MathF.Sin(frame / 184f) * headingAmplitude;
            var commandTurnRate = MathEx.DeltaAngle(previousCommandHeading, heading) / dt;
            previousCommandHeading = heading;
            var delta = MathEx.FromAngle(heading) *
                        (displaySpeed * dt / lizard.Profile.Appearance.VisualScale);
            lizard.Update(
                dt,
                new LizardAnimationInput(
                    heading,
                    MathEx.Clamp01(displaySpeed / speed.AnimationNormalization),
                    LizardPoseMode.Locomotion,
                    mood,
                    0f,
                    delta));
            worldOffset += delta;

            var startedLegs = lizard.Legs
                .Where(leg => leg.StepSerial != lastStepSerial[leg.Index])
                .ToArray();
            var swingingLegs = lizard.Legs.Count(leg => leg.IsStepping);
            if (startedLegs.Length > 0)
            {
                var startedPairs = startedLegs.Select(leg => leg.Pair).Distinct().ToArray();
                if (startedLegs.Length != 2 || startedPairs.Length != 1)
                {
                    pairSequenceViolations++;
                }
                else
                {
                    var pair = startedPairs[0];
                    if (lastStartedPair >= 0 && pair != 1 - lastStartedPair)
                    {
                        pairSequenceViolations++;
                    }
                    lastStartedPair = pair;
                    if (lastStepFrame >= 0)
                    {
                        var gap = (frame - lastStepFrame) / simulationRate;
                        maximumStepGap = Math.Max(maximumStepGap, gap);
                        var stableReference = displaySpeed <= speed.ReferenceMaximumCrawl + 0.1f &&
                                              segmentFrame >= 180;
                        if (stableReference && previousStartWasReference)
                        {
                            referenceGapTotal += gap;
                            referenceGapSamples++;
                        }
                        if (displaySpeed >= 72f)
                        {
                            maximumHighSpeedStepGap = Math.Max(maximumHighSpeedStepGap, gap);
                            if (previousStartWasHighSpeed)
                            {
                                highSpeedGapTotal += gap;
                                highSpeedGapSamples++;
                            }
                        }
                    }
                    if (displaySpeed >= 72f)
                    {
                        foreach (var leg in startedLegs)
                        {
                            highSpeedSpanTotal += leg.StepSpan;
                            highSpeedHeightTotal += leg.StepHeight;
                            highSpeedStepSamples++;
                        }
                    }
                    if (frame is >= 1380 and < 2400 && MathF.Abs(commandTurnRate) >= 0.05f)
                    {
                        var minimumSpan = startedLegs.Min(leg => leg.StepSpan);
                        var maximumSpan = startedLegs.Max(leg => leg.StepSpan);
                        if (minimumSpan > 0.01f)
                        {
                            curvePairRatioTotal += maximumSpan / minimumSpan;
                            curvePairRatioSamples++;
                        }
                        var insideSide = Math.Sign(commandTurnRate);
                        foreach (var leg in startedLegs)
                        {
                            if (leg.Side == insideSide)
                            {
                                curveInnerSpanTotal += leg.StepSpan;
                                curveInnerSpanSamples++;
                            }
                            else
                            {
                                curveOuterSpanTotal += leg.StepSpan;
                                curveOuterSpanSamples++;
                            }
                        }
                    }
                    previousStartWasHighSpeed = displaySpeed >= 72f;
                    previousStartWasReference = displaySpeed <= speed.ReferenceMaximumCrawl + 0.1f &&
                                                segmentFrame >= 180;
                    lastStepFrame = frame;
                    stepTransitions++;
                    visualPair = pair;
                    visualPairIsReference = displaySpeed <= speed.ReferenceMaximumCrawl + 0.1f &&
                                            segmentFrame >= 180;
                    visualPairStartFrames[0] = -1;
                    visualPairStartFrames[1] = -1;
                    if (allFeetDownStartFrame >= 0)
                    {
                        var supportDuration = (frame - allFeetDownStartFrame) / simulationRate;
                        minimumAllFeetSupport = Math.Min(minimumAllFeetSupport, supportDuration);
                        if (previousStartWasReference)
                        {
                            referenceSupportTotal += supportDuration;
                            referenceSupportSamples++;
                        }
                        if (displaySpeed >= 72f)
                        {
                            highSpeedSupportTotal += supportDuration;
                            highSpeedSupportSamples++;
                        }
                    }
                    allFeetDownStartFrame = -1;
                }
            }

            maximumSwingingLegs = Math.Max(maximumSwingingLegs, swingingLegs);
            if (swingingLegs > 2)
            {
                pairSequenceViolations++;
            }
            if (previousSwingingLegs > 0 && swingingLegs == 0)
            {
                allFeetDownStartFrame = frame;
            }
            previousSwingingLegs = swingingLegs;
            if (swingingLegs > 0 && lizard.Legs.Where(leg => leg.IsStepping).Select(leg => leg.Pair).Distinct().Count() != 1)
            {
                pairSequenceViolations++;
            }

            if (visualPair >= 0)
            {
                var pairLegs = lizard.Legs.Where(leg => leg.Pair == visualPair).ToArray();
                for (var pairLegIndex = 0; pairLegIndex < pairLegs.Length; pairLegIndex++)
                {
                    if (visualPairStartFrames[pairLegIndex] < 0 && pairLegs[pairLegIndex].IsSwingMoving)
                    {
                        visualPairStartFrames[pairLegIndex] = frame;
                    }
                }
                if (visualPairStartFrames[0] >= 0 && visualPairStartFrames[1] >= 0)
                {
                    var stagger = MathF.Abs(visualPairStartFrames[0] - visualPairStartFrames[1]) / simulationRate;
                    if (visualPairIsReference)
                    {
                        visualStaggerTotal += stagger;
                        visualStaggerMaximum = Math.Max(visualStaggerMaximum, stagger);
                        visualStaggerSamples++;
                    }
                    visualPair = -1;
                }
            }

            for (var i = 0; i < lizard.Legs.Count; i++)
            {
                var leg = lizard.Legs[i];
                maximumReachOverflow = Math.Max(
                    maximumReachOverflow,
                    Vector2.Distance(leg.Shoulder, leg.Foot) - leg.MaximumReach);
                if (leg.LastReachCorrection > maximumReachCorrection)
                {
                    maximumReachCorrection = leg.LastReachCorrection;
                    maximumReachCorrectionFrame = frame;
                }
                if (!leg.IsStepping && !lastStepping[i])
                {
                    plantedSamples++;
                    var expectedLocalShift = -delta;
                    var actualShift = leg.Foot - lastFeet[i];
                    var plantedDrift = Vector2.Distance(actualShift, expectedLocalShift);
                    if (plantedDrift > plantedDriftMaximum)
                    {
                        plantedDriftMaximum = plantedDrift;
                        plantedDriftSpeed = displaySpeed;
                        plantedDriftLeg = i;
                    }
                }
                if (!leg.IsStepping && lastStepping[i])
                {
                    var worldLanding = leg.Foot + worldOffset;
                    if (lastWorldLandings[i] is { } lastWorldLanding)
                    {
                        var stride = Vector2.Distance(worldLanding, lastWorldLanding) * lizard.Profile.Appearance.VisualScale;
                        if (displaySpeed <= speed.ReferenceMaximumCrawl + 0.1f && segmentFrame >= 180)
                        {
                            referenceWorldStrideTotal += stride;
                            referenceWorldStrideSamples++;
                        }
                        else if (displaySpeed >= 72f && segmentFrame >= 180)
                        {
                            highSpeedWorldStrideTotal += stride;
                            highSpeedWorldStrideSamples++;
                        }
                    }
                    lastWorldLandings[i] = worldLanding;
                }
                lastFeet[i] = leg.Foot;
                lastStepping[i] = leg.IsStepping;
                lastStepSerial[i] = leg.StepSerial;
            }
        }

        var averageHighSpeedStepGap = highSpeedGapSamples > 0 ? highSpeedGapTotal / highSpeedGapSamples : 0f;
        var averageHighSpeedAllFeetSupport = highSpeedSupportSamples > 0
            ? highSpeedSupportTotal / highSpeedSupportSamples
            : 0f;
        var averageReferencePairGap = referenceGapSamples > 0 ? referenceGapTotal / referenceGapSamples : 0f;
        var averageReferenceAllFeetSupport = referenceSupportSamples > 0
            ? referenceSupportTotal / referenceSupportSamples
            : 0f;
        var averageVisualPairStagger = visualStaggerSamples > 0 ? visualStaggerTotal / visualStaggerSamples : 0f;
        var averageReferenceWorldStride = referenceWorldStrideSamples > 0
            ? referenceWorldStrideTotal / referenceWorldStrideSamples
            : 0f;
        var averageHighSpeedWorldStride = highSpeedWorldStrideSamples > 0
            ? highSpeedWorldStrideTotal / highSpeedWorldStrideSamples
            : 0f;
        var averageCurvePairSpanRatio = curvePairRatioSamples > 0
            ? curvePairRatioTotal / curvePairRatioSamples
            : 0f;
        var averageCurveOuterSpan = curveOuterSpanSamples > 0 ? curveOuterSpanTotal / curveOuterSpanSamples : 0f;
        var averageCurveInnerSpan = curveInnerSpanSamples > 0 ? curveInnerSpanTotal / curveInnerSpanSamples : 0f;
        var curveOuterInnerSpanRatio = averageCurveInnerSpan > 0.01f
            ? averageCurveOuterSpan / averageCurveInnerSpan
            : 0f;
        var tightTurn = RunTightTurnCheck();
        var fastSCurve = RunFastSCurveStressCheck();
        var profileSmoke = AnimationProfileSmokeTest.Run();
        averageCurvePairSpanRatio = tightTurn.PairSpanRatio;
        curveOuterInnerSpanRatio = tightTurn.OuterInnerSpanRatio;
        var averageHighSpeedStepSpan = highSpeedStepSamples > 0 ? highSpeedSpanTotal / highSpeedStepSamples : 0f;
        var averageHighSpeedStepHeight = highSpeedStepSamples > 0 ? highSpeedHeightTotal / highSpeedStepSamples : 0f;
        var stepTargetClamps = lizard.Legs.Sum(leg => leg.StepTargetClampSerial);
        var swingClamps = lizard.Legs.Sum(leg => leg.SwingClampSerial);
        var maximumKinematicCorrection = lizard.Legs.Max(leg => leg.MaximumKinematicCorrection);
        maximumReachCorrection = Math.Max(maximumReachCorrection, maximumKinematicCorrection);
        var passed = stepTransitions is >= 105 and <= 170 && pairSequenceViolations == 0 && maximumSwingingLegs == 2 &&
                     plantedDriftMaximum <= 0.01f && maximumReachOverflow <= 0.01f &&
                     lizard.Legs.Sum(leg => leg.ReachProjectionSerial) == 0 &&
                     stepTargetClamps == 0 && swingClamps == 0 && maximumReachCorrection <= 0.01f &&
                     maximumStepGap <= 0.73f && maximumHighSpeedStepGap <= 0.26f &&
                     averageReferencePairGap is >= 0.60f and <= 0.72f &&
                     averageReferenceAllFeetSupport is >= 0.42f and <= 0.56f &&
                     averageHighSpeedStepGap is >= 0.165f and <= 0.240f &&
                     minimumAllFeetSupport >= 0.008f &&
                     averageHighSpeedAllFeetSupport is >= 0.055f and <= 0.150f &&
                     averageVisualPairStagger is >= 0.008f and <= 0.040f &&
                     visualStaggerMaximum <= 0.050f &&
                     averageReferenceWorldStride is >= 25f and <= 38f &&
                      averageHighSpeedWorldStride is >= 25f and <= 40f &&
                      averageCurvePairSpanRatio is >= 1.80f and <= 3.20f &&
                      tightTurn.MaximumPlantedDrift <= 0.01f && tightTurn.ReachCorrections == 0 &&
                      averageHighSpeedStepSpan >= 42f && averageHighSpeedStepHeight >= 8f &&
                      fastSCurve.CasesPassed == fastSCurve.CaseCount &&
                      fastSCurve.CaseCount == 4 &&
                      fastSCurve.PairSequenceViolations == 0 &&
                      fastSCurve.MaximumSwingingLegs == 2 &&
                      fastSCurve.MaximumPlantedDrift <= 0.01f &&
                      fastSCurve.ReachProjections == 0 &&
                      fastSCurve.StepTargetClamps == 0 &&
                      fastSCurve.SwingClamps == 0 &&
                      fastSCurve.MinimumPeakSpeed >= speed.MaximumCrawl - 0.05f &&
                      fastSCurve.MinimumLobeAngle >= 0.36f &&
                      fastSCurve.MaximumHeadingBias <= 0.25f &&
                      fastSCurve.NonFiniteSamples == 0 &&
                      profileSmoke.Passed;
        return new GaitTestResult(
            passed,
            stepTransitions,
            pairSequenceViolations,
            maximumSwingingLegs,
            plantedSamples,
            plantedDriftMaximum,
            plantedDriftSpeed,
            plantedDriftLeg,
            Math.Max(0f, maximumReachOverflow),
            lizard.Legs.Sum(leg => leg.ReachProjectionSerial),
            stepTargetClamps,
            swingClamps,
            maximumReachCorrection,
            maximumReachCorrectionFrame,
            maximumStepGap,
            maximumHighSpeedStepGap,
            averageHighSpeedStepGap,
            minimumAllFeetSupport == float.MaxValue ? 0f : minimumAllFeetSupport,
            averageHighSpeedAllFeetSupport,
            averageReferencePairGap,
            averageReferenceAllFeetSupport,
            averageVisualPairStagger,
            visualStaggerMaximum,
            averageReferenceWorldStride,
            averageHighSpeedWorldStride,
            averageCurvePairSpanRatio,
            curveOuterInnerSpanRatio,
            tightTurn.MaximumPlantedDrift,
            tightTurn.ReachCorrections,
            tightTurn.CorrectionBreakdown,
            averageHighSpeedStepSpan,
            averageHighSpeedStepHeight,
            fastSCurve.CasesPassed,
            fastSCurve.CaseCount,
            fastSCurve.StepTransitions,
            fastSCurve.PairSequenceViolations,
            fastSCurve.MaximumSwingingLegs,
            fastSCurve.MaximumPlantedDrift,
            fastSCurve.ReachProjections,
            fastSCurve.StepTargetClamps,
            fastSCurve.SwingClamps,
            fastSCurve.MinimumPeakSpeed,
            fastSCurve.MinimumLobeAngle,
            fastSCurve.MaximumHeadingBias,
            fastSCurve.NonFiniteSamples,
            fastSCurve.FailureDetails,
            profileSmoke.Passed,
            profileSmoke.ProfileApplied,
            profileSmoke.StepTransitions,
            profileSmoke.NonFiniteSamples,
            profileSmoke.MaximumConstraintError,
            profileSmoke.MaximumGrabError,
            profileSmoke.FailureDetail);
    }

    private static TightTurnResult RunTightTurnCheck()
    {
        const float simulationRate = 120f;
        const float dt = 1f / simulationRate;
        var displaySpeed = (DefaultBehavior.Speed.ReferenceMinimumCrawl +
                            DefaultBehavior.Speed.ReferenceMaximumCrawl) * 0.5f;
        const float warmupDuration = 3f;
        var turnDuration = DefaultBehavior.Locomotion.TurnAroundMaximumDuration;
        var turnRate = (DefaultBehavior.Locomotion.TurnAroundMinimumRate +
                        DefaultBehavior.Locomotion.TurnAroundMaximumRate) * 0.5f;
        var lizard = new ProceduralLizard(DefaultProfile);
        var mood = EmotionBlend.Normalize(new EmotionBlend(0.3f, 0.3f, 0.3f, 0.1f));
        var heading = -0.18f;
        var lastFeet = lizard.Legs.Select(leg => leg.Foot).ToArray();
        var lastStepping = new bool[lizard.Legs.Count];
        var lastSerial = new int[lizard.Legs.Count];
        var maximumDrift = 0f;
        var pairRatioTotal = 0f;
        var pairRatioSamples = 0;
        var outerTotal = 0f;
        var outerSamples = 0;
        var innerTotal = 0f;
        var innerSamples = 0;
        var reachCorrections = 0;
        var turnProjectionCorrections = 0;
        var turnTargetCorrections = 0;
        var turnSwingCorrections = 0;
        var previousProjectionCorrections = 0;
        var previousTargetCorrections = 0;
        var previousSwingCorrections = 0;

        var totalFrames = (int)MathF.Ceiling((warmupDuration + turnDuration) / dt);
        for (var frame = 0; frame < totalFrames; frame++)
        {
            var turning = frame * dt >= warmupDuration;
            var state = turning ? RoamingState.TurnAround : RoamingState.ForwardCrawl;
            var activeTurnRate = turning ? turnRate : 0f;
            heading = MathEx.SimplifyAngle(heading + activeTurnRate * dt);
            var delta = MathEx.FromAngle(heading) *
                        (displaySpeed * dt / lizard.Profile.Appearance.VisualScale);
            lizard.Update(
                dt,
                new LizardAnimationInput(
                    heading,
                    MathEx.Clamp01(displaySpeed / DefaultBehavior.Speed.AnimationNormalization),
                    LizardPoseMode.Locomotion,
                    mood,
                    0f,
                    delta));
            var currentProjectionCorrections = lizard.Legs.Sum(leg => leg.ReachProjectionSerial);
            var currentTargetCorrections = lizard.Legs.Sum(leg => leg.StepTargetClampSerial);
            var currentSwingCorrections = lizard.Legs.Sum(leg => leg.SwingClampSerial);
            var curvedStressState = state == RoamingState.TurnAround;
            if (curvedStressState)
            {
                turnProjectionCorrections += Math.Max(0, currentProjectionCorrections - previousProjectionCorrections);
                turnTargetCorrections += Math.Max(0, currentTargetCorrections - previousTargetCorrections);
                turnSwingCorrections += Math.Max(0, currentSwingCorrections - previousSwingCorrections);
            }
            previousProjectionCorrections = currentProjectionCorrections;
            previousTargetCorrections = currentTargetCorrections;
            previousSwingCorrections = currentSwingCorrections;

            var started = lizard.Legs.Where(leg => leg.StepSerial != lastSerial[leg.Index]).ToArray();
            if (state == RoamingState.TurnAround && started.Length == 2)
            {
                var minimum = started.Min(leg => leg.StepSpan);
                var maximum = started.Max(leg => leg.StepSpan);
                if (minimum > 0.01f)
                {
                    pairRatioTotal += maximum / minimum;
                    pairRatioSamples++;
                }
                var insideSide = Math.Sign(activeTurnRate);
                foreach (var leg in started)
                {
                    if (leg.Side == insideSide)
                    {
                        innerTotal += leg.StepSpan;
                        innerSamples++;
                    }
                    else
                    {
                        outerTotal += leg.StepSpan;
                        outerSamples++;
                    }
                }
            }

            for (var index = 0; index < lizard.Legs.Count; index++)
            {
                var leg = lizard.Legs[index];
                if (curvedStressState && !leg.IsStepping && !lastStepping[index])
                {
                    maximumDrift = Math.Max(
                        maximumDrift,
                        Vector2.Distance(leg.Foot - lastFeet[index], -delta));
                }
                lastFeet[index] = leg.Foot;
                lastStepping[index] = leg.IsStepping;
                lastSerial[index] = leg.StepSerial;
            }
        }

        var pairRatio = pairRatioSamples > 0 ? pairRatioTotal / pairRatioSamples : 0f;
        var outerAverage = outerSamples > 0 ? outerTotal / outerSamples : 0f;
        var innerAverage = innerSamples > 0 ? innerTotal / innerSamples : 0f;
        var sideRatio = innerAverage > 0.01f ? outerAverage / innerAverage : 0f;
        reachCorrections = turnProjectionCorrections + turnTargetCorrections + turnSwingCorrections;
        return new TightTurnResult(
            pairRatio,
            sideRatio,
            maximumDrift,
            reachCorrections,
            $"{turnProjectionCorrections}/{turnTargetCorrections}/{turnSwingCorrections}");
    }

    private static FastSCurveStressResult RunFastSCurveStressCheck()
    {
        const float simulationRate = 120f;
        const float dt = 1f / simulationRate;
        var speed = DefaultBehavior.Speed;
        var fastSCurve = DefaultBehavior.SCurve.Fast;
        var settleDuration = DefaultBehavior.SCurve.SettleDuration;
        var fastAcceleration = speed.FastCrawlAcceleration;
        var cases = new[]
        {
            (fastSCurve.MinimumCycleDuration, fastSCurve.MinimumAmplitude, -1f, 0, 0.40f),
            (fastSCurve.MinimumCycleDuration, fastSCurve.MinimumAmplitude, 1f, 1, -0.40f),
            (fastSCurve.MaximumCycleDuration, fastSCurve.MaximumAmplitude, -1f, 1, 0.40f),
            (fastSCurve.MaximumCycleDuration, fastSCurve.MaximumAmplitude, 1f, 0, -0.40f)
        };

        var casesPassed = 0;
        var stepTransitions = 0;
        var pairSequenceViolations = 0;
        var maximumSwingingLegs = 0;
        var maximumPlantedDrift = 0f;
        var reachProjections = 0;
        var stepTargetClamps = 0;
        var swingClamps = 0;
        var minimumPeakSpeed = float.MaxValue;
        var minimumLobeAngle = float.MaxValue;
        var maximumHeadingBias = 0f;
        var nonFiniteSamples = 0;
        var failureDetails = new List<string>();
        var mood = EmotionBlend.Normalize(new EmotionBlend(0.3f, 0.3f, 0.3f, 0.1f));

        foreach (var scenario in cases)
        {
            var lizard = new ProceduralLizard(DefaultProfile);
            var displaySpeed = (speed.ReferenceMinimumCrawl + speed.ReferenceMaximumCrawl) * 0.5f;
            var heading = 0f;
            var phaseReady = false;

            // Establish a stable reference-speed gait, then begin the burst
            // with a requested diagonal pair. Alternating the requested pair
            // across cases covers both possible entry phases deterministically.
            for (var frame = 0; frame < 720; frame++)
            {
                var delta = Vector2.UnitX *
                            (displaySpeed * dt / lizard.Profile.Appearance.VisualScale);
                lizard.Update(
                    dt,
                    new LizardAnimationInput(
                        heading,
                        MathEx.Clamp01(displaySpeed / speed.AnimationNormalization),
                        LizardPoseMode.Locomotion,
                        mood,
                        0f,
                        delta));

                if (frame < 180 || lizard.Legs.Any(leg => leg.IsStepping))
                {
                    continue;
                }

                if (lizard.CaptureDebugSnapshot().NextPair == scenario.Item4)
                {
                    phaseReady = true;
                    break;
                }
            }

            if (!phaseReady)
            {
                failureDetails.Add(
                    $"T{scenario.Item1:F2}/A{scenario.Item2:F2}/D{scenario.Item3:+0;-0}/" +
                    $"P{scenario.Item4}/W{scenario.Item5:+0.00;-0.00}: entry phase unavailable");
                continue;
            }

            var lastFeet = lizard.Legs.Select(leg => leg.Foot).ToArray();
            var lastStepping = lizard.Legs.Select(leg => leg.IsStepping).ToArray();
            var lastStepSerial = lizard.Legs.Select(leg => leg.StepSerial).ToArray();
            var initialReachProjections = lizard.Legs.Sum(leg => leg.ReachProjectionSerial);
            var initialTargetClamps = lizard.Legs.Sum(leg => leg.StepTargetClampSerial);
            var initialSwingClamps = lizard.Legs.Sum(leg => leg.SwingClampSerial);
            var lastReachProjectionSerial = lizard.Legs.Select(leg => leg.ReachProjectionSerial).ToArray();
            var lastTargetClampSerial = lizard.Legs.Select(leg => leg.StepTargetClampSerial).ToArray();
            var lastSwingClampSerial = lizard.Legs.Select(leg => leg.SwingClampSerial).ToArray();
            var correctionEvents = new List<string>();
            var lastStartedPair = -1;
            var caseStepTransitions = 0;
            var casePairSequenceViolations = 0;
            var caseMaximumSwingingLegs = 0;
            var caseMaximumPlantedDrift = 0f;
            var casePeakSpeed = displaySpeed;
            var casePositiveTurn = 0f;
            var caseNegativeTurn = 0f;
            var caseTurnSign = 0;
            var caseReversals = 0;
            var caseMinimumTurnRadius = float.MaxValue;
            var caseNonFinite = 0;
            var frameCount = (int)MathF.Round(
                (scenario.Item1 * fastSCurve.MinimumCycleCount +
                 settleDuration) * simulationRate);
            var turnVelocity = scenario.Item5;

            for (var frame = 0; frame < frameCount; frame++)
            {
                var elapsed = (frame + 1f) * dt;
                var desiredTurnVelocity = SCurveTrajectory.ComputeTurnVelocity(
                    elapsed,
                    scenario.Item1,
                    fastSCurve.MinimumCycleCount,
                    scenario.Item2,
                    scenario.Item3);
                // Mirror BehaviorController's production steering response,
                // including an opposing residual turn from the preceding
                // action. Directly integrating the requested sine would make
                // the geometry test easier than the real transition.
                turnVelocity = MathEx.Lerp(
                    turnVelocity,
                    desiredTurnVelocity,
                    MathEx.ExpLerpFactor(fastSCurve.SteeringResponse, dt));
                var headingDelta = turnVelocity * dt;
                heading = MathEx.SimplifyAngle(heading + headingDelta);
                if (headingDelta >= 0f) casePositiveTurn += headingDelta;
                else caseNegativeTurn -= headingDelta;

                displaySpeed = Math.Min(
                    speed.MaximumCrawl,
                    displaySpeed + fastAcceleration * dt);
                casePeakSpeed = Math.Max(casePeakSpeed, displaySpeed);
                if (MathF.Abs(turnVelocity) >= 0.035f)
                {
                    var turnSign = Math.Sign(turnVelocity);
                    if (caseTurnSign != 0 && turnSign != caseTurnSign)
                    {
                        caseReversals++;
                    }
                    caseTurnSign = turnSign;
                }
                if (elapsed >= 0.45f && MathF.Abs(turnVelocity) > 0.00001f)
                {
                    caseMinimumTurnRadius = Math.Min(
                        caseMinimumTurnRadius,
                        displaySpeed / MathF.Abs(turnVelocity));
                }
                var delta = MathEx.FromAngle(heading) *
                            (displaySpeed * dt / lizard.Profile.Appearance.VisualScale);
                lizard.Update(
                    dt,
                    new LizardAnimationInput(
                        heading,
                        MathEx.Clamp01(displaySpeed / speed.AnimationNormalization),
                        LizardPoseMode.FastSCurve,
                        mood,
                        0f,
                        delta));

                for (var index = 0; index < lizard.Legs.Count; index++)
                {
                    var leg = lizard.Legs[index];
                    var reachDelta = leg.ReachProjectionSerial - lastReachProjectionSerial[index];
                    var targetDelta = leg.StepTargetClampSerial - lastTargetClampSerial[index];
                    var swingDelta = leg.SwingClampSerial - lastSwingClampSerial[index];
                    if ((reachDelta > 0 || targetDelta > 0 || swingDelta > 0) && correctionEvents.Count < 12)
                    {
                        correctionEvents.Add(
                            $"f{frame}/v{displaySpeed:F2}/w{turnVelocity:+0.000;-0.000}/" +
                            $"leg{leg.Index}/r{reachDelta}/t{targetDelta}/s{swingDelta}");
                    }
                    lastReachProjectionSerial[index] = leg.ReachProjectionSerial;
                    lastTargetClampSerial[index] = leg.StepTargetClampSerial;
                    lastSwingClampSerial[index] = leg.SwingClampSerial;
                }

                var started = lizard.Legs
                    .Where(leg => leg.StepSerial != lastStepSerial[leg.Index])
                    .ToArray();
                if (started.Length > 0)
                {
                    caseStepTransitions++;
                    var startedPairs = started.Select(leg => leg.Pair).Distinct().ToArray();
                    if (started.Length != 2 || startedPairs.Length != 1)
                    {
                        casePairSequenceViolations++;
                    }
                    else
                    {
                        var startedPair = startedPairs[0];
                        if ((lastStartedPair < 0 && startedPair != scenario.Item4) ||
                            lastStartedPair == startedPair)
                        {
                            casePairSequenceViolations++;
                        }
                        lastStartedPair = startedPair;
                    }
                }

                caseMaximumSwingingLegs = Math.Max(
                    caseMaximumSwingingLegs,
                    lizard.Legs.Count(leg => leg.IsStepping));
                for (var index = 0; index < lizard.Legs.Count; index++)
                {
                    var leg = lizard.Legs[index];
                    if (!leg.IsStepping && !lastStepping[index])
                    {
                        caseMaximumPlantedDrift = Math.Max(
                            caseMaximumPlantedDrift,
                            Vector2.Distance(leg.Foot - lastFeet[index], -delta));
                    }
                    lastFeet[index] = leg.Foot;
                    lastStepping[index] = leg.IsStepping;
                    lastStepSerial[index] = leg.StepSerial;
                }

                if (!float.IsFinite(heading) ||
                    !float.IsFinite(displaySpeed) ||
                    lizard.Spine.Joints.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y)) ||
                    lizard.Legs.Any(leg =>
                        !float.IsFinite(leg.Elbow.X) || !float.IsFinite(leg.Elbow.Y) ||
                        !float.IsFinite(leg.Foot.X) || !float.IsFinite(leg.Foot.Y)))
                {
                    caseNonFinite++;
                }
            }

            var caseReachProjections = lizard.Legs.Sum(leg => leg.ReachProjectionSerial) - initialReachProjections;
            var caseTargetClamps = lizard.Legs.Sum(leg => leg.StepTargetClampSerial) - initialTargetClamps;
            var caseSwingClamps = lizard.Legs.Sum(leg => leg.SwingClampSerial) - initialSwingClamps;
            var caseMinimumLobe = Math.Min(casePositiveTurn, caseNegativeTurn);
            var caseHeadingBias = MathF.Abs(casePositiveTurn - caseNegativeTurn);
            var casePassed =
                caseStepTransitions >= 6 &&
                casePairSequenceViolations == 0 &&
                caseMaximumSwingingLegs == 2 &&
                caseMaximumPlantedDrift <= 0.01f &&
                caseReachProjections == 0 &&
                caseTargetClamps == 0 &&
                caseSwingClamps == 0 &&
                casePeakSpeed >= speed.MaximumCrawl - 0.05f &&
                caseMinimumLobe >= 0.36f &&
                caseHeadingBias <= 0.25f &&
                caseReversals >= fastSCurve.MinimumCycleCount &&
                caseMinimumTurnRadius >= 180f &&
                caseNonFinite == 0;
            if (casePassed)
            {
                casesPassed++;
            }
            else
            {
                var events = correctionEvents.Count > 0
                    ? string.Join(",", correctionEvents)
                    : "none";
                failureDetails.Add(
                    $"T{scenario.Item1:F2}/A{scenario.Item2:F2}/D{scenario.Item3:+0;-0}/" +
                    $"P{scenario.Item4}/W{scenario.Item5:+0.00;-0.00}: " +
                    $"steps{caseStepTransitions},pair{casePairSequenceViolations},swing{caseMaximumSwingingLegs}," +
                    $"drift{caseMaximumPlantedDrift:F4},corr{caseReachProjections}/{caseTargetClamps}/{caseSwingClamps}," +
                    $"peak{casePeakSpeed:F2},lobe{caseMinimumLobe:F3},bias{caseHeadingBias:F3}," +
                    $"reversals{caseReversals},radius{caseMinimumTurnRadius:F1},finite{caseNonFinite}; " +
                    $"events[{events}]");
            }

            stepTransitions += caseStepTransitions;
            pairSequenceViolations += casePairSequenceViolations;
            maximumSwingingLegs = Math.Max(maximumSwingingLegs, caseMaximumSwingingLegs);
            maximumPlantedDrift = Math.Max(maximumPlantedDrift, caseMaximumPlantedDrift);
            reachProjections += caseReachProjections;
            stepTargetClamps += caseTargetClamps;
            swingClamps += caseSwingClamps;
            minimumPeakSpeed = Math.Min(minimumPeakSpeed, casePeakSpeed);
            minimumLobeAngle = Math.Min(minimumLobeAngle, caseMinimumLobe);
            maximumHeadingBias = Math.Max(maximumHeadingBias, caseHeadingBias);
            nonFiniteSamples += caseNonFinite;
        }

        if (!float.IsFinite(minimumPeakSpeed)) minimumPeakSpeed = 0f;
        if (!float.IsFinite(minimumLobeAngle)) minimumLobeAngle = 0f;
        return new FastSCurveStressResult(
            casesPassed,
            cases.Length,
            stepTransitions,
            pairSequenceViolations,
            maximumSwingingLegs,
            maximumPlantedDrift,
            reachProjections,
            stepTargetClamps,
            swingClamps,
            minimumPeakSpeed,
            minimumLobeAngle,
            maximumHeadingBias,
            nonFiniteSamples,
            failureDetails.Count > 0 ? string.Join(" | ", failureDetails) : "none");
    }

    private static float SmoothStep(float value) => value * value * (3f - 2f * value);
}
