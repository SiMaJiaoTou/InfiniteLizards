namespace DesktopLizard.Core;

internal sealed record DurationRangeConfiguration(float Minimum, float Maximum)
{
    internal void Validate(string name, List<string> failures, float floor = 0.05f) =>
        BehaviorConfiguration.ValidateRange(Minimum, Maximum, floor, name, failures);
}
/// <summary>
/// Durations belonging to state transitions rather than to a specific motion
/// model. Keeping the ranges named makes state-machine tuning data-driven.
/// </summary>
internal sealed record BehaviorTimingConfiguration
{
    public float InitialSpawnDuration { get; init; } = 0.55f;
    public float ResetSpawnDuration { get; init; } = 0.48f;
    public DurationRangeConfiguration SpawnIdle { get; init; } = new(0.48f, 0.82f);
    public float PauseIdleDuration { get; init; } = 0.50f;
    public float CenterIdleDuration { get; init; } = 0.65f;
    public float ReleaseSettleDuration { get; init; } = 0.24f;
    public DurationRangeConfiguration InitialForward { get; init; } = new(2.5f, 4.1f);
    public DurationRangeConfiguration AfterForward { get; init; } = new(2.2f, 4.0f);
    public DurationRangeConfiguration AfterCurve { get; init; } = new(1.9f, 3.4f);
    public DurationRangeConfiguration AfterSCurve { get; init; } = new(2.0f, 3.6f);
    public DurationRangeConfiguration AfterFast { get; init; } = new(1.8f, 3.0f);
    public DurationRangeConfiguration AfterTurnAround { get; init; } = new(1.8f, 3.0f);
    public DurationRangeConfiguration Curve { get; init; } = new(1.4f, 2.6f);
    public DurationRangeConfiguration Observe { get; init; } = new(0.55f, 1.0f);
    public float GrabPointerSuppressionDuration { get; init; } = 2.5f;
    public float PausePointerSuppressionDuration { get; init; } = 2.0f;

    internal void Validate(List<string> failures)
    {
        BehaviorConfiguration.RequirePositive(InitialSpawnDuration, "initial spawn duration", failures);
        BehaviorConfiguration.RequirePositive(ResetSpawnDuration, "reset spawn duration", failures);
        SpawnIdle.Validate("spawn idle duration", failures);
        BehaviorConfiguration.RequirePositive(PauseIdleDuration, "pause idle duration", failures);
        BehaviorConfiguration.RequirePositive(CenterIdleDuration, "center idle duration", failures);
        BehaviorConfiguration.RequirePositive(ReleaseSettleDuration, "release settle duration", failures);
        InitialForward.Validate("initial forward duration", failures);
        AfterForward.Validate("after-forward duration", failures);
        AfterCurve.Validate("after-curve duration", failures);
        AfterSCurve.Validate("after-S-curve duration", failures);
        AfterFast.Validate("after-fast duration", failures);
        AfterTurnAround.Validate("after-turn-around duration", failures);
        Curve.Validate("curve duration", failures);
        Observe.Validate("observe duration", failures);
        BehaviorConfiguration.RequireNonNegative(GrabPointerSuppressionDuration, "grab pointer suppression", failures);
        BehaviorConfiguration.RequireNonNegative(PausePointerSuppressionDuration, "pause pointer suppression", failures);
    }
}

internal sealed record LocomotionConfiguration
{
    public float StraightHeadingJitter { get; init; } = 0.055f;
    public float CurveMinimumTurnRate { get; init; } = 0.16f;
    public float CurveMaximumTurnRate { get; init; } = 0.40f;
    public float CurveSpeedFactor { get; init; } = 0.88f;
    public float TurnAroundSpeedFactor { get; init; } = 0.52f;
    public float DefaultSteeringResponse { get; init; } = 2.6f;
    public float IdleTurnDampingResponse { get; init; } = 5f;
    public float TurnAroundSteeringResponse { get; init; } = 3.2f;
    public float SteeringSlowdownMinimumFactor { get; init; } = 0.90f;
    public float SteeringSlowdownTurnRate { get; init; } = 1.10f;
    public float TurnAroundMaximumDuration { get; init; } = 5.6f;
    public float TurnAroundMinimumAngle { get; init; } = 2.72f;
    public float TurnAroundMaximumAngle { get; init; } = 3.02f;
    public float TurnAroundMinimumRate { get; init; } = 0.55f;
    public float TurnAroundMaximumRate { get; init; } = 0.65f;
    public float TurnAroundCompletionTolerance { get; init; } = 0.04f;
    public float TurnDirectionMemoryThreshold { get; init; } = 0.02f;

