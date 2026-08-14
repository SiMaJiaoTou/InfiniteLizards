using System.IO;
using System.Text;

namespace DesktopLizard.Diagnostics;

/// <summary>
/// Owns the command-line diagnostics surface. The WPF application startup path
/// only needs to decide whether a diagnostic command consumed the process.
/// </summary>
internal static class DiagnosticCommandRunner
{
    public static bool TryRun(IReadOnlyList<string> arguments, out int exitCode)
    {
        exitCode = 0;

        if (TryGetValue(arguments, "--dangling-analysis=", out var danglingPath))
        {
            WriteText(danglingPath, DanglingJitterAnalyzer.Run());
            return true;
        }

        if (TryGetValue(arguments, "--lost-grip-test=", out var lostGripPath))
        {
            var result = LostGripFallSelfTest.Run();
            WriteText(lostGripPath, FormatLostGripFall(result));
            exitCode = result.Passed ? 0 : 9;
            return true;
        }

        if (TryGetValue(arguments, "--grab-release-test=", out var grabPath))
        {
            var result = GrabReleaseSelfTest.Run();
            WriteText(grabPath, FormatGrabRelease(result));
            exitCode = result.Passed ? 0 : 6;
            return true;
        }

        if (TryGetValue(arguments, "--mouse-chase-test=", out var mousePath))
        {
            var result = MouseChaseSelfTest.Run();
            WriteText(mousePath, FormatMouseChase(result));
            exitCode = result.Passed ? 0 : 5;
            return true;
        }

        if (TryGetValue(arguments, "--liveness-test=", out var livenessPath))
        {
            var result = LivenessSelfTest.Run();
            WriteText(livenessPath, FormatLiveness(result));
            exitCode = result.Passed ? 0 : 4;
            return true;
        }

        if (TryGetValue(arguments, "--session-test=", out var sessionPath))
        {
            var result = SimulationSessionSelfTest.Run();
            WriteText(sessionPath, FormatSimulationSession(result));
            exitCode = result.Passed ? 0 : 7;
            return true;
        }

        if (TryGetValue(arguments, "--configuration-test=", out var configurationPath))
        {
            var result = ConfigurationSelfTest.Run();
            WriteText(configurationPath, FormatConfiguration(result));
            exitCode = result.Passed ? 0 : 8;
            return true;
        }

        if (TryGetValue(arguments, "--roaming-test=", out var roamingPath))
        {
            var result = RoamingSelfTest.Run();
            WriteText(roamingPath, FormatRoaming(result));
            exitCode = result.Passed ? 0 : 3;
            return true;
        }

        if (TryGetValue(arguments, "--self-test=", out var gaitPath))
        {
            var result = GaitSelfTest.Run();
            WriteText(gaitPath, FormatGait(result));
            exitCode = result.Passed ? 0 : 2;
            return true;
        }

        if (TryGetValue(arguments, "--preview-dir=", out var previewDirectory))
        {
            PreviewExporter.Export(previewDirectory);
            return true;
        }

        return false;
    }

