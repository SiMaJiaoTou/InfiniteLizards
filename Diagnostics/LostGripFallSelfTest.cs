using System.Numerics;
using DesktopLizard.AppRuntime;
using DesktopLizard.Core;
using DesktopLizard.Diagnostics.Framework;
using DesktopLizard.Rendering;

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
    int ContactStopViolations,
    int MinimumNonZeroSeekingSteps,
    float MinimumBehaviorReachPeak,
    float MinimumReachTargetErrorReduction,
    float MinimumFrontFootUpwardTravel,
    float MinimumFrontPawCatchDirectionTravel,
    float MaximumReachEntryPoseJump,
    float MaximumTerminalReachError,
    float MaximumContactError,
    float MaximumContactSpineDrift,
    float MaximumPostContactWindowDrift,
    float MaximumPostContactVisualCentroidDrift,
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
    bool MidSeekingSafetyLossFallbackPassed,
    float MaximumSafetyLossPoseJump,
    float MinimumSafetyLossBoundaryMargin,
    int SafetyLossFakeContactSamples,
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
        var safetyLossFallback = RunMidSeekingSafetyLossFallback(
            profile,
            screens[^1]);
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
            metrics.ContactStopViolations,
            metrics.MinimumNonZeroSeekingSteps,
            metrics.MinimumBehaviorReachPeak,
            metrics.MinimumReachTargetErrorReduction,
            metrics.MinimumFrontFootUpwardTravel,
            metrics.MinimumFrontPawCatchDirectionTravel,
            metrics.MaximumReachEntryPoseJump,
            metrics.MaximumTerminalReachError,
            metrics.MaximumContactError,
            metrics.MaximumContactSpineDrift,
            metrics.MaximumPostContactWindowDrift,
            metrics.MaximumPostContactVisualCentroidDrift,
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
            safetyLossFallback.Passed,
            safetyLossFallback.MaximumPoseJump,
            safetyLossFallback.MinimumBoundaryMargin,
            safetyLossFallback.FakeContactSamples,
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
            lizard);

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
                visualCenter);
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
        for (var step = 0; step < 8f / DeltaTime; step++)
        {
            session.Advance(Input(
                DeltaTime,
                screen.NavigationArea,
                profile,
                lostGripSafety: screen.LostGripSafety));
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
            fakeContactSamples == 0;
        return new SafetyLossFallbackResult(
            passed,
            maximumPoseJump,
            minimumBoundaryMargin,
            fakeContactSamples);

        void ObserveFallbackFrame(PetSimulationFrameOutput frame)
        {
            var pose = PoseSnapshot.Capture(lizard);
            maximumPoseJump = Math.Max(
                maximumPoseJump,
                previousPose.MaximumDistance(pose));
            previousPose = pose;

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
        IsFinite(lizard.RegripFrontContactTarget0) &&
        IsFinite(lizard.RegripFrontContactTarget1) &&
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
            .AddMetric("contact_stop_violations", value.ContactStopViolations)
            .AddMetric("minimum_nonzero_seeking_steps", value.MinimumNonZeroSeekingSteps)
            .AddMetric("minimum_behavior_reach_peak", value.MinimumBehaviorReachPeak)
            .AddMetric("minimum_reach_target_error_reduction", value.MinimumReachTargetErrorReduction, "model px")
            .AddMetric("minimum_front_foot_upward_travel", value.MinimumFrontFootUpwardTravel, "model px")
            .AddMetric("minimum_front_paw_catch_direction_travel", value.MinimumFrontPawCatchDirectionTravel, "model px")
            .AddMetric("maximum_reach_entry_pose_jump", value.MaximumReachEntryPoseJump, "model px")
            .AddMetric("maximum_terminal_reach_error", value.MaximumTerminalReachError, "model px")
            .AddMetric("maximum_contact_error", value.MaximumContactError, "model px")
            .AddMetric("maximum_contact_spine_drift", value.MaximumContactSpineDrift, "model px")
            .AddMetric("maximum_post_contact_window_drift", value.MaximumPostContactWindowDrift, "screen px")
            .AddMetric("maximum_post_contact_visual_centroid_drift", value.MaximumPostContactVisualCentroidDrift, "screen px")
            .AddMetric("real_grab_maximum_error", value.RealGrabMaximumError, "model px")
            .AddMetric("maximum_safety_loss_pose_jump", value.MaximumSafetyLossPoseJump, "model px")
            .AddMetric("minimum_safety_loss_boundary_margin", value.MinimumSafetyLossBoundaryMargin, "screen px")
            .AddMetric("safety_loss_fake_contact_samples", value.SafetyLossFakeContactSamples)
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
                "front paws reach upward toward catch targets",
                value.ReachDirectionViolations == 0 &&
                value.MinimumReachTargetErrorReduction > 1f &&
                value.MinimumFrontFootUpwardTravel > 1f &&
                value.MinimumFrontPawCatchDirectionTravel > 1f,
                "both front paws reduce target error, move upward, and advance toward the catch point by > 1 model px",
                $"reduction {value.MinimumReachTargetErrorReduction:F2}; " +
                $"up {value.MinimumFrontFootUpwardTravel:F2}; " +
                $"toward {value.MinimumFrontPawCatchDirectionTravel:F2}; " +
                $"violations {value.ReachDirectionViolations}")
            .AddCheck(
                "contact occurs before the stationary hold",
                value.ContactStopViolations == 0 &&
                value.MaximumTerminalReachError <= 0.25f &&
                value.MaximumContactError <= 0.25f,
                "final moving Seeking frame contacts both paws; next stationary frame holds them",
                $"violations {value.ContactStopViolations}; " +
                $"errors {value.MaximumTerminalReachError:F3}/{value.MaximumContactError:F3}")
            .AddCheck(
                "reach and contact pose continuity",
                value.MaximumReachEntryPoseJump <= 3f &&
                value.MaximumContactSpineDrift <= 0.01f,
                "Seeking entry jump <= 3 model px; contact spine drift <= 0.01 model px",
                $"{value.MaximumReachEntryPoseJump:F3}/{value.MaximumContactSpineDrift:F4}")
            .AddCheck(
                "post-contact host and visual stability",
                value.MaximumPostContactWindowDrift <= 0.001f &&
                value.MaximumPostContactVisualCentroidDrift <= 12f,
                "window <= 0.001 screen px; visual AABB centroid <= 12 screen px",
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
                "mid-seeking safety loss degrades without a fake catch",
                value.MidSeekingSafetyLossFallbackPassed &&
                value.MaximumSafetyLossPoseJump <= 3f &&
                value.MinimumSafetyLossBoundaryMargin >= -0.001f &&
                value.SafetyLossFakeContactSamples == 0,
                "SafetyForced keeps the final moving frame partial, then uses continuous non-contact recovery",
                $"passed {value.MidSeekingSafetyLossFallbackPassed}; " +
                $"local-pose jump {value.MaximumSafetyLossPoseJump:F3}; " +
                $"margin {value.MinimumSafetyLossBoundaryMargin:F2}px; " +
                $"fake contacts {value.SafetyLossFakeContactSamples}")
            .AddCheck("pause pre-empts fall", value.PausePriorityPassed, "true", value.PausePriorityPassed.ToString())
            .AddCheck("finite behavior and pose", value.NonFiniteSamples == 0, "0", value.NonFiniteSamples.ToString())
            .AddCheck("Chinese debug labels", value.DebugChineseLabelsPassed, "true", value.DebugChineseLabelsPassed.ToString())
            .Build();
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
        int FakeContactSamples)
    {
        public static SafetyLossFallbackResult Failed => new(
            false,
            float.PositiveInfinity,
            float.NegativeInfinity,
            0);
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
        public int ContactStopViolations;
        public int MinimumNonZeroSeekingSteps = int.MaxValue;
        public float MinimumBehaviorReachPeak = float.PositiveInfinity;
        public float MinimumReachTargetErrorReduction = float.PositiveInfinity;
        public float MinimumFrontFootUpwardTravel = float.PositiveInfinity;
        public float MinimumFrontPawCatchDirectionTravel = float.PositiveInfinity;
        public float MaximumReachEntryPoseJump;
        public float MaximumTerminalReachError;
        public float MaximumContactError;
        public float MaximumContactSpineDrift;
        public float MaximumPostContactWindowDrift;
        public float MaximumPostContactVisualCentroidDrift;
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
            ContactStopViolations += result.ContactStopViolations;
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
                MinimumFrontFootUpwardTravel = Math.Min(
                    MinimumFrontFootUpwardTravel,
                    result.FrontFootUpwardTravel);
                MinimumFrontPawCatchDirectionTravel = Math.Min(
                    MinimumFrontPawCatchDirectionTravel,
                    result.FrontPawCatchDirectionTravel);
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
        }

        public void NormalizeReachMinimums()
        {
            if (MinimumNonZeroSeekingSteps == int.MaxValue)
            {
                MinimumNonZeroSeekingSteps = 0;
            }
            if (!float.IsFinite(MinimumBehaviorReachPeak))
            {
                MinimumBehaviorReachPeak = 0f;
            }
            if (!float.IsFinite(MinimumReachTargetErrorReduction))
            {
                MinimumReachTargetErrorReduction = 0f;
            }
            if (!float.IsFinite(MinimumFrontFootUpwardTravel))
            {
                MinimumFrontFootUpwardTravel = 0f;
            }
            if (!float.IsFinite(MinimumFrontPawCatchDirectionTravel))
            {
                MinimumFrontPawCatchDirectionTravel = 0f;
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
}
