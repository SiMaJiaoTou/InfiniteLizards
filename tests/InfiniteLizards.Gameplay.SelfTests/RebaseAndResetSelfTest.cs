using System.Numerics;
using System.Reflection;
using DesktopLizard.AppRuntime;
using DesktopLizard.Core;

internal static class RebaseAndResetSelfTest
{
    private const float Step = 1f / 120f;
    private const float StateTolerance = 0.002f;
    // Absolute world coordinates use single precision in the mature gameplay
    // core. Rebasing can therefore change only subpixel local-pose rounding;
    // with the default profile exercised here, 0.1 model unit is 0.065 DIP
    // (0.13 physical pixel even at 200% backing scale).
    private const float PoseTolerance = 0.1f;
    private static readonly LizardProfile Profile = LizardProfile.Default;
    private static readonly FloatRect Area = new(-1800f, -1300f, 1800f, 1300f);
    private static readonly LostGripSafetyContext Safety = new(
        new FloatRect(-1500f, -1000f, 1500f, 1000f),
        true);
    private static readonly Vector2 RebaseDelta = new(256f, -128f);

    public static RebaseAndResetResult Run()
    {
        try
        {
            VerifyLocomotionRebase();
            VerifyPointerChaseRebase();
            VerifyPausedRebase();
            VerifyLostGripRebase();
            VerifyLegacyTelemetryLifecycle();
            VerifyDeterministicFullReset();
            return new RebaseAndResetResult(
                true,
                "locomotion, pointer chase, pause, lost-grip, debug trail, RNG continuation, and full seeded reset remained equivalent");
        }
        catch (Exception exception)
        {
            return new RebaseAndResetResult(false, exception.Message);
        }
    }

    private static void VerifyLocomotionRebase()
    {
        var (baseline, rebased) = CreateReadyPair(0x191A1);
        StartActionPair(baseline, rebased, AutonomousAction.Forward);
        AdvanceEquivalent(baseline, rebased, Vector2.Zero, 24, captureDebug: true);
        AssertTrue(
            baseline.BehaviorForDiagnostics.State.IsLocomoting(),
            "locomotion rebase precondition was not reached");

        var beforeZero = rebased.BehaviorForDiagnostics.CaptureDebugSnapshot(Area);
        rebased.RebaseWorldPosition(Vector2.Zero);
        var afterZero = rebased.BehaviorForDiagnostics.CaptureDebugSnapshot(Area);
        AssertTrue(beforeZero.Equals(afterZero),
            "zero-delta rebase changed behavior state");

        rebased.RebaseWorldPosition(RebaseDelta);
        AssertEquivalentState(baseline, rebased, RebaseDelta, "locomotion immediate");
        AssertRenderEquivalent(
            baseline.CaptureSnapshot(),
            rebased.CaptureSnapshot(),
            "locomotion immediate pose");

        // Long enough to cross multiple seeded autonomous choices. Equality
        // after those choices proves the rebase did not consume or reseed RNG.
        AdvanceEquivalent(
            baseline,
            rebased,
            RebaseDelta,
            720,
            captureDebug: true,
            verifyDebugTrail: true);
    }

    private static void VerifyPointerChaseRebase()
    {
        var (baseline, rebased) = CreateReadyPair(0x2A2B2);
        var pointer = new PointerObservation(
            baseline.Position + new Vector2(180f, 35f),
            true);
        for (var step = 0; step < 60 &&
             baseline.BehaviorForDiagnostics.State != RoamingState.MouseChase; step++)
        {
            AdvanceEquivalent(
                baseline,
                rebased,
                Vector2.Zero,
                1,
                pointer);
        }
        AssertTrue(
            baseline.BehaviorForDiagnostics.State == RoamingState.MouseChase &&
            rebased.BehaviorForDiagnostics.State == RoamingState.MouseChase,
            "pointer-chase rebase precondition was not reached");

        rebased.RebaseWorldPosition(RebaseDelta);
        AssertEquivalentState(baseline, rebased, RebaseDelta, "pointer immediate");
        AdvanceEquivalent(
            baseline,
            rebased,
            RebaseDelta,
            240,
            pointer);
    }

    private static void VerifyPausedRebase()
    {
        var (baseline, rebased) = CreateReadyPair(0x3B3C3);
        StartActionPair(baseline, rebased, AutonomousAction.Curve);
        AdvanceEquivalent(baseline, rebased, Vector2.Zero, 30);
        AssertTrue(baseline.TogglePaused() && rebased.TogglePaused(),
            "pause command did not enter paused state");
        AdvanceEquivalent(baseline, rebased, Vector2.Zero, 1);

        rebased.RebaseWorldPosition(RebaseDelta);
        AssertEquivalentState(baseline, rebased, RebaseDelta, "paused immediate");
        AdvanceEquivalent(baseline, rebased, RebaseDelta, 90);
        AssertTrue(!baseline.TogglePaused() && !rebased.TogglePaused(),
            "pause command did not leave paused state");
        AdvanceEquivalent(baseline, rebased, RebaseDelta, 600);
    }

