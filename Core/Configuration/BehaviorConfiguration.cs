namespace DesktopLizard.Core;

internal sealed record BehaviorConfiguration
{
    public SpeedConfiguration Speed { get; init; } = new();
    public PointerChaseConfiguration Pointer { get; init; } = new();
    public SCurveConfiguration SCurve { get; init; } = new();
    public FastForwardConfiguration FastForward { get; init; } = new();
    public RestConfiguration Rest { get; init; } = new();
    public BehaviorTimingConfiguration Timing { get; init; } = new();
    public LocomotionConfiguration Locomotion { get; init; } = new();
    public BoundaryNavigationConfiguration Boundary { get; init; } = new();
    public EscapeSprintConfiguration EscapeSprint { get; init; } = new();
    public LostGripFallConfiguration LostGripFall { get; init; } = new();
    public EmotionConfiguration Emotion { get; init; } = new();
    public DecisionConfiguration Decisions { get; init; } = new();
    public TransitionMatrixConfiguration TransitionMatrix { get; init; } =
        TransitionMatrixConfiguration.CreateDefault();

    public float WalkBoutMinimumDuration { get; init; } = 10.5f;
    public float WalkBoutMaximumDuration { get; init; } = 14.5f;
    public float EdgeRecoveryBoutMinimumDuration { get; init; } = 8.5f;
    public float EdgeRecoveryBoutMaximumDuration { get; init; } = 12.5f;
    public float MotionWatchdogInterval { get; init; } = 0.50f;
    public float MotionWatchdogMinimumDistance { get; init; } = 0.80f;
    public float MotionWatchdogMinimumSpeed { get; init; } = 4f;

    internal void Validate(List<string> failures)
    {
        Speed.Validate(failures);
        Pointer.Validate(failures);
        SCurve.Validate(failures);
        FastForward.Validate(failures);
        Rest.Validate(failures);
        Timing.Validate(failures);
        Locomotion.Validate(failures);
        Boundary.Validate(failures);
        EscapeSprint.Validate(failures);
        LostGripFall.Validate(failures);
        Emotion.Validate(failures);
        Decisions.Validate(failures);
        TransitionMatrix.Validate(failures);
        ValidateRange(WalkBoutMinimumDuration, WalkBoutMaximumDuration, 0.05f, "behavior walk-bout duration", failures);
        ValidateRange(EdgeRecoveryBoutMinimumDuration, EdgeRecoveryBoutMaximumDuration, 0.05f, "edge recovery duration", failures);
        RequirePositive(MotionWatchdogInterval, "motion watchdog interval", failures);
        RequireNonNegative(MotionWatchdogMinimumDistance, "motion watchdog minimum distance", failures);
        RequireNonNegative(MotionWatchdogMinimumSpeed, "motion watchdog minimum speed", failures);
    }

    internal static void ValidateRange(float minimum, float maximum, float floor, string name, List<string> failures)
    {
        if (!float.IsFinite(minimum) || !float.IsFinite(maximum) || minimum < floor || maximum < minimum)
        {
            failures.Add($"Invalid {name}: [{minimum}, {maximum}].");
        }
    }

    internal static void RequirePositive(float value, string name, List<string> failures)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            failures.Add($"{name} must be finite and > 0 (actual {value}).");
        }
    }

    internal static void RequireNonNegative(float value, string name, List<string> failures)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            failures.Add($"{name} must be finite and >= 0 (actual {value}).");
        }
    }
}

internal sealed record SpeedConfiguration
{
    public float BoundaryRecoverySpeedMultiplier { get; init; } = 1.30f;
    public float ReferenceMinimumCrawl { get; init; } = 22f;
    public float ReferenceMaximumCrawl { get; init; } = 26f;
    public float MaximumCrawl { get; init; } = 96.2f;
    public float AnimationNormalization { get; init; } = 49.4f;
    public float CrawlAcceleration { get; init; } = 248f;
    public float CrawlDeceleration { get; init; } = 326f;
    public float RestDeceleration { get; init; } = 312f;
    public float FastCrawlAcceleration { get; init; } = 120f;

