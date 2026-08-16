namespace DesktopLizard.Core;

/// <summary>
/// Owns the mutable phase of one multi-wave S-curve episode. Configuration
/// sampling remains in BehaviorController so the FSM's random stream keeps a
/// stable, reviewable order.
/// </summary>
internal sealed class SCurveMotionController
{
    public float Elapsed { get; private set; }
    public float Duration { get; private set; }
    public float CycleDuration { get; private set; }
    public int CycleCount { get; private set; }
    public float Amplitude { get; private set; }
    public float Direction { get; private set; } = 1f;

    public float Progress => Duration > 0.001f
        ? MathEx.Clamp01(Elapsed / Duration)
        : 0f;

    public float EasedCycleProgress
    {
        get
        {
            var progress = CycleDuration > 0.001f && CycleCount > 0
                ? MathEx.Clamp01(
                    (Elapsed - MathF.Floor(Elapsed / CycleDuration) * CycleDuration) /
                    CycleDuration)
                : 0f;
            return progress * progress * (3f - 2f * progress);
        }
    }

    public void Begin(
        int cycleCount,
        float cycleDuration,
        float amplitude,
        float direction,
        float settleDuration)
    {
        CycleCount = cycleCount;
        CycleDuration = cycleDuration;
        Duration = cycleDuration * cycleCount + settleDuration;
        Amplitude = amplitude;
        Direction = direction;
        Elapsed = 0f;
    }

    public float Advance(float dt)
    {
        Elapsed = Math.Min(Duration, Elapsed + dt);
        return SCurveTrajectory.ComputeTurnVelocity(
            Elapsed,
            CycleDuration,
            CycleCount,
            Amplitude,
            Direction);
    }

    public void Reset()
    {
        Elapsed = 0f;
        Duration = 0f;
        CycleDuration = 0f;
        CycleCount = 0;
        Amplitude = 0f;
        Direction = 1f;
    }
}
