using System.Diagnostics;
using System.Numerics;
using DesktopLizard.AppRuntime;
using DesktopLizard.Core;
using DesktopLizard.Diagnostics.Framework;
using DesktopLizard.Rendering;
using DesktopPet.Engine;
using InfiniteLizards.Gameplay;

namespace DesktopLizard.Diagnostics;

internal readonly record struct LostGripFallTestResult(
    bool Passed,
    int CompletedScenarios,
    int FallingEntries,
    int RegripEntries,
    int AutonomousRecoveries,
    int InvalidTransitions,
    float MinimumFallDistance,
    float MaximumFallDistance,
    float MaximumFallVelocity,
    float MaximumRegripPoseJump,
    float MaximumCatchCentroidError,
    float MaximumRegripCentroidDrift,
    float MaximumConstraintError,
    float MaximumGrabError,
    float MaximumContainmentCorrectionTotal,
    float MaximumContainmentCorrection,
    float MaximumReferenceCorrectionTotal,
    float MaximumReferenceCorrection,
    float MaximumReferenceCenterError,
    int ReachSeekingEntries,
    int ContactHoldEntries,
    int ReachOrderingViolations,
    int ReachDirectionViolations,
    int ReachIkBranchViolations,
    int ReachIkBranchTransitions,
    float MinimumIkBranchTransitionStraightness,
    float MaximumIkBranchTransitionElbowJump,
    string WorstIkBranchTransitionDetail,
    float MaximumNonBranchLimbPoseJump,
    string WorstNonBranchLimbPoseJumpDetail,
    int ReachRequiresProjectionLegMask,
    int ReachProjectedLimbSamples,
    int ReachRollbackLimbSamples,
    int ReachProjectionContinuityViolations,
    float MaximumProjectionLimbPoseJump,
    string WorstProjectionLimbPoseJumpDetail,
    int ReachBodyClearanceViolations,
    int ReachOwnEnvelopeReentryViolations,
    string WorstReachBodyClearanceDetail,
    string FirstReachOwnEnvelopeReentryDetail,
    string PerLegFirstReachOwnEnvelopeReentryDetail,
    string PerLegReachGeometryDetail,
    string WorstReachTimingDetail,
    double CaptureStepTimingMedianMilliseconds,
    double CaptureStepTimingMaximumMilliseconds,
    int ContactStopViolations,
    int FourTargetScenarios,
    int FourLegObservedScenarios,
    int MinimumNonZeroSeekingSteps,
    float MinimumBehaviorReachPeak,
    float MinimumReachTargetErrorReduction,
    float MinimumFootUpwardTravel,
    float MinimumPawCatchDirectionTravel,
    float MinimumArmRadiusRetention,
    float MaximumFirstVisibleArmRetraction,
    float MinimumFrontHeadwardTargetTravel,
    float MinimumRearTailwardTargetTravel,
    float MinimumTargetSeparation,
    float MinimumNonOwnedBodyClearance,
    float MaximumReachEntryPoseJump,
    float MaximumTerminalReachError,
    float MaximumContactError,
    float MaximumContactSpineDrift,
    float MaximumPostContactWindowDrift,
    float MaximumPostContactVisualCentroidDrift,
    int MinimumRecordedLostGripSteps,
    int MinimumMovingVisiblePawSteps,
    int MinimumVisiblePawMotionLeadSteps,
    int MinimumMovingVisiblePawDisplayFrames60Hz,
    int MinimumVisiblePawMotionLeadDisplayFrames60Hz,
    int PreStopPawMotionViolations,
    float MinimumCanvasMargin,
    int NavigationBoundaryViolations,
    int FullRenderBoundaryViolations,
    int VisualBoundaryViolations,
    int TargetDistanceBoundaryViolations,
    int MonotonicityViolations,
    int NonFiniteSamples,
    float MinimumAvailableDistance,
    float MaximumAvailableDistance,
    float MinimumVisualMargin,
    int ScreenHeightsCovered,
    int SeedsCovered,
    int StartBandsCovered,
    int LongFallScreenHeights,
    int HeightScalingPairs,
    int HeightScalingFailures,
    bool TruncatedFallPassed,
    bool NearBottomCancellationPassed,
    bool SideEdgeCancellationPassed,
    bool TinyAreaCancellationPassed,
    bool NonFreeFallInputIsolationPassed,
    bool RealGrabAnimationIsolationPassed,
    float RealGrabMaximumError,
    bool CollapsedLeadFourPawContinuityPassed,
    bool IkBranchTransitionPolicyPassed,
    bool MinimalLeadBranchTransitionPassed,
    string MinimalLeadBranchTransitionDetail,
    bool ContactSafeNegativeContractPassed,
    string ContactSafeNegativeContractDetail,
    bool RotatedHeadingFourPawGeometryPassed,
    string RotatedHeadingFourPawGeometryDetail,
    bool DpiRefreshDeterminismPassed,
    bool MidSeekingSafetyLossFallbackPassed,
    string MidSeekingSafetyLossFallbackDetail,
    float MaximumSafetyLossPoseJump,
    float MinimumSafetyLossBoundaryMargin,
    int SafetyLossFakeContactSamples,
    int SafetyLossBranchTransitionsAfterTrigger,
    bool PointerPriorityPassed,
    bool GrabPriorityPassed,
    bool PausePriorityPassed,
    bool DebugChineseLabelsPassed);

/// <summary>
/// Deterministic end-to-end coverage for the rare autonomous loss-of-grip
/// event. A test profile gives the event 100% transition weight so this gate
/// never depends on finding the production 2% bucket in a particular RNG run.
/// </summary>
internal static class LostGripFallSelfTest
{
    private const float DeltaTime = 1f / 120f;
    private const float LegacyFixedMaximumDistance = 132f;
    private const float DrawableRasterMargin = 2f;
    private const int ScreenHeightCount = 3;
    private const int StartBandCount = 4;
    private const int SeedsPerBand = 3;
    private const int HeightScalingPairCount = 8;
    internal const int ExpectedScenarioCount =
        ScreenHeightCount * StartBandCount * SeedsPerBand;

    public static LostGripFallTestResult Run()
    {
        var result = Measure();
        return result with { Passed = BuildReport(result).Passed };
    }

    public static DiagnosticReport RunReport() => BuildReport(Measure());

    private static LostGripFallTestResult Measure()
    {
        var profile = CreateDeterministicProfile();
        var metrics = new Metrics();
        var fall = profile.Behavior.LostGripFall;
        var screens = new[]
        {
            CreateScreenContext(profile, new FloatRect(0f, 0f, 1280f, 720f)),
            CreateScreenContext(profile, new FloatRect(-1920f, -1080f, 0f, 0f)),
            CreateScreenContext(profile, new FloatRect(0f, 0f, 3840f, 2160f))
        };

        for (var screenIndex = 0; screenIndex < screens.Length; screenIndex++)
        {
            var screen = screens[screenIndex];
            var safeArea = FullRenderSafeArea(profile, screen.NavigationArea);
            var maximumAvailable = Math.Max(
                fall.MinimumDistance,
                safeArea.Height - 2f);
            var availableDistances = new[]
            {
                Math.Min(maximumAvailable, fall.MinimumDistance + 14f),
                MathEx.Lerp(fall.MinimumDistance, maximumAvailable, 0.34f),
                MathEx.Lerp(fall.MinimumDistance, maximumAvailable, 0.68f),
                maximumAvailable
            };
            for (var band = 0; band < availableDistances.Length; band++)
            {
                var start = new Vector2(
                    safeArea.Center.X,
                    safeArea.Bottom - availableDistances[band]);
                for (var seedIndex = 0; seedIndex < SeedsPerBand; seedIndex++)
                {
                    var seed = 0x5A17 +
                               screenIndex * 104729 +
                               band * 7919 +
                               seedIndex * 1543;
                    RunFallScenario(
                        profile,
                        screen,
                        seed,
                        band,
                        start,
                        pointerWhileFalling:
                        screenIndex == screens.Length - 1 &&
                        band == availableDistances.Length - 1 &&
                        seedIndex == SeedsPerBand - 1,
                        metrics);
                }
            }
        }

        var truncatedFallPassed =
            metrics.TruncatedScenarioCompleted &&
            metrics.TruncatedTargetDistance >= fall.MinimumDistance - 0.01f &&
            metrics.TruncatedTargetDistance <= fall.MinimumDistance + 14.01f;
        var nearBottomCancellationPassed = screens.All(screen =>
            RunNearBottomCancellation(profile, screen));
        var sideEdgeCancellationPassed = screens.All(screen =>
            RunSideEdgeCancellations(profile, screen));
        var tinyAreaCancellationPassed = RunTinyAreaCancellations(profile);
        RunHeightScalingChecks(profile, screens[0], screens[^1], metrics);
        var nonFreeFallInputIsolationPassed = RunNonFreeFallInputIsolation(profile);
        var realGrabAnimationIsolationPassed = RunRealGrabAnimationIsolation(
            profile,
            screens[1],
            out var realGrabMaximumError);
        var collapsedLeadFourPawContinuityPassed =
            RunCollapsedLeadFourPawContinuity(profile);
        var ikBranchTransitionPolicyPassed =
            RunIkBranchTransitionPolicyContract();
        var minimalLeadBranchTransitionPassed =
            RunMinimalLeadBranchTransitionContract(
                profile,
                out var minimalLeadBranchTransitionDetail);
        var contactSafeNegativeContractPassed =
            RunContactSafeNegativeContract(
                profile,
                out var contactSafeNegativeContractDetail);
        var rotatedHeadingFourPawGeometryPassed =
            RunRotatedHeadingFourPawGeometry(
                profile,
                out var rotatedHeadingFourPawGeometryDetail);
        var dpiRefreshDeterminismPassed = RunDpiRefreshDeterminism(profile);
        var safetyLossFallback = RunMidSeekingSafetyLossFallback(
            profile,
            screens[^1]);
        safetyLossFallback = safetyLossFallback with
        {
            Passed = safetyLossFallback.Passed &&
                     RunNearCompleteSafetyContactGate(profile) &&
                     RunNearCompleteReachedTargetContactGate(profile)
        };
        var grabPriorityPassed = RunGrabPriority(profile, screens[1]);
        var pausePriorityPassed = RunPausePriority(profile, screens[1]);
        var debugChineseLabelsPassed =
            DebugPanelView.StateLabel(RoamingState.LostGripFall) == "失手下坠" &&
            DebugPanelView.LostGripPhaseLabel(LostGripFallPhase.Falling) == "自由下坠" &&
            DebugPanelView.LostGripPhaseLabel(LostGripFallPhase.Regripping) == "重新抓稳" &&
            DebugPanelView.LostGripProgressLabel(
                LostGripFallPhase.Falling,
                LostGripCatchReason.None,
                0.42f,
                0f) == "伸手42%" &&
            DebugPanelView.LostGripProgressLabel(
                LostGripFallPhase.Regripping,
                LostGripCatchReason.ReachedTarget,
                0f,
                0.5f) == "抓稳50%" &&
            DebugPanelView.LostGripProgressLabel(
                LostGripFallPhase.Regripping,
                LostGripCatchReason.SafetyForced,
                0f,
                0.5f) == "安全恢复50%";

        if (!float.IsFinite(metrics.MinimumFallDistance))
        {
            metrics.MinimumFallDistance = 0f;
        }
        if (!float.IsFinite(metrics.MinimumCanvasMargin))
        {
            metrics.MinimumCanvasMargin = 0f;
        }
        if (!float.IsFinite(metrics.MinimumAvailableDistance))
        {
            metrics.MinimumAvailableDistance = 0f;
        }
        if (!float.IsFinite(metrics.MinimumVisualMargin))
        {
            metrics.MinimumVisualMargin = 0f;
        }
        metrics.NormalizeReachMinimums();
        var captureStepTiming = MeasureCaptureStepTiming(profile, screens[0]);

        return new LostGripFallTestResult(
            false,
            metrics.CompletedScenarios,
            metrics.FallingEntries,
            metrics.RegripEntries,
            metrics.AutonomousRecoveries,
            metrics.InvalidTransitions,
            metrics.MinimumFallDistance,
            metrics.MaximumFallDistance,
            metrics.MaximumFallVelocity,
            metrics.MaximumRegripPoseJump,
            metrics.MaximumCatchCentroidError,
            metrics.MaximumRegripCentroidDrift,
            metrics.MaximumConstraintError,
            metrics.MaximumGrabError,
            metrics.MaximumContainmentCorrectionTotal,
            metrics.MaximumContainmentCorrection,
            metrics.MaximumReferenceCorrectionTotal,
            metrics.MaximumReferenceCorrection,
            metrics.MaximumReferenceCenterError,
            metrics.ReachSeekingEntries,
            metrics.ContactHoldEntries,
            metrics.ReachOrderingViolations,
            metrics.ReachDirectionViolations,
            metrics.ReachIkBranchViolations,
            metrics.ReachIkBranchTransitions,
            metrics.MinimumIkBranchTransitionStraightness,
            metrics.MaximumIkBranchTransitionElbowJump,
            metrics.WorstIkBranchTransitionDetail,
            metrics.MaximumNonBranchLimbPoseJump,
            metrics.WorstNonBranchLimbPoseJumpDetail,
            metrics.ReachRequiresProjectionLegMask,
            metrics.ReachProjectedLimbSamples,
            metrics.ReachRollbackLimbSamples,
            metrics.ReachProjectionContinuityViolations,
            metrics.MaximumProjectionLimbPoseJump,
            metrics.WorstProjectionLimbPoseJumpDetail,
            metrics.ReachBodyClearanceViolations,
            metrics.ReachOwnEnvelopeReentryViolations,
            metrics.WorstReachBodyClearanceDetail,
            metrics.FirstReachOwnEnvelopeReentryDetail,
            metrics.PerLegFirstReachOwnEnvelopeReentryDetail,
            metrics.PerLegReachGeometryDetail,
            metrics.WorstReachTimingDetail,
            captureStepTiming.MedianMilliseconds,
            captureStepTiming.MaximumMilliseconds,
            metrics.ContactStopViolations,
            metrics.FourTargetScenarios,
            metrics.FourLegObservedScenarios,
            metrics.MinimumNonZeroSeekingSteps,
            metrics.MinimumBehaviorReachPeak,
            metrics.MinimumReachTargetErrorReduction,
            metrics.MinimumFootUpwardTravel,
            metrics.MinimumPawCatchDirectionTravel,
            metrics.MinimumArmRadiusRetention,
            metrics.MaximumFirstVisibleArmRetraction,
            metrics.MinimumFrontHeadwardTargetTravel,
            metrics.MinimumRearTailwardTargetTravel,
            metrics.MinimumTargetSeparation,
            metrics.MinimumNonOwnedBodyClearance,
            metrics.MaximumReachEntryPoseJump,
            metrics.MaximumTerminalReachError,
            metrics.MaximumContactError,
            metrics.MaximumContactSpineDrift,
            metrics.MaximumPostContactWindowDrift,
            metrics.MaximumPostContactVisualCentroidDrift,
            metrics.MinimumRecordedLostGripSteps,
            metrics.MinimumMovingVisiblePawSteps,
            metrics.MinimumVisiblePawMotionLeadSteps,
            metrics.MinimumMovingVisiblePawDisplayFrames60Hz,
            metrics.MinimumVisiblePawMotionLeadDisplayFrames60Hz,
            metrics.PreStopPawMotionViolations,
            metrics.MinimumCanvasMargin,
            metrics.NavigationBoundaryViolations,
            metrics.FullRenderBoundaryViolations,
            metrics.VisualBoundaryViolations,
            metrics.TargetDistanceBoundaryViolations,
            metrics.MonotonicityViolations,
            metrics.NonFiniteSamples,
            metrics.MinimumAvailableDistance,
            metrics.MaximumAvailableDistance,
            metrics.MinimumVisualMargin,
            metrics.ScreenHeights.Count,
            metrics.Seeds.Count,
            metrics.StartBands.Count,
            metrics.LongFallScreenHeights.Count,
            metrics.HeightScalingPairs,
            metrics.HeightScalingFailures,
            truncatedFallPassed,
            nearBottomCancellationPassed,
            sideEdgeCancellationPassed,
            tinyAreaCancellationPassed,
            nonFreeFallInputIsolationPassed,
            realGrabAnimationIsolationPassed,
            realGrabMaximumError,
            collapsedLeadFourPawContinuityPassed,
            ikBranchTransitionPolicyPassed,
            minimalLeadBranchTransitionPassed,
            minimalLeadBranchTransitionDetail,
            contactSafeNegativeContractPassed,
            contactSafeNegativeContractDetail,
            rotatedHeadingFourPawGeometryPassed,
            rotatedHeadingFourPawGeometryDetail,
            dpiRefreshDeterminismPassed,
            safetyLossFallback.Passed,
            safetyLossFallback.Detail,
            safetyLossFallback.MaximumPoseJump,
            safetyLossFallback.MinimumBoundaryMargin,
            safetyLossFallback.FakeContactSamples,
            safetyLossFallback.BranchTransitionsAfterTrigger,
            metrics.PointerPriorityViolations == 0,
            grabPriorityPassed,
            pausePriorityPassed,
            debugChineseLabelsPassed);
    }