    private static void VerifyLostGripRebase()
    {
        var (baseline, rebased) = CreateReadyPair(0x4C4D4);
        StartActionPair(baseline, rebased, AutonomousAction.LostGripFall);
        AssertTrue(
            baseline.BehaviorForDiagnostics.State == RoamingState.LostGripFall &&
            baseline.BehaviorForDiagnostics.LostGripPhase == LostGripFallPhase.Falling,
            "lost-grip rebase precondition was not reached");
        AdvanceEquivalent(baseline, rebased, Vector2.Zero, 8);
        AssertTrue(baseline.BehaviorForDiagnostics.LostGripDistance > 0f,
            "lost-grip fall did not accumulate distance before rebase");

        rebased.RebaseWorldPosition(RebaseDelta);
        AssertEquivalentState(baseline, rebased, RebaseDelta, "lost-grip immediate");
        AdvanceEquivalent(baseline, rebased, RebaseDelta, 720);
    }

    private static void VerifyDeterministicFullReset()
    {
        const int seed = 0x5D5E5;
        var resetPosition = new Vector2(130f, -70f);
        var mutated = new PetSimulationSession(seed, Profile);
        mutated.Reset(resetPosition, Profile.Runtime.InitialHeading);
        for (var frame = 0; frame < 240; frame++)
        {
            mutated.Advance(Input(1f / 60f, Area, Safety));
        }

        mutated.BeginGrab(new Vector2(64f, 64f));
        mutated.DragTo(resetPosition + new Vector2(95f, 55f));
        mutated.Advance(Input(1f / 60f, Area, Safety, isDragging: true));
        _ = mutated.TogglePaused();
        mutated.EndGrab(mutated.Position);
        mutated.Advance(Input(1f / 240f, Area, Safety));

        mutated.Reset(resetPosition, Profile.Runtime.InitialHeading);
        var fresh = new PetSimulationSession(seed, Profile);
        fresh.Reset(resetPosition, Profile.Runtime.InitialHeading);
        AssertEquivalentState(fresh, mutated, Vector2.Zero, "full reset immediate");
        AssertRenderEquivalent(
            fresh.CaptureSnapshot(),
            mutated.CaptureSnapshot(),
            "full reset initial pose");

        var frameDeltas = new[]
        {
            1f / 50f,
            1f / 60f,
            1f / 120f,
            1f / 144f,
            1f / 240f,
            0f
        };
        for (var frame = 0; frame < 720; frame++)
        {
            var pointer = frame is >= 90 and < 310
                ? new PointerObservation(
                    resetPosition + new Vector2(190f, 45f),
                    true)
                : default;
            var input = Input(
                frameDeltas[frame % frameDeltas.Length],
                Area,
                Safety,
                pointer: pointer,
                captureDebug: frame % 17 == 0);
            var expected = fresh.Advance(input);
            var actual = mutated.Advance(input);
            AssertEqual(expected.SimulationSteps, actual.SimulationSteps,
                $"full reset frame {frame} step count");
            AssertNear(expected.FrameDelta, actual.FrameDelta,
                $"full reset frame {frame} accepted delta");
            AssertEquivalentState(fresh, mutated, Vector2.Zero, $"full reset frame {frame}");
            AssertRenderEquivalent(
                expected.RenderFrame,
                actual.RenderFrame,
                $"full reset frame {frame} pose");
        }
    }

    private static void VerifyLegacyTelemetryLifecycle()
    {
        var session = new PetSimulationSession(0x51525, Profile);
        session.Reset(Vector2.Zero, Profile.Runtime.InitialHeading);
        for (var step = 0; step < 240 &&
             session.BehaviorForDiagnostics.State == RoamingState.Spawn; step++)
        {
            session.Advance(Input(Step, Area, Safety));
        }
        var playback = session.TryPlayDebugAction(
            AutonomousAction.Forward,
            Area,
            Safety);
        AssertTrue(playback.Accepted,
            "legacy telemetry lifecycle action was not accepted");

        DebugFrameSnapshot? populated = null;
        for (var frame = 0; frame < 120; frame++)
        {
            populated = session.Advance(Input(
                Step,
                Area,
                Safety,
                captureDebug: true)).DebugFrame;
        }
        AssertTrue(populated is not null && populated.TrailPoints.Length > 1,
            "legacy telemetry did not populate a diagnostic trail");

        session.ClearDebugPath();
        var afterClear = session.Advance(Input(
            0f,
            Area,
            Safety,
            captureDebug: true)).DebugFrame;
        AssertTrue(afterClear is not null && afterClear.TrailPoints.Length == 1,
            "legacy adapter ClearDebugPath did not restart the trail");

        session.Reset(Vector2.Zero, Profile.Runtime.InitialHeading);
        var afterReset = session.Advance(Input(
            0f,
            Area,
            Safety,
            captureDebug: true)).DebugFrame;
        AssertTrue(afterReset is not null &&
                   afterReset.FrameId == 1 &&
                   afterReset.TrailPoints.Length == 1,
            "legacy adapter did not reset telemetry with the gameplay generation");
    }