    internal void Validate(List<string> failures)
    {
        BehaviorConfiguration.RequireNonNegative(StraightHeadingJitter, "straight heading jitter", failures);
        BehaviorConfiguration.ValidateRange(CurveMinimumTurnRate, CurveMaximumTurnRate, 0f, "curve turn rate", failures);
        if (CurveSpeedFactor is < 0f or > 1f || TurnAroundSpeedFactor is < 0f or > 1f ||
            SteeringSlowdownMinimumFactor is < 0f or > 1f)
        {
            failures.Add("locomotion speed factors must be in [0,1].");
        }
        BehaviorConfiguration.RequirePositive(DefaultSteeringResponse, "default steering response", failures);
        BehaviorConfiguration.RequirePositive(IdleTurnDampingResponse, "idle turn damping response", failures);
        BehaviorConfiguration.RequirePositive(TurnAroundSteeringResponse, "turn-around steering response", failures);
        BehaviorConfiguration.RequirePositive(SteeringSlowdownTurnRate, "steering slowdown turn rate", failures);
        BehaviorConfiguration.RequirePositive(TurnAroundMaximumDuration, "turn-around maximum duration", failures);
        BehaviorConfiguration.ValidateRange(TurnAroundMinimumAngle, TurnAroundMaximumAngle, 0f, "turn-around angle", failures);
        BehaviorConfiguration.ValidateRange(TurnAroundMinimumRate, TurnAroundMaximumRate, 0f, "turn-around rate", failures);
        BehaviorConfiguration.RequireNonNegative(TurnAroundCompletionTolerance, "turn-around completion tolerance", failures);
        BehaviorConfiguration.RequireNonNegative(TurnDirectionMemoryThreshold, "turn direction memory threshold", failures);
    }
}

internal sealed record BoundaryNavigationConfiguration
{
    public float WalkStartMargin { get; init; } = 107f;
    public float LookAheadBaseDistance { get; init; } = 90f;
    public float LookAheadSpeedFactor { get; init; } = 2.35f;
    public float MinimumEdgeMargin { get; init; } = 75f;
    public float EdgeMarginSpeedFactor { get; init; } = 1.10f;
    public float SideProbeDistance { get; init; } = 156f;
    public float SidePreferenceThreshold { get; init; } = 12f;
    public float RecoveryInset { get; init; } = 3f;
    public float RecoveryTargetJitterFactor { get; init; } = 0.12f;
    public float RecoveryMinimumSpeed { get; init; } = 24f;
    public float RecoveryMaximumSpeed { get; init; } = 30f;
    public float RecoveryStateDuration { get; init; } = 4.2f;
    public float HeadingGain { get; init; } = 1.18f;
    public float MaximumTurnRate { get; init; } = 1.05f;
    public float MinimumTurnError { get; init; } = 0.24f;
    public float MinimumTurnRate { get; init; } = 0.38f;
    public float SteeringResponse { get; init; } = 3.2f;
    public float MinimumInwardSpeed { get; init; } = 2.6f;
    public float SafeAreaInset { get; init; } = 55f;
    public float SafeAreaInsetFactor { get; init; } = 0.12f;
    public float SafeAheadDistance { get; init; } = 136f;
    public float CompletionHeadingTolerance { get; init; } = 0.38f;

    internal void Validate(List<string> failures)
    {
        BehaviorConfiguration.RequireNonNegative(WalkStartMargin, "walk-start edge margin", failures);
        BehaviorConfiguration.RequireNonNegative(LookAheadBaseDistance, "boundary look-ahead base", failures);
        BehaviorConfiguration.RequireNonNegative(LookAheadSpeedFactor, "boundary look-ahead speed factor", failures);
        BehaviorConfiguration.RequireNonNegative(MinimumEdgeMargin, "minimum edge margin", failures);
        BehaviorConfiguration.RequireNonNegative(EdgeMarginSpeedFactor, "edge margin speed factor", failures);
        BehaviorConfiguration.RequirePositive(SideProbeDistance, "side probe distance", failures);
        BehaviorConfiguration.RequireNonNegative(SidePreferenceThreshold, "side preference threshold", failures);
        BehaviorConfiguration.RequireNonNegative(RecoveryInset, "boundary recovery inset", failures);
        BehaviorConfiguration.RequireNonNegative(RecoveryTargetJitterFactor, "boundary target jitter", failures);
        BehaviorConfiguration.ValidateRange(RecoveryMinimumSpeed, RecoveryMaximumSpeed, 0f, "boundary recovery speed", failures);
        BehaviorConfiguration.RequirePositive(RecoveryStateDuration, "boundary recovery state duration", failures);
        BehaviorConfiguration.RequirePositive(HeadingGain, "boundary heading gain", failures);
        BehaviorConfiguration.RequirePositive(MaximumTurnRate, "boundary maximum turn rate", failures);
        BehaviorConfiguration.RequireNonNegative(MinimumTurnError, "boundary minimum turn error", failures);
        BehaviorConfiguration.RequireNonNegative(MinimumTurnRate, "boundary minimum turn rate", failures);
        BehaviorConfiguration.RequirePositive(SteeringResponse, "boundary steering response", failures);
        BehaviorConfiguration.RequireNonNegative(MinimumInwardSpeed, "minimum inward speed", failures);
        BehaviorConfiguration.RequireNonNegative(SafeAreaInset, "boundary safe-area inset", failures);
        BehaviorConfiguration.RequireNonNegative(SafeAreaInsetFactor, "boundary safe-area inset factor", failures);
        BehaviorConfiguration.RequireNonNegative(SafeAheadDistance, "boundary safe-ahead distance", failures);
        BehaviorConfiguration.RequireNonNegative(CompletionHeadingTolerance, "boundary heading tolerance", failures);
    }
}

