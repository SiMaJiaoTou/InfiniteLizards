using System.Numerics;
using DesktopPet.Engine;
using InfiniteLizards.Gameplay;

internal static class PortableDebugBridgeSelfTest
{
    private static readonly WorldRect Navigation = new(100f, 80f, 1500f, 900f);
    private static readonly SafetyArea Safety = new(
        new WorldRect(60f, 40f, 1540f, 940f),
        true);

    public static PortableDebugBridgeResult Run()
    {
        try
        {
            var game = new LizardGameModule(
                DesktopLizard.Core.LizardProfile.Default,
                0x51A7);
            var bridge = game.DebugBridge;
            Assert(!bridge.TryCapture(out _),
                "The bridge exposed a snapshot before receiving a world environment.");

            var unavailable = bridge.TryPlay(PortableDebugAction.Forward);
            Assert(
                unavailable.Status == PortableDebugPlaybackStatus.EnvironmentUnavailable,
                "Playback did not fail closed before the first world environment.");

            game.Reset(new Vector2(700f, 400f));
            Advance(game, 1);
            Assert(bridge.TryCapture(out var initial),
                "The bridge did not expose a snapshot after a fixed step.");
            Assert(initial.SpineJoints.Length > 1 && initial.Legs.Length == 4,
                "Portable overlay pose data is incomplete.");
            Assert(initial.Legs.All(leg =>
                    IsFinite(leg.Shoulder) &&
                    IsFinite(leg.Elbow) &&
                    IsFinite(leg.Foot) &&
                    IsFinite(leg.StepTo) &&
                    float.IsFinite(leg.MaximumReach) &&
                    leg.MaximumReach > 0f),
                "Portable leg overlay data is invalid.");
            Assert(
                bridge.PathPolicy.SampleInterval > 0f &&
                bridge.PathPolicy.MinimumDistance > 0f &&
                bridge.PathPolicy.Capacity >= 2,
                "Portable path policy is invalid.");

            var extension = bridge.GetActionProbability(
                PortableDebugTransitionRow.AfterForward,
                PortableDebugAction.ForwardExtension,
                forwardExtended: false);
            var extensionAfterExtension = bridge.GetActionProbability(
                PortableDebugTransitionRow.AfterForward,
                PortableDebugAction.ForwardExtension,
                forwardExtended: true);
            var baseSCurve = bridge.GetActionProbability(
                PortableDebugTransitionRow.AfterForward,
                PortableDebugAction.SCurve,
                forwardExtended: false);
            var remappedSCurve = bridge.GetActionProbability(
                PortableDebugTransitionRow.AfterForward,
                PortableDebugAction.SCurve,
                forwardExtended: true);
            Assert(extension > 0f && extensionAfterExtension == 0f,
                "ForwardExtension probability was not removed after extension.");
            Assert(MathF.Abs(remappedSCurve - (baseSCurve + extension)) < 0.00001f,
                "ForwardExtension probability was not remapped to SCurve.");

            var manualResetBefore = initial.PathResetGeneration;
            bridge.RequestPathReset();
            Assert(bridge.TryCapture(out var manuallyReset) &&
                   manuallyReset.PathResetGeneration != manualResetBefore,
                "A path reset request was not observable by the host.");

            var rebase = new Vector2(321f, -74f);
            var beforeRebase = manuallyReset;
            game.RebaseWorldPosition(rebase);
            Assert(bridge.TryCapture(out var afterRebase),
                "Capture failed after a world rebase.");
            Assert(Near(afterRebase.Position, beforeRebase.Position + rebase) &&
                   Near(afterRebase.CumulativeWorldRebase,
                       beforeRebase.CumulativeWorldRebase + rebase),
                "The portable coordinate epoch did not follow the world rebase.");

            game.TogglePaused();
            Assert(bridge.TryCapture(out var beforeRejected),
                "Capture failed before rejected playback.");
            var rejected = bridge.TryPlay(PortableDebugAction.SCurve);
            Assert(bridge.TryCapture(out var afterRejected),
                "Capture failed after rejected playback.");
            Assert(
                rejected.Status == PortableDebugPlaybackStatus.BlockedByPause &&
                afterRejected.PathResetGeneration == beforeRejected.PathResetGeneration &&
                afterRejected.TransitionSerial == beforeRejected.TransitionSerial &&
                afterRejected.State == beforeRejected.State &&
                Near(afterRejected.Position, beforeRejected.Position),
                "Rejected playback mutated gameplay or requested a path reset.");
            game.TogglePaused();

            Advance(game, 360);
            Assert(bridge.TryCapture(out var beforeAccepted),
                "Capture failed before accepted playback.");
            var accepted = bridge.TryPlay(PortableDebugAction.SCurve);
            Assert(bridge.TryCapture(out var afterAccepted),
                "Capture failed after accepted playback.");
            Assert(accepted.Accepted &&
                   afterAccepted.PathResetGeneration != beforeAccepted.PathResetGeneration,
                "Accepted playback did not request a fresh host-owned path.");

            var lostGrip = bridge.TryPlay(PortableDebugAction.LostGripFall);
            Assert(lostGrip.Accepted,
                "The portable bridge could not start the lost-grip diagnostic action.");
            var sawPartialReach = false;
            var sawReachedTarget = false;
            var sawRegripProgress = false;
            for (var step = 0; step < 900; step++)
            {
                Advance(game, 1);
                Assert(bridge.TryCapture(out var lostGripFrame),
                    "Capture failed during lost-grip playback.");
                sawPartialReach |=
                    lostGripFrame.LostGripPhase == PortableDebugLostGripPhase.Falling &&
                    lostGripFrame.LostGripReachProgress is > 0f and < 1f;
                sawReachedTarget |=
                    lostGripFrame.LostGripPhase == PortableDebugLostGripPhase.Regripping &&
                    lostGripFrame.LostGripCatchReason ==
                    PortableDebugLostGripCatchReason.ReachedTarget;
                sawRegripProgress |= sawReachedTarget &&
                                     lostGripFrame.LostGripRegripProgress > 0f;
                if (sawPartialReach && sawReachedTarget && sawRegripProgress)
                {
                    break;
                }
            }
            Assert(sawPartialReach && sawReachedTarget && sawRegripProgress,
                "The portable debug DTO did not expose reach, contact reason, and regrip progress.");

            return new PortableDebugBridgeResult(
                true,
                "Portable gameplay snapshots, lost-grip reach/contact progress, overlay pose data, probability remapping, playback side effects, path reset signaling, and world rebase epochs passed.");
        }
        catch (Exception exception)
        {
            return new PortableDebugBridgeResult(false, exception.Message);
        }
    }

    private static void Advance(LizardGameModule game, int steps)
    {
        for (var index = 0; index < steps; index++)
        {
            game.AdvanceFixedStep(new DesktopPetFixedStepInput(
                1f / 120f,
                1,
                Navigation,
                Safety,
                new PointerSample(Vector2.Zero, false),
                false));
        }
    }

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool Near(Vector2 left, Vector2 right) =>
        Vector2.DistanceSquared(left, right) < 0.0001f;

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal readonly record struct PortableDebugBridgeResult(bool Passed, string Detail)
{
    public override string ToString() => Detail;
}