    private static (PetSimulationSession Baseline, PetSimulationSession Rebased)
        CreateReadyPair(int seed)
    {
        var baseline = new PetSimulationSession(seed, Profile);
        var rebased = new PetSimulationSession(seed, Profile);
        baseline.Reset(Vector2.Zero, Profile.Runtime.InitialHeading);
        rebased.Reset(Vector2.Zero, Profile.Runtime.InitialHeading);
        for (var step = 0; step < 240 &&
             baseline.BehaviorForDiagnostics.State == RoamingState.Spawn; step++)
        {
            AdvanceEquivalent(baseline, rebased, Vector2.Zero, 1);
        }
        AssertTrue(
            baseline.BehaviorForDiagnostics.State == RoamingState.Idle &&
            rebased.BehaviorForDiagnostics.State == RoamingState.Idle,
            "ready pair did not leave Spawn in the same Idle state");
        return (baseline, rebased);
    }

    private static void StartActionPair(
        PetSimulationSession baseline,
        PetSimulationSession rebased,
        AutonomousAction action)
    {
        var expected = baseline.TryPlayDebugAction(action, Area, Safety);
        var actual = rebased.TryPlayDebugAction(action, Area, Safety);
        AssertTrue(expected.Accepted && actual.Accepted,
            $"debug action {action} was not accepted");
        AssertEqual(expected.State, actual.State, $"debug action {action} state");
    }

    private static void AdvanceEquivalent(
        PetSimulationSession baseline,
        PetSimulationSession rebased,
        Vector2 rebaseDelta,
        int stepCount,
        PointerObservation pointer = default,
        bool captureDebug = false,
        bool verifyDebugTrail = false)
    {
        var shiftedArea = Translate(Area, rebaseDelta);
        var shiftedSafety = Translate(Safety, rebaseDelta);
        var shiftedPointer = Translate(pointer, rebaseDelta);
        for (var step = 0; step < stepCount; step++)
        {
            var expected = baseline.Advance(Input(
                Step,
                Area,
                Safety,
                pointer: pointer,
                captureDebug: captureDebug));
            var actual = rebased.Advance(Input(
                Step,
                shiftedArea,
                shiftedSafety,
                pointer: shiftedPointer,
                captureDebug: captureDebug));
            AssertEqual(expected.SimulationSteps, actual.SimulationSteps,
                $"rebase step {step} fixed-step count");
            AssertEquivalentState(
                baseline,
                rebased,
                rebaseDelta,
                $"rebase step {step}");
            AssertRenderEquivalent(
                expected.RenderFrame,
                actual.RenderFrame,
                $"rebase step {step} pose");
            if (verifyDebugTrail && captureDebug)
            {
                AssertDebugTrailEquivalent(
                    expected.DebugFrame,
                    actual.DebugFrame,
                    $"rebase step {step} debug trail");
            }
        }
    }

    private static PetSimulationFrameInput Input(
        float frameDelta,
        FloatRect area,
        LostGripSafetyContext safety,
        bool isDragging = false,
        PointerObservation pointer = default,
        bool captureDebug = false) => new(
            frameDelta,
            area,
            pointer,
            isDragging,
            Profile.Appearance.VisualScale)
        {
            LostGripSafety = safety,
            CaptureDebugFrame = captureDebug,
            DpiScale = 1f
        };

    private static void AssertEquivalentState(
        PetSimulationSession baseline,
        PetSimulationSession rebased,
        Vector2 delta,
        string context)
    {
        var expected = baseline.BehaviorForDiagnostics.CaptureDebugSnapshot(Area);
        var actual = rebased.BehaviorForDiagnostics.CaptureDebugSnapshot(
            Translate(Area, delta));
        foreach (var property in typeof(BehaviorDebugSnapshot).GetProperties(
                     BindingFlags.Instance | BindingFlags.Public))
        {
            var expectedValue = property.GetValue(expected);
            var actualValue = property.GetValue(actual);
            if (property.PropertyType == typeof(Vector2))
            {
                var expectedVector = (Vector2)expectedValue! + delta;
                var actualVector = (Vector2)actualValue!;
                AssertVector(expectedVector, actualVector, $"{context} {property.Name}");
            }
            else if (property.PropertyType == typeof(float))
            {
                AssertNear(
                    (float)expectedValue!,
                    (float)actualValue!,
                    $"{context} {property.Name}");
            }
            else if (!Equals(expectedValue, actualValue))
            {
                throw new InvalidOperationException(
                    $"{context} {property.Name}: expected {expectedValue}, actual {actualValue}.");
            }
        }
    }