internal sealed record EscapeSprintConfiguration
{
    public float MinimumDistance { get; init; } = 115f;
    public float MaximumDistance { get; init; } = 155f;
    public float MinimumDuration { get; init; } = 0.72f;
    public float MaximumDuration { get; init; } = 2.20f;
    public float MinimumSpeed { get; init; } = 82f;
    public float MaximumSpeed { get; init; } = 96f;
    public float SafeAreaInset { get; init; } = 46f;
    public int DirectionCandidateCount { get; init; } = 16;
    public float HeadingGain { get; init; } = 2.35f;
    public float MaximumTurnRate { get; init; } = 3.15f;
    public float SteeringResponse { get; init; } = 6.2f;
    public float MinimumFacingSpeedFactor { get; init; } = 0.12f;
    public float FacingAngleFactor { get; init; } = 0.72f;
    public float Acceleration { get; init; } = 360f;
    public float Deceleration { get; init; } = 270f;
    public float CompletionDistance { get; init; } = 16f;
    public float PointerSuppressionDuration { get; init; } = 3.5f;

    internal void Validate(List<string> failures)
    {
        BehaviorConfiguration.ValidateRange(MinimumDistance, MaximumDistance, 0f, "escape sprint distance", failures);
        BehaviorConfiguration.ValidateRange(MinimumDuration, MaximumDuration, 0.05f, "escape sprint duration", failures);
        BehaviorConfiguration.ValidateRange(MinimumSpeed, MaximumSpeed, 0f, "escape sprint speed", failures);
        BehaviorConfiguration.RequireNonNegative(SafeAreaInset, "escape sprint safe-area inset", failures);
        if (DirectionCandidateCount < 1)
        {
            failures.Add("escape sprint direction candidate count must be >= 1.");
        }
        BehaviorConfiguration.RequirePositive(HeadingGain, "escape sprint heading gain", failures);
        BehaviorConfiguration.RequirePositive(MaximumTurnRate, "escape sprint maximum turn rate", failures);
        BehaviorConfiguration.RequirePositive(SteeringResponse, "escape sprint steering response", failures);
        if (MinimumFacingSpeedFactor is < 0f or > 1f || FacingAngleFactor <= 0f)
        {
            failures.Add("escape sprint facing factors must satisfy 0 <= minimum <= 1 and angle factor > 0.");
        }
        BehaviorConfiguration.RequirePositive(Acceleration, "escape sprint acceleration", failures);
        BehaviorConfiguration.RequirePositive(Deceleration, "escape sprint deceleration", failures);
        BehaviorConfiguration.RequireNonNegative(CompletionDistance, "escape sprint completion distance", failures);
        BehaviorConfiguration.RequireNonNegative(PointerSuppressionDuration, "escape sprint pointer suppression", failures);
    }
}

internal sealed record EmotionConfiguration
{
    public float InitialCalm { get; init; } = 0.38f;
    public float InitialCurious { get; init; } = 0.32f;
    public float InitialPlayful { get; init; } = 0.20f;
    public float InitialWary { get; init; } = 0.10f;
    public float InitialRetargetDelay { get; init; } = 0.10f;
    public float CalmTargetMinimum { get; init; } = 0.16f;
    public float CalmTargetRange { get; init; } = 0.64f;
    public float CuriousTargetMinimum { get; init; } = 0.12f;
    public float CuriousTargetRange { get; init; } = 0.78f;
    public float PlayfulTargetMinimum { get; init; } = 0.08f;
    public float PlayfulTargetRange { get; init; } = 0.62f;
    public float WaryTargetMinimum { get; init; } = 0.05f;
    public float WaryTargetRange { get; init; } = 0.38f;
    public float MinimumRetargetDuration { get; init; } = 6.5f;
    public float MaximumRetargetDuration { get; init; } = 13.5f;
    public float BlendResponse { get; init; } = 0.9f;
    public float LookResponse { get; init; } = 5.5f;