    private static void RunFallScenario(
        LizardProfile profile,
        ScreenContext screen,
        int seed,
        int startBand,
        Vector2 start,
        bool pointerWhileFalling,
        Metrics metrics)
    {
        var session = new PetSimulationSession(seed, profile);
        session.Reset(start, 0f);
        var behavior = session.BehaviorForDiagnostics;
        var lizard = session.LizardForDiagnostics;
        var previousState = behavior.State;
        var previousPhase = behavior.LostGripPhase;
        var previousY = behavior.Position.Y;
        var previousPose = PoseSnapshot.Capture(lizard);
        var previousDangling = lizard.CaptureDebugSnapshot().DanglingActive;
        var entered = false;
        var regripped = false;
        var returnedToIdle = false;
        var recovered = false;
        var entryAvailableDistance = float.NaN;
        var entryWorldSpineCenter = Vector2.Zero;
        var regripWorldSpineCenter = Vector2.Zero;
        var hasRegripWorldSpineCenter = false;
        var reachProbe = new LostGripRegripAnimationProbe(
            behavior.Position,
            lizard,
            $"seed={seed},screen={screen.WorkArea.Width:F0}x" +
            $"{screen.WorkArea.Height:F0},band={startBand}," +
            $"start=({start.X:F1},{start.Y:F1})");

        for (var step = 0; step < 12f / DeltaTime; step++)
        {
            var pointer = pointerWhileFalling && entered && behavior.State == RoamingState.LostGripFall
                ? new PointerObservation(behavior.Position + new Vector2(40f, 0f), true)
                : default;
            var frame = session.Advance(
                Input(
                    DeltaTime,
                    screen.NavigationArea,
                    profile,
                    pointer,
                    lostGripSafety: screen.LostGripSafety));

            if (behavior.State != previousState)
            {
                if (!RoamingStateMachine.IsLegal(
                        previousState,
                        behavior.State,
                        behavior.LastTransitionReason))
                {
                    metrics.InvalidTransitions++;
                }
                previousState = behavior.State;
            }

            if (behavior.State == RoamingState.LostGripFall && !entered)
            {
                entered = true;
                metrics.FallingEntries++;
                previousY = behavior.Position.Y;
                entryWorldSpineCenter = WorldSpineCenter(profile, behavior, lizard);
                var safeArea = FullRenderSafeArea(profile, screen.NavigationArea);
                entryAvailableDistance = Math.Max(0f, safeArea.Bottom - behavior.Position.Y);
                metrics.MinimumAvailableDistance = Math.Min(
                    metrics.MinimumAvailableDistance,
                    entryAvailableDistance);
                metrics.MaximumAvailableDistance = Math.Max(
                    metrics.MaximumAvailableDistance,
                    entryAvailableDistance);
                metrics.ScreenHeights.Add((int)MathF.Round(screen.WorkArea.Height));
                metrics.Seeds.Add(seed);
                metrics.StartBands.Add(startBand);
                if (behavior.LostGripTargetDistance + 0.01f <
                        profile.Behavior.LostGripFall.MinimumDistance ||
                    behavior.LostGripTargetDistance > entryAvailableDistance + 0.01f)
                {
                    metrics.TargetDistanceBoundaryViolations++;
                }
                if (behavior.LostGripTargetDistance > LegacyFixedMaximumDistance + 0.01f)
                {
                    metrics.LongFallScreenHeights.Add(
                        (int)MathF.Round(screen.WorkArea.Height));
                }
            }
            if (entered && behavior.State == RoamingState.MouseChase)
            {
                metrics.PointerPriorityViolations++;
            }

            if (behavior.State == RoamingState.LostGripFall)
            {
                if (behavior.Position.Y + 0.001f < previousY)
                {
                    metrics.MonotonicityViolations++;
                }
                previousY = behavior.Position.Y;

                if (previousPhase == LostGripFallPhase.Falling &&
                    behavior.LostGripPhase == LostGripFallPhase.Regripping)
                {
                    regripped = true;
                    metrics.RegripEntries++;
                    RecordFallDistance(behavior, metrics);
                    regripWorldSpineCenter = WorldSpineCenter(
                        profile,
                        behavior,
                        lizard);
                    hasRegripWorldSpineCenter = true;
                    metrics.MaximumCatchCentroidError = Math.Max(
                        metrics.MaximumCatchCentroidError,
                        MathF.Abs(
                            Vector2.Distance(
                                entryWorldSpineCenter,
                                regripWorldSpineCenter) -
                            behavior.LostGripTargetDistance));
                    var fall = profile.Behavior.LostGripFall;
                    if (entryAvailableDistance <= fall.MinimumDistance + 14.01f)
                    {
                        // Every screen includes a deterministic start with only
                        // a narrow amount of full-render-safe space below it.
                        metrics.TruncatedScenarioCompleted = true;
                        metrics.TruncatedTargetDistance = behavior.LostGripTargetDistance;
                    }
                }
            }
            else if (entered && regripped && behavior.State == RoamingState.Idle)
            {
                returnedToIdle = true;
            }
            else if (returnedToIdle && behavior.State == RoamingState.ForwardCrawl)
            {
                recovered = true;
                metrics.AutonomousRecoveries++;
            }

            var visualCenter = ObserveSafety(
                profile,
                screen,
                behavior,
                lizard,
                frame.RenderFrame,
                metrics);
            var debugPose = lizard.CaptureDebugSnapshot();
            if (debugPose.DanglingActive)
            {
                metrics.MaximumConstraintError = Math.Max(
                    metrics.MaximumConstraintError,
                    debugPose.DanglingConstraintError);
                metrics.MaximumGrabError = Math.Max(
                    metrics.MaximumGrabError,
                    debugPose.DanglingGrabError);
            }
            if (behavior.LostGripPhase == LostGripFallPhase.Falling &&
                !debugPose.DanglingActive)
            {
                metrics.NonFiniteSamples++;
            }

            var currentPose = PoseSnapshot.Capture(lizard);
            reachProbe.Observe(
                behavior,
                lizard,
                visualCenter,
                frame.RenderFrame);
            if (previousDangling &&
                !debugPose.DanglingActive &&
                behavior.LostGripPhase == LostGripFallPhase.Regripping)
            {
                metrics.MaximumRegripPoseJump = Math.Max(
                    metrics.MaximumRegripPoseJump,
                    previousPose.MaximumDistance(currentPose));
            }
            previousPose = currentPose;
            previousDangling = debugPose.DanglingActive;
            previousPhase = behavior.LostGripPhase;

            metrics.MaximumContainmentCorrectionTotal = Math.Max(
                metrics.MaximumContainmentCorrectionTotal,
                lizard.FreeFallContainmentCorrectionTotal);
            metrics.MaximumContainmentCorrection = Math.Max(
                metrics.MaximumContainmentCorrection,
                lizard.FreeFallContainmentCorrectionMaximum);
            metrics.MaximumReferenceCorrectionTotal = Math.Max(
                metrics.MaximumReferenceCorrectionTotal,
                lizard.FreeFallReferenceCorrectionTotal);
            metrics.MaximumReferenceCorrection = Math.Max(
                metrics.MaximumReferenceCorrection,
                lizard.FreeFallReferenceCorrectionMaximum);
            metrics.MaximumReferenceCenterError = Math.Max(
                metrics.MaximumReferenceCenterError,
                lizard.FreeFallReferenceCenterError);
            if (hasRegripWorldSpineCenter)
            {
                metrics.MaximumRegripCentroidDrift = Math.Max(
                    metrics.MaximumRegripCentroidDrift,
                    Vector2.Distance(
                        regripWorldSpineCenter,
                        WorldSpineCenter(profile, behavior, lizard)));
            }

            if (recovered)
            {
                metrics.RecordReach(reachProbe.Complete());
                metrics.CompletedScenarios++;
                return;
            }
        }
    }

    private static void RecordFallDistance(
        BehaviorController behavior,
        Metrics metrics)
    {
        metrics.MinimumFallDistance = Math.Min(
            metrics.MinimumFallDistance,
            behavior.LostGripDistance);
        metrics.MaximumFallDistance = Math.Max(
            metrics.MaximumFallDistance,
            behavior.LostGripDistance);
        metrics.MaximumFallVelocity = Math.Max(
            metrics.MaximumFallVelocity,
            behavior.LostGripVerticalVelocity);
        if (MathF.Abs(behavior.LostGripDistance - behavior.LostGripTargetDistance) > 0.01f)
        {
            metrics.MonotonicityViolations++;
        }
    }

    private static bool RunNearBottomCancellation(
        LizardProfile profile,
        ScreenContext screen)
    {
        var fall = profile.Behavior.LostGripFall;
        var safeBottom = screen.NavigationArea.Bottom - fall.BottomSafetyInset;
        var start = new Vector2(
            screen.NavigationArea.Center.X,
            safeBottom - fall.MinimumDistance + 4f);
        return RunCancelledEntry(profile, screen, start, 0x71C3);
    }

    private static bool RunSideEdgeCancellations(
        LizardProfile profile,
        ScreenContext screen)
    {
        const float edgeOffset = 10f;
        var area = screen.NavigationArea;
        var starts = new[]
        {
            new Vector2(area.Left + edgeOffset, area.Center.Y),
            new Vector2(area.Right - edgeOffset, area.Center.Y),
            new Vector2(area.Center.X, area.Top + edgeOffset)
        };
        return starts.Select((start, index) => (start, index)).All(candidate =>
            RunCancelledEntry(
                profile,
                screen,
                candidate.start,
                0x72D1 + candidate.index * 173));
    }

    private static bool RunTinyAreaCancellations(LizardProfile profile)
    {
        var inset = profile.Behavior.LostGripFall.BottomSafetyInset;
        var insufficientSpan = Math.Max(2f, inset * 2f - 1f);
        var areas = new[]
        {
            new FloatRect(0f, 0f, insufficientSpan, 1200f),
            new FloatRect(0f, 0f, 1200f, insufficientSpan)
        };

        var derivedAreasPassed = areas.Select((area, index) => (area, index)).All(candidate =>
        {
            var session = new PetSimulationSession(0x73A1 + candidate.index * 211, profile);
            session.Reset(candidate.area.Center, profile.Runtime.InitialHeading);
            session.MoveToCenter(candidate.area.Center);
            var result = session.TryPlayDebugAction(
                AutonomousAction.LostGripFall,
                candidate.area);
            var behavior = session.BehaviorForDiagnostics;
            return result.Status == DebugPlaybackStatus.RedirectedToEdgeTurn &&
                   behavior.State == RoamingState.EdgeTurn &&
                   behavior.LastTransitionReason == StateTransitionReason.Boundary &&
                   behavior.LostGripPhase == LostGripFallPhase.None &&
                   IsFinite(session);
        });

        var fallbackArea = new FloatRect(-300f, -800f, 300f, 800f);
        var explicitUnavailableSession = new PetSimulationSession(0x75B7, profile);
        explicitUnavailableSession.Reset(
            fallbackArea.Center,
            profile.Runtime.InitialHeading);
        explicitUnavailableSession.MoveToCenter(fallbackArea.Center);
        var explicitUnavailableResult = explicitUnavailableSession.TryPlayDebugAction(
            AutonomousAction.LostGripFall,
            fallbackArea,
            new LostGripSafetyContext(fallbackArea, IsAvailable: false));
        var explicitUnavailableBehavior =
            explicitUnavailableSession.BehaviorForDiagnostics;
        var explicitUnavailablePassed =
            explicitUnavailableResult.Status == DebugPlaybackStatus.RedirectedToEdgeTurn &&
            explicitUnavailableBehavior.State == RoamingState.EdgeTurn &&
            explicitUnavailableBehavior.LastTransitionReason ==
                StateTransitionReason.Boundary &&
            explicitUnavailableBehavior.LostGripPhase == LostGripFallPhase.None &&
            explicitUnavailableBehavior.LostGripDistance == 0f &&
            explicitUnavailableBehavior.LostGripTargetDistance == 0f &&
            IsFinite(explicitUnavailableSession);
        return derivedAreasPassed && explicitUnavailablePassed;
    }

    private static void RunHeightScalingChecks(
        LizardProfile profile,
        ScreenContext shortScreen,
        ScreenContext tallScreen,
        Metrics metrics)
    {
        var shortSafeArea = FullRenderSafeArea(profile, shortScreen.NavigationArea);
        var tallSafeArea = FullRenderSafeArea(profile, tallScreen.NavigationArea);
        for (var index = 0; index < HeightScalingPairCount; index++)
        {
            var seed = 0x6D31 + index * 3571;
            var shortStart = new Vector2(shortSafeArea.Center.X, shortSafeArea.Top + 2f);
            var tallStart = new Vector2(tallSafeArea.Center.X, tallSafeArea.Top + 2f);
            var shortSession = CreateDebugPlaybackSession(
                profile,
                shortScreen.NavigationArea,
                shortScreen.LostGripSafety,
                shortStart,
                seed);
            var tallSession = CreateDebugPlaybackSession(
                profile,
                tallScreen.NavigationArea,
                tallScreen.LostGripSafety,
                tallStart,
                seed);
            var shortResult = shortSession.TryPlayDebugAction(
                AutonomousAction.LostGripFall,
                shortScreen.NavigationArea,
                shortScreen.LostGripSafety);
            var tallResult = tallSession.TryPlayDebugAction(
                AutonomousAction.LostGripFall,
                tallScreen.NavigationArea,
                tallScreen.LostGripSafety);
            var shortBehavior = shortSession.BehaviorForDiagnostics;
            var tallBehavior = tallSession.BehaviorForDiagnostics;
            var shortAvailable = shortSafeArea.Bottom - shortStart.Y;
            var tallAvailable = tallSafeArea.Bottom - tallStart.Y;
            var pairPassed =
                shortResult.Status == DebugPlaybackStatus.Started &&
                tallResult.Status == DebugPlaybackStatus.Started &&
                shortBehavior.LostGripTargetDistance >=
                    profile.Behavior.LostGripFall.MinimumDistance - 0.01f &&
                shortBehavior.LostGripTargetDistance <= shortAvailable + 0.01f &&
                tallBehavior.LostGripTargetDistance >=
                    profile.Behavior.LostGripFall.MinimumDistance - 0.01f &&
                tallBehavior.LostGripTargetDistance <= tallAvailable + 0.01f &&
                tallAvailable > shortAvailable &&
                tallBehavior.LostGripTargetDistance >
                    shortBehavior.LostGripTargetDistance + 0.01f &&
                IsFinite(shortSession) &&
                IsFinite(tallSession);
            metrics.HeightScalingPairs++;
            metrics.HeightScalingFailures += pairPassed ? 0 : 1;
        }
    }

    private static PetSimulationSession CreateDebugPlaybackSession(
        LizardProfile profile,
        FloatRect area,
        LostGripSafetyContext lostGripSafety,
        Vector2 start,
        int seed)
    {
        var session = new PetSimulationSession(seed, profile);
        session.Reset(start, profile.Runtime.InitialHeading);
        session.MoveToCenter(start);
        session.Advance(Input(
            DeltaTime,
            area,
            profile,
            lostGripSafety: lostGripSafety));
        return session;
    }

    private static bool RunCancelledEntry(
        LizardProfile profile,
        ScreenContext screen,
        Vector2 start,
        int seed)
    {
        var session = new PetSimulationSession(seed, profile);
        session.Reset(start, 0f);
        var behavior = session.BehaviorForDiagnostics;
        var sawEdgeTurn = false;
        for (var step = 0; step < 2f / DeltaTime; step++)
        {
            session.Advance(Input(
                DeltaTime,
                screen.NavigationArea,
                profile,
                lostGripSafety: screen.LostGripSafety));
            if (behavior.State == RoamingState.LostGripFall)
            {
                return false;
            }
            sawEdgeTurn |= behavior.State == RoamingState.EdgeTurn;
            if (!IsFinite(session) || !screen.NavigationArea.Contains(behavior.Position))
            {
                return false;
            }
            if (sawEdgeTurn)
            {
                return behavior.LastTransitionReason == StateTransitionReason.Boundary;
            }
        }
        return false;
    }

    private static bool RunGrabPriority(LizardProfile profile, ScreenContext screen)
    {
        var session = CreateEnteredSession(profile, screen, 0x3E91);
        if (session is null)
        {
            return false;
        }
        var behavior = session.BehaviorForDiagnostics;
        var lizard = session.LizardForDiagnostics;
        session.BeginGrab(lizard.Spine.Joints[6]);
        if (behavior.State != RoamingState.Grabbed ||
            behavior.LostGripPhase != LostGripFallPhase.None)
        {
            return false;
        }
        session.Advance(Input(
            DeltaTime,
            screen.NavigationArea,
            profile,
            isDragging: true,
            lostGripSafety: screen.LostGripSafety));
        session.EndGrab(behavior.Position);
        return behavior.State == RoamingState.ReleaseSettle && IsFinite(session);
    }

    private static bool RunNonFreeFallInputIsolation(LizardProfile profile)
    {
        var lizard = new ProceduralLizard(profile);
        var mood = EmotionBlend.Normalize(new EmotionBlend(0.25f, 0.25f, 0.25f, 0.25f));
        var nonFallInput = new LizardAnimationInput(
            0f,
            0f,
            LizardPoseMode.Rest,
            mood,
            0f,
            Vector2.Zero)
        {
            CatchPreparationProgress = 1f
        };
        lizard.Update(DeltaTime, nonFallInput);
        var restIsolated =
            lizard.CurrentPoseMode == LizardPoseMode.Rest &&
            lizard.RegripAnimationPhase == RegripAnimationPhase.None &&
            !lizard.RegripReachActive &&
            lizard.RegripReachProgress == 0f &&
            !lizard.RegripContacted &&
            lizard.RegripContactLegMask == 0;

        lizard.BeginGrab(lizard.Spine.Joints[6]);
        var grabbedInput = nonFallInput with { PoseMode = LizardPoseMode.Grabbed };
        lizard.Update(DeltaTime, grabbedInput);
        return restIsolated &&
               lizard.CurrentPoseMode == LizardPoseMode.Grabbed &&
               lizard.RegripAnimationPhase == RegripAnimationPhase.None &&
               !lizard.RegripReachActive &&
               lizard.RegripReachProgress == 0f &&
               !lizard.RegripContacted &&
               lizard.RegripContactLegMask == 0 &&
               lizard.Spine.Joints.All(IsFinite) &&
               lizard.Legs.All(leg => IsFinite(leg.Elbow) && IsFinite(leg.Foot));
    }

    private static bool RunRealGrabAnimationIsolation(
        LizardProfile profile,
        ScreenContext screen,
        out float maximumGrabError)
    {
        var session = new PetSimulationSession(0x5B91, profile);
        session.Reset(screen.NavigationArea.Center, 0f);
        var behavior = session.BehaviorForDiagnostics;
        var lizard = session.LizardForDiagnostics;
        session.BeginGrab(lizard.Spine.Joints[6]);
        maximumGrabError = 0f;
        var passed = behavior.State == RoamingState.Grabbed;
        for (var step = 0; step < 0.75f / DeltaTime; step++)
        {
            var target = screen.NavigationArea.Center + new Vector2(
                MathF.Sin(step * 0.071f) * 36f,
                MathF.Cos(step * 0.053f) * 24f);
            session.DragTo(target);
            session.Advance(Input(
                DeltaTime,
                screen.NavigationArea,
                profile,
                isDragging: true,
                lostGripSafety: screen.LostGripSafety));
            maximumGrabError = Math.Max(maximumGrabError, lizard.DanglingGrabError);
            passed &=
                behavior.State == RoamingState.Grabbed &&
                lizard.CurrentPoseMode == LizardPoseMode.Grabbed &&
                lizard.RegripAnimationPhase == RegripAnimationPhase.None &&
                !lizard.RegripReachActive &&
                lizard.RegripReachProgress == 0f &&
                !lizard.RegripContacted &&
                lizard.RegripContactLegMask == 0 &&
                lizard.DanglingGrabError <= profile.Physics.MaximumGrabError + 0.001f &&
                lizard.DanglingConstraintError <=
                    profile.Physics.MaximumConstraintError + 0.001f &&
                IsFinite(session);
        }
        session.EndGrab(behavior.Position);
        return passed &&
               behavior.State == RoamingState.ReleaseSettle &&
               IsFinite(session);
    }

    private static bool RunCollapsedLeadFourPawContinuity(LizardProfile profile)
    {
        var baseline = new ProceduralLizard(profile);
        var collapsed = new ProceduralLizard(profile);
        var emotion = EmotionBlend.Normalize(
            new EmotionBlend(1f, 0f, 0f, 0f));
        var input = new LizardAnimationInput(
            0f,
            0f,
            LizardPoseMode.FreeFall,
            emotion,
            0.9f,
            new Vector2(0f, 1f));

        baseline.Update(DeltaTime, input);
        collapsed.Update(
            DeltaTime,
            input with
            {
                CatchPreparationProgress = 1f,
                CatchPreparationContactAllowed = false
            });

        var baselinePose = PoseSnapshot.Capture(baseline);
        var collapsedPose = PoseSnapshot.Capture(collapsed);
        return baselinePose.MaximumDistance(collapsedPose) <= 0.0001f &&
               BendSignsMatch(baseline, collapsed) &&
               collapsed.RegripAnimationPhase == RegripAnimationPhase.Seeking &&
               collapsed.RegripReachActive &&
               collapsed.RegripReachProgress >= 1f - 0.0001f &&
               collapsed.RegripContactError <= 0.0001f &&
               !collapsed.RegripContacted &&
               collapsed.RegripContactLegMask == 0 &&
               Enumerable.Range(0, 4).All(index =>
                   Vector2.Distance(
                       collapsed.Legs[index].Foot,
                       collapsed.RegripContactTarget(index)) <= 0.0001f);
    }

