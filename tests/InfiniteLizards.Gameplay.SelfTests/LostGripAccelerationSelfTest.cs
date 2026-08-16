using System.Text.Json.Nodes;
using DesktopLizard.Core;

namespace DesktopLizard.Diagnostics;

internal readonly record struct LostGripAccelerationTestResult(
    bool Passed,
    bool ConfigurationPassed,
    bool SchemaMigrationPassed,
    bool AccelerationCurvePassed,
    bool FixedStepRateInvariancePassed,
    bool SafetyAndReachPassed,
    float InitialAcceleration,
    float FinalAcceleration,
    float MaximumObservedVelocity,
    float RatePositionError,
    string FailureDetails);

internal static class LostGripAccelerationSelfTest
{
    public static LostGripAccelerationTestResult Run()
    {
        var failures = new List<string>();
        var fall = LizardConfiguration.Default.Behavior.LostGripFall;

        var configurationPassed =
            LizardConfiguration.Default.Validate().Count == 0 &&
            fall.AccelerationRampDuration > 0f &&
            fall.InitialAccelerationRatio is > 0f and < 1f &&
            Invalid(fall with { AccelerationRampDuration = -0.001f }) &&
            Invalid(fall with { AccelerationRampDuration = float.NaN }) &&
            Invalid(fall with { InitialAccelerationRatio = -0.001f }) &&
            Invalid(fall with { InitialAccelerationRatio = 1.001f }) &&
            Invalid(fall with { InitialAccelerationRatio = float.NaN }) &&
            Valid(fall with { AccelerationRampDuration = 0f }) &&
            Valid(fall with { InitialAccelerationRatio = 0f }) &&
            Valid(fall with { InitialAccelerationRatio = 1f });
        Record(configurationPassed, "configuration validation", failures);

        var schemaMigrationPassed = VerifySchemaSixMigration(out var migrationDetail);
        Record(schemaMigrationPassed, migrationDetail, failures);

        var initialAcceleration = LostGripFallMotion.AccelerationAtTime(fall, 0f);
        var middleAcceleration = LostGripFallMotion.AccelerationAtTime(
            fall,
            fall.AccelerationRampDuration * 0.5f);
        var finalAcceleration = LostGripFallMotion.AccelerationAtTime(
            fall,
            fall.AccelerationRampDuration);
        var expectedInitialAcceleration = fall.Gravity * fall.InitialAccelerationRatio;
        var expectedMiddleAcceleration = fall.Gravity *
            MathEx.Lerp(fall.InitialAccelerationRatio, 1f, 0.5f);
        var curveMaximumVelocity = 0f;
        var accelerationCurvePassed =
            NearlyEqual(initialAcceleration, expectedInitialAcceleration, 0.001f) &&
            NearlyEqual(middleAcceleration, expectedMiddleAcceleration, 0.001f) &&
            NearlyEqual(finalAcceleration, fall.Gravity, 0.001f) &&
            initialAcceleration < middleAcceleration &&
            middleAcceleration < finalAcceleration &&
            VerifyMonotonicVelocityCurve(fall, out curveMaximumVelocity);
        Record(accelerationCurvePassed, "smooth acceleration curve and terminal velocity", failures);

        var sample60 = SimulateForDuration(fall, 1f / 60f, 0.5f);
        var sample120 = SimulateForDuration(fall, 1f / 120f, 0.5f);
        var sample240 = SimulateForDuration(fall, 1f / 240f, 0.5f);
        var ratePositionError = Math.Max(
            MathF.Abs(sample60.Distance - sample120.Distance),
            MathF.Abs(sample240.Distance - sample120.Distance));
        var fixedStepRateInvariancePassed =
            MathF.Abs(sample60.Velocity - sample120.Velocity) <= 0.001f &&
            MathF.Abs(sample240.Velocity - sample120.Velocity) <= 0.001f &&
            ratePositionError <= 0.08f;
        Record(fixedStepRateInvariancePassed, "60/120/240 Hz trajectory invariance", failures);

        var safetyAndReachPassed = VerifyBehaviorSafetyAndReach(
            out var maximumObservedVelocity,
            out var behaviorDetail);
        Record(safetyAndReachPassed, behaviorDetail, failures);

        return new LostGripAccelerationTestResult(
            failures.Count == 0,
            configurationPassed,
            schemaMigrationPassed,
            accelerationCurvePassed,
            fixedStepRateInvariancePassed,
            safetyAndReachPassed,
            initialAcceleration,
            finalAcceleration,
            Math.Max(curveMaximumVelocity, maximumObservedVelocity),
            ratePositionError,
            string.Join("; ", failures));
    }

