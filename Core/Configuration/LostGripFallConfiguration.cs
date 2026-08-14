namespace DesktopLizard.Core;

/// <summary>
/// Controls the rare autonomous loss-of-grip event. Distances and velocities
/// use screen coordinates, where positive Y points down.
/// </summary>
internal sealed record LostGripFallConfiguration
{
    internal const float DefaultMinimumDistance = 150f;
    internal const float LegacyDefaultMinimumDistance = 78f;

    /// <summary>
    /// Minimum visible drop before the pet may regrip. The upper endpoint is
    /// deliberately not configured: it is the full-render-safe bottom of the
    /// current work area at the instant the event begins.
    /// </summary>
    public float MinimumDistance { get; init; } = DefaultMinimumDistance;
    /// <summary>
    /// Distance before the catch point over which the free-fall pose prepares
    /// its reach. Runtime clamps this to the sampled fall distance, so older
    /// profiles with a shorter custom minimum remain valid.
    /// </summary>
    public float ReachLeadDistance { get; init; } = 56f;
    public float MinimumInitialVelocity { get; init; } = 6f;
    public float MaximumInitialVelocity { get; init; } = 18f;
    public float Gravity { get; init; } = 520f;
    public float MaximumFallVelocity { get; init; } = 310f;
    public float BottomSafetyInset { get; init; } = 70f;
    public float RegripDuration { get; init; } = 0.34f;
    public float ResumeIdleDuration { get; init; } = 0.18f;
    public float PointerSuppressionDuration { get; init; } = 1.2f;

    internal void Validate(List<string> failures)
    {
        BehaviorConfiguration.RequirePositive(
            MinimumDistance,
            "lost-grip minimum fall distance",
            failures);
        BehaviorConfiguration.RequirePositive(
            ReachLeadDistance,
            "lost-grip reach lead distance",
            failures);
        BehaviorConfiguration.ValidateRange(
            MinimumInitialVelocity,
            MaximumInitialVelocity,
            0f,
            "lost-grip initial velocity",
            failures);
        BehaviorConfiguration.RequirePositive(Gravity, "lost-grip gravity", failures);
        BehaviorConfiguration.RequirePositive(
            MaximumFallVelocity,
            "lost-grip maximum fall velocity",
            failures);
        if (float.IsFinite(MaximumInitialVelocity) &&
            float.IsFinite(MaximumFallVelocity) &&
            MaximumFallVelocity < MaximumInitialVelocity)
        {
            failures.Add(
                "lost-grip maximum fall velocity must be >= maximum initial velocity.");
        }
        BehaviorConfiguration.RequireNonNegative(
            BottomSafetyInset,
            "lost-grip bottom safety inset",
            failures);
        BehaviorConfiguration.RequirePositive(
            RegripDuration,
            "lost-grip regrip duration",
            failures);
        BehaviorConfiguration.RequirePositive(
            ResumeIdleDuration,
            "lost-grip resume-idle duration",
            failures);
        BehaviorConfiguration.RequireNonNegative(
            PointerSuppressionDuration,
            "lost-grip pointer suppression duration",
            failures);
    }
}