    private static bool RunIkBranchTransitionPolicyContract() =>
        LostGripRegripAnimationProbe.IsNearStraightIkBranchTransitionAllowed(
            transitionsBefore: 0,
            previousStraightness: 0.99f,
            currentStraightness: 1f,
            elbowJump:
                LostGripRegripAnimationProbe.MaximumBranchTransitionElbowJump) &&
        !LostGripRegripAnimationProbe.IsNearStraightIkBranchTransitionAllowed(
            transitionsBefore: 0,
            previousStraightness: 0.95f,
            currentStraightness: 1f,
            elbowJump: 0.1f) &&
        !LostGripRegripAnimationProbe.IsNearStraightIkBranchTransitionAllowed(
            transitionsBefore: 1,
            previousStraightness: 1f,
            currentStraightness: 1f,
            elbowJump: 0.1f) &&
        !LostGripRegripAnimationProbe.IsNearStraightIkBranchTransitionAllowed(
            transitionsBefore: 0,
            previousStraightness: 1f,
            currentStraightness: 1f,
            elbowJump:
                LostGripRegripAnimationProbe.MaximumBranchTransitionElbowJump +
                0.01f);

    private static bool BendSignsMatch(
        ProceduralLizard first,
        ProceduralLizard second) =>
        Enumerable.Range(0, 4).All(index =>
            MeasureBendSign(first.Legs[index]) ==
            MeasureBendSign(second.Legs[index]));

    private static float MeasureBendSign(LegRig leg)
    {
        var shoulderToFoot = MathEx.SafeNormalize(
            leg.Foot - leg.Shoulder,
            -Vector2.UnitY);
        var bendHeight = Vector2.Dot(
            leg.Elbow - leg.Shoulder,
            MathEx.Perpendicular(shoulderToFoot));
        return MathF.Abs(bendHeight) > 0.0001f
            ? MathF.Sign(bendHeight)
            : 0f;
    }

    private static int CountAndUpdateBendSignTransitions(
        ProceduralLizard lizard,
        float[] lastNonZeroSigns)
    {
        var transitions = 0;
        for (var index = 0; index < 4; index++)
        {
            var current = MeasureBendSign(lizard.Legs[index]);
            if (current == 0f)
            {
                continue;
            }
            if (lastNonZeroSigns[index] != 0f &&
                lastNonZeroSigns[index] != current)
            {
                transitions++;
            }
            lastNonZeroSigns[index] = current;
        }
        return transitions;
    }

    private static Vector2 SolveTwoBoneElbow(
        Vector2 shoulder,
        Vector2 foot,
        float upperLength,
        float lowerLength,
        float bendSign)
    {
        var shoulderToFoot = foot - shoulder;
        var distance = Math.Max(0.0001f, shoulderToFoot.Length());
        var direction = shoulderToFoot / distance;
        var along = Math.Clamp(
            (upperLength * upperLength - lowerLength * lowerLength +
             distance * distance) /
            (2f * distance),
            0f,
            upperLength);
        var perpendicularDistance = MathF.Sqrt(Math.Max(
            0f,
            upperLength * upperLength - along * along));
        return shoulder + direction * along +
               MathEx.Perpendicular(direction) *
               (perpendicularDistance * bendSign);
    }

    private static ProceduralLizard? CreateLegalCapturePoseLizard(
        LizardProfile profile,
        float rotation = 0f)
    {
        var screen = CreateScreenContext(
            profile,
            new FloatRect(0f, 0f, 1280f, 720f));
        var safeArea = screen.LostGripSafety.SafeArea;
        var maximumAvailable = Math.Max(
            profile.Behavior.LostGripFall.MinimumDistance,
            safeArea.Height - 2f);
        var availableDistance = Math.Min(
            maximumAvailable,
            profile.Behavior.LostGripFall.MinimumDistance + 14f);
        var start = new Vector2(
            safeArea.Center.X,
            safeArea.Bottom - availableDistance);
        var session = new PetSimulationSession(0x5A17, profile);
        session.Reset(start, 0f);
        for (var step = 0; step < 1000; step++)
        {
            session.Advance(Input(
                DeltaTime,
                screen.NavigationArea,
                profile,
                lostGripSafety: screen.LostGripSafety));
            var source = session.LizardForDiagnostics;
            if (source.RegripAnimationPhase != RegripAnimationPhase.Seeking ||
                source.RegripReachProgress > 0.0001f ||
                !source.RegripReachContactSafe)
            {
                continue;
            }

            var center = source.Spine.Joints.Aggregate(
                Vector2.Zero,
                static (sum, point) => sum + point) /
                source.Spine.Joints.Count;
            Vector2 Transform(Vector2 point)
            {
                var offset = point - center;
                var cosine = MathF.Cos(rotation);
                var sine = MathF.Sin(rotation);
                return center + new Vector2(
                    offset.X * cosine - offset.Y * sine,
                    offset.X * sine + offset.Y * cosine);
            }

            var clone = new ProceduralLizard(profile);
            clone.Spine.SetPose(source.Spine.Joints.Select(Transform).ToArray());
            for (var index = 0; index < 4; index++)
            {
                var sourceLeg = source.Legs[index];
                clone.Legs[index].SetDanglingPose(
                    Transform(sourceLeg.Shoulder),
                    Transform(sourceLeg.Elbow),
                    Transform(sourceLeg.Foot));
            }
            return clone;
        }
        return null;
    }

    private static (
        double MedianMilliseconds,
        double MaximumMilliseconds) MeasureCaptureStepTiming(
        LizardProfile profile,
        ScreenContext screen)
    {
        const int WarmupRuns = 1;
        const int MeasuredRuns = 7;
        var safeArea = screen.LostGripSafety.SafeArea;
        var maximumAvailable = Math.Max(
            profile.Behavior.LostGripFall.MinimumDistance,
            safeArea.Height - 2f);
        var availableDistance = Math.Min(
            maximumAvailable,
            profile.Behavior.LostGripFall.MinimumDistance + 14f);
        var start = new Vector2(
            safeArea.Center.X,
            safeArea.Bottom - availableDistance);
        var samples = new List<double>(MeasuredRuns);
        for (var run = 0; run < WarmupRuns + MeasuredRuns; run++)
        {
            var session = new PetSimulationSession(0x5A17, profile);
            session.Reset(start, 0f);
            for (var step = 0; step < 1000; step++)
            {
                var stopwatch = Stopwatch.StartNew();
                session.Advance(Input(
                    DeltaTime,
                    screen.NavigationArea,
                    profile,
                    lostGripSafety: screen.LostGripSafety));
                stopwatch.Stop();
                if (session.LizardForDiagnostics.RegripAnimationPhase !=
                    RegripAnimationPhase.Seeking)
                {
                    continue;
                }
                if (run >= WarmupRuns)
                {
                    samples.Add(stopwatch.Elapsed.TotalMilliseconds);
                }
                break;
            }
        }
        if (samples.Count == 0)
        {
            return (0d, 0d);
        }
        samples.Sort();
        return (samples[samples.Count / 2], samples[^1]);
    }

    private static bool RunMinimalLeadBranchTransitionContract(
        LizardProfile profile,
        out string detail)
    {
        const float CaptureProgress = 0.1f;
        // After capture remapping and the linear/SmoothStep blend, these map
        // to path amounts about 0.182 and 0.258. The former commits the
        // captured branch at full extension; the latter switches branch on
        // the same full-extension plateau.
        const float ExtensionProgress = 0.307f;
        const float SwitchProgress = 0.37f;
        var emotion = EmotionBlend.Normalize(
            new EmotionBlend(1f, 0f, 0f, 0f));

        LizardAnimationInput Input(float progress, bool contactAllowed) =>
            new(
                0f,
                0f,
                LizardPoseMode.FreeFall,
                emotion,
                0.9f,
                new Vector2(0f, 1f))
            {
                CatchPreparationProgress = progress,
                CatchPreparationContactAllowed = contactAllowed
            };

        var threeStep = CreateLegalCapturePoseLizard(profile);
        if (threeStep is null)
        {
            detail = "legal-capture-pose=not-found";
            return false;
        }
        threeStep.Update(
            DeltaTime,
            Input(CaptureProgress, contactAllowed: false));
        var capturePose = BranchPoseSnapshot.Capture(threeStep);
        var targetsAreReadable = Enumerable.Range(0, 4).All(index =>
            Vector2.Distance(
                threeStep.Legs[index].Foot,
                threeStep.RegripContactTarget(index)) >= 14f - 0.001f);
        var captureIsNonContact =
            threeStep.RegripAnimationPhase == RegripAnimationPhase.Seeking &&
            threeStep.RegripReachProgress <= 0.0001f &&
            !threeStep.RegripContacted &&
            threeStep.RegripContactLegMask == 0;

        threeStep.Update(
            DeltaTime,
            Input(ExtensionProgress, contactAllowed: false));
        var extensionPose = BranchPoseSnapshot.Capture(
            threeStep,
            capturePose.ArmLengths);
        var extensionKeepsCapturedBranch = Enumerable.Range(0, 4).All(index =>
            capturePose.BendSigns[index] == 0f ||
            extensionPose.BendSigns[index] == 0f ||
            capturePose.BendSigns[index] == extensionPose.BendSigns[index]);
        var extensionIsNonContact =
            threeStep.RegripAnimationPhase == RegripAnimationPhase.Seeking &&
            threeStep.RegripReachProgress > 0f &&
            threeStep.RegripReachProgress < 1f &&
            !threeStep.RegripContacted &&
            threeStep.RegripContactLegMask == 0;

        threeStep.Update(
            DeltaTime,
            Input(SwitchProgress, contactAllowed: false));
        var plateauPose = BranchPoseSnapshot.Capture(
            threeStep,
            capturePose.ArmLengths);
        var transitionCount = 0;
        var transitionsAreNarrow = true;
        var minimumTransitionStraightness = float.PositiveInfinity;
        var maximumTransitionElbowJump = 0f;
        for (var index = 0; index < 4; index++)
        {
            if (extensionPose.BendSigns[index] == 0f ||
                plateauPose.BendSigns[index] == 0f ||
                extensionPose.BendSigns[index] == plateauPose.BendSigns[index])
            {
                continue;
            }
            transitionCount++;
            var transitionStraightness = Math.Min(
                extensionPose.Straightness[index],
                plateauPose.Straightness[index]);
            var elbowJump = Vector2.Distance(
                extensionPose.Elbows[index],
                plateauPose.Elbows[index]);
            minimumTransitionStraightness = Math.Min(
                minimumTransitionStraightness,
                transitionStraightness);
            maximumTransitionElbowJump = Math.Max(
                maximumTransitionElbowJump,
                elbowJump);
            transitionsAreNarrow &=
                LostGripRegripAnimationProbe
                    .IsNearStraightIkBranchTransitionAllowed(
                        transitionsBefore: 0,
                        extensionPose.Straightness[index],
                        plateauPose.Straightness[index],
                        elbowJump);
        }
        var plateauIsNonContact =
            threeStep.RegripAnimationPhase == RegripAnimationPhase.Seeking &&
            threeStep.RegripReachProgress > 0f &&
            threeStep.RegripReachProgress < 1f &&
            !threeStep.RegripContacted &&
            threeStep.RegripContactLegMask == 0 &&
            LostGripRegripAnimationProbe.IsFourLimbBodyGeometrySafe(
                threeStep,
                out _,
                out _);

        threeStep.Update(DeltaTime, Input(1f, contactAllowed: true));
        var targetPose = BranchPoseSnapshot.Capture(
            threeStep,
            capturePose.ArmLengths);
        var targetContactIsAtomic =
            threeStep.RegripAnimationPhase == RegripAnimationPhase.Seeking &&
            threeStep.RegripReachProgress >= 1f - 0.0001f &&
            threeStep.RegripContacted &&
            threeStep.RegripContactLegMask == 0b1111 &&
            Enumerable.Range(0, 4).All(index =>
                Vector2.Distance(
                    threeStep.Legs[index].Foot,
                    threeStep.RegripContactTarget(index)) <= 0.25f) &&
            Enumerable.Range(0, 4).All(index =>
                plateauPose.BendSigns[index] == 0f ||
                targetPose.BendSigns[index] == 0f ||
                plateauPose.BendSigns[index] == targetPose.BendSigns[index]) &&
            LostGripRegripAnimationProbe.IsFourLimbBodyGeometrySafe(
                threeStep,
                out _,
                out _);

        threeStep.Update(
            DeltaTime,
            new LizardAnimationInput(
                0f,
                0f,
                LizardPoseMode.Regrip,
                emotion,
                0f,
                Vector2.Zero)
            {
                CatchPreparationContactAllowed = true
            });
        var stationaryContactHold =
            threeStep.RegripAnimationPhase == RegripAnimationPhase.ContactHold &&
            threeStep.RegripContacted &&
            threeStep.RegripContactLegMask == 0b1111;

        var twoStep = CreateLegalCapturePoseLizard(profile);
        if (twoStep is null)
        {
            detail = "two-step-legal-capture-pose=not-found";
            return false;
        }
        twoStep.Update(
            DeltaTime,
            Input(CaptureProgress, contactAllowed: false));
        twoStep.Update(DeltaTime, Input(1f, contactAllowed: true));
        var twoStepEndpointJumpIsNonContact =
            twoStep.RegripAnimationPhase == RegripAnimationPhase.Seeking &&
            !twoStep.RegripContacted &&
            twoStep.RegripContactLegMask == 0 &&
            twoStep.RegripContactError > RegripPoseController.ContactTolerance;

        detail =
            $"capture={captureIsNonContact},targets={targetsAreReadable}," +
            $"extension={extensionIsNonContact}," +
            $"old-sign={extensionKeepsCapturedBranch}," +
            $"switches={transitionCount},narrow={transitionsAreNarrow}," +
            $"straight=" +
            $"{(float.IsFinite(minimumTransitionStraightness) ? minimumTransitionStraightness : 0f):F5}," +
            $"jump={maximumTransitionElbowJump:F4}," +
            $"plateau={plateauIsNonContact}," +
            $"target={targetContactIsAtomic},hold={stationaryContactHold}," +
            $"two-step-noncontact={twoStepEndpointJumpIsNonContact}," +
            $"two-step-error={twoStep.RegripContactError:F3}," +
            $"two-step-mask={Convert.ToString(twoStep.RegripContactLegMask, 2)}";
        return captureIsNonContact && targetsAreReadable &&
               extensionIsNonContact && extensionKeepsCapturedBranch &&
               transitionCount > 0 && transitionsAreNarrow &&
               plateauIsNonContact && targetContactIsAtomic &&
               stationaryContactHold && twoStepEndpointJumpIsNonContact;
    }

