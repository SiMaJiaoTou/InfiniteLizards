namespace DesktopLizard.Core;

internal static class SCurveTrajectory
{
    public static float ComputeTurnVelocity(
        float elapsed,
        float cycleDuration,
        int cycleCount,
        float amplitude,
        float direction)
    {
        if (!float.IsFinite(elapsed) ||
            !float.IsFinite(cycleDuration) ||
            cycleDuration <= 0f ||
            cycleCount <= 0 ||
            elapsed < 0f)
        {
            return 0f;
        }

        var waveDuration = cycleDuration * cycleCount;
        if (elapsed >= waveDuration)
        {
            // The remaining settle duration is a straightening tail that lets
            // the filtered angular velocity decay before the next state.
            return 0f;
        }

        var scaledCycle = elapsed / cycleDuration;
        var cycleIndex = Math.Min((int)MathF.Floor(scaledCycle), cycleCount - 1);
        var localProgress = MathEx.Clamp01(scaledCycle - cycleIndex);
        var easedProgress = localProgress * localProgress * (3f - 2f * localProgress);
        var cycleDirection = (cycleIndex & 1) == 0 ? direction : -direction;
        return cycleDirection * amplitude * MathF.Sin(MathEx.TwoPi * easedProgress);
    }
}
