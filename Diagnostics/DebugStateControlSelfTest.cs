using System.Numerics;
using DesktopLizard.AppRuntime;
using DesktopLizard.Core;
using DesktopLizard.Diagnostics.Framework;
using DesktopLizard.Rendering;

namespace DesktopLizard.Diagnostics;

internal static class DebugStateControlSelfTest
{
    private const float Tolerance = 0.0001f;

    public static DiagnosticReport RunReport()
    {
        var profile = LizardProfile.Default;
        var area = new FloatRect(-2000f, -1500f, 2000f, 1500f);
        var expectedStates = new Dictionary<AutonomousAction, RoamingState>
        {
            [AutonomousAction.Forward] = RoamingState.ForwardCrawl,
            [AutonomousAction.ForwardExtension] = RoamingState.ForwardCrawl,
            [AutonomousAction.Curve] = RoamingState.CurveCrawl,
            [AutonomousAction.SCurve] = RoamingState.SCurveCrawl,
            [AutonomousAction.FastForward] = RoamingState.FastForwardCrawl,
            [AutonomousAction.FastSCurve] = RoamingState.FastSCurveCrawl,
            [AutonomousAction.TurnAround] = RoamingState.TurnAround,
            [AutonomousAction.LostGripFall] = RoamingState.LostGripFall
        };

        var playbackFailures = 0;
        var initializationFailures = 0;
        var nonFiniteSamples = 0;
        foreach (var pair in expectedStates)
        {
            var session = ReadySession(81420 + (int)pair.Key, profile, area);
            var result = session.TryPlayDebugAction(pair.Key, area);
            var behavior = session.BehaviorForDiagnostics;
            if (!result.Accepted || behavior.State != pair.Value)
            {
                playbackFailures++;
                continue;
            }

            var snapshot = behavior.CaptureDebugSnapshot(area);
            var initialized = pair.Key switch
            {
                AutonomousAction.Forward =>
                    snapshot.StateTimeRemaining > 0f && !snapshot.ForwardExtended,
                AutonomousAction.ForwardExtension =>
                    snapshot.StateTimeRemaining > 0f && snapshot.ForwardExtended,
                AutonomousAction.Curve =>
                    snapshot.StateTimeRemaining > 0f &&
                    MathF.Abs(snapshot.DesiredTurnVelocity) > 0f,
                AutonomousAction.SCurve or AutonomousAction.FastSCurve =>
                    snapshot.SCurveCycleCount >= 2 &&
                    snapshot.SCurveDuration > 0f &&
                    snapshot.SCurveAmplitude > 0f,
                AutonomousAction.FastForward =>
                    MathF.Abs(snapshot.DesiredSpeed - profile.Behavior.Speed.MaximumCrawl) <= Tolerance,
                AutonomousAction.TurnAround =>
                    snapshot.TurnRemaining > 0f &&
                    MathF.Abs(snapshot.DesiredTurnVelocity) > 0f,
                AutonomousAction.LostGripFall =>
                    snapshot.LostGripPhase == LostGripFallPhase.Falling &&
                    snapshot.LostGripTargetDistance >=
                    profile.Behavior.LostGripFall.MinimumDistance,
                _ => false
            };
            initializationFailures += initialized ? 0 : 1;
            nonFiniteSamples += IsFinite(snapshot) ? 0 : 1;
        }

        var probabilityFailures = 0;
        var maximumProbabilitySumError = 0f;
        foreach (var row in new[]
                 {
                     AutonomousTransitionRow.AfterForward,
                     AutonomousTransitionRow.AfterCurve,
                     AutonomousTransitionRow.AfterSCurve,
                     AutonomousTransitionRow.AfterFast
                 })
        {
            var sum = DebugStateControlsView.Actions.Sum(action =>
                AutonomousTransitionInspector.EffectiveProbability(
                    profile.Behavior.TransitionMatrix,
                    row,
                    action,
                    forwardExtended: false));
            maximumProbabilitySumError = Math.Max(maximumProbabilitySumError, MathF.Abs(1f - sum));
            probabilityFailures += MathF.Abs(1f - sum) <= Tolerance ? 0 : 1;
        }

        var extendedForward = AutonomousTransitionInspector.EffectiveProbability(
            profile.Behavior.TransitionMatrix,
            AutonomousTransitionRow.AfterForward,
            AutonomousAction.ForwardExtension,
            forwardExtended: true);
        var extendedSCurve = AutonomousTransitionInspector.EffectiveProbability(
            profile.Behavior.TransitionMatrix,
            AutonomousTransitionRow.AfterForward,
            AutonomousAction.SCurve,
            forwardExtended: true);
        var forwardExtensionRedirectPassed =
            MathF.Abs(extendedForward) <= Tolerance &&
            MathF.Abs(extendedSCurve - 0.60f) <= Tolerance;

        var uniqueActions =
            DebugStateControlsView.Actions.Length == 8 &&
            DebugStateControlsView.Actions.Distinct().Count() == 8;
        var chineseLabels = DebugStateControlsView.Actions.All(action =>
            !string.IsNullOrWhiteSpace(DebugLabelCatalog.Action(action)) &&
            DebugLabelCatalog.Action(action) != action.ToString());

        var pausedSession = ReadySession(81501, profile, area);
        pausedSession.TogglePaused();
        var pausedSerial = pausedSession.BehaviorForDiagnostics.TransitionSerial;
        var pausedResult = pausedSession.TryPlayDebugAction(AutonomousAction.SCurve, area);
        var pausePriorityPassed =
            pausedResult.Status == DebugPlaybackStatus.BlockedByPause &&
            pausedSession.BehaviorForDiagnostics.TransitionSerial == pausedSerial;

        var grabbedSession = ReadySession(81502, profile, area);
        grabbedSession.BeginGrab(new Vector2(
            profile.Appearance.RenderCanvasSize * 0.5f,
            profile.Appearance.RenderCanvasSize * 0.5f));
        var grabbedSerial = grabbedSession.BehaviorForDiagnostics.TransitionSerial;
        var grabbedResult = grabbedSession.TryPlayDebugAction(AutonomousAction.FastForward, area);
        var grabPriorityPassed =
            grabbedResult.Status == DebugPlaybackStatus.BlockedByProtectedState &&
            grabbedSession.BehaviorForDiagnostics.State == RoamingState.Grabbed &&
            grabbedSession.BehaviorForDiagnostics.TransitionSerial == grabbedSerial;

        return new DiagnosticReportBuilder(nameof(DebugStateControlSelfTest))
            .AddMetric("button_actions", DebugStateControlsView.Actions.Length)
            .AddMetric("playback_failures", playbackFailures)
            .AddMetric("initialization_failures", initializationFailures)
            .AddMetric("probability_failures", probabilityFailures)
            .AddMetric("maximum_probability_sum_error", maximumProbabilitySumError)
            .AddMetric("non_finite_samples", nonFiniteSamples)
            .AddCheck(
                "unique matrix action buttons",
                uniqueActions,
                "8 unique configured actions",
                uniqueActions ? "8/8" : "missing or duplicate")
            .AddCheck(
                "debug playback through state entries",
                playbackFailures == 0 && initializationFailures == 0,
                "all buttons enter and initialize their requested action",
                $"playback {playbackFailures}, initialization {initializationFailures}")
            .AddCheck(
                "resolved transition probabilities",
                probabilityFailures == 0 && maximumProbabilitySumError <= Tolerance,
                "every displayed transition row sums to one",
                $"failures {probabilityFailures}, max error {maximumProbabilitySumError:E3}")
            .AddCheck(
                "forward-extension redirect",
                forwardExtensionRedirectPassed,
                "extended forward moves its bucket into S-curve",
                $"extension {extendedForward:F3}, S {extendedSCurve:F3}")
            .AddCheck(
                "Chinese action labels",
                chineseLabels,
                "every button has a Chinese label",
                chineseLabels ? "8/8" : "missing")
            .AddCheck(
                "pause and grab priority",
                pausePriorityPassed && grabPriorityPassed,
                "protected interactions reject playback without side effects",
                $"pause {pausePriorityPassed}, grab {grabPriorityPassed}")
            .AddCheck(
                "finite playback state",
                nonFiniteSamples == 0,
                "0 non-finite samples",
                nonFiniteSamples.ToString())
            .Build();
    }

    private static PetSimulationSession ReadySession(
        int seed,
        LizardProfile profile,
        FloatRect area)
    {
        var session = new PetSimulationSession(seed, profile);
        session.Reset(area.Center, profile.Runtime.InitialHeading);
        for (var frame = 0; frame < 180 && session.BehaviorForDiagnostics.State == RoamingState.Spawn; frame++)
        {
            session.Advance(new PetSimulationFrameInput(
                1f / 60f,
                area,
                default,
                IsDragging: false,
                ModelToScreenScale: profile.Appearance.VisualScale));
        }
        return session;
    }

    private static bool IsFinite(BehaviorDebugSnapshot snapshot) =>
        float.IsFinite(snapshot.Position.X) &&
        float.IsFinite(snapshot.Position.Y) &&
        float.IsFinite(snapshot.Heading) &&
        float.IsFinite(snapshot.Speed) &&
        float.IsFinite(snapshot.DesiredSpeed) &&
        float.IsFinite(snapshot.TurnVelocity) &&
        float.IsFinite(snapshot.DesiredTurnVelocity);
}