    private static bool RunRotatedHeadingFourPawGeometry(
        LizardProfile profile,
        out string failureDetail)
    {
        failureDetail = "pass";
        var maximumNonBranchLimbJump = 0f;
        var worstNonBranchLimbJumpDetail = "none";
        var maximumProjectedLimbJump = 0f;
        var worstProjectedLimbJumpDetail = "none";
        var projectedLimbSamples = 0;
        var rollbackLimbSamples = 0;
        var emotion = EmotionBlend.Normalize(
            new EmotionBlend(1f, 0f, 0f, 0f));
        foreach (var heading in new[]
                 {
                     0f,
                     MathF.PI * 0.5f,
                     -MathF.PI * 0.5f,
                     MathF.PI
                 })
        {
            var headingDegrees = heading * 180f / MathF.PI;
            var lizard = CreateLegalCapturePoseLizard(profile, heading);
            var noReach = CreateLegalCapturePoseLizard(profile, heading);
            if (lizard is null || noReach is null)
            {
                failureDetail =
                    $"heading={headingDegrees:F0},stage=legal-capture-pose";
                return false;
            }
            var freeFallInput = new LizardAnimationInput(
                heading,
                0f,
                LizardPoseMode.FreeFall,
                emotion,
                0.6f,
                new Vector2(0f, 1f));
            lizard.Update(
                DeltaTime,
                freeFallInput with { CatchPreparationProgress = 0.05f });
            noReach.Update(DeltaTime, freeFallInput);
            var entryPoseJump = PoseSnapshot.Capture(lizard).MaximumDistance(
                PoseSnapshot.Capture(noReach));
            if (entryPoseJump > 0.0001f)
            {
                failureDetail =
                    $"heading={headingDegrees:F0},stage=entry," +
                    $"pose-jump={entryPoseJump:F4}";
                return false;
            }

            var startFeet = new Vector2[4];
            var startShoulders = new Vector2[4];
            var startRadii = new float[4];
            var armLengths = new float[4];
            var upperLengths = new float[4];
            var lowerLengths = new float[4];
            var lastNonZeroBendSigns = new float[4];
            var previousStraightness = new float[4];
            var previousShoulders = new Vector2[4];
            var previousElbows = new Vector2[4];
            var previousFeet = new Vector2[4];
            var branchTransitions = new int[4];
            var targets = new Vector2[4];
            var minimumTargetDisplacement = float.PositiveInfinity;
            var minimumTargetUpwardTravel = float.PositiveInfinity;
            for (var legIndex = 0; legIndex < 4; legIndex++)
            {
                var leg = lizard.Legs[legIndex];
                startFeet[legIndex] = leg.Foot;
                startShoulders[legIndex] = leg.Shoulder;
                startRadii[legIndex] = Vector2.Distance(
                    leg.Foot,
                    leg.Shoulder);
                armLengths[legIndex] =
                    Vector2.Distance(leg.Shoulder, leg.Elbow) +
                    Vector2.Distance(leg.Elbow, leg.Foot);
                upperLengths[legIndex] =
                    Vector2.Distance(leg.Shoulder, leg.Elbow);
                lowerLengths[legIndex] =
                    Vector2.Distance(leg.Elbow, leg.Foot);
                previousStraightness[legIndex] =
                    startRadii[legIndex] /
                    Math.Max(armLengths[legIndex], 0.0001f);
                previousShoulders[legIndex] = leg.Shoulder;
                previousElbows[legIndex] = leg.Elbow;
                previousFeet[legIndex] = leg.Foot;
                targets[legIndex] = lizard.RegripContactTarget(legIndex);
                minimumTargetDisplacement = Math.Min(
                    minimumTargetDisplacement,
                    Vector2.Distance(leg.Foot, targets[legIndex]));
                minimumTargetUpwardTravel = Math.Min(
                    minimumTargetUpwardTravel,
                    leg.Foot.Y - targets[legIndex].Y);
                var direction = MathEx.SafeNormalize(
                    leg.Foot - leg.Shoulder,
                    -Vector2.UnitY);
                var bend = Vector2.Dot(
                    leg.Elbow - leg.Shoulder,
                    MathEx.Perpendicular(direction));
                lastNonZeroBendSigns[legIndex] = MathF.Abs(bend) > 0.0001f
                    ? MathF.Sign(bend)
                    : DanglingTopology2D.GetSide(legIndex);
                if (targets[legIndex].Y >= leg.Shoulder.Y - 0.0001f)
                {
                    failureDetail =
                        $"heading={headingDegrees:F0},stage=target," +
                        $"leg={legIndex},targetY={targets[legIndex].Y:F3}," +
                        $"shoulderY={leg.Shoulder.Y:F3}";
                    return false;
                }
            }
            var captureContactSafe = lizard.RegripReachContactSafe;
            var targetGeometryDetail = string.Join(
                "/",
                Enumerable.Range(0, 4).Select(index =>
                {
                    var leg = lizard.Legs[index];
                    var targetElbow = SolveTwoBoneElbow(
                        leg.Shoulder,
                        targets[index],
                        upperLengths[index],
                        lowerLengths[index],
                        -lastNonZeroBendSigns[index]);
                    var safe = LostGripRegripAnimationProbe
                        .IsLimbBodyGeometrySafe(
                            lizard,
                            index,
                            leg.Shoulder,
                            targetElbow,
                            targets[index],
                            out var clearance,
                            out var reentries);
                    var readable =
                        Vector2.Distance(startFeet[index], targets[index]) >=
                            14f - 0.0001f &&
                        startFeet[index].Y - targets[index].Y >=
                            1.1f - 0.0001f;
                    var requiresProjection =
                        (lizard.RegripReachRequiresProjectionLegMask &
                         (1 << index)) != 0;
                    return
                        $"L{index}:d{Vector2.Distance(startFeet[index], targets[index]):F2}," +
                        $"u{startFeet[index].Y - targets[index].Y:F2}," +
                        $"read{readable},c{clearance:F2},r{reentries}," +
                        $"endpoint{safe},requires-projection{requiresProjection}";
                }));
            if (minimumTargetDisplacement < 14f - 0.0001f ||
                minimumTargetUpwardTravel < 1.1f - 0.0001f)
            {
                _ = LostGripRegripAnimationProbe.IsFourLimbBodyGeometrySafe(
                    lizard,
                    out var captureClearance,
                    out var captureReentries);
                failureDetail =
                    $"heading={headingDegrees:F0},stage=capture-safe," +
                    $"safe={captureContactSafe}," +
                    $"disp={minimumTargetDisplacement:F3}," +
                    $"up={minimumTargetUpwardTravel:F3}," +
                    $"clearance={captureClearance:F3}," +
                    $"reentries={captureReentries}," +
                    $"targets={targetGeometryDetail}";
                return false;
            }
            for (var first = 0; first < 4; first++)
            {
                for (var second = first + 1; second < 4; second++)
                {
                    if (Vector2.Distance(targets[first], targets[second]) <= 1f)
                    {
                        failureDetail =
                            $"heading={headingDegrees:F0},stage=target-gap," +
                            $"legs={first}/{second},gap=" +
                            $"{Vector2.Distance(targets[first], targets[second]):F3}";
                        return false;
                    }
                }
            }

            var displayOffsets = new[]
            {
                Enumerable.Range(0, 4)
                    .Select(index => startFeet[index] - startShoulders[index])
                    .ToArray(),
                Enumerable.Range(0, 4)
                    .Select(index => startFeet[index] - startShoulders[index])
                    .ToArray()
            };
            var visibleDisplayFrames = new int[2];
            var visibleDisplayFramesByLeg = new int[2, 4];
            var cumulativeTargetAdvances = new float[4];
            var minimumObservedPathClearance = float.PositiveInfinity;
            var observedPathReentries = 0;
            var headingRollbackLimbSamples = 0;
            var stepTrace = new List<string>();

            for (var reachStep = 1; reachStep <= 24; reachStep++)
            {
                var inputProgress = MathEx.Lerp(
                    0.05f,
                    1f,
                    reachStep / 24f);
                lizard.Update(
                    DeltaTime,
                    freeFallInput with
                    {
                        CatchPreparationProgress = inputProgress,
                        CatchPreparationContactAllowed = false
                    });
                var requiresProjectionMask =
                    lizard.RegripReachRequiresProjectionLegMask;
                var projectedMask =
                    lizard.RegripReachProjectedLegMaskThisStep;
                var rollbackMask =
                    lizard.RegripReachRollbackLegMaskThisStep;
                projectedLimbSamples += BitOperations.PopCount(
                    (uint)(projectedMask & 0b1111));
                var stepRollbackLimbSamples = BitOperations.PopCount(
                    (uint)(rollbackMask & 0b1111));
                rollbackLimbSamples += stepRollbackLimbSamples;
                headingRollbackLimbSamples += stepRollbackLimbSamples;
                var elbowFrameDeltas = new float[4];
                var footFrameDeltas = new float[4];
                var branchTransitionFrame = new bool[4];
                var oneStepTargetAdvances = new float[4];
                if (!LostGripRegripAnimationProbe.IsFourLimbBodyGeometrySafe(
                        lizard,
                        out var minimumRemoteClearance,
                        out var ownEnvelopeReentries))
                {
                    var branchDetail = string.Join(
                        "/",
                        Enumerable.Range(0, 4).Select(index =>
                        {
                            var leg = lizard.Legs[index];
                            var sign = MeasureBendSign(leg);
                            var straightness =
                                Vector2.Distance(leg.Shoulder, leg.Foot) /
                                Math.Max(armLengths[index], 0.0001f);
                            var oppositeElbow = SolveTwoBoneElbow(
                                leg.Shoulder,
                                leg.Foot,
                                upperLengths[index],
                                lowerLengths[index],
                                -lastNonZeroBendSigns[index]);
                            var oppositeGap = Vector2.Distance(
                                leg.Elbow,
                                oppositeElbow);
                            return
                                $"L{index}:{lastNonZeroBendSigns[index]:F0}>" +
                                $"{sign:F0},n{branchTransitions[index]}," +
                                $"straight{previousStraightness[index]:F5}/" +
                                $"{straightness:F5}," +
                                $"frameJump" +
                                $"{Vector2.Distance(previousElbows[index], leg.Elbow):F3}," +
                                $"oppositeGap{oppositeGap:F3}," +
                                $"readyProxy=" +
                                $"{(straightness >= 0.99f && oppositeGap <= 0.75f)}";
                        }));
                    failureDetail =
                        $"heading={headingDegrees:F0},stage=path," +
                        $"step={reachStep},progress={lizard.RegripReachProgress:F4}," +
                        $"clearance={minimumRemoteClearance:F3}," +
                        $"reentries={ownEnvelopeReentries}," +
                        $"geometry=" +
                        LostGripRegripAnimationProbe
                            .DescribeFourLimbBodyGeometry(lizard) +
                        $",branch={branchDetail}";
                    return false;
                }
                minimumObservedPathClearance = Math.Min(
                    minimumObservedPathClearance,
                    minimumRemoteClearance);
                observedPathReentries += ownEnvelopeReentries;
                for (var legIndex = 0; legIndex < 4; legIndex++)
                {
                    var leg = lizard.Legs[legIndex];
                    var direction = MathEx.SafeNormalize(
                        leg.Foot - leg.Shoulder,
                        -Vector2.UnitY);
                    var signedBend = Vector2.Dot(
                        leg.Elbow - leg.Shoulder,
                        MathEx.Perpendicular(direction));
                    var currentBendSign = MathF.Abs(signedBend) > 0.0001f
                        ? MathF.Sign(signedBend)
                        : 0f;
                    var currentStraightness =
                        Vector2.Distance(leg.Foot, leg.Shoulder) /
                        Math.Max(armLengths[legIndex], 0.0001f);
                    var isBranchTransition =
                        currentBendSign != 0f &&
                        currentBendSign != lastNonZeroBendSigns[legIndex];
                    branchTransitionFrame[legIndex] = isBranchTransition;
                    elbowFrameDeltas[legIndex] = Vector2.Distance(
                        previousElbows[legIndex] - previousShoulders[legIndex],
                        leg.Elbow - leg.Shoulder);
                    footFrameDeltas[legIndex] = Vector2.Distance(
                        previousFeet[legIndex] - previousShoulders[legIndex],
                        leg.Foot - leg.Shoulder);
                    var previousFootOffset =
                        previousFeet[legIndex] - previousShoulders[legIndex];
                    var currentFootOffset = leg.Foot - leg.Shoulder;
                    var targetOffset = targets[legIndex] - leg.Shoulder;
                    oneStepTargetAdvances[legIndex] = Vector2.Dot(
                        currentFootOffset - previousFootOffset,
                        MathEx.SafeNormalize(
                            targetOffset - previousFootOffset,
                            -Vector2.UnitY));
                    cumulativeTargetAdvances[legIndex] +=
                        oneStepTargetAdvances[legIndex];
                    if (!isBranchTransition)
                    {
                        var limbJump = Math.Max(
                            elbowFrameDeltas[legIndex],
                            footFrameDeltas[legIndex]);
                        if (limbJump > maximumNonBranchLimbJump)
                        {
                            maximumNonBranchLimbJump = limbJump;
                            worstNonBranchLimbJumpDetail =
                                $"heading={headingDegrees:F0}," +
                                $"step={reachStep},leg={legIndex}," +
                                $"progress={lizard.RegripReachProgress:F4}," +
                                $"elbow={elbowFrameDeltas[legIndex]:F4}," +
                                $"foot={footFrameDeltas[legIndex]:F4}";
                        }
                    }
                    if ((projectedMask & (1 << legIndex)) != 0)
                    {
                        var projectedJump = Math.Max(
                            elbowFrameDeltas[legIndex],
                            footFrameDeltas[legIndex]);
                        if (projectedJump > maximumProjectedLimbJump)
                        {
                            maximumProjectedLimbJump = projectedJump;
                            worstProjectedLimbJumpDetail =
                                $"heading={headingDegrees:F0}," +
                                $"step={reachStep},leg={legIndex}," +
                                $"progress={lizard.RegripReachProgress:F4}," +
                                $"elbow={elbowFrameDeltas[legIndex]:F4}," +
                                $"foot={footFrameDeltas[legIndex]:F4}";
                        }
                        if (projectedJump >
                            LostGripRegripAnimationProbe
                                .MaximumProjectionLimbPoseJump + 0.0001f)
                        {
                            failureDetail =
                                $"heading={headingDegrees:F0}," +
                                $"stage=projection-continuity," +
                                $"step={reachStep},leg={legIndex}," +
                                $"jump={projectedJump:F4}," +
                                $"requires={Convert.ToString(requiresProjectionMask, 2)}," +
                                $"projected={Convert.ToString(projectedMask, 2)}," +
                                $"rollback={Convert.ToString(rollbackMask, 2)}";
                            return false;
                        }
                    }
                    if (isBranchTransition)
                    {
                        var elbowJump = Vector2.Distance(
                            previousElbows[legIndex],
                            leg.Elbow);
                        if (!LostGripRegripAnimationProbe
                                .IsNearStraightIkBranchTransitionAllowed(
                                    branchTransitions[legIndex],
                                    previousStraightness[legIndex],
                                    currentStraightness,
                                    elbowJump))
                        {
                            failureDetail =
                                $"heading={headingDegrees:F0},stage=branch," +
                                $"step={reachStep},leg={legIndex}," +
                                $"switch={branchTransitions[legIndex] + 1}," +
                                $"straight=" +
                                $"{Math.Min(previousStraightness[legIndex], currentStraightness):F5}," +
                                $"elbow-jump={elbowJump:F4}";
                            return false;
                        }
                        branchTransitions[legIndex]++;
                        lastNonZeroBendSigns[legIndex] = currentBendSign;
                    }
                    var radiusRetention = Vector2.Distance(
                        leg.Foot,
                        leg.Shoulder) / Math.Max(startRadii[legIndex], 0.0001f);
                    if (radiusRetention < 0.74f ||
                        lizard.RegripContactTarget(legIndex).Y >=
                            leg.Shoulder.Y - 0.0001f)
                    {
                        failureDetail =
                            $"heading={headingDegrees:F0},stage=limb," +
                            $"step={reachStep},leg={legIndex}," +
                            $"bend={signedBend:F4},radius={radiusRetention:F4}," +
                            $"targetY={lizard.RegripContactTarget(legIndex).Y:F3}," +
                            $"shoulderY={leg.Shoulder.Y:F3}";
                        return false;
                    }
                    previousStraightness[legIndex] = currentStraightness;
                    previousShoulders[legIndex] = leg.Shoulder;
                    previousElbows[legIndex] = leg.Elbow;
                    previousFeet[legIndex] = leg.Foot;
                }

                var displayPhase = reachStep & 1;
                var allPawsVisiblyAdvance = true;
                var minimumDisplayDisplacement = float.PositiveInfinity;
                var minimumDisplayAdvance = float.PositiveInfinity;
                var minimumDisplayDisplacementLeg = -1;
                var minimumDisplayAdvanceLeg = -1;
                for (var legIndex = 0; legIndex < 4; legIndex++)
                {
                    var leg = lizard.Legs[legIndex];
                    var previousOffset = displayOffsets[displayPhase][legIndex];
                    var currentOffset = leg.Foot - leg.Shoulder;
                    var delta = currentOffset - previousOffset;
                    var targetRelative = targets[legIndex] - leg.Shoulder;
                    var catchDirection = MathEx.SafeNormalize(
                        targetRelative - previousOffset,
                        -Vector2.UnitY);
                    var displayDisplacement =
                        delta.Length() * profile.Appearance.VisualScale;
                    var displayAdvance =
                        Vector2.Dot(delta, catchDirection) *
                        profile.Appearance.VisualScale;
                    if (displayDisplacement < minimumDisplayDisplacement)
                    {
                        minimumDisplayDisplacement = displayDisplacement;
                        minimumDisplayDisplacementLeg = legIndex;
                    }
                    if (displayAdvance < minimumDisplayAdvance)
                    {
                        minimumDisplayAdvance = displayAdvance;
                        minimumDisplayAdvanceLeg = legIndex;
                    }
                    var visiblyAdvances =
                        displayDisplacement >= 0.5f &&
                        displayAdvance >= 0.5f;
                    allPawsVisiblyAdvance &= visiblyAdvances;
                    if (visiblyAdvances)
                    {
                        visibleDisplayFramesByLeg[displayPhase, legIndex]++;
                    }
                    displayOffsets[displayPhase][legIndex] = currentOffset;
                }
                if (allPawsVisiblyAdvance)
                {
                    visibleDisplayFrames[displayPhase]++;
                }
                var heldLegs = string.Join(
                    "",
                    Enumerable.Range(0, 4)
                        .Where(index =>
                            elbowFrameDeltas[index] <= 0.001f &&
                            footFrameDeltas[index] <= 0.001f));
                stepTrace.Add(
                    $"{reachStep}:p{lizard.RegripReachProgress:F3}," +
                    $"s{(lizard.RegripReachContactSafe ? 1 : 0)}," +
                    $"m{Convert.ToString(requiresProjectionMask, 16)}/" +
                    $"{Convert.ToString(projectedMask, 16)}/" +
                    $"{Convert.ToString(rollbackMask, 16)}," +
                    $"h[{(heldLegs.Length == 0 ? "-" : heldLegs)}]," +
                    $"e[{string.Join('/', elbowFrameDeltas.Select(value => value.ToString("F2")))}]," +
                    $"f[{string.Join('/', footFrameDeltas.Select(value => value.ToString("F2")))}]," +
                    $"b[{string.Join("", branchTransitionFrame.Select(value => value ? '1' : '0'))}]," +
                    $"q{displayPhase}:dL{minimumDisplayDisplacementLeg}=" +
                    $"{minimumDisplayDisplacement:F2},aL{minimumDisplayAdvanceLeg}=" +
                    $"{minimumDisplayAdvance:F2}");
            }

            if (lizard.RegripReachProgress < 1f - 0.0001f ||
                lizard.RegripContactError > 0.25f ||
                lizard.RegripContacted ||
                lizard.RegripContactLegMask != 0)
            {
                failureDetail =
                    $"heading={headingDegrees:F0},stage=terminal-state," +
                    $"progress={lizard.RegripReachProgress:F4}," +
                    $"error={lizard.RegripContactError:F3}," +
                    $"contacted={lizard.RegripContacted}," +
                    $"mask={Convert.ToString(lizard.RegripContactLegMask, 2)}";
                return false;
            }
            if (visibleDisplayFrames.Min() < 6)
            {
                var perLegFrames = string.Join(
                    "/",
                    Enumerable.Range(0, 4).Select(index =>
                        $"L{index}:{visibleDisplayFramesByLeg[0, index]}/" +
                        $"{visibleDisplayFramesByLeg[1, index]}"));
                failureDetail =
                    $"heading={headingDegrees:F0},stage=timing," +
                    $"60hz-all={visibleDisplayFrames[0]}/" +
                    $"{visibleDisplayFrames[1]},per-leg={perLegFrames}," +
                    $"advance=[{string.Join('/', cumulativeTargetAdvances.Select(value => value.ToString("F3")))}]," +
                    $"terminal-safe={lizard.RegripReachContactSafe}," +
                    $"terminal-error={lizard.RegripContactError:F3}," +
                    $"targets={targetGeometryDetail}," +
                    $"trace={string.Join(';', stepTrace)}";
                return false;
            }
            if (!captureContactSafe || !lizard.RegripReachContactSafe)
            {
                failureDetail =
                    $"heading={headingDegrees:F0},stage=contact-safe," +
                    $"capture={captureContactSafe}," +
                    $"terminal={lizard.RegripReachContactSafe}," +
                    $"actual-clearance={minimumObservedPathClearance:F3}," +
                    $"actual-reentries={observedPathReentries}," +
                    $"targets={targetGeometryDetail}";
                return false;
            }
            if (headingRollbackLimbSamples != 0)
            {
                failureDetail =
                    $"heading={headingDegrees:F0},stage=rollback," +
                    $"count={headingRollbackLimbSamples}," +
                    $"requires={Convert.ToString(lizard.RegripReachRequiresProjectionLegMask, 2)}," +
                    $"projected-total={lizard.RegripReachProjectionCount}," +
                    $"rollback-total={lizard.RegripReachRollbackCount}";
                return false;
            }

            var terminalPose = PoseSnapshot.Capture(lizard);
            lizard.Update(
                DeltaTime,
                freeFallInput with
                {
                    PoseMode = LizardPoseMode.Regrip,
                    DropProgress = 0f,
                    CatchPreparationProgress = 1f,
                    CatchPreparationContactAllowed = true,
                    ScreenDeltaModel = Vector2.Zero
                });
            var contactPoseJump = terminalPose.MaximumDistance(
                PoseSnapshot.Capture(lizard));
            if (lizard.RegripAnimationPhase != RegripAnimationPhase.ContactHold ||
                !lizard.RegripContacted ||
                lizard.RegripContactLegMask != 0b1111 ||
                lizard.RegripContactError > 0.25f ||
                contactPoseJump > 0.01f)
            {
                failureDetail =
                    $"heading={headingDegrees:F0},stage=contact," +
                    $"phase={lizard.RegripAnimationPhase}," +
                    $"mask={Convert.ToString(lizard.RegripContactLegMask, 2)}," +
                    $"error={lizard.RegripContactError:F3}," +
                    $"jump={contactPoseJump:F4}";
                return false;
            }
        }
        failureDetail =
            $"pass,max-nonbranch-limb-jump={maximumNonBranchLimbJump:F4}," +
            $"worst-nonbranch={worstNonBranchLimbJumpDetail}," +
            $"projected/rollback={projectedLimbSamples}/" +
            $"{rollbackLimbSamples}," +
            $"max-projected-jump={maximumProjectedLimbJump:F4}," +
            $"worst-projected={worstProjectedLimbJumpDetail}";
        return true;
    }