    private static bool TryGetValue(
        IReadOnlyList<string> arguments,
        string prefix,
        out string value)
    {
        foreach (var argument in arguments)
        {
            if (!argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = argument[prefix.Length..].Trim('"');
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, text);
    }

    private static string FormatGrabRelease(GrabReleaseTestResult result) => BuildReport(
        "GrabReleaseSelfTest", result.Passed,
        $"grab source states covered: {result.GrabbedStatesCovered}",
        $"grab attachment types covered: {result.GrabAttachmentsCovered}/13",
        $"release sequences: {result.ReleaseSequences}",
        $"invalid transitions: {result.InvalidTransitions}",
        $"missing source states: {result.MissingSourceStates}",
        $"minimum body center-of-mass drop: {result.MinimumHangDrop:F2} model px",
        $"minimum foot drop: {result.MinimumFootDrop:F2} model px",
        $"maximum limb constraint error: {result.MaximumConstraintError:F3} model px",
        $"constraint error spine/upper/lower: {result.MaximumSpineError:F3}/{result.MaximumUpperLegError:F3}/{result.MaximumLowerLegError:F3}",
        $"maximum grab attachment error: {result.MaximumGrabError:F3} model px",
        $"minimum canvas margin: {result.MinimumCanvasMargin:F2} model px",
        $"maximum first-frame release pose jump: {result.MaximumReleasePoseJump:F2} model px",
        $"release jump spine/elbow/foot: {result.MaximumReleaseSpineJump:F2}/{result.MaximumReleaseElbowJump:F2}/{result.MaximumReleaseFootJump:F2}",
        $"sprint distance range: {result.MinimumSprintDistance:F2}-{result.MaximumSprintDistance:F2} px",
        $"maximum sprint speed: {result.MaximumSprintSpeed:F2} px/s",
        $"sprint quadrants: {result.SprintQuadrants}/4",
        $"boundary violations: {result.BoundaryViolations}",
        $"regrab violations: {result.RegrabViolations}",
        $"non-finite samples: {result.NonFiniteSamples}");

    private static string FormatLostGripFall(LostGripFallTestResult result) => BuildReport(
        "LostGripFallSelfTest", result.Passed,
        $"completed scenarios: {result.CompletedScenarios}/{LostGripFallSelfTest.ExpectedScenarioCount}",
        $"falling/regrip/autonomous coverage: {result.FallingEntries}/{result.RegripEntries}/{result.AutonomousRecoveries}",
        $"invalid transitions: {result.InvalidTransitions}",
        $"fall distance range: {result.MinimumFallDistance:F2}-{result.MaximumFallDistance:F2} px",
        $"available safe-bottom range: {result.MinimumAvailableDistance:F2}-{result.MaximumAvailableDistance:F2} px",
        $"screen heights / start bands / seeds: {result.ScreenHeightsCovered}/{result.StartBandsCovered}/{result.SeedsCovered}",
        $"long-fall heights / same-seed scaling: {result.LongFallScreenHeights}/{result.HeightScalingPairs - result.HeightScalingFailures}/{result.HeightScalingPairs}",
        $"target-distance bound violations: {result.TargetDistanceBoundaryViolations}",
        $"maximum fall velocity: {result.MaximumFallVelocity:F2} px/s",
        $"maximum regrip pose jump / world-centroid catch error / drift: {result.MaximumRegripPoseJump:F3} model px / {result.MaximumCatchCentroidError:F3} px / {result.MaximumRegripCentroidDrift:F3} px",
        $"maximum constraint/grab error: {result.MaximumConstraintError:F3}/{result.MaximumGrabError:F3} model px",
        $"containment total/max: {result.MaximumContainmentCorrectionTotal:F4}/{result.MaximumContainmentCorrection:F4} model px",
        $"reference correction total/max/center error: {result.MaximumReferenceCorrectionTotal:F3}/{result.MaximumReferenceCorrection:F3}/{result.MaximumReferenceCenterError:F4} model px",
        $"minimum drawable canvas / screen margin: {result.MinimumCanvasMargin:F2} model px / {result.MinimumVisualMargin:F2} px",
        $"navigation/full-HWND/visual boundary violations: {result.NavigationBoundaryViolations}/{result.FullRenderBoundaryViolations}/{result.VisualBoundaryViolations}",
        $"monotonicity/non-finite violations: {result.MonotonicityViolations}/{result.NonFiniteSamples}",
        $"truncated fall / near-bottom cancellation: {result.TruncatedFallPassed}/{result.NearBottomCancellationPassed}",
        $"left/right/top / undersized-area cancellation: {result.SideEdgeCancellationPassed}/{result.TinyAreaCancellationPassed}",
        $"pointer / real-grab / pause priority: {result.PointerPriorityPassed}/{result.GrabPriorityPassed}/{result.PausePriorityPassed}",
        $"Chinese debug labels: {result.DebugChineseLabelsPassed}");

    private static string FormatMouseChase(MouseChaseTestResult result) => BuildReport(
        "MouseChaseSelfTest", result.Passed,
        $"chase entries: {result.ChaseEntries}",
        $"trigger delay: {result.TriggerDelay:F3} s",
        $"sustained chase probe: {result.SustainedChaseDuration:F3} s",
        $"distance closed: {result.DistanceClosed:F2} px",
        $"maximum chase speed: {result.MaximumChaseSpeed:F2} px/s",
        $"sustained while near: {result.SustainedWhileNear}",
        $"returned after leave: {result.ReturnedAfterLeave}",
        $"rearmed after leave: {result.RearmedAfterLeave}",
        $"interaction priority: {result.InteractionPriorityPassed}",
        $"near-distance triggers: {result.NearDistanceTriggers}/4",
        $"outside-navigation pointer trigger: {result.OutsideNavigationPointerTriggered}",
        $"60 Hz cursor / 120 Hz substep path: {result.SixtyHzSubstepPassed}",
        $"S-curve mouse interruption: {result.SCurveInterruptionPassed}",
        $"fast-forward mouse interruption: {result.FastForwardInterruptionPassed}",
        $"fast S-curve mouse interruption: {result.FastSCurveInterruptionPassed}",
        $"boundary violations: {result.BoundaryViolations}",
        $"non-finite samples: {result.NonFiniteSamples}");

    private static string FormatLiveness(LivenessTestResult result) => BuildReport(
        "LivenessSelfTest", result.Passed,
        $"samples: {result.Samples}",
        $"longest active freeze: {result.LongestActiveFreeze:F3} s",
        $"longest visual freeze: {result.LongestVisualFreeze:F3} s",
        $"longest rest chain: {result.LongestRestChain:F3} s",
        $"minimum 1-second idle tail range: {result.MinimumIdleTailRange:F3} px",
        $"watchdog recoveries: {result.WatchdogRecoveries}",
        $"non-finite samples: {result.NonFiniteSamples}");

    private static string FormatSimulationSession(SimulationSessionTestResult result) => BuildReport(
        "SimulationSessionSelfTest", result.Passed,
        $"compared display frames: {result.ComparedFrames}",
        $"state/transition mismatches: {result.StateMismatches}/{result.TransitionMismatches}",
        $"maximum position difference: {result.MaximumPositionDifference:F6} px",
        $"maximum heading difference: {result.MaximumHeadingDifference:F6} rad",
        $"maximum joint difference: {result.MaximumJointDifference:F6} model px",
        $"catch-up clamp matched: {result.CatchUpClampMatched}",
        $"fixed steps bounded: {result.FixedStepBounded}",
        $"non-finite samples: {result.NonFiniteSamples}");

    private static string FormatConfiguration(ConfigurationTestResult result) => BuildReport(
        "ConfigurationSelfTest", result.Passed,
        $"default validation: {result.DefaultValidationPassed}",
        $"JSON round-trip: {result.RoundTripPassed}",
        $"persistent generated seed: {result.PersistentSeedPassed}",
        $"legacy schema migration: {result.SchemaMigrationPassed}",
        $"invalid configuration fallback: {result.InvalidFallbackPassed}",
        $"invalid file preserved: {result.InvalidFilePreserved}",
        $"deterministic same-seed profile: {result.DeterministicProfilePassed}",
        $"distinct generated individuals: {result.DistinctIndividualsPassed}",
        $"variation applied across modules: {result.VariationApplied}",
        $"transition/rest probabilities normalized: {result.TransitionNormalizationPassed}",
        $"custom transition matrix controls selection: {result.CustomMatrixControlPassed}",
        $"unsafe matrix entries rejected: {result.UnsafeMatrixRejected}",
        $"unsafe numeric configuration rejected: {result.UnsafeNumericConfigurationRejected}",
        $"lost-grip configuration and migration: {result.LostGripConfigurationPassed}",
        $"expected fallback warnings: {result.WarningCount}",
        $"failure details: {result.FailureDetails}");

    private static string FormatRoaming(RoamingTestResult result) => BuildReport(
        "RoamingSelfTest", result.Passed,
        $"moving samples: {result.MovingSamples}",
        $"curved fraction: {result.CurvedFraction:P1}",
        $"turn density: {result.TurnDensity:F4} rad/DIP",
        $"turnarounds: {result.Turnarounds}",
        $"boundary contacts: {result.BoundaryContacts}",
        $"max angular speed: {result.MaximumAngularSpeed:F3} rad/s",
        $"max angular acceleration: {result.MaximumAngularAcceleration:F1} rad/s^2",
        $"autonomous state coverage: {result.AutonomousStateCoverage}/{result.RequiredAutonomousStateCount}",
        $"interaction state coverage: {result.InteractionStateCoverage}/3",
        $"invalid transitions: {result.InvalidTransitions}",
        $"complete forward-turn-rest cycles: {result.CompleteCycles}",
        $"distinct transition sequences: {result.DistinctSequences}",
        $"valid S-curve episodes: {result.ValidSCurveEpisodes}/{result.SCurveEpisodes}",
        $"minimum S-curve lobe angle: {result.MinimumSCurveLobeAngle:F3} rad",
        $"maximum S-curve heading bias: {result.MaximumSCurveHeadingBias:F3} rad",
        $"minimum S-curve reversals: {result.MinimumSCurveReversals}",
        $"minimum normal/fast S-curve radius: {result.MinimumNormalSCurveTurnRadius:F1}/{result.MinimumFastSCurveTurnRadius:F1} DIP",
        $"normal/fast forward entries: {result.NormalForwardEntries}/{result.FastForwardEntries}",
        $"normal/fast S-curve entries: {result.NormalSCurveEntries}/{result.FastSCurveEntries}",
        $"median/peak fast crawl speed: {result.MedianFastCrawlSpeed:F2}/{result.PeakFastCrawlSpeed:F2} DIP/s",
        $"turnaround action ratio: {result.TurnaroundActionRatio:P1}",
        $"median forward speed: {result.MedianForwardSpeed:F2} DIP/s",
        $"peak speed: {result.PeakSpeed:F2} DIP/s",
        $"minimum complete-cycle forward distance: {result.MinimumCycleForwardDistance:F2} DIP",
        $"minimum completed turnaround: {result.MinimumTurnaroundAngle:F3} rad",
        $"rest duration violations: {result.RestDurationViolations}",
        $"boundary recovery violations: {result.BoundaryRecoveryViolations}");

    private static string FormatGait(GaitTestResult result) => BuildReport(
        "GaitSelfTest", result.Passed,
        $"step transitions: {result.StepTransitions}",
        $"pair sequence violations: {result.PairSequenceViolations}",
        $"maximum swinging legs: {result.MaximumSwingingLegs}",
        $"planted samples: {result.PlantedSamples}",
        $"max planted-foot drift error: {result.MaximumPlantedDrift:F4} DIP/frame",
        $"worst planted drift speed/leg: {result.MaximumPlantedDriftSpeed:F1} px/s / {result.MaximumPlantedDriftLeg}",
        $"max reach overflow: {result.MaximumReachOverflow:F4} DIP",
        $"reach projections/target clamps/swing clamps: {result.ReachProjections}/{result.StepTargetClamps}/{result.SwingClamps}",
        $"maximum kinematic correction: {result.MaximumReachCorrection:F4} DIP",
        $"maximum reach-correction frame: {result.MaximumReachCorrectionFrame}",
        $"max step gap while moving: {result.MaximumStepGap:F3} s",
        $"max step gap at 72-96.2 px/s: {result.MaximumHighSpeedStepGap:F3} s",
        $"average high-speed pair gap: {result.AverageHighSpeedStepGap:F3} s",
        $"all-feet support min/high-speed avg: {result.MinimumAllFeetSupport:F3}/{result.AverageHighSpeedAllFeetSupport:F3} s",
        $"reference pair gap/support: {result.AverageReferencePairGap:F3}/{result.AverageReferenceAllFeetSupport:F3} s",
        $"visual pair stagger avg/max: {result.AverageVisualPairStagger:F3}/{result.MaximumVisualPairStagger:F3} s",
        $"world landing stride reference/high: {result.AverageReferenceWorldStride:F2}/{result.AverageHighSpeedWorldStride:F2} px",
        $"curve pair long/short and outer/inner ratios: {result.AverageCurvePairSpanRatio:F2}/{result.CurveOuterInnerSpanRatio:F2}",
        $"tight-turn drift/reach corrections: {result.TightTurnPlantedDrift:F4}/{result.TightTurnReachCorrections}",
        $"tight-turn projection/target/swing: {result.TightTurnCorrectionBreakdown}",
        $"average high-speed stride/arc: {result.AverageHighSpeedStepSpan:F2}/{result.AverageHighSpeedStepHeight:F2} model px",
        $"deterministic fast S-curve cases: {result.FastSCurveCasesPassed}/{result.FastSCurveCaseCount}",
        $"fast S-curve steps/pair violations/max swing: {result.FastSCurveStepTransitions}/{result.FastSCurvePairSequenceViolations}/{result.FastSCurveMaximumSwingingLegs}",
        $"fast S-curve planted drift and projection/target/swing: {result.FastSCurveMaximumPlantedDrift:F4}/{result.FastSCurveReachProjections}/{result.FastSCurveStepTargetClamps}/{result.FastSCurveSwingClamps}",
        $"fast S-curve min peak/lobe, max bias, non-finite: {result.FastSCurveMinimumPeakSpeed:F2}/{result.FastSCurveMinimumLobeAngle:F3}/{result.FastSCurveMaximumHeadingBias:F3}/{result.FastSCurveNonFiniteSamples}",
        $"fast S-curve failure details: {result.FastSCurveFailureDetails}",
        $"non-default profile smoke: {result.ProfileSmokePassed} (applied {result.ProfileSmokeApplied})",
        $"profile smoke steps/non-finite: {result.ProfileSmokeStepTransitions}/{result.ProfileSmokeNonFiniteSamples}",
        $"profile smoke constraint/grab error: {result.ProfileSmokeMaximumConstraintError:F3}/{result.ProfileSmokeMaximumGrabError:F3}",
        $"profile smoke failure details: {result.ProfileSmokeFailureDetail}");

    private static string BuildReport(string name, bool passed, params string[] metrics)
    {
        var report = new StringBuilder();
        report.Append(name)
            .Append(": ")
            .AppendLine(passed ? "PASS" : "FAIL");
        foreach (var metric in metrics)
        {
            report.AppendLine(metric);
        }
        return report.ToString();
    }
}