    internal void Validate(List<string> failures)
    {
        ValidateUnit(InitialCalm, "initial calm", failures);
        ValidateUnit(InitialCurious, "initial curiosity", failures);
        ValidateUnit(InitialPlayful, "initial playfulness", failures);
        ValidateUnit(InitialWary, "initial wariness", failures);
        BehaviorConfiguration.RequireNonNegative(InitialRetargetDelay, "initial emotion delay", failures);
        ValidateTarget(CalmTargetMinimum, CalmTargetRange, "calm", failures);
        ValidateTarget(CuriousTargetMinimum, CuriousTargetRange, "curiosity", failures);
        ValidateTarget(PlayfulTargetMinimum, PlayfulTargetRange, "playfulness", failures);
        ValidateTarget(WaryTargetMinimum, WaryTargetRange, "wariness", failures);
        BehaviorConfiguration.ValidateRange(MinimumRetargetDuration, MaximumRetargetDuration, 0.05f, "emotion retarget duration", failures);
        BehaviorConfiguration.RequirePositive(BlendResponse, "emotion blend response", failures);
        BehaviorConfiguration.RequirePositive(LookResponse, "look response", failures);
    }

    private static void ValidateTarget(float minimum, float range, string name, List<string> failures)
    {
        ValidateUnit(minimum, $"{name} target minimum", failures);
        BehaviorConfiguration.RequireNonNegative(range, $"{name} target range", failures);
        if (minimum + range > 1f)
        {
            failures.Add($"{name} target maximum must be <= 1.");
        }
    }

    private static void ValidateUnit(float value, string name, List<string> failures)
    {
        if (!float.IsFinite(value) || value < 0f || value > 1f)
        {
            failures.Add($"{name} must be in [0,1].");
        }
    }
}

internal sealed record DecisionConfiguration
{
    public float ObserveChanceBase { get; init; } = 0.18f;
    public float ObserveChanceCuriousFactor { get; init; } = 0.18f;
    public float ObserveChanceWaryFactor { get; init; } = 0.08f;
    public float ObserveChanceMinimum { get; init; } = 0.18f;
    public float ObserveChanceMaximum { get; init; } = 0.40f;
    public float RestChanceBase { get; init; } = 0.68f;
    public float RestChanceCalmFactor { get; init; } = 0.12f;
    public float IdleDurationMinimumFactor { get; init; } = 0.85f;
    public float IdleDurationMaximumFactor { get; init; } = 1.10f;
    public float WalkPaceBase { get; init; } = 0.10f;
    public float WalkPaceEmotionFactor { get; init; } = 0.42f;
    public float WalkPaceRandomFactor { get; init; } = 0.48f;
    public float WalkPacePlayfulWeight { get; init; } = 0.75f;
    public float WalkPaceCuriousWeight { get; init; } = 0.25f;
    public float ObserveMinimumLookAngle { get; init; } = 0.62f;
    public float ObserveMaximumLookAngle { get; init; } = 0.95f;
    public float LookTargetDistance { get; init; } = 180f;

    internal void Validate(List<string> failures)
    {
        BehaviorConfiguration.ValidateRange(ObserveChanceMinimum, ObserveChanceMaximum, 0f, "observe chance clamp", failures);
        if (ObserveChanceMaximum > 1f || RestChanceBase < 0f || RestChanceBase + RestChanceCalmFactor > 1f)
        {
            failures.Add("decision probabilities must remain in [0,1].");
        }
        BehaviorConfiguration.RequireNonNegative(ObserveChanceBase, "observe chance base", failures);
        BehaviorConfiguration.RequireNonNegative(ObserveChanceCuriousFactor, "observe curiosity factor", failures);
        BehaviorConfiguration.RequireNonNegative(ObserveChanceWaryFactor, "observe wary factor", failures);
        BehaviorConfiguration.ValidateRange(IdleDurationMinimumFactor, IdleDurationMaximumFactor, 0f, "idle duration factor", failures);
        BehaviorConfiguration.RequireNonNegative(WalkPaceBase, "walk pace base", failures);
        BehaviorConfiguration.RequireNonNegative(WalkPaceEmotionFactor, "walk pace emotion factor", failures);
        BehaviorConfiguration.RequireNonNegative(WalkPaceRandomFactor, "walk pace random factor", failures);
        BehaviorConfiguration.RequireNonNegative(WalkPacePlayfulWeight, "walk pace playful weight", failures);
        BehaviorConfiguration.RequireNonNegative(WalkPaceCuriousWeight, "walk pace curious weight", failures);
        BehaviorConfiguration.ValidateRange(ObserveMinimumLookAngle, ObserveMaximumLookAngle, 0f, "observe look angle", failures);
        BehaviorConfiguration.RequirePositive(LookTargetDistance, "look target distance", failures);
    }
}
