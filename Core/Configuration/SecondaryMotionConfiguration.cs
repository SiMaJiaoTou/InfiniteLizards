namespace DesktopLizard.Core;

/// <summary>
/// Render-only life, heading response and facial timing. These values do not
/// affect the roaming FSM or planted-foot world positions.
/// </summary>
internal sealed record SecondaryMotionConfiguration
{
    public int RandomSeed { get; init; } = 41821;
    public float WalkingSpeedThreshold { get; init; } = 0.06f;
    public float GrabbedHeadingRate { get; init; } = 3.2f;
    public float MinimumHeadingRate { get; init; } = 2.5f;
    public float MaximumHeadingRate { get; init; } = 5f;
    public float BodyBobMinimumFrequency { get; init; } = 5.2f;
    public float BodyBobMaximumFrequency { get; init; } = 10.5f;
    public float BodyBobBaseAmplitude { get; init; } = 0.8f;
    public float BodyBobSpeedAmplitude { get; init; } = 1.6f;
    public float LandingBobAmplitude { get; init; } = 5f;
    public float IdleBlendInResponse { get; init; } = 4.5f;
    public float IdleBlendOutResponse { get; init; } = 7f;
    public float IdleTailAmplitude { get; init; } = 1.35f;
    public float ObserveTailAmplitude { get; init; } = 1.8f;
    public float TailPrimaryFrequency { get; init; } = 2.15f;
    public float TailSecondaryFrequency { get; init; } = 1.07f;
    public float TailSecondaryWeight { get; init; } = 0.16f;
    public float TailSecondaryPhase { get; init; } = 1.2f;
    public float BreathingFrequency { get; init; } = 2.05f;
    public float BreathingPhase { get; init; } = 0.45f;
    public float BreathingAmplitude { get; init; } = 0.018f;
    public int BreathingChestEndIndex { get; init; } = 6;
    public float BreathingChestSpan { get; init; } = 8f;
    public float LandingSquashAmplitude { get; init; } = 0.03f;
    public float TailStartBodyIndex { get; init; } = 5f;
    public float EyeForwardOffset { get; init; } = 7f;
    public float EyeLateralOffset { get; init; } = 13f;
    public float EyeBobFactor { get; init; } = 0.12f;
    public float NoseForwardOffset { get; init; } = 11f;
    public float BlinkInitialDelay { get; init; } = 2.2f;
    public float BlinkDuration { get; init; } = 0.16f;
    public float BlinkMinimumInterval { get; init; } = 2f;
    public float BlinkMaximumInterval { get; init; } = 4f;
    public float BlinkMinimumCalmFactor { get; init; } = 0.88f;
    public float BlinkMaximumCalmFactor { get; init; } = 1.10f;

    internal void Validate(List<string> failures)
    {
        BehaviorConfiguration.RequireNonNegative(WalkingSpeedThreshold, nameof(WalkingSpeedThreshold), failures);
        BehaviorConfiguration.RequirePositive(GrabbedHeadingRate, nameof(GrabbedHeadingRate), failures);
        BehaviorConfiguration.RequirePositive(MinimumHeadingRate, nameof(MinimumHeadingRate), failures);
        BehaviorConfiguration.RequirePositive(MaximumHeadingRate, nameof(MaximumHeadingRate), failures);
        if (MaximumHeadingRate < MinimumHeadingRate)
        {
            failures.Add("secondary-motion heading-rate range is inverted.");
        }
        RequireRange(BodyBobMinimumFrequency, BodyBobMaximumFrequency, "body-bob frequency", failures);
        BehaviorConfiguration.RequireNonNegative(BodyBobBaseAmplitude, nameof(BodyBobBaseAmplitude), failures);
        BehaviorConfiguration.RequireNonNegative(BodyBobSpeedAmplitude, nameof(BodyBobSpeedAmplitude), failures);
        BehaviorConfiguration.RequireNonNegative(LandingBobAmplitude, nameof(LandingBobAmplitude), failures);
        BehaviorConfiguration.RequirePositive(IdleBlendInResponse, nameof(IdleBlendInResponse), failures);
        BehaviorConfiguration.RequirePositive(IdleBlendOutResponse, nameof(IdleBlendOutResponse), failures);
        BehaviorConfiguration.RequireNonNegative(IdleTailAmplitude, nameof(IdleTailAmplitude), failures);
        BehaviorConfiguration.RequireNonNegative(ObserveTailAmplitude, nameof(ObserveTailAmplitude), failures);
        BehaviorConfiguration.RequirePositive(TailPrimaryFrequency, nameof(TailPrimaryFrequency), failures);
        BehaviorConfiguration.RequirePositive(TailSecondaryFrequency, nameof(TailSecondaryFrequency), failures);
        BehaviorConfiguration.RequireNonNegative(TailSecondaryWeight, nameof(TailSecondaryWeight), failures);
        BehaviorConfiguration.RequirePositive(BreathingFrequency, nameof(BreathingFrequency), failures);
        BehaviorConfiguration.RequireNonNegative(BreathingAmplitude, nameof(BreathingAmplitude), failures);
        if (BreathingChestEndIndex < 0)
        {
            failures.Add("breathing chest end index must be non-negative.");
        }
        BehaviorConfiguration.RequirePositive(BreathingChestSpan, nameof(BreathingChestSpan), failures);
        BehaviorConfiguration.RequireNonNegative(LandingSquashAmplitude, nameof(LandingSquashAmplitude), failures);
        BehaviorConfiguration.RequireNonNegative(TailStartBodyIndex, nameof(TailStartBodyIndex), failures);
        BehaviorConfiguration.RequirePositive(EyeLateralOffset, nameof(EyeLateralOffset), failures);
        BehaviorConfiguration.RequireNonNegative(EyeBobFactor, nameof(EyeBobFactor), failures);
        BehaviorConfiguration.RequirePositive(NoseForwardOffset, nameof(NoseForwardOffset), failures);
        BehaviorConfiguration.RequireNonNegative(BlinkInitialDelay, nameof(BlinkInitialDelay), failures);
        BehaviorConfiguration.RequirePositive(BlinkDuration, nameof(BlinkDuration), failures);
        RequireRange(BlinkMinimumInterval, BlinkMaximumInterval, "blink interval", failures);
        RequireRange(BlinkMinimumCalmFactor, BlinkMaximumCalmFactor, "blink calm factor", failures);
    }

    private static void RequireRange(float minimum, float maximum, string name, List<string> failures)
    {
        BehaviorConfiguration.RequirePositive(minimum, $"{name} minimum", failures);
        BehaviorConfiguration.RequirePositive(maximum, $"{name} maximum", failures);
        if (maximum < minimum)
        {
            failures.Add($"{name} range is inverted.");
        }
    }
}
