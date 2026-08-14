namespace DesktopLizard.Core;

internal sealed record PhysicsConfiguration
{
    public float Gravity { get; init; } = 820f;
    public float LinearDrag { get; init; } = 2.35f;
    public int ConstraintIterations { get; init; } = 24;
    public float ConstraintVelocityResponseRate { get; init; } = 65.35f;
    public float MaximumSpeed { get; init; } = 820f;
    public float TeleportThreshold { get; init; } = 180f;
    public float UpperLimbAngularSpring { get; init; } = 0.72f;
    public float LowerLimbAngularSpring { get; init; } = 0.96f;
    public float AngularSpringDelay { get; init; } = 0.04f;
    public float AngularSpringRampDuration { get; init; } = 0.24f;
    public float UpperLimbHorizontalBias { get; init; } = 0.22f;
    public float LowerLimbHorizontalBias { get; init; } = 0.12f;
    public float SpineSkipLengthFactor { get; init; } = 0.9765625f;
    public float SpineSkipStiffness { get; init; } = 0.22f;
    public float ShoulderAttachmentRadiusFactor { get; init; } = 0.62f;
    public float ReleasePoseRecoveryDuration { get; init; } = 0.24f;
    public float RegripFrontReachLengthFactor { get; init; } = 0.82f;
    public float RegripFrontReachOutwardWeight { get; init; } = 0.32f;
    public float RegripContactHoldFraction { get; init; } = 0.28f;
    public float MinimumBoneLength { get; init; } = 1f;
    public float MinimumSimulationStep { get; init; } = 0.0001f;
    public float MaximumSimulationStep { get; init; } = 1f / 60f;
    public float FallbackSimulationStep { get; init; } = 1f / 120f;
    public float MaximumConstraintError { get; init; } = 5f;
    public float MaximumGrabError { get; init; } = 1.5f;
    public float MaximumCoordinateCanvasFactor { get; init; } = 3f;

    internal void Validate(List<string> failures)
    {
        BehaviorConfiguration.RequireNonNegative(Gravity, "dangling gravity", failures);
        BehaviorConfiguration.RequireNonNegative(LinearDrag, "dangling linear drag", failures);
        if (ConstraintIterations is < 1 or > 128)
        {
            failures.Add("dangling constraint iterations must be in [1,128].");
        }
        BehaviorConfiguration.RequirePositive(ConstraintVelocityResponseRate, "constraint velocity response", failures);
        BehaviorConfiguration.RequirePositive(MaximumSpeed, "dangling maximum speed", failures);
        BehaviorConfiguration.RequirePositive(TeleportThreshold, "dangling teleport threshold", failures);
        BehaviorConfiguration.RequireNonNegative(UpperLimbAngularSpring, "upper-limb angular spring", failures);
        BehaviorConfiguration.RequireNonNegative(LowerLimbAngularSpring, "lower-limb angular spring", failures);
        BehaviorConfiguration.RequireNonNegative(AngularSpringDelay, "angular-spring delay", failures);
        BehaviorConfiguration.RequirePositive(AngularSpringRampDuration, "angular-spring ramp duration", failures);
        BehaviorConfiguration.RequireNonNegative(UpperLimbHorizontalBias, "upper-limb horizontal bias", failures);
        BehaviorConfiguration.RequireNonNegative(LowerLimbHorizontalBias, "lower-limb horizontal bias", failures);
        BehaviorConfiguration.RequirePositive(SpineSkipLengthFactor, "spine skip-length factor", failures);
        if (!float.IsFinite(SpineSkipStiffness) || SpineSkipStiffness is < 0f or > 1f)
        {
            failures.Add("spine skip stiffness must be in [0,1].");
        }
        if (!float.IsFinite(ShoulderAttachmentRadiusFactor) || ShoulderAttachmentRadiusFactor is < 0f or > 1f)
        {
            failures.Add("shoulder attachment radius factor must be in [0,1].");
        }
        BehaviorConfiguration.RequirePositive(ReleasePoseRecoveryDuration, "release-pose recovery duration", failures);
        RequireUnitInterval(
            RegripFrontReachLengthFactor,
            "regrip front-reach length factor",
            failures);
        RequireUnitInterval(
            RegripFrontReachOutwardWeight,
            "regrip front-reach outward weight",
            failures);
        if (!float.IsFinite(RegripContactHoldFraction) ||
            RegripContactHoldFraction is < 0f or > 0.8f)
        {
            failures.Add("regrip contact-hold fraction must be in [0,0.8].");
        }
        BehaviorConfiguration.RequirePositive(MinimumBoneLength, "minimum bone length", failures);
        BehaviorConfiguration.RequirePositive(MinimumSimulationStep, "minimum physics step", failures);
        BehaviorConfiguration.RequirePositive(MaximumSimulationStep, "maximum physics step", failures);
        BehaviorConfiguration.RequirePositive(FallbackSimulationStep, "fallback physics step", failures);
        if (MaximumSimulationStep < MinimumSimulationStep)
        {
            failures.Add("physics simulation-step range is inverted.");
        }
        if (float.IsFinite(FallbackSimulationStep) &&
            float.IsFinite(MinimumSimulationStep) &&
            float.IsFinite(MaximumSimulationStep) &&
            (FallbackSimulationStep < MinimumSimulationStep ||
             FallbackSimulationStep > MaximumSimulationStep))
        {
            failures.Add("fallback physics step must be inside the configured simulation-step range.");
        }
        BehaviorConfiguration.RequirePositive(MaximumConstraintError, "maximum constraint error", failures);
        BehaviorConfiguration.RequirePositive(MaximumGrabError, "maximum grab error", failures);
        BehaviorConfiguration.RequirePositive(
            MaximumCoordinateCanvasFactor,
            "maximum coordinate canvas factor",
            failures);
    }

    private static void RequireUnitInterval(
        float value,
        string name,
        List<string> failures)
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
        {
            failures.Add($"{name} must be in [0,1].");
        }
    }
}
