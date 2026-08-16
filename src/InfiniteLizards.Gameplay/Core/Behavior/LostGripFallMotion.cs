namespace DesktopLizard.Core;

/// <summary>
/// Deterministic velocity curve for the autonomous lost-grip fall. The
/// acceleration integral is evaluated analytically so changing render cadence
/// cannot change the velocity reached by a fixed-step simulation timeline.
/// </summary>
internal static class LostGripFallMotion
{
    public static float AccelerationAtTime(
        LostGripFallConfiguration configuration,
        float elapsed)
    {
        if (configuration.AccelerationRampDuration <= 0f)
        {
            return configuration.Gravity;
        }

        var progress = MathEx.Clamp01(
            Math.Max(0f, elapsed) / configuration.AccelerationRampDuration);
        var easedProgress = progress * progress * (3f - 2f * progress);
        return configuration.Gravity * MathEx.Lerp(
            configuration.InitialAccelerationRatio,
            1f,
            easedProgress);
    }

    public static float AdvanceVelocity(
        LostGripFallConfiguration configuration,
        float previousVelocity,
        float previousElapsed,
        float nextElapsed)
    {
        var velocityGain =
            IntegratedAcceleration(configuration, nextElapsed) -
            IntegratedAcceleration(configuration, previousElapsed);
        return Math.Min(
            configuration.MaximumFallVelocity,
            previousVelocity + Math.Max(0f, velocityGain));
    }

    internal static float IntegratedAcceleration(
        LostGripFallConfiguration configuration,
        float elapsed)
    {
        elapsed = Math.Max(0f, elapsed);
        var rampDuration = configuration.AccelerationRampDuration;
        if (rampDuration <= 0f)
        {
            return configuration.Gravity * elapsed;
        }

        var rampElapsed = Math.Min(elapsed, rampDuration);
        var progress = rampElapsed / rampDuration;
        var progressSquared = progress * progress;
        var progressCubed = progressSquared * progress;
        var progressFourth = progressCubed * progress;
        var initialRatio = configuration.InitialAccelerationRatio;
        var rampIntegral = configuration.Gravity * rampDuration *
            (initialRatio * progress +
             (1f - initialRatio) * (progressCubed - 0.5f * progressFourth));
        return rampIntegral +
               configuration.Gravity * Math.Max(0f, elapsed - rampDuration);
    }
}