    private static bool VerifySchemaSixMigration(out string detail)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "InfiniteLizards.LostGripAccelerationSelfTest",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "schema6.json");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(
                path,
                "{\"SchemaVersion\":6,\"IndividualSeed\":7419," +
                "\"Behavior\":{\"LostGripFall\":{" +
                "\"MinimumInitialVelocity\":7,\"MaximumInitialVelocity\":19," +
                "\"Gravity\":477,\"MaximumFallVelocity\":299}}}");
            var result = LizardConfigurationStore.LoadOrCreate(path);
            var storedFall = JsonNode.Parse(File.ReadAllText(path))!
                ["Behavior"]!["LostGripFall"]!.AsObject();
            var migrated = result.Configuration.Behavior.LostGripFall;
            var passed =
                result.LoadedFromDisk &&
                result.Warnings.Count == 1 &&
                result.Configuration.SchemaVersion ==
                    LizardConfiguration.CurrentSchemaVersion &&
                NearlyEqual(migrated.MinimumInitialVelocity, 7f) &&
                NearlyEqual(migrated.MaximumInitialVelocity, 19f) &&
                NearlyEqual(migrated.Gravity, 477f) &&
                NearlyEqual(migrated.MaximumFallVelocity, 299f) &&
                NearlyEqual(
                    migrated.AccelerationRampDuration,
                    LostGripFallConfiguration.DefaultAccelerationRampDuration) &&
                NearlyEqual(
                    migrated.InitialAccelerationRatio,
                    LostGripFallConfiguration.DefaultInitialAccelerationRatio) &&
                storedFall.ContainsKey("AccelerationRampDuration") &&
                storedFall.ContainsKey("InitialAccelerationRatio");
            detail = passed
                ? "schema 6 acceleration migration"
                : $"schema 6 migration (loaded={result.LoadedFromDisk}, " +
                  $"warnings={result.Warnings.Count}, version={result.Configuration.SchemaVersion})";
            return passed;
        }
        catch (Exception exception)
        {
            detail = $"schema 6 migration threw {exception.GetType().Name}: {exception.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // A cleanup failure cannot change the simulated motion result.
            }
        }
    }

    private static bool VerifyMonotonicVelocityCurve(
        LostGripFallConfiguration fall,
        out float maximumVelocity)
    {
        const float dt = 1f / 120f;
        var elapsed = 0f;
        var velocity = (fall.MinimumInitialVelocity + fall.MaximumInitialVelocity) * 0.5f;
        var distance = 0f;
        var previousGain = 0f;
        var sawRampGainGrowth = false;
        var reachedTerminal = false;
        maximumVelocity = velocity;

        for (var step = 0; step < 480 && distance < fall.MinimumDistance; step++)
        {
            var nextElapsed = elapsed + dt;
            var nextVelocity = LostGripFallMotion.AdvanceVelocity(
                fall,
                velocity,
                elapsed,
                nextElapsed);
            var gain = nextVelocity - velocity;
            if (nextVelocity + 0.0001f < velocity ||
                nextVelocity > fall.MaximumFallVelocity + 0.0001f)
            {
                return false;
            }
            if (elapsed < fall.AccelerationRampDuration - dt &&
                previousGain > 0f)
            {
                if (gain + 0.0001f < previousGain)
                {
                    return false;
                }
                sawRampGainGrowth |= gain > previousGain + 0.0001f;
            }

            distance += (velocity + nextVelocity) * 0.5f * dt;
            elapsed = nextElapsed;
            velocity = nextVelocity;
            previousGain = gain;
            maximumVelocity = Math.Max(maximumVelocity, velocity);
            reachedTerminal |= NearlyEqual(velocity, fall.MaximumFallVelocity, 0.001f);
        }

        return sawRampGainGrowth &&
               reachedTerminal &&
               distance >= fall.MinimumDistance &&
               maximumVelocity <= fall.MaximumFallVelocity + 0.001f;
    }

    private static (float Velocity, float Distance) SimulateForDuration(
        LostGripFallConfiguration fall,
        float dt,
        float duration)
    {
        var velocity = (fall.MinimumInitialVelocity + fall.MaximumInitialVelocity) * 0.5f;
        var distance = 0f;
        var elapsed = 0f;
        var steps = (int)MathF.Round(duration / dt);
        for (var step = 0; step < steps; step++)
        {
            var nextElapsed = elapsed + dt;
            var nextVelocity = LostGripFallMotion.AdvanceVelocity(
                fall,
                velocity,
                elapsed,
                nextElapsed);
            distance += (velocity + nextVelocity) * 0.5f * dt;
            elapsed = nextElapsed;
            velocity = nextVelocity;
        }
        return (velocity, distance);
    }

    private static bool VerifyBehaviorSafetyAndReach(
        out float maximumObservedVelocity,
        out string detail)
    {
        const float dt = 1f / 120f;
        var configuration = LizardConfiguration.Default;
        var profile = IndividualProfileFactory.Create(
            configuration,
            0x7419,
            applyVariation: false);
        var fall = profile.Behavior.LostGripFall;
        var navigationArea = new FloatRect(0f, 0f, 2000f, 2000f);
        var start = new System.Numerics.Vector2(1000f, 300f);
        var safety = new LostGripSafetyContext(
            new FloatRect(
                100f,
                100f,
                1900f,
                start.Y + fall.MinimumDistance + 2f),
            IsAvailable: true);
        var behavior = new BehaviorController(0x7419, profile);
        behavior.Reset(start, 0f);
        for (var step = 0; step < 20 && behavior.State != RoamingState.Idle; step++)
        {
            behavior.Update(0.05f, navigationArea, safety);
        }
        if (behavior.State != RoamingState.Idle)
        {
            maximumObservedVelocity = 0f;
            detail = "behavior did not reach Idle before debug playback";
            return false;
        }

        var playback = behavior.TryPlayDebugAction(
            AutonomousAction.LostGripFall,
            navigationArea,
            safety);
        if (!playback.Accepted || behavior.LostGripPhase != LostGripFallPhase.Falling)
        {
            maximumObservedVelocity = 0f;
            detail = $"lost-grip playback was not accepted ({playback.Status})";
            return false;
        }

        var previousY = behavior.Position.Y;
        var previousVelocity = behavior.LostGripVerticalVelocity;
        var firstVelocity = previousVelocity;
        var laterVelocity = previousVelocity;
        var fallingSteps = 0;
        var reachMovingSteps = 0;
        var reachedTerminal = false;
        var reachedCatch = false;
        maximumObservedVelocity = previousVelocity;
        for (var step = 0; step < 480; step++)
        {
            behavior.Update(dt, navigationArea, safety);
            if (behavior.Position.Y + 0.0001f < previousY ||
                behavior.Position.Y > safety.SafeArea.Bottom + 0.0001f)
            {
                detail = "behavior violated monotonic or bottom-safe motion";
                return false;
            }

            if (behavior.LostGripPhase == LostGripFallPhase.Falling)
            {
                fallingSteps++;
                if (behavior.LostGripVerticalVelocity + 0.0001f < previousVelocity)
                {
                    detail = "behavior velocity decreased during free fall";
                    return false;
                }
                if (fallingSteps == 1)
                {
                    firstVelocity = behavior.LostGripVerticalVelocity;
                }
                if (fallingSteps == 24)
                {
                    laterVelocity = behavior.LostGripVerticalVelocity;
                }
                if (behavior.LostGripReachProgress > 0f &&
                    behavior.Position.Y > previousY + 0.0001f)
                {
                    reachMovingSteps++;
                }
                maximumObservedVelocity = Math.Max(
                    maximumObservedVelocity,
                    behavior.LostGripVerticalVelocity);
                reachedTerminal |= NearlyEqual(
                    behavior.LostGripVerticalVelocity,
                    fall.MaximumFallVelocity,
                    0.001f);
                previousVelocity = behavior.LostGripVerticalVelocity;
            }
            else if (behavior.LostGripPhase == LostGripFallPhase.Regripping)
            {
                reachedCatch = true;
                break;
            }
            previousY = behavior.Position.Y;
        }

        var passed =
            fallingSteps > 24 &&
            laterVelocity > firstVelocity + 20f &&
            reachedTerminal &&
            reachedCatch &&
            reachMovingSteps >= 3 &&
            NearlyEqual(behavior.Position.Y, behavior.LostGripTargetDistance + start.Y, 0.01f) &&
            behavior.Position.Y <= safety.SafeArea.Bottom + 0.0001f;
        detail = passed
            ? "accelerating behavior remains reach-compatible and bottom-safe"
            : $"behavior acceleration (steps={fallingSteps}, first={firstVelocity:F2}, " +
              $"later={laterVelocity:F2}, max={maximumObservedVelocity:F2}, " +
              $"terminal={reachedTerminal}, catch={reachedCatch}, reachSteps={reachMovingSteps})";
        return passed;
    }

    private static bool Invalid(LostGripFallConfiguration fall) => !Valid(fall);

    private static bool Valid(LostGripFallConfiguration fall)
    {
        var failures = new List<string>();
        fall.Validate(failures);
        return failures.Count == 0;
    }

    private static bool NearlyEqual(float left, float right, float tolerance = 0.0001f) =>
        MathF.Abs(left - right) <= tolerance;

    private static void Record(bool passed, string name, List<string> failures)
    {
        if (!passed)
        {
            failures.Add(name);
        }
    }
}