    private static bool RunContactSafeNegativeContract(
        LizardProfile profile,
        out string detail)
    {
        var source = CreateLegalCapturePoseLizard(profile);
        if (source is null)
        {
            detail = "setup-failed";
            return false;
        }

        var unsafeProfile = profile with
        {
            Appearance = profile.Appearance with { LimbWidth = 160f }
        };
        var lizard = new ProceduralLizard(unsafeProfile);
        lizard.Spine.SetPose(source.Spine.Joints.ToArray());
        for (var index = 0; index < 4; index++)
        {
            var leg = source.Legs[index];
            lizard.Legs[index].SetDanglingPose(
                leg.Shoulder,
                leg.Elbow,
                leg.Foot);
        }

        var emotion = EmotionBlend.Normalize(
            new EmotionBlend(1f, 0f, 0f, 0f));
        var input = new LizardAnimationInput(
            0f,
            0f,
            LizardPoseMode.FreeFall,
            emotion,
            0.85f,
            new Vector2(0f, 1f));
        lizard.Update(
            DeltaTime,
            input with
            {
                CatchPreparationProgress = 0.1f,
                CatchPreparationContactAllowed = false
            });
        var captureSafe = lizard.RegripReachContactSafe;
        var captureMask = lizard.RegripContactLegMask;
        var captureContacted = lizard.RegripContacted;
        var unsafeGeometryObserved =
            !LostGripRegripAnimationProbe.IsFourLimbBodyGeometrySafe(
                lizard,
                out var captureClearance,
                out var captureReentries);

        foreach (var progress in new[] { 0.307f, 0.37f, 0.99995f })
        {
            lizard.Update(
                DeltaTime,
                input with
                {
                    CatchPreparationProgress = progress,
                    CatchPreparationContactAllowed = true
                });
        }
        var terminalSafe = lizard.RegripReachContactSafe;
        var terminalMask = lizard.RegripContactLegMask;
        var terminalContacted = lizard.RegripContacted;
        lizard.Update(
            DeltaTime,
            input with
            {
                PoseMode = LizardPoseMode.Regrip,
                DropProgress = 0f,
                CatchPreparationProgress = 1f,
                CatchPreparationContactAllowed = true,
                ScreenDeltaModel = Vector2.Zero
            });

        detail =
            $"capture-safe={captureSafe},geometry-unsafe=" +
            $"{unsafeGeometryObserved},clearance={captureClearance:F3}," +
            $"reentries={captureReentries},capture-mask=" +
            $"{Convert.ToString(captureMask, 2)}," +
            $"capture-contacted={captureContacted},terminal-safe=" +
            $"{terminalSafe},terminal-mask=" +
            $"{Convert.ToString(terminalMask, 2)}," +
            $"terminal-contacted={terminalContacted},after-phase=" +
            $"{lizard.RegripAnimationPhase},after-mask=" +
            $"{Convert.ToString(lizard.RegripContactLegMask, 2)}," +
            $"after-contacted={lizard.RegripContacted}," +
            $"rollback={lizard.RegripReachRollbackCount}";
        return unsafeGeometryObserved &&
               !captureSafe &&
               captureMask == 0 &&
               !captureContacted &&
               !terminalSafe &&
               terminalMask == 0 &&
               !terminalContacted &&
               lizard.RegripAnimationPhase == RegripAnimationPhase.None &&
               lizard.RegripContactLegMask == 0 &&
               !lizard.RegripContacted &&
               lizard.Spine.Joints.All(IsFinite) &&
               lizard.Legs.All(leg =>
                   IsFinite(leg.Shoulder) &&
                   IsFinite(leg.Elbow) &&
                   IsFinite(leg.Foot));
    }