    private static void AssertRenderEquivalent(
        in LizardRenderFrame expected,
        in LizardRenderFrame actual,
        string context)
    {
        AssertEqual(expected.BodyOutline.Length, actual.BodyOutline.Length,
            $"{context} body count");
        for (var index = 0; index < expected.BodyOutline.Length; index++)
        {
            AssertPoseVector(expected.BodyOutline[index], actual.BodyOutline[index],
                $"{context} body {index}");
        }
        AssertEqual(expected.Legs.Length, actual.Legs.Length, $"{context} leg count");
        for (var index = 0; index < expected.Legs.Length; index++)
        {
            AssertPoseVector(expected.Legs[index].Shoulder, actual.Legs[index].Shoulder,
                $"{context} shoulder {index}");
            AssertPoseVector(expected.Legs[index].Elbow, actual.Legs[index].Elbow,
                $"{context} elbow {index}");
            AssertPoseVector(expected.Legs[index].Foot, actual.Legs[index].Foot,
                $"{context} foot {index}");
            AssertPoseNear(expected.Legs[index].Lift, actual.Legs[index].Lift,
                $"{context} lift {index}");
        }
        AssertPoseVector(expected.HeadNose, actual.HeadNose, $"{context} nose");
        AssertPoseVector(expected.NegativeEyeCenter, actual.NegativeEyeCenter,
            $"{context} negative eye");
        AssertPoseVector(expected.PositiveEyeCenter, actual.PositiveEyeCenter,
            $"{context} positive eye");
        AssertPoseNear(expected.Heading, actual.Heading, $"{context} heading");
        AssertPoseNear(expected.BlinkAmount, actual.BlinkAmount, $"{context} blink");
    }

    private static void AssertDebugTrailEquivalent(
        DebugFrameSnapshot? expected,
        DebugFrameSnapshot? actual,
        string context)
    {
        AssertTrue(expected is not null && actual is not null,
            $"{context} snapshots were not captured");
        AssertEqual(expected!.TrailPoints.Length, actual!.TrailPoints.Length,
            $"{context} point count");
        for (var index = 0; index < expected.TrailPoints.Length; index++)
        {
            AssertPoseVector(expected.TrailPoints[index], actual.TrailPoints[index],
                $"{context} point {index}");
        }
    }

    private static FloatRect Translate(FloatRect value, Vector2 delta) => new(
        value.Left + delta.X,
        value.Top + delta.Y,
        value.Right + delta.X,
        value.Bottom + delta.Y);

    private static LostGripSafetyContext Translate(
        LostGripSafetyContext value,
        Vector2 delta) => new(Translate(value.SafeArea, delta), value.IsAvailable);

    private static PointerObservation Translate(
        PointerObservation value,
        Vector2 delta) => value.IsAvailable
        ? value with { Position = value.Position + delta }
        : value;

    private static void AssertVector(Vector2 expected, Vector2 actual, string context)
    {
        AssertNear(expected.X, actual.X, context + " X");
        AssertNear(expected.Y, actual.Y, context + " Y");
    }

    private static void AssertNear(float expected, float actual, string context)
    {
        if (!float.IsFinite(expected) ||
            !float.IsFinite(actual) ||
            MathF.Abs(expected - actual) > StateTolerance)
        {
            throw new InvalidOperationException(
                $"{context}: expected {expected:R}, actual {actual:R}.");
        }
    }

    private static void AssertPoseVector(
        Vector2 expected,
        Vector2 actual,
        string context)
    {
        AssertPoseNear(expected.X, actual.X, context + " X");
        AssertPoseNear(expected.Y, actual.Y, context + " Y");
    }

    private static void AssertPoseNear(float expected, float actual, string context)
    {
        if (!float.IsFinite(expected) ||
            !float.IsFinite(actual) ||
            MathF.Abs(expected - actual) > PoseTolerance)
        {
            throw new InvalidOperationException(
                $"{context}: expected {expected:R}, actual {actual:R}.");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string context)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{context}: expected {expected}, actual {actual}.");
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message + ".");
        }
    }
}

internal readonly record struct RebaseAndResetResult(bool Passed, string Detail)
{
    public override string ToString() => Detail;
}