    internal void Validate(List<string> failures)
    {
        BehaviorConfiguration.RequirePositive(
            BoundaryRecoverySpeedMultiplier,
            "boundary-recovery speed multiplier",
            failures);
        BehaviorConfiguration.ValidateRange(ReferenceMinimumCrawl, ReferenceMaximumCrawl, 0.1f, "reference crawl speed", failures);
        if (float.IsFinite(ReferenceMinimumCrawl) &&
            float.IsFinite(ReferenceMaximumCrawl) &&
            ReferenceMaximumCrawl - ReferenceMinimumCrawl <= 0.001f)
        {
            failures.Add("reference crawl speed range must have a positive width.");
        }
        if (!float.IsFinite(MaximumCrawl) ||
            MaximumCrawl < ReferenceMaximumCrawl ||
            MaximumCrawl - ReferenceMinimumCrawl <= 0.001f)
        {
            failures.Add(
                "maximum crawl speed must be finite, >= reference maximum crawl speed, " +
                "and greater than reference minimum crawl speed.");
        }
        BehaviorConfiguration.RequirePositive(AnimationNormalization, "animation speed normalization", failures);
        BehaviorConfiguration.RequirePositive(CrawlAcceleration, "crawl acceleration", failures);
        BehaviorConfiguration.RequirePositive(CrawlDeceleration, "crawl deceleration", failures);
        BehaviorConfiguration.RequirePositive(RestDeceleration, "rest deceleration", failures);
        BehaviorConfiguration.RequirePositive(FastCrawlAcceleration, "fast crawl acceleration", failures);
    }
}

internal sealed record PointerChaseConfiguration
{
    public float TriggerMinimumDistance { get; init; } = 20f;
    public float TriggerMaximumDistance { get; init; } = 330f;
    public float RearmDistance { get; init; } = 360f;
    public float LostDistance { get; init; } = 430f;
    public float AttentionDuration { get; init; } = 0.12f;
    public float StopDistance { get; init; } = 48f;
    public float LostGraceDuration { get; init; } = 0.45f;
    public float MinimumCooldown { get; init; } = 5f;
    public float MaximumCooldown { get; init; } = 8f;
    public float TargetInset { get; init; } = 48f;
    public float EdgeInterruptDistance { get; init; } = 75f;
    public float TurnGain { get; init; } = 1.50f;
    public float MaximumTurnRate { get; init; } = 1.25f;
    public float SteeringResponse { get; init; } = 4f;
    public float TargetTrackingResponse { get; init; } = 10f;
    public float ApproachDistance { get; init; } = 90f;
    public float MinimumFacingSpeedFactor { get; init; } = 0.20f;
    public float FacingAngleFactor { get; init; } = 0.72f;
    public float MinimumChaseSpeedFactor { get; init; } = 0.96f;
    public float MaximumChaseSpeedFactor { get; init; } = 1f;

    internal void Validate(List<string> failures)
    {
        BehaviorConfiguration.ValidateRange(TriggerMinimumDistance, TriggerMaximumDistance, 0f, "pointer trigger distance", failures);
        if (RearmDistance <= TriggerMaximumDistance || LostDistance <= TriggerMaximumDistance)
        {
            failures.Add("pointer rearm/lost distances must be greater than trigger maximum distance.");
        }
        BehaviorConfiguration.RequirePositive(AttentionDuration, "pointer attention duration", failures);
        BehaviorConfiguration.RequireNonNegative(StopDistance, "pointer stop distance", failures);
        BehaviorConfiguration.RequirePositive(LostGraceDuration, "pointer lost grace duration", failures);
        BehaviorConfiguration.ValidateRange(MinimumCooldown, MaximumCooldown, 0f, "pointer cooldown", failures);
        BehaviorConfiguration.RequireNonNegative(TargetInset, "pointer target inset", failures);
        BehaviorConfiguration.RequireNonNegative(EdgeInterruptDistance, "pointer edge interrupt distance", failures);
        BehaviorConfiguration.RequirePositive(TurnGain, "pointer turn gain", failures);
        BehaviorConfiguration.RequirePositive(MaximumTurnRate, "pointer maximum turn rate", failures);
        BehaviorConfiguration.RequirePositive(SteeringResponse, "pointer steering response", failures);
        BehaviorConfiguration.RequirePositive(TargetTrackingResponse, "pointer target tracking response", failures);
        BehaviorConfiguration.RequirePositive(ApproachDistance, "pointer approach distance", failures);
        if (MinimumFacingSpeedFactor is < 0f or > 1f || FacingAngleFactor <= 0f)
        {
            failures.Add("pointer facing factors must satisfy 0 <= minimum <= 1 and angle factor > 0.");
        }
        BehaviorConfiguration.ValidateRange(
            MinimumChaseSpeedFactor,
            MaximumChaseSpeedFactor,
            0f,
            "pointer chase speed factor",
            failures);
    }
}