    private static bool RunDpiRefreshDeterminism(LizardProfile profile)
    {
        try
        {
            var scenarios = new[] { 1d, 1.25d, 1.5d, 2d }
                .SelectMany(scale => new[] { 60, 120, 240 }
                    .Select(rate => new DpiCadenceScenario(profile, scale, rate)))
                .ToArray();

            foreach (var scenario in scenarios)
            {
                scenario.AdvanceForCommonTicks(180);
                scenario.Runtime.MoveTo(new Vector2(
                    scenario.NavigationArea.Center.X,
                    scenario.NavigationArea.Top + 2f));
                if (!scenario.Module.DebugBridge
                        .TryPlay(PortableDebugAction.LostGripFall)
                        .Accepted)
                {
                    return false;
                }
            }

            var partialReachSeen = false;
            var reachedTargetSeen = false;
            for (var commonTick = 0; commonTick < 600; commonTick++)
            {
                foreach (var scenario in scenarios)
                {
                    scenario.AdvanceForCommonTicks(1);
                }

                var baseline = scenarios[0];
                for (var index = 1; index < scenarios.Length; index++)
                {
                    if (Vector2.Distance(
                            baseline.Runtime.Position,
                            scenarios[index].Runtime.Position) > 0.0001f ||
                        !RenderFramesNear(
                            baseline.Runtime.CurrentSnapshot,
                            scenarios[index].Runtime.CurrentSnapshot,
                            0.0001f))
                    {
                        return false;
                    }
                }

                if (!baseline.Module.DebugBridge.TryCapture(out var snapshot))
                {
                    return false;
                }
                partialReachSeen |=
                    snapshot.LostGripPhase ==
                        PortableDebugLostGripPhase.Falling &&
                    snapshot.LostGripReachProgress is > 0f and < 1f;
                reachedTargetSeen |=
                    snapshot.LostGripPhase ==
                        PortableDebugLostGripPhase.Regripping &&
                    snapshot.LostGripCatchReason ==
                        PortableDebugLostGripCatchReason.ReachedTarget;
                if (partialReachSeen && reachedTargetSeen &&
                    snapshot.LostGripRegripProgress > 0f)
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool RenderFramesNear(
        LizardRenderFrame expected,
        LizardRenderFrame actual,
        float tolerance)
    {
        if (expected.BodyOutline.Length != actual.BodyOutline.Length ||
            expected.Legs.Length != actual.Legs.Length)
        {
            return false;
        }
        for (var index = 0; index < expected.BodyOutline.Length; index++)
        {
            if (Vector2.Distance(
                    expected.BodyOutline[index],
                    actual.BodyOutline[index]) > tolerance)
            {
                return false;
            }
        }
        for (var index = 0; index < expected.Legs.Length; index++)
        {
            if (Vector2.Distance(
                    expected.Legs[index].Shoulder,
                    actual.Legs[index].Shoulder) > tolerance ||
                Vector2.Distance(
                    expected.Legs[index].Elbow,
                    actual.Legs[index].Elbow) > tolerance ||
                Vector2.Distance(
                    expected.Legs[index].Foot,
                    actual.Legs[index].Foot) > tolerance)
            {
                return false;
            }
        }
        return Vector2.Distance(expected.HeadNose, actual.HeadNose) <= tolerance &&
               Vector2.Distance(
                   expected.NegativeEyeCenter,
                   actual.NegativeEyeCenter) <= tolerance &&
               Vector2.Distance(
                   expected.PositiveEyeCenter,
                   actual.PositiveEyeCenter) <= tolerance &&
               MathF.Abs(expected.Heading - actual.Heading) <= tolerance &&
               MathF.Abs(expected.BlinkAmount - actual.BlinkAmount) <= tolerance;
    }

    private static SafetyLossFallbackResult RunMidSeekingSafetyLossFallback(
        LizardProfile profile,
        ScreenContext screen)
    {
        const float ForcedHostShift = 8f;
        const float PartialReachMinimum = 0.25f;
        const float PartialReachMaximum = 0.75f;
        var safeArea = screen.LostGripSafety.SafeArea;
        var start = new Vector2(safeArea.Center.X, safeArea.Top + 2f);
        var session = CreateDebugPlaybackSession(
            profile,
            screen.NavigationArea,
            screen.LostGripSafety,
            start,
            0x67D5);
        var started = session.TryPlayDebugAction(
            AutonomousAction.LostGripFall,
            screen.NavigationArea,
            screen.LostGripSafety);
        if (started.Status != DebugPlaybackStatus.Started)
        {
            return SafetyLossFallbackResult.Failed;
        }

        var behavior = session.BehaviorForDiagnostics;
        var lizard = session.LizardForDiagnostics;
        var foundPartialSeeking = false;
        var lastNonZeroBendSigns = new float[4];
        for (var step = 0; step < 8f / DeltaTime; step++)
        {
            session.Advance(Input(
                DeltaTime,
                screen.NavigationArea,
                profile,
                lostGripSafety: screen.LostGripSafety));
            if (lizard.RegripAnimationPhase == RegripAnimationPhase.Seeking)
            {
                _ = CountAndUpdateBendSignTransitions(
                    lizard,
                    lastNonZeroBendSigns);
            }
            if (behavior.LostGripPhase == LostGripFallPhase.Falling &&
                lizard.RegripAnimationPhase == RegripAnimationPhase.Seeking &&
                lizard.RegripReachProgress >= PartialReachMinimum &&
                lizard.RegripReachProgress <= PartialReachMaximum &&
                !lizard.RegripContacted &&
                lizard.RegripContactLegMask == 0)
            {
                foundPartialSeeking = true;
                break;
            }
        }
        if (!foundPartialSeeking)
        {
            return SafetyLossFallbackResult.Failed;
        }

        var previousPosition = behavior.Position;
        var previousPose = PoseSnapshot.Capture(lizard);
        var previousReachProgress = lizard.RegripReachProgress;
        var forcedLeft = previousPosition.X + ForcedHostShift;
        if (forcedLeft >= safeArea.Right - 1f)
        {
            return SafetyLossFallbackResult.Failed;
        }
        var unavailableSafety = new LostGripSafetyContext(
            new FloatRect(
                forcedLeft,
                safeArea.Top,
                safeArea.Right,
                safeArea.Bottom),
            IsAvailable: false);

        var maximumPoseJump = 0f;
        var minimumBoundaryMargin = float.PositiveInfinity;
        var fakeContactSamples = 0;
        var branchTransitionsAfterTrigger = 0;
        var finiteAndBounded = true;

        var movingFallbackFrame = session.Advance(Input(
            DeltaTime,
            screen.NavigationArea,
            profile,
            lostGripSafety: unavailableSafety));
        ObserveFallbackFrame(movingFallbackFrame);
        var partialMovingFallback =
            behavior.LostGripPhase == LostGripFallPhase.Regripping &&
            behavior.LostGripCatchReason == LostGripCatchReason.SafetyForced &&
            behavior.Position.X >= forcedLeft - 0.001f &&
            Vector2.Distance(previousPosition, behavior.Position) > 0.001f &&
            lizard.CurrentPoseMode == LizardPoseMode.FreeFall &&
            lizard.RegripAnimationPhase == RegripAnimationPhase.Seeking &&
            lizard.RegripReachActive &&
            lizard.RegripReachProgress > 0f &&
            lizard.RegripReachProgress < 1f - 0.0001f &&
            lizard.RegripReachProgress <= previousReachProgress + 0.08f &&
            !lizard.RegripContacted &&
            lizard.RegripContactLegMask == 0;

        var stationaryFallbackFrame = session.Advance(Input(
            DeltaTime,
            screen.NavigationArea,
            profile,
            lostGripSafety: unavailableSafety));
        ObserveFallbackFrame(stationaryFallbackFrame);
        var continuousNonContactFallback =
            behavior.LostGripPhase == LostGripFallPhase.Regripping &&
            behavior.LostGripCatchReason == LostGripCatchReason.SafetyForced &&
            lizard.CurrentPoseMode == LizardPoseMode.Regrip &&
            lizard.RegripAnimationPhase == RegripAnimationPhase.None &&
            !lizard.RegripReachActive &&
            lizard.RegripReachProgress == 0f &&
            !lizard.RegripContacted &&
            lizard.RegripContactLegMask == 0 &&
            !lizard.CaptureDebugSnapshot().DanglingActive;

        var recovered = false;
        for (var step = 0; step < 2f / DeltaTime; step++)
        {
            var frame = session.Advance(Input(
                DeltaTime,
                screen.NavigationArea,
                profile,
                lostGripSafety: unavailableSafety));
            ObserveFallbackFrame(frame);
            if (behavior.State == RoamingState.Idle &&
                behavior.LostGripPhase == LostGripFallPhase.None &&
                lizard.RegripAnimationPhase == RegripAnimationPhase.None)
            {
                recovered = true;
                break;
            }
        }

        if (!float.IsFinite(minimumBoundaryMargin))
        {
            minimumBoundaryMargin = float.NegativeInfinity;
        }
        var passed =
            partialMovingFallback &&
            continuousNonContactFallback &&
            recovered &&
            finiteAndBounded &&
            maximumPoseJump <= 3f &&
            minimumBoundaryMargin >= -0.001f &&
            fakeContactSamples == 0 &&
            branchTransitionsAfterTrigger == 0;
        return new SafetyLossFallbackResult(
            passed,
            maximumPoseJump,
            minimumBoundaryMargin,
            fakeContactSamples,
            branchTransitionsAfterTrigger,
            $"partial-moving={partialMovingFallback}," +
            $"continuous={continuousNonContactFallback}," +
            $"recovered={recovered},finite={finiteAndBounded}," +
            $"jump={maximumPoseJump:F4},margin={minimumBoundaryMargin:F3}," +
            $"fake={fakeContactSamples}," +
            $"post-trigger-switches={branchTransitionsAfterTrigger}");

        void ObserveFallbackFrame(PetSimulationFrameOutput frame)
        {
            var pose = PoseSnapshot.Capture(lizard);
            maximumPoseJump = Math.Max(
                maximumPoseJump,
                previousPose.MaximumDistance(pose));
            previousPose = pose;
            if (lizard.RegripAnimationPhase == RegripAnimationPhase.Seeking)
            {
                branchTransitionsAfterTrigger +=
                    CountAndUpdateBendSignTransitions(
                        lizard,
                        lastNonZeroBendSigns);
            }

            var drawableMargin = MeasureDrawableMargins(
                profile,
                screen.WorkArea,
                behavior.Position,
                frame.RenderFrame).Screen;
            var hostMargin = Math.Min(
                Math.Min(
                    behavior.Position.X - screen.FullRenderRadius -
                    screen.WorkArea.Left,
                    screen.WorkArea.Right - behavior.Position.X -
                    screen.FullRenderRadius),
                Math.Min(
                    behavior.Position.Y - screen.FullRenderRadius -
                    screen.WorkArea.Top,
                    screen.WorkArea.Bottom - behavior.Position.Y -
                    screen.FullRenderRadius));
            minimumBoundaryMargin = Math.Min(
                minimumBoundaryMargin,
                Math.Min(drawableMargin, hostMargin));
            if (lizard.RegripAnimationPhase == RegripAnimationPhase.ContactHold ||
                lizard.RegripContacted ||
                lizard.RegripContactLegMask != 0)
            {
                fakeContactSamples++;
            }
            finiteAndBounded &=
                screen.NavigationArea.Contains(behavior.Position) &&
                IsFinite(session) &&
                drawableMargin >= -0.001f &&
                hostMargin >= -0.001f;
        }
    }

    private static bool RunNearCompleteSafetyContactGate(LizardProfile profile)
    {
        var lizard = CreateLegalCapturePoseLizard(profile);
        if (lizard is null)
        {
            return false;
        }
        var neutralEmotion = EmotionBlend.Normalize(
            new EmotionBlend(1f, 0f, 0f, 0f));
        lizard.Update(
            DeltaTime,
            new LizardAnimationInput(
                0f,
                0f,
                LizardPoseMode.FreeFall,
                neutralEmotion,
                0.85f,
                new Vector2(0f, 1f))
            {
                CatchPreparationProgress = 0.1f,
                CatchPreparationContactAllowed = false
            });
        lizard.Update(
            DeltaTime,
            new LizardAnimationInput(
                0f,
                0f,
                LizardPoseMode.FreeFall,
                neutralEmotion,
                0.88f,
                new Vector2(0f, 1f))
            {
                CatchPreparationProgress = 0.307f,
                CatchPreparationContactAllowed = false
            });
        lizard.Update(
            DeltaTime,
            new LizardAnimationInput(
                0f,
                0f,
                LizardPoseMode.FreeFall,
                neutralEmotion,
                0.89f,
                new Vector2(0f, 1f))
            {
                CatchPreparationProgress = 0.37f,
                CatchPreparationContactAllowed = false
            });
        lizard.Update(
            DeltaTime,
            new LizardAnimationInput(
                0f,
                0f,
                LizardPoseMode.FreeFall,
                neutralEmotion,
                0.9f,
                new Vector2(0f, 1f))
            {
                CatchPreparationProgress = 0.99995f,
                CatchPreparationContactAllowed = false
            });
        var preparedInsideCompletionEpsilon =
            lizard.RegripReachActive &&
            lizard.RegripReachProgress >= 1f - 0.0001f &&
            lizard.RegripContactError <= 0.25f &&
            !lizard.RegripContacted &&
            lizard.RegripContactLegMask == 0;

        lizard.Update(
            DeltaTime,
            new LizardAnimationInput(
                0f,
                0f,
                LizardPoseMode.Regrip,
                neutralEmotion,
                0f,
                Vector2.Zero));
        return preparedInsideCompletionEpsilon &&
               lizard.RegripAnimationPhase == RegripAnimationPhase.None &&
               !lizard.RegripContacted &&
               lizard.RegripContactLegMask == 0;
    }

    private static bool RunNearCompleteReachedTargetContactGate(
        LizardProfile profile)
    {
        var lizard = CreateLegalCapturePoseLizard(profile);
        if (lizard is null)
        {
            return false;
        }
        var neutralEmotion = EmotionBlend.Normalize(
            new EmotionBlend(1f, 0f, 0f, 0f));
        lizard.Update(
            DeltaTime,
            new LizardAnimationInput(
                0f,
                0f,
                LizardPoseMode.FreeFall,
                neutralEmotion,
                0.85f,
                new Vector2(0f, 1f))
            {
                CatchPreparationProgress = 0.1f
            });
        lizard.Update(
            DeltaTime,
            new LizardAnimationInput(
                0f,
                0f,
                LizardPoseMode.FreeFall,
                neutralEmotion,
                0.88f,
                new Vector2(0f, 1f))
            {
                CatchPreparationProgress = 0.307f
            });
        lizard.Update(
            DeltaTime,
            new LizardAnimationInput(
                0f,
                0f,
                LizardPoseMode.FreeFall,
                neutralEmotion,
                0.89f,
                new Vector2(0f, 1f))
            {
                CatchPreparationProgress = 0.37f
            });
        lizard.Update(
            DeltaTime,
            new LizardAnimationInput(
                0f,
                0f,
                LizardPoseMode.FreeFall,
                neutralEmotion,
                0.9f,
                new Vector2(0f, 1f))
            {
                CatchPreparationProgress = 0.99995f
            });
        var preparedInsideCompletionEpsilon =
            lizard.RegripReachActive &&
            lizard.RegripReachProgress >= 1f - 0.0001f &&
            lizard.RegripContactError <= 0.25f &&
            !lizard.RegripContacted;

        lizard.Update(
            DeltaTime,
            new LizardAnimationInput(
                0f,
                0f,
                LizardPoseMode.Regrip,
                neutralEmotion,
                0f,
                Vector2.Zero)
            {
                CatchPreparationContactAllowed = true
            });
        return preparedInsideCompletionEpsilon &&
               lizard.RegripAnimationPhase ==
               RegripAnimationPhase.ContactHold &&
               lizard.RegripContacted &&
               lizard.RegripContactLegMask == 0b1111 &&
               Enumerable.Range(0, 4).All(index =>
                   Vector2.Distance(
                       lizard.Legs[index].Foot,
                       lizard.RegripContactTarget(index)) <= 0.25f);
    }

    private static bool RunPausePriority(LizardProfile profile, ScreenContext screen)
    {
        var session = CreateEnteredSession(profile, screen, 0x4FA7);
        if (session is null)
        {
            return false;
        }
        var beforePause = session.Position;
        session.TogglePaused();
        session.Advance(Input(
            DeltaTime,
            screen.NavigationArea,
            profile,
            lostGripSafety: screen.LostGripSafety));
        var behavior = session.BehaviorForDiagnostics;
        if (behavior.State != RoamingState.Idle ||
            behavior.LostGripPhase != LostGripFallPhase.None ||
            behavior.Speed != 0f)
        {
            return false;
        }
        for (var step = 0; step < 0.35f / DeltaTime; step++)
        {
            session.Advance(Input(
                DeltaTime,
                screen.NavigationArea,
                profile,
                lostGripSafety: screen.LostGripSafety));
        }
        return session.IsPaused &&
               Vector2.Distance(beforePause, session.Position) <= 0.001f &&
               behavior.State == RoamingState.Idle &&
               IsFinite(session);
    }

    private static PetSimulationSession? CreateEnteredSession(
        LizardProfile profile,
        ScreenContext screen,
        int seed)
    {
        var session = new PetSimulationSession(seed, profile);
        session.Reset(screen.NavigationArea.Center, 0f);
        for (var step = 0; step < 2f / DeltaTime; step++)
        {
            session.Advance(Input(
                DeltaTime,
                screen.NavigationArea,
                profile,
                lostGripSafety: screen.LostGripSafety));
            if (session.BehaviorForDiagnostics.State == RoamingState.LostGripFall &&
                session.BehaviorForDiagnostics.LostGripPhase == LostGripFallPhase.Falling)
            {
                return session;
            }
        }
        return null;
    }

    private static Vector2 ObserveSafety(
        LizardProfile profile,
        ScreenContext screen,
        BehaviorController behavior,
        ProceduralLizard lizard,
        LizardRenderFrame renderFrame,
        Metrics metrics)
    {
        if (!screen.NavigationArea.Contains(behavior.Position))
        {
            metrics.NavigationBoundaryViolations++;
        }
        var visualCenter = behavior.Position;
        if (behavior.State == RoamingState.LostGripFall)
        {
            var safeBottom = screen.NavigationArea.Bottom -
                             profile.Behavior.LostGripFall.BottomSafetyInset;
            if (behavior.Position.Y > safeBottom + 0.001f ||
                behavior.Position.X - screen.FullRenderRadius < screen.WorkArea.Left - 0.001f ||
                behavior.Position.X + screen.FullRenderRadius > screen.WorkArea.Right + 0.001f ||
                behavior.Position.Y - screen.FullRenderRadius < screen.WorkArea.Top - 0.001f ||
                behavior.Position.Y + screen.FullRenderRadius > screen.WorkArea.Bottom + 0.001f)
            {
                metrics.FullRenderBoundaryViolations++;
            }

            var drawableMargins = MeasureDrawableMargins(
                profile,
                screen.WorkArea,
                behavior.Position,
                renderFrame);
            metrics.MinimumCanvasMargin = Math.Min(
                metrics.MinimumCanvasMargin,
                drawableMargins.Canvas);
            metrics.MinimumVisualMargin = Math.Min(
                metrics.MinimumVisualMargin,
                drawableMargins.Screen);
            visualCenter = drawableMargins.Center;
            if (drawableMargins.Screen < -0.001f)
            {
                metrics.VisualBoundaryViolations++;
            }
        }

        metrics.MaximumFallVelocity = Math.Max(
            metrics.MaximumFallVelocity,
            behavior.LostGripVerticalVelocity);
        if (!IsFinite(behavior, lizard))
        {
            metrics.NonFiniteSamples++;
        }
        return visualCenter;
    }

    private static (float Canvas, float Screen, Vector2 Center) MeasureDrawableMargins(
        LizardProfile profile,
        FloatRect workArea,
        Vector2 hostCenter,
        LizardRenderFrame frame)
    {
        var appearance = profile.Appearance;
        var rendering = profile.Rendering;
        var canvasCenter = appearance.RenderCanvasSize * 0.5f;
        var scale = appearance.VisualScale;
        var shadowOffset = new Vector2(
            rendering.ShadowOffsetX,
            rendering.ShadowOffsetY);
        var minimumCanvasMargin = float.PositiveInfinity;
        var minimumScreenMargin = float.PositiveInfinity;
        var minimumModel = new Vector2(float.PositiveInfinity);
        var maximumModel = new Vector2(float.NegativeInfinity);

        foreach (var point in frame.BodyOutline)
        {
            Include(point, 0f);
            Include(point + shadowOffset, 0f);
        }

        Include(frame.HeadNose, rendering.NoseRadius);
        Include(frame.HeadNose + shadowOffset, rendering.NoseRadius);
        var eyeAndPupilRadius = Math.Max(
            rendering.EyeRadius,
            rendering.PupilOffset + rendering.PupilRadius);
        Include(frame.NegativeEyeCenter, eyeAndPupilRadius);
        Include(frame.PositiveEyeCenter, eyeAndPupilRadius);
        Include(
            frame.NegativeEyeCenter + shadowOffset,
            rendering.EyeShadowRadius);
        Include(
            frame.PositiveEyeCenter + shadowOffset,
            rendering.EyeShadowRadius);

        foreach (var leg in frame.Legs)
        {
            var footRadius = leg.IsFront
                ? rendering.FrontFootRadius
                : rendering.RearFootRadius;
            var limbRadius = appearance.LimbWidth * 0.5f;
            var shadowLimbRadius = appearance.ShadowLimbWidth * 0.5f;
            Include(leg.Shoulder, limbRadius);
            Include(leg.Elbow, limbRadius);
            Include(leg.Foot, Math.Max(limbRadius, footRadius));
            Include(leg.Shoulder + shadowOffset, shadowLimbRadius);
            Include(leg.Elbow + shadowOffset, shadowLimbRadius);
            Include(
                leg.Foot + shadowOffset,
                Math.Max(shadowLimbRadius, footRadius));
        }

        return (
            minimumCanvasMargin - DrawableRasterMargin,
            minimumScreenMargin - DrawableRasterMargin * scale,
            hostCenter +
            ((minimumModel + maximumModel) * 0.5f - new Vector2(canvasCenter)) * scale);

        void Include(Vector2 modelPoint, float modelRadius)
        {
            var screenPoint = hostCenter +
                              (modelPoint - new Vector2(canvasCenter)) * scale;
            var radius = modelRadius * scale;
            minimumModel = Vector2.Min(
                minimumModel,
                modelPoint - new Vector2(modelRadius));
            maximumModel = Vector2.Max(
                maximumModel,
                modelPoint + new Vector2(modelRadius));
            minimumCanvasMargin = Math.Min(
                minimumCanvasMargin,
                Math.Min(
                    Math.Min(
                        modelPoint.X - modelRadius,
                        appearance.RenderCanvasSize - modelPoint.X - modelRadius),
                    Math.Min(
                        modelPoint.Y - modelRadius,
                        appearance.RenderCanvasSize - modelPoint.Y - modelRadius)));
            minimumScreenMargin = Math.Min(
                minimumScreenMargin,
                Math.Min(
                    Math.Min(
                        screenPoint.X - radius - workArea.Left,
                        workArea.Right - screenPoint.X - radius),
                    Math.Min(
                        screenPoint.Y - radius - workArea.Top,
                        workArea.Bottom - screenPoint.Y - radius)));
        }
    }

    private static LizardProfile CreateDeterministicProfile()
    {
        var source = LizardConfiguration.Default;
        var fallOnly = new TransitionRowConfiguration
        {
            Entries = [new WeightedTransitionConfiguration(AutonomousAction.LostGripFall, 1f)]
        };
        var configuration = source with
        {
            IndividualVariation = source.IndividualVariation with { Enabled = false },
            Behavior = source.Behavior with
            {
                Timing = source.Behavior.Timing with
                {
                    ResetSpawnDuration = 0.05f,
                    SpawnIdle = new DurationRangeConfiguration(0.05f, 0.05f),
                    InitialForward = new DurationRangeConfiguration(0.12f, 0.12f),
                    AfterForward = new DurationRangeConfiguration(0.12f, 0.12f),
                    AfterCurve = new DurationRangeConfiguration(0.12f, 0.12f),
                    AfterSCurve = new DurationRangeConfiguration(0.12f, 0.12f),
                    AfterFast = new DurationRangeConfiguration(0.12f, 0.12f)
                },
                Decisions = source.Behavior.Decisions with
                {
                    ObserveChanceBase = 0f,
                    ObserveChanceCuriousFactor = 0f,
                    ObserveChanceWaryFactor = 0f,
                    ObserveChanceMinimum = 0f,
                    ObserveChanceMaximum = 0f
                },
                Boundary = source.Behavior.Boundary with
                {
                    WalkStartMargin = 2f,
                    LookAheadBaseDistance = 8f,
                    LookAheadSpeedFactor = 0f,
                    MinimumEdgeMargin = 2f,
                    EdgeMarginSpeedFactor = 0f
                },
                WalkBoutMinimumDuration = 8f,
                WalkBoutMaximumDuration = 8f,
                TransitionMatrix = new TransitionMatrixConfiguration
                {
                    AfterForward = fallOnly,
                    AfterCurve = fallOnly,
                    AfterSCurve = fallOnly,
                    AfterFast = fallOnly
                }
            }
        };
        return IndividualProfileFactory.Create(configuration, 0x1057, applyVariation: false);
    }

    private static ScreenContext CreateScreenContext(
        LizardProfile profile,
        FloatRect workArea)
    {
        var envelope = LizardGeometryEnvelope.Calculate(
            profile.Appearance,
            profile.Gait,
            profile.SecondaryMotion,
            profile.Rendering);
        var normalRadius =
            (Math.Max(
                 profile.Appearance.CreatureCanvasSize * 0.5f,
                 envelope.NormalModelRadius) +
             profile.Runtime.NavigationMarginModel) *
            profile.Appearance.VisualScale;
        var fullRenderRadius =
            profile.Appearance.RenderCanvasSize * 0.5f * profile.Appearance.VisualScale +
            profile.Runtime.ReleaseRenderMarginPixels;
        var navigationArea = workArea.Inset(normalRadius);
        var lostGripSafeArea = FullRenderSafeArea(profile, navigationArea);
        return new ScreenContext(
            workArea,
            navigationArea,
            fullRenderRadius,
            new LostGripSafetyContext(lostGripSafeArea, IsAvailable: true));
    }

    private static FloatRect FullRenderSafeArea(
        LizardProfile profile,
        FloatRect navigationArea) =>
        new BoundaryNavigator(profile.Behavior.Boundary).SafeInset(
            navigationArea,
            profile.Behavior.LostGripFall.BottomSafetyInset);

    private static PetSimulationFrameInput Input(
        float frameDelta,
        FloatRect area,
        LizardProfile profile,
        PointerObservation pointer = default,
        bool isDragging = false,
        LostGripSafetyContext? lostGripSafety = null) => new(
            frameDelta,
            area,
            pointer,
            isDragging,
            profile.Appearance.VisualScale)
        {
            LostGripSafety = lostGripSafety
        };

    private static bool IsFinite(PetSimulationSession session) =>
        IsFinite(session.BehaviorForDiagnostics, session.LizardForDiagnostics);

    private static Vector2 WorldSpineCenter(
        LizardProfile profile,
        BehaviorController behavior,
        ProceduralLizard lizard)
    {
        var modelCenter = Vector2.Zero;
        foreach (var joint in lizard.Spine.Joints)
        {
            modelCenter += joint;
        }
        modelCenter /= lizard.Spine.Joints.Count;
        var canvasCenter = new Vector2(profile.Appearance.RenderCanvasSize * 0.5f);
        return behavior.Position +
               (modelCenter - canvasCenter) * profile.Appearance.VisualScale;
    }

    private static bool IsFinite(BehaviorController behavior, ProceduralLizard lizard) =>
        float.IsFinite(behavior.Position.X) &&
        float.IsFinite(behavior.Position.Y) &&
        float.IsFinite(behavior.Heading) &&
        float.IsFinite(behavior.Speed) &&
        float.IsFinite(behavior.LostGripFallProgress) &&
        float.IsFinite(behavior.LostGripReachProgress) &&
        float.IsFinite(behavior.LostGripRegripProgress) &&
        float.IsFinite(behavior.LostGripVerticalVelocity) &&
        float.IsFinite(behavior.LostGripDistance) &&
        float.IsFinite(behavior.LostGripTargetDistance) &&
        float.IsFinite(lizard.FreeFallContainmentCorrectionTotal) &&
        float.IsFinite(lizard.FreeFallContainmentCorrectionMaximum) &&
        float.IsFinite(lizard.FreeFallReferenceCorrectionTotal) &&
        float.IsFinite(lizard.FreeFallReferenceCorrectionMaximum) &&
        float.IsFinite(lizard.FreeFallReferenceCenterError) &&
        float.IsFinite(lizard.RegripReachProgress) &&
        Enumerable.Range(0, 4).All(index =>
            IsFinite(lizard.RegripContactTarget(index))) &&
        float.IsFinite(lizard.RegripContactError) &&
        float.IsFinite(lizard.RegripContactSpineDrift) &&
        lizard.Spine.Joints.All(IsFinite) &&
        lizard.Legs.All(leg => IsFinite(leg.Elbow) && IsFinite(leg.Foot));

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static DiagnosticReport BuildReport(LostGripFallTestResult value)
    {
        var fall = LizardConfiguration.Default.Behavior.LostGripFall;
        return new DiagnosticReportBuilder(nameof(LostGripFallSelfTest))
            .AddMetric("completed_scenarios", value.CompletedScenarios)
            .AddMetric("falling_entries", value.FallingEntries)
            .AddMetric("regrip_entries", value.RegripEntries)
            .AddMetric("autonomous_recoveries", value.AutonomousRecoveries)
            .AddMetric("invalid_transitions", value.InvalidTransitions)
            .AddMetric("minimum_fall_distance", value.MinimumFallDistance, "px")
            .AddMetric("maximum_fall_distance", value.MaximumFallDistance, "px")
            .AddMetric("minimum_available_distance", value.MinimumAvailableDistance, "px")
            .AddMetric("maximum_available_distance", value.MaximumAvailableDistance, "px")
            .AddMetric("maximum_fall_velocity", value.MaximumFallVelocity, "px/s")
            .AddMetric("maximum_regrip_pose_jump", value.MaximumRegripPoseJump, "model px")
            .AddMetric("maximum_catch_centroid_error", value.MaximumCatchCentroidError, "screen px")
            .AddMetric("maximum_regrip_centroid_drift", value.MaximumRegripCentroidDrift, "screen px")
            .AddMetric("maximum_constraint_error", value.MaximumConstraintError, "model px")
            .AddMetric("maximum_grab_error", value.MaximumGrabError, "model px")
            .AddMetric("maximum_containment_correction_total", value.MaximumContainmentCorrectionTotal, "model px")
            .AddMetric("maximum_containment_correction", value.MaximumContainmentCorrection, "model px")
            .AddMetric("maximum_reference_correction_total", value.MaximumReferenceCorrectionTotal, "model px")
            .AddMetric("maximum_reference_correction", value.MaximumReferenceCorrection, "model px")
            .AddMetric("maximum_reference_center_error", value.MaximumReferenceCenterError, "model px")
            .AddMetric("reach_seeking_entries", value.ReachSeekingEntries)
            .AddMetric("contact_hold_entries", value.ContactHoldEntries)
            .AddMetric("reach_ordering_violations", value.ReachOrderingViolations)
            .AddMetric("reach_direction_violations", value.ReachDirectionViolations)
            .AddMetric("reach_ik_branch_violations", value.ReachIkBranchViolations)
            .AddMetric("reach_ik_branch_transitions", value.ReachIkBranchTransitions)
            .AddMetric(
                "minimum_ik_branch_transition_straightness",
                value.MinimumIkBranchTransitionStraightness)
            .AddMetric(
                "maximum_ik_branch_transition_elbow_jump",
                value.MaximumIkBranchTransitionElbowJump,
                "model px")
            .AddMetric(
                "maximum_non_branch_limb_pose_jump",
                value.MaximumNonBranchLimbPoseJump,
                "model px")
            .AddMetric(
                "reach_requires_projection_leg_mask",
                value.ReachRequiresProjectionLegMask)
            .AddMetric(
                "reach_projected_limb_samples",
                value.ReachProjectedLimbSamples)
            .AddMetric(
                "reach_rollback_limb_samples",
                value.ReachRollbackLimbSamples)
            .AddMetric(
                "reach_projection_continuity_violations",
                value.ReachProjectionContinuityViolations)
            .AddMetric(
                "maximum_projection_limb_pose_jump",
                value.MaximumProjectionLimbPoseJump,
                "model px")
            .AddMetric("reach_body_clearance_violations", value.ReachBodyClearanceViolations)
            .AddMetric("reach_own_envelope_reentry_violations", value.ReachOwnEnvelopeReentryViolations)
            .AddMetric("contact_stop_violations", value.ContactStopViolations)
            .AddMetric("four_target_scenarios", value.FourTargetScenarios)
            .AddMetric("four_leg_observed_scenarios", value.FourLegObservedScenarios)
            .AddMetric("minimum_nonzero_seeking_steps", value.MinimumNonZeroSeekingSteps)
            .AddMetric("minimum_behavior_reach_peak", value.MinimumBehaviorReachPeak)
            .AddMetric("minimum_reach_target_error_reduction", value.MinimumReachTargetErrorReduction, "model px")
            .AddMetric("minimum_foot_upward_travel", value.MinimumFootUpwardTravel, "model px")
            .AddMetric("minimum_paw_catch_direction_travel", value.MinimumPawCatchDirectionTravel, "model px")
            .AddMetric("minimum_arm_radius_retention", value.MinimumArmRadiusRetention)
            .AddMetric("maximum_first_visible_arm_retraction", value.MaximumFirstVisibleArmRetraction, "screen px")
            .AddMetric("minimum_front_headward_target_travel", value.MinimumFrontHeadwardTargetTravel, "model px")
            .AddMetric("minimum_rear_tailward_target_travel", value.MinimumRearTailwardTargetTravel, "model px")
            .AddMetric("minimum_target_separation", value.MinimumTargetSeparation, "model px")
            .AddMetric("minimum_non_owned_body_clearance", value.MinimumNonOwnedBodyClearance, "model px")
            .AddMetric("maximum_reach_entry_pose_jump", value.MaximumReachEntryPoseJump, "model px")
            .AddMetric("maximum_terminal_reach_error", value.MaximumTerminalReachError, "model px")
            .AddMetric("maximum_contact_error", value.MaximumContactError, "model px")
            .AddMetric("maximum_contact_spine_drift", value.MaximumContactSpineDrift, "model px")
            .AddMetric("maximum_post_contact_window_drift", value.MaximumPostContactWindowDrift, "screen px")
            .AddMetric("maximum_post_contact_visual_centroid_drift", value.MaximumPostContactVisualCentroidDrift, "screen px")
            .AddMetric("minimum_recorded_lost_grip_steps", value.MinimumRecordedLostGripSteps)
            .AddMetric("minimum_moving_visible_paw_steps", value.MinimumMovingVisiblePawSteps)
            .AddMetric("minimum_visible_paw_motion_lead_steps", value.MinimumVisiblePawMotionLeadSteps)
            .AddMetric("minimum_moving_visible_paw_display_frames_60hz", value.MinimumMovingVisiblePawDisplayFrames60Hz)
            .AddMetric("minimum_visible_paw_motion_lead_display_frames_60hz", value.MinimumVisiblePawMotionLeadDisplayFrames60Hz)
            .AddMetric("pre_stop_paw_motion_violations", value.PreStopPawMotionViolations)
            .AddMetric(
                "capture_step_timing_median",
                value.CaptureStepTimingMedianMilliseconds,
                "ms")
            .AddMetric(
                "capture_step_timing_maximum",
                value.CaptureStepTimingMaximumMilliseconds,
                "ms")
            .AddMetric("real_grab_maximum_error", value.RealGrabMaximumError, "model px")
            .AddMetric("maximum_safety_loss_pose_jump", value.MaximumSafetyLossPoseJump, "model px")
            .AddMetric("minimum_safety_loss_boundary_margin", value.MinimumSafetyLossBoundaryMargin, "screen px")
            .AddMetric("safety_loss_fake_contact_samples", value.SafetyLossFakeContactSamples)
            .AddMetric(
                "safety_loss_branch_transitions_after_trigger",
                value.SafetyLossBranchTransitionsAfterTrigger)
            .AddMetric("minimum_canvas_margin", value.MinimumCanvasMargin, "model px")
            .AddMetric("minimum_visual_margin", value.MinimumVisualMargin, "screen px")
            .AddMetric("screen_heights_covered", value.ScreenHeightsCovered)
            .AddMetric("seeds_covered", value.SeedsCovered)
            .AddMetric("start_bands_covered", value.StartBandsCovered)
            .AddMetric("long_fall_screen_heights", value.LongFallScreenHeights)
            .AddMetric("height_scaling_pairs", value.HeightScalingPairs)
            .AddMetric("height_scaling_failures", value.HeightScalingFailures)
            .AddMetric("navigation_boundary_violations", value.NavigationBoundaryViolations)
            .AddMetric("full_render_boundary_violations", value.FullRenderBoundaryViolations)
            .AddMetric("visual_boundary_violations", value.VisualBoundaryViolations)
            .AddMetric(
                "target_distance_boundary_violations",
                value.TargetDistanceBoundaryViolations)
            .AddMetric("monotonicity_violations", value.MonotonicityViolations)
            .AddMetric("non_finite_samples", value.NonFiniteSamples)
            .AddCheck(
                "explicit phase coverage",
                value.CompletedScenarios == ExpectedScenarioCount &&
                value.FallingEntries == ExpectedScenarioCount &&
                value.RegripEntries == ExpectedScenarioCount,
                $"{ExpectedScenarioCount} complete Falling -> Regripping scenarios",
                $"{value.CompletedScenarios}/{value.FallingEntries}/{value.RegripEntries}")
            .AddCheck(
                "returns to autonomous state",
                value.AutonomousRecoveries == ExpectedScenarioCount,
                ExpectedScenarioCount.ToString(),
                value.AutonomousRecoveries.ToString())
            .AddCheck("legal transitions", value.InvalidTransitions == 0, "0", value.InvalidTransitions.ToString())
            .AddCheck(
                "screen-bounded fall distance",
                value.MinimumFallDistance >= fall.MinimumDistance - 0.01f &&
                value.TargetDistanceBoundaryViolations == 0,
                $">= {fall.MinimumDistance:F0} px and <= each entry's safe-bottom distance",
                $"{value.MinimumFallDistance:F2}-{value.MaximumFallDistance:F2}")
            .AddCheck(
                "multi-height, multi-start, multi-seed coverage",
                value.ScreenHeightsCovered == ScreenHeightCount &&
                value.StartBandsCovered == StartBandCount &&
                value.SeedsCovered == ExpectedScenarioCount,
                $"{ScreenHeightCount} heights, {StartBandCount} starts, " +
                $"{ExpectedScenarioCount} unique seeds",
                $"{value.ScreenHeightsCovered}/{value.StartBandsCovered}/{value.SeedsCovered}")
            .AddCheck(
                "dynamic screen-height upper endpoint",
                value.LongFallScreenHeights >= 2 &&
                value.MaximumFallDistance > LegacyFixedMaximumDistance + 1f &&
                value.HeightScalingPairs == HeightScalingPairCount &&
                value.HeightScalingFailures == 0,
                $"> {LegacyFixedMaximumDistance:F0}px on >= 2 heights and " +
                $"{HeightScalingPairCount} same-seed tall-screen increases",
                $"max {value.MaximumFallDistance:F2}, heights {value.LongFallScreenHeights}, " +
                $"pairs {value.HeightScalingPairs - value.HeightScalingFailures}/" +
                value.HeightScalingPairs)
            .AddCheck("truncated near-bottom fall", value.TruncatedFallPassed, "safe shortened target", value.TruncatedFallPassed.ToString())
            .AddCheck("near-bottom cancellation", value.NearBottomCancellationPassed, "EdgeTurn without fake fall", value.NearBottomCancellationPassed.ToString())
            .AddCheck("left/right/top edge cancellation", value.SideEdgeCancellationPassed, "3 EdgeTurn cancellations without fake fall", value.SideEdgeCancellationPassed.ToString())
            .AddCheck("undersized-area cancellation", value.TinyAreaCancellationPassed, "width/height must both support the full safety inset", value.TinyAreaCancellationPassed.ToString())
            .AddCheck(
                "fall speed cap",
                value.MaximumFallVelocity <= fall.MaximumFallVelocity + 0.01f,
                $"<= {fall.MaximumFallVelocity:F0} px/s",
                $"{value.MaximumFallVelocity:F2}")
            .AddCheck(
                "regrip pose continuity",
                value.MaximumRegripPoseJump <= 2f,
                "<= 2 model px",
                $"{value.MaximumRegripPoseJump:F3}")
            .AddCheck(
                "world-centroid fall and regrip continuity",
                value.MaximumCatchCentroidError <= 1f &&
                value.MaximumRegripCentroidDrift <= 12f,
                "catch error <= 1 screen px; regrip drift <= 12 screen px",
                $"{value.MaximumCatchCentroidError:F3}/" +
                $"{value.MaximumRegripCentroidDrift:F3}")
            .AddCheck(
                "pre-contact reach coverage and ordering",
                value.ReachSeekingEntries == ExpectedScenarioCount &&
                value.ContactHoldEntries == ExpectedScenarioCount &&
                value.ReachOrderingViolations == 0 &&
                value.MinimumNonZeroSeekingSteps >= 2 &&
                value.MinimumBehaviorReachPeak > 0f,
                $"{ExpectedScenarioCount} Seeking/ContactHold; >= 2 nonzero steps; 0 ordering violations",
                $"{value.ReachSeekingEntries}/{value.ContactHoldEntries}; " +
                $"steps {value.MinimumNonZeroSeekingSteps}; " +
                $"peak {value.MinimumBehaviorReachPeak:F3}; " +
                $"violations {value.ReachOrderingViolations}")
            .AddCheck(
                "four limbs reach upward toward distinct body-frame targets",
                value.ReachDirectionViolations == 0 &&
                value.FourTargetScenarios == ExpectedScenarioCount &&
                value.FourLegObservedScenarios == ExpectedScenarioCount &&
                value.MinimumReachTargetErrorReduction > 1f &&
                value.MinimumFootUpwardTravel > 1f &&
                value.MinimumPawCatchDirectionTravel > 1f &&
                value.MinimumArmRadiusRetention >= 0.74f &&
                value.MaximumFirstVisibleArmRetraction <= 0.5f,
                "all four paws move upward toward independent targets without collapsing through a shoulder or visibly retracting first",
                $"reduction {value.MinimumReachTargetErrorReduction:F2}; " +
                $"up {value.MinimumFootUpwardTravel:F2}; " +
                $"toward {value.MinimumPawCatchDirectionTravel:F2}; " +
                $"radius retention {value.MinimumArmRadiusRetention:P0}; " +
                $"targets/legs {value.FourTargetScenarios}/" +
                $"{value.FourLegObservedScenarios}; " +
                $"first retraction {value.MaximumFirstVisibleArmRetraction:F2}px; " +
                $"violations {value.ReachDirectionViolations}")
            .AddCheck(
                "visible four-paw reach begins while the host is falling",
                value.PreStopPawMotionViolations == 0 &&
                value.MinimumRecordedLostGripSteps > 0 &&
                value.MinimumMovingVisiblePawSteps >= 2 &&
                value.MinimumVisiblePawMotionLeadSteps >= 1 &&
                value.MinimumMovingVisiblePawDisplayFrames60Hz >= 6 &&
                value.MinimumVisiblePawMotionLeadDisplayFrames60Hz >= 6,
                "all four rendered paws visibly advance while falling for >= 6 committed 60 Hz frames (>= 100 ms), across both cadence phase offsets",
                $"moving steps {value.MinimumMovingVisiblePawSteps}; " +
                $"lead {value.MinimumVisiblePawMotionLeadSteps}; " +
                $"60Hz moving/lead " +
                $"{value.MinimumMovingVisiblePawDisplayFrames60Hz}/" +
                $"{value.MinimumVisiblePawMotionLeadDisplayFrames60Hz}; " +
                $"recorded {value.MinimumRecordedLostGripSteps}; " +
                $"violations {value.PreStopPawMotionViolations}; " +
                $"worst {value.WorstReachTimingDetail}")
            .AddCheck(
                "four-paw target spread and limb/body clearance",
                value.ReachIkBranchViolations == 0 &&
                value.ReachIkBranchTransitions > 0 &&
                value.MinimumIkBranchTransitionStraightness >=
                    LostGripRegripAnimationProbe.MinimumBranchTransitionStraightness &&
                value.MaximumIkBranchTransitionElbowJump <=
                    LostGripRegripAnimationProbe.MaximumBranchTransitionElbowJump &&
                value.IkBranchTransitionPolicyPassed &&
                value.ReachBodyClearanceViolations == 0 &&
                value.ReachOwnEnvelopeReentryViolations == 0 &&
                value.MinimumFrontHeadwardTargetTravel > 0.5f &&
                value.MinimumRearTailwardTargetTravel > 0.5f &&
                value.MinimumTargetSeparation > 1f &&
                value.MinimumNonOwnedBodyClearance >= 0f,
                "front targets escape headward, rear targets tailward; each IK branch may switch once only at near-full extension without an elbow jump; both limb links avoid distant body capsules and do not re-enter the owned silhouette",
                $"head/tail {value.MinimumFrontHeadwardTargetTravel:F2}/" +
                $"{value.MinimumRearTailwardTargetTravel:F2}; " +
                $"target gap {value.MinimumTargetSeparation:F2}; " +
                $"clearance {value.MinimumNonOwnedBodyClearance:F2}; " +
                $"branch switches/violations " +
                $"{value.ReachIkBranchTransitions}/" +
                $"{value.ReachIkBranchViolations}, " +
                $"straight/jump " +
                $"{value.MinimumIkBranchTransitionStraightness:F5}/" +
                $"{value.MaximumIkBranchTransitionElbowJump:F4}; " +
                $"policy {value.IkBranchTransitionPolicyPassed}; " +
                $"body/reentry " +
                $"{value.ReachBodyClearanceViolations}/" +
                $"{value.ReachOwnEnvelopeReentryViolations}; " +
                $"per-leg {value.PerLegReachGeometryDetail}; " +
                $"worst {value.WorstReachBodyClearanceDetail}; " +
                $"worst switch {value.WorstIkBranchTransitionDetail}; " +
                $"first reentry {value.FirstReachOwnEnvelopeReentryDetail}; " +
                $"per-leg first {value.PerLegFirstReachOwnEnvelopeReentryDetail}")
            .AddCheck(
                "bounded runtime safety projection",
                value.ReachProjectionContinuityViolations == 0 &&
                value.MaximumProjectionLimbPoseJump <=
                    LostGripRegripAnimationProbe.MaximumProjectionLimbPoseJump &&
                value.ReachRollbackLimbSamples == 0,
                "any projected limb changes by <= 4 model px per fixed step; nominal scenarios never roll back and hold a paw",
                $"requires-mask=" +
                $"{Convert.ToString(value.ReachRequiresProjectionLegMask, 2)}," +
                $"projected/rollback=" +
                $"{value.ReachProjectedLimbSamples}/" +
                $"{value.ReachRollbackLimbSamples}," +
                $"max-jump={value.MaximumProjectionLimbPoseJump:F4}," +
                $"violations={value.ReachProjectionContinuityViolations}," +
                $"worst={value.WorstProjectionLimbPoseJumpDetail}")
            .AddCheck(
                "contact occurs before the stationary hold",
                value.ContactStopViolations == 0 &&
                value.MaximumTerminalReachError <= 0.25f &&
                value.MaximumContactError <= 0.25f,
                "final moving Seeking frame contacts all four paws with mask 0b1111; next stationary frame holds them",
                $"violations {value.ContactStopViolations}; " +
                $"errors {value.MaximumTerminalReachError:F3}/{value.MaximumContactError:F3}")
            .AddCheck(
                "reach and contact pose continuity",
                value.MaximumReachEntryPoseJump <= 3f &&
                value.MaximumContactSpineDrift <= 0.01f,
                "Seeking entry jump <= 3 model px; contact spine drift <= 0.01 model px; non-branch limb jump remains a diagnostic because ordinary full extension can exceed 3 px",
                $"entry/nonbranch/contact " +
                $"{value.MaximumReachEntryPoseJump:F3}/" +
                $"{value.MaximumNonBranchLimbPoseJump:F3}/" +
                $"{value.MaximumContactSpineDrift:F4}; " +
                $"worst {value.WorstNonBranchLimbPoseJumpDetail}")
            .AddCheck(
                "post-contact host and visual stability",
                value.MaximumPostContactWindowDrift <= 0.001f &&
                value.MaximumPostContactVisualCentroidDrift <= 13f,
                "window <= 0.001 screen px; visual AABB centroid <= 13 screen px",
                $"{value.MaximumPostContactWindowDrift:F4}/" +
                $"{value.MaximumPostContactVisualCentroidDrift:F3}")
            .AddCheck(
                "free-fall reference-frame safety",
                value.MaximumContainmentCorrectionTotal <= 0.01f &&
                value.MaximumContainmentCorrection <= 0.01f &&
                value.MaximumReferenceCenterError <= 0.01f,
                "containment total/max and reference-center error <= 0.01 model px",
                $"{value.MaximumContainmentCorrectionTotal:F4}/" +
                $"{value.MaximumContainmentCorrection:F4}/" +
                $"{value.MaximumReferenceCenterError:F4}")
            .AddCheck(
                "particle constraints",
                value.MaximumConstraintError <= 1.5f && value.MaximumGrabError <= 0.001f,
                "constraint <= 1.5; free-fall grab error = 0",
                $"{value.MaximumConstraintError:F3}/{value.MaximumGrabError:F3}")
            .AddCheck(
                "transparent canvas safety",
                value.MinimumCanvasMargin >= 0f,
                ">= 0 model px",
                $"{value.MinimumCanvasMargin:F2}")
            .AddCheck(
                "navigation, full-HWND and visual-AABB safety",
                value.NavigationBoundaryViolations == 0 &&
                value.FullRenderBoundaryViolations == 0 &&
                value.VisualBoundaryViolations == 0 &&
                value.MinimumVisualMargin >= -0.001f,
                "0/0/0 violations",
                $"{value.NavigationBoundaryViolations}/" +
                $"{value.FullRenderBoundaryViolations}/" +
                $"{value.VisualBoundaryViolations}; margin {value.MinimumVisualMargin:F2}px")
            .AddCheck("monotonic downward motion", value.MonotonicityViolations == 0, "0 violations", value.MonotonicityViolations.ToString())
            .AddCheck("pointer cannot steal fall", value.PointerPriorityPassed, "true", value.PointerPriorityPassed.ToString())
            .AddCheck("real grab pre-empts fall", value.GrabPriorityPassed, "true", value.GrabPriorityPassed.ToString())
            .AddCheck(
                "catch input is isolated from non-free-fall poses",
                value.NonFreeFallInputIsolationPassed,
                "true",
                value.NonFreeFallInputIsolationPassed.ToString())
            .AddCheck(
                "real mouse grab remains isolated and constrained",
                value.RealGrabAnimationIsolationPassed &&
                value.RealGrabMaximumError <= LizardConfiguration.Default.Physics.MaximumGrabError + 0.001f,
                "auto-regrip phase None; grab error within configured limit",
                $"{value.RealGrabAnimationIsolationPassed}; {value.RealGrabMaximumError:F3}")
            .AddCheck(
                "collapsed lead preserves the first four-paw pose",
                value.CollapsedLeadFourPawContinuityPassed,
                "all four feet/targets and bend branches remain continuous and contact mask stays zero",
                value.CollapsedLeadFourPawContinuityPassed.ToString())
            .AddCheck(
                "minimal lead branch transition is stateful and contact-safe",
                value.MinimalLeadBranchTransitionPassed,
                "three updates capture, switch on the full-extension plateau, then reach/contact; a two-update endpoint jump must not fabricate contact",
                value.MinimalLeadBranchTransitionDetail)
            .AddCheck(
                "unsafe four-limb reach cannot authorize contact",
                value.ContactSafeNegativeContractPassed,
                "an intentionally impossible body-clearance profile keeps contact-safe false and mask zero through an allowed-contact Regrip input",
                value.ContactSafeNegativeContractDetail)
            .AddCheck(
                "four-paw reach geometry survives rotated body headings",
                value.RotatedHeadingFourPawGeometryPassed,
                "0/+90/-90/180 degree poses retain screen-up targets, branch/radius continuity, six 60 Hz moving frames, distant-body clearance, no owned-envelope reentry, and atomic 0b1111 contact",
                value.RotatedHeadingFourPawGeometryDetail)
            .AddCheck(
                "lost-grip four-paw pose is DPI and refresh deterministic",
                value.DpiRefreshDeterminismPassed,
                "equivalent 1x/1.25x/1.5x/2x worlds and 60/120/240 Hz display callbacks commit the same common-time four-paw poses",
                value.DpiRefreshDeterminismPassed.ToString())
            .AddCheck(
                "mid-seeking safety loss degrades without a fake catch",
                value.MidSeekingSafetyLossFallbackPassed &&
                value.MaximumSafetyLossPoseJump <= 3f &&
                value.MinimumSafetyLossBoundaryMargin >= -0.001f &&
                value.SafetyLossFakeContactSamples == 0 &&
                value.SafetyLossBranchTransitionsAfterTrigger == 0,
                "SafetyForced adds no IK branch switch or fake contact on/after its trigger, then uses continuous non-contact recovery",
                $"passed {value.MidSeekingSafetyLossFallbackPassed}; " +
                $"local-pose jump {value.MaximumSafetyLossPoseJump:F3}; " +
                $"margin {value.MinimumSafetyLossBoundaryMargin:F2}px; " +
                $"fake contacts {value.SafetyLossFakeContactSamples}; " +
                $"post-trigger switches " +
                $"{value.SafetyLossBranchTransitionsAfterTrigger}; " +
                value.MidSeekingSafetyLossFallbackDetail)
            .AddCheck("pause pre-empts fall", value.PausePriorityPassed, "true", value.PausePriorityPassed.ToString())
            .AddCheck("finite behavior and pose", value.NonFiniteSamples == 0, "0", value.NonFiniteSamples.ToString())
            .AddCheck("Chinese debug labels", value.DebugChineseLabelsPassed, "true", value.DebugChineseLabelsPassed.ToString())
            .Build();
    }

    private sealed class DpiCadenceScenario
    {
        private const double DeviceLeft = -320d;
        private const double DeviceTop = 180d;
        private const double LogicalWidth = 1280d;
        private const double LogicalHeight = 800d;
        private readonly int _displayRate;
        private readonly SafetyArea _fullRenderSafety;

        public LizardGameModule Module { get; }
        public DesktopPetRuntime<LizardRenderFrame> Runtime { get; }
        public WorldRect NavigationArea { get; }

        public DpiCadenceScenario(
            LizardProfile profile,
            double scale,
            int displayRate)
        {
            _displayRate = displayRate;
            var topology = new DisplayTopology(
            [
                new DisplayDescriptor(
                    $"lost-grip-{scale:0.##}x-{displayRate}hz",
                    DeviceRectFromLogical(
                        0d,
                        0d,
                        LogicalWidth,
                        LogicalHeight,
                        scale),
                    DeviceRectFromLogical(
                        48d,
                        32d,
                        1232d,
                        744d,
                        scale),
                    scale,
                    true)
            ]);
            NavigationArea = ToWorldRect(topology.Primary.WorldWorkingArea);
            _fullRenderSafety = new SafetyArea(NavigationArea, true);
            Module = new LizardGameModule(profile, 0x6D41);
            Runtime = new DesktopPetRuntime<LizardRenderFrame>(Module);
            var deviceSpawn = new DevicePoint(
                DeviceLeft + 640d * scale,
                DeviceTop + 96d * scale);
            var worldSpawn = topology.DeviceToWorld(deviceSpawn);
            Runtime.Reset(new Vector2((float)worldSpawn.X, (float)worldSpawn.Y));
        }

        public void AdvanceForCommonTicks(int ticks)
        {
            var callsPerTick = _displayRate / 60;
            for (var call = 0; call < ticks * callsPerTick; call++)
            {
                Runtime.Advance(new DesktopPetInput(
                    1f / _displayRate,
                    NavigationArea,
                    _fullRenderSafety,
                    new PointerSample(Vector2.Zero, false),
                    false));
            }
        }

        private static DeviceRect DeviceRectFromLogical(
            double left,
            double top,
            double right,
            double bottom,
            double scale) => new(
                DeviceLeft + left * scale,
                DeviceTop + top * scale,
                DeviceLeft + right * scale,
                DeviceTop + bottom * scale);

        private static WorldRect ToWorldRect(WorldRectD rect) => new(
            (float)rect.Left,
            (float)rect.Top,
            (float)rect.Right,
            (float)rect.Bottom);
    }

    private readonly record struct ScreenContext(
        FloatRect WorkArea,
        FloatRect NavigationArea,
        float FullRenderRadius,
        LostGripSafetyContext LostGripSafety);

    private readonly record struct SafetyLossFallbackResult(
        bool Passed,
        float MaximumPoseJump,
        float MinimumBoundaryMargin,
        int FakeContactSamples,
        int BranchTransitionsAfterTrigger,
        string Detail)
    {
        public static SafetyLossFallbackResult Failed => new(
            false,
            float.PositiveInfinity,
            float.NegativeInfinity,
            0,
            0,
            "setup-failed");
    }


    private sealed class Metrics
    {
        public int CompletedScenarios;
        public int FallingEntries;
        public int RegripEntries;
        public int AutonomousRecoveries;
        public int InvalidTransitions;
        public float MinimumFallDistance = float.PositiveInfinity;
        public float MaximumFallDistance;
        public float MaximumFallVelocity;
        public float MaximumRegripPoseJump;
        public float MaximumCatchCentroidError;
        public float MaximumRegripCentroidDrift;
        public float MaximumConstraintError;
        public float MaximumGrabError;
        public float MaximumContainmentCorrectionTotal;
        public float MaximumContainmentCorrection;
        public float MaximumReferenceCorrectionTotal;
        public float MaximumReferenceCorrection;
        public float MaximumReferenceCenterError;
        public int ReachSeekingEntries;
        public int ContactHoldEntries;
        public int ReachOrderingViolations;
        public int ReachDirectionViolations;
        public int ReachIkBranchViolations;
        public int ReachIkBranchTransitions;
        public float MinimumIkBranchTransitionStraightness =
            float.PositiveInfinity;
        public float MaximumIkBranchTransitionElbowJump;
        public string WorstIkBranchTransitionDetail = "none";
        public float MaximumNonBranchLimbPoseJump;
        public string WorstNonBranchLimbPoseJumpDetail = "none";
        public int ReachRequiresProjectionLegMask;
        public int ReachProjectedLimbSamples;
        public int ReachRollbackLimbSamples;
        public int ReachProjectionContinuityViolations;
        public float MaximumProjectionLimbPoseJump;
        public string WorstProjectionLimbPoseJumpDetail = "none";
        public int ReachBodyClearanceViolations;
        public int ReachOwnEnvelopeReentryViolations;
        public string WorstReachBodyClearanceDetail = "none";
        public string FirstReachOwnEnvelopeReentryDetail = "none";
        public string[] FirstReachOwnEnvelopeReentryDetailByLeg { get; } =
            Enumerable.Repeat("none", 4).ToArray();
        public string PerLegFirstReachOwnEnvelopeReentryDetail => string.Join(
            " | ",
            Enumerable.Range(0, 4).Select(index =>
                $"L{index}:{FirstReachOwnEnvelopeReentryDetailByLeg[index]}"));
        public string WorstReachTimingDetail = "none";
        public float[] MinimumRemoteClearanceByLeg { get; } =
            Enumerable.Repeat(float.PositiveInfinity, 4).ToArray();
        public int[] OwnEnvelopeReentriesByLeg { get; } = new int[4];
        public string PerLegReachGeometryDetail => string.Join(
            "/",
            Enumerable.Range(0, 4).Select(index =>
                $"L{index}:{MinimumRemoteClearanceByLeg[index]:F2}," +
                $"R{OwnEnvelopeReentriesByLeg[index]}"));
        public int ContactStopViolations;
        public int FourTargetScenarios;
        public int FourLegObservedScenarios;
        public int MinimumNonZeroSeekingSteps = int.MaxValue;
        public float MinimumBehaviorReachPeak = float.PositiveInfinity;
        public float MinimumReachTargetErrorReduction = float.PositiveInfinity;
        public float MinimumFootUpwardTravel = float.PositiveInfinity;
        public float MinimumPawCatchDirectionTravel = float.PositiveInfinity;
        public float MinimumArmRadiusRetention = float.PositiveInfinity;
        public float MaximumFirstVisibleArmRetraction;
        public float MinimumFrontHeadwardTargetTravel = float.PositiveInfinity;
        public float MinimumRearTailwardTargetTravel = float.PositiveInfinity;
        public float MinimumTargetSeparation = float.PositiveInfinity;
        public float MinimumNonOwnedBodyClearance = float.PositiveInfinity;
        public float MaximumReachEntryPoseJump;
        public float MaximumTerminalReachError;
        public float MaximumContactError;
        public float MaximumContactSpineDrift;
        public float MaximumPostContactWindowDrift;
        public float MaximumPostContactVisualCentroidDrift;
        public int MinimumRecordedLostGripSteps = int.MaxValue;
        public int MinimumMovingVisiblePawSteps = int.MaxValue;
        public int MinimumVisiblePawMotionLeadSteps = int.MaxValue;
        public int MinimumMovingVisiblePawDisplayFrames60Hz = int.MaxValue;
        public int MinimumVisiblePawMotionLeadDisplayFrames60Hz = int.MaxValue;
        public int PreStopPawMotionViolations;
        public float MinimumCanvasMargin = float.PositiveInfinity;
        public float MinimumVisualMargin = float.PositiveInfinity;
        public float MinimumAvailableDistance = float.PositiveInfinity;
        public float MaximumAvailableDistance;
        public int NavigationBoundaryViolations;
        public int FullRenderBoundaryViolations;
        public int VisualBoundaryViolations;
        public int TargetDistanceBoundaryViolations;
        public int MonotonicityViolations;
        public int NonFiniteSamples;
        public int PointerPriorityViolations;
        public int HeightScalingPairs;
        public int HeightScalingFailures;
        public bool TruncatedScenarioCompleted;
        public float TruncatedTargetDistance;
        public HashSet<int> ScreenHeights { get; } = [];
        public HashSet<int> Seeds { get; } = [];
        public HashSet<int> StartBands { get; } = [];
        public HashSet<int> LongFallScreenHeights { get; } = [];

        public void RecordReach(LostGripRegripProbeResult result)
        {
            ReachSeekingEntries += result.SeekingSeen ? 1 : 0;
            ContactHoldEntries += result.ContactHoldSeen ? 1 : 0;
            ReachOrderingViolations += result.OrderingViolations;
            ReachDirectionViolations += result.DirectionViolations;
            ReachIkBranchViolations += result.IkBranchViolations;
            ReachIkBranchTransitions += result.IkBranchTransitions;
            if (result.IkBranchTransitions > 0)
            {
                if (result.MinimumIkBranchTransitionStraightness <
                    MinimumIkBranchTransitionStraightness ||
                    result.MaximumIkBranchTransitionElbowJump >
                    MaximumIkBranchTransitionElbowJump)
                {
                    WorstIkBranchTransitionDetail =
                        result.WorstIkBranchTransitionDetail;
                }
                MinimumIkBranchTransitionStraightness = Math.Min(
                    MinimumIkBranchTransitionStraightness,
                    result.MinimumIkBranchTransitionStraightness);
                MaximumIkBranchTransitionElbowJump = Math.Max(
                    MaximumIkBranchTransitionElbowJump,
                    result.MaximumIkBranchTransitionElbowJump);
            }
            if (result.MaximumNonBranchLimbPoseJump >
                MaximumNonBranchLimbPoseJump)
            {
                MaximumNonBranchLimbPoseJump =
                    result.MaximumNonBranchLimbPoseJump;
                WorstNonBranchLimbPoseJumpDetail =
                    result.WorstNonBranchLimbPoseJumpDetail;
            }
            ReachRequiresProjectionLegMask |= result.RequiresProjectionLegMask;
            ReachProjectedLimbSamples += result.ProjectedLimbSamples;
            ReachRollbackLimbSamples += result.RollbackLimbSamples;
            ReachProjectionContinuityViolations +=
                result.ProjectionContinuityViolations;
            if (result.MaximumProjectionLimbPoseJump >
                MaximumProjectionLimbPoseJump)
            {
                MaximumProjectionLimbPoseJump =
                    result.MaximumProjectionLimbPoseJump;
                WorstProjectionLimbPoseJumpDetail =
                    result.WorstProjectionLimbPoseJumpDetail;
            }
            ReachBodyClearanceViolations += result.BodyClearanceViolations;
            ReachOwnEnvelopeReentryViolations +=
                result.OwnEnvelopeReentryViolations;
            if (result.MinimumNonOwnedBodyClearance <
                MinimumNonOwnedBodyClearance)
            {
                WorstReachBodyClearanceDetail =
                    result.WorstBodyClearanceDetail +
                    "; selected-path " + result.MotionTimingDetail;
            }
            if (FirstReachOwnEnvelopeReentryDetail == "none" &&
                result.OwnEnvelopeReentryViolations > 0)
            {
                FirstReachOwnEnvelopeReentryDetail =
                    result.FirstOwnEnvelopeReentryDetail;
            }
            for (var index = 0; index < 4; index++)
            {
                MinimumRemoteClearanceByLeg[index] = Math.Min(
                    MinimumRemoteClearanceByLeg[index],
                    result.MinimumNonOwnedBodyClearanceByLeg[index]);
                OwnEnvelopeReentriesByLeg[index] +=
                    result.OwnEnvelopeReentriesByLeg[index];
                if (FirstReachOwnEnvelopeReentryDetailByLeg[index] == "none" &&
                    result.FirstOwnEnvelopeReentryDetailByLeg[index] != "none")
                {
                    FirstReachOwnEnvelopeReentryDetailByLeg[index] =
                        result.FirstOwnEnvelopeReentryDetailByLeg[index];
                }
            }
            ContactStopViolations += result.ContactStopViolations;
            FourTargetScenarios += result.DistinctTargetsSeen ? 1 : 0;
            FourLegObservedScenarios += result.ObservedLegMask == 0b1111 ? 1 : 0;
            if (result.SeekingSeen)
            {
                MinimumNonZeroSeekingSteps = Math.Min(
                    MinimumNonZeroSeekingSteps,
                    result.NonZeroSeekingSteps);
                MinimumBehaviorReachPeak = Math.Min(
                    MinimumBehaviorReachPeak,
                    result.BehaviorReachPeak);
                MinimumReachTargetErrorReduction = Math.Min(
                    MinimumReachTargetErrorReduction,
                    result.TargetErrorReduction);
                MinimumFootUpwardTravel = Math.Min(
                    MinimumFootUpwardTravel,
                    result.MinimumFootUpwardTravel);
                MinimumPawCatchDirectionTravel = Math.Min(
                    MinimumPawCatchDirectionTravel,
                    result.MinimumPawCatchDirectionTravel);
                MinimumArmRadiusRetention = Math.Min(
                    MinimumArmRadiusRetention,
                    result.MinimumArmRadiusRetention);
                MaximumFirstVisibleArmRetraction = Math.Max(
                    MaximumFirstVisibleArmRetraction,
                    result.FirstVisibleArmRetraction);
                MinimumFrontHeadwardTargetTravel = Math.Min(
                    MinimumFrontHeadwardTargetTravel,
                    result.MinimumFrontHeadwardTargetTravel);
                MinimumRearTailwardTargetTravel = Math.Min(
                    MinimumRearTailwardTargetTravel,
                    result.MinimumRearTailwardTargetTravel);
                MinimumTargetSeparation = Math.Min(
                    MinimumTargetSeparation,
                    result.MinimumTargetSeparation);
                MinimumNonOwnedBodyClearance = Math.Min(
                    MinimumNonOwnedBodyClearance,
                    result.MinimumNonOwnedBodyClearance);
            }
            if (!result.TerminalReachSeen)
            {
                ReachOrderingViolations++;
            }
            MaximumReachEntryPoseJump = Math.Max(
                MaximumReachEntryPoseJump,
                result.ReachEntryPoseJump);
            MaximumTerminalReachError = Math.Max(
                MaximumTerminalReachError,
                result.TerminalReachError);
            MaximumContactError = Math.Max(
                MaximumContactError,
                result.ContactError);
            MaximumContactSpineDrift = Math.Max(
                MaximumContactSpineDrift,
                result.ContactSpineDrift);
            MaximumPostContactWindowDrift = Math.Max(
                MaximumPostContactWindowDrift,
                result.PostContactWindowDrift);
            MaximumPostContactVisualCentroidDrift = Math.Max(
                MaximumPostContactVisualCentroidDrift,
                result.PostContactVisualCentroidDrift);
            MinimumRecordedLostGripSteps = Math.Min(
                MinimumRecordedLostGripSteps,
                result.RecordedLostGripSteps);
            if (result.MovingVisiblePawDisplayFrames60Hz <
                    MinimumMovingVisiblePawDisplayFrames60Hz ||
                (result.MovingVisiblePawDisplayFrames60Hz ==
                     MinimumMovingVisiblePawDisplayFrames60Hz &&
                 result.VisiblePawMotionLeadDisplayFrames60Hz <
                     MinimumVisiblePawMotionLeadDisplayFrames60Hz) ||
                (WorstReachTimingDetail == "none" &&
                 result.PreStopPawMotionViolations > 0))
            {
                WorstReachTimingDetail = result.MotionTimingDetail;
            }
            MinimumMovingVisiblePawSteps = Math.Min(
                MinimumMovingVisiblePawSteps,
                result.MovingVisiblePawSteps);
            MinimumVisiblePawMotionLeadSteps = Math.Min(
                MinimumVisiblePawMotionLeadSteps,
                result.VisiblePawMotionLeadSteps);
            MinimumMovingVisiblePawDisplayFrames60Hz = Math.Min(
                MinimumMovingVisiblePawDisplayFrames60Hz,
                result.MovingVisiblePawDisplayFrames60Hz);
            MinimumVisiblePawMotionLeadDisplayFrames60Hz = Math.Min(
                MinimumVisiblePawMotionLeadDisplayFrames60Hz,
                result.VisiblePawMotionLeadDisplayFrames60Hz);
            PreStopPawMotionViolations += result.PreStopPawMotionViolations;
        }

        public void NormalizeReachMinimums()
        {
            if (MinimumNonZeroSeekingSteps == int.MaxValue)
            {
                MinimumNonZeroSeekingSteps = 0;
            }
            if (!float.IsFinite(MinimumIkBranchTransitionStraightness))
            {
                MinimumIkBranchTransitionStraightness = 0f;
            }
            if (!float.IsFinite(MinimumBehaviorReachPeak))
            {
                MinimumBehaviorReachPeak = 0f;
            }
            if (!float.IsFinite(MinimumReachTargetErrorReduction))
            {
                MinimumReachTargetErrorReduction = 0f;
            }
            if (!float.IsFinite(MinimumFootUpwardTravel))
            {
                MinimumFootUpwardTravel = 0f;
            }
            if (!float.IsFinite(MinimumPawCatchDirectionTravel))
            {
                MinimumPawCatchDirectionTravel = 0f;
            }
            if (!float.IsFinite(MinimumArmRadiusRetention))
            {
                MinimumArmRadiusRetention = 0f;
            }
            if (!float.IsFinite(MinimumFrontHeadwardTargetTravel))
            {
                MinimumFrontHeadwardTargetTravel = 0f;
            }
            if (!float.IsFinite(MinimumRearTailwardTargetTravel))
            {
                MinimumRearTailwardTargetTravel = 0f;
            }
            if (!float.IsFinite(MinimumTargetSeparation))
            {
                MinimumTargetSeparation = 0f;
            }
            if (!float.IsFinite(MinimumNonOwnedBodyClearance))
            {
                MinimumNonOwnedBodyClearance = 0f;
            }
            if (MinimumRecordedLostGripSteps == int.MaxValue)
            {
                MinimumRecordedLostGripSteps = 0;
            }
            if (MinimumMovingVisiblePawSteps == int.MaxValue)
            {
                MinimumMovingVisiblePawSteps = 0;
            }
            if (MinimumVisiblePawMotionLeadSteps == int.MaxValue)
            {
                MinimumVisiblePawMotionLeadSteps = 0;
            }
            if (MinimumMovingVisiblePawDisplayFrames60Hz == int.MaxValue)
            {
                MinimumMovingVisiblePawDisplayFrames60Hz = 0;
            }
            if (MinimumVisiblePawMotionLeadDisplayFrames60Hz == int.MaxValue)
            {
                MinimumVisiblePawMotionLeadDisplayFrames60Hz = 0;
            }
        }
    }

    private sealed record PoseSnapshot(
        Vector2[] Spine,
        Vector2[] Elbows,
        Vector2[] Feet)
    {
        public static PoseSnapshot Capture(ProceduralLizard lizard) => new(
            lizard.Spine.Joints.ToArray(),
            lizard.Legs.Select(leg => leg.Elbow).ToArray(),
            lizard.Legs.Select(leg => leg.Foot).ToArray());

        public float MaximumDistance(PoseSnapshot other)
        {
            var maximum = 0f;
            for (var index = 0; index < Spine.Length; index++)
            {
                maximum = Math.Max(maximum, Vector2.Distance(Spine[index], other.Spine[index]));
            }
            for (var index = 0; index < Elbows.Length; index++)
            {
                maximum = Math.Max(maximum, Vector2.Distance(Elbows[index], other.Elbows[index]));
                maximum = Math.Max(maximum, Vector2.Distance(Feet[index], other.Feet[index]));
            }
            return maximum;
        }
    }

    private sealed record BranchPoseSnapshot(
        Vector2[] Elbows,
        float[] BendSigns,
        float[] Straightness,
        float[] ArmLengths)
    {
        public static BranchPoseSnapshot Capture(
            ProceduralLizard lizard,
            float[]? capturedArmLengths = null)
        {
            var elbows = new Vector2[4];
            var bendSigns = new float[4];
            var straightness = new float[4];
            var armLengths = capturedArmLengths?.ToArray() ?? new float[4];
            for (var index = 0; index < 4; index++)
            {
                var leg = lizard.Legs[index];
                elbows[index] = leg.Elbow;
                bendSigns[index] = MeasureBendSign(leg);
                if (capturedArmLengths is null)
                {
                    armLengths[index] =
                        Vector2.Distance(leg.Shoulder, leg.Elbow) +
                        Vector2.Distance(leg.Elbow, leg.Foot);
                }
                straightness[index] = armLengths[index] > 0.0001f
                    ? Vector2.Distance(leg.Shoulder, leg.Foot) /
                      armLengths[index]
                    : 0f;
            }
            return new BranchPoseSnapshot(
                elbows,
                bendSigns,
                straightness,
                armLengths);
        }
    }
}
