namespace DesktopLizard.Core;

internal sealed record GaitConfiguration
{
    public float FrontLegLinkLength { get; init; } = 30.5f;
    public float RearLegLinkLength { get; init; } = 29f;
    public float MaximumReachFactor { get; init; } = 1.99f;
    public float MinimumStepDuration { get; init; } = 0.060f;
    public float StepHeightBase { get; init; } = 4.5f;
    public float StepHeightDistanceFactor { get; init; } = 0.115f;
    public float MinimumStepHeight { get; init; } = 6.5f;
    public float MaximumStepHeight { get; init; } = 9f;
    public float MinimumStepHeightScale { get; init; } = 0.35f;
    public float ElbowMidpointFactor { get; init; } = 0.50f;
    public float FrontElbowLongitudinalOffset { get; init; } = 4.5f;
    public float RearElbowLongitudinalOffset { get; init; } = -4f;
    public float ElbowLateralOffset { get; init; } = 1.8f;
    public float FrontLateralReach { get; init; } = 20f;
    public float RearLateralReach { get; init; } = 18f;
    public float FrontLongitudinalBase { get; init; } = 7f;
    public float RearLongitudinalBase { get; init; } = -3f;
    public float[] StanceSkew { get; init; } = [9f, 2f, -8f, -2f];
    public float ShoulderInsetFactor { get; init; } = 0.38f;
    public float ReferencePairSpacing { get; init; } = 16.5f;
    public float MinimumPairGap { get; init; } = 0.160f;
    public float MaximumPairGap { get; init; } = 0.720f;
    public float SlowPairErrorMemoryDuration { get; init; } = 0.085f;
    public float FastPairErrorMemoryDuration { get; init; } = 0.045f;
    public float MaximumPairErrorWeight { get; init; } = 0.65f;
    public float MinimumPairErrorWeight { get; init; } = 0.35f;
    public float SlowMinimumSupportDuration { get; init; } = 0.085f;
    public float FastMinimumSupportDuration { get; init; } = 0.060f;
    public float IdleSettleError { get; init; } = 14f;
    public float IdleStepDuration { get; init; } = 0.16f;
    public float IdleSupportDuration { get; init; } = 0.18f;
    public float DropStepError { get; init; } = 4f;
    public float SlowStepError { get; init; } = 11.5f;
    public float FastStepError { get; init; } = 16f;
    public float SlowMaximumStepWait { get; init; } = 0.070f;
    public float FastMaximumStepWait { get; init; } = 0.025f;
    public float SoftReachRatio { get; init; } = 0.90f;
    public float HardReachRatio { get; init; } = 0.96f;
    public float TightTurnCadenceStart { get; init; } = 0.45f;
    public float TightTurnCadenceRange { get; init; } = 0.45f;
    public float SlowStepDuration { get; init; } = 0.145f;
    public float FastStepDuration { get; init; } = 0.075f;
    public float TightTurnDurationFactor { get; init; } = 0.42f;
    public float DropStepDuration { get; init; } = 0.12f;
    public float FastSCurveDurationFactor { get; init; } = 0.84f;
    public float MinimumLead { get; init; } = 22f;
    public float MaximumLead { get; init; } = 24f;
    public float TightTurnLeadFactor { get; init; } = 0.58f;
    public float PairStaggerMinimum { get; init; } = 0f;
    public float PairStaggerMaximum { get; init; } = 0.028f;
    public float TightTurnStaggerFactor { get; init; } = 0.35f;
    public float TightTurnPairGapFactor { get; init; } = 0.15f;
    public float DropSupportDuration { get; init; } = 0.04f;
    public float NormalSupportFloor { get; init; } = 0.065f;
    public float TightTurnSupportFloor { get; init; } = 0.025f;
    public float TurnBiasRate { get; init; } = 0.35f;
    public float FrontInnerLeadReduction { get; init; } = 0.08f;
    public float RearInnerLeadReduction { get; init; } = 0.20f;
    public float FrontOuterLeadBoost { get; init; } = 0.18f;
    public float RearOuterLeadBoost { get; init; } = 0.12f;
    public float TightTurnLiftStart { get; init; } = 0.55f;
    public float TightTurnLiftRange { get; init; } = 0.35f;
    public float TightTurnLiftHeightFactor { get; init; } = 0.45f;

    internal void Validate(List<string> failures)
    {
        BehaviorConfiguration.RequirePositive(FrontLegLinkLength, "front leg link length", failures);
        BehaviorConfiguration.RequirePositive(RearLegLinkLength, "rear leg link length", failures);
        BehaviorConfiguration.RequirePositive(MaximumReachFactor, "maximum reach factor", failures);
        BehaviorConfiguration.RequirePositive(MinimumStepDuration, "minimum step duration", failures);
        BehaviorConfiguration.RequireNonNegative(StepHeightBase, "step height base", failures);
        BehaviorConfiguration.RequireNonNegative(StepHeightDistanceFactor, "step height distance factor", failures);
        BehaviorConfiguration.ValidateRange(MinimumStepHeight, MaximumStepHeight, 0f, "step height", failures);
        ValidateUnitFactor(MinimumStepHeightScale, "minimum step-height scale", failures);
        if (!float.IsFinite(ElbowMidpointFactor) || ElbowMidpointFactor is < 0f or > 1f)
        {
            failures.Add("gait elbow midpoint factor must be in [0,1].");
        }
        if (!float.IsFinite(FrontElbowLongitudinalOffset) ||
            !float.IsFinite(RearElbowLongitudinalOffset) ||
            !float.IsFinite(ElbowLateralOffset))
        {
            failures.Add("gait elbow offsets must be finite.");
        }
        BehaviorConfiguration.RequireNonNegative(FrontLateralReach, "front lateral reach", failures);
        BehaviorConfiguration.RequireNonNegative(RearLateralReach, "rear lateral reach", failures);
        if (!float.IsFinite(FrontLongitudinalBase) || !float.IsFinite(RearLongitudinalBase))
        {
            failures.Add("gait longitudinal bases must be finite.");
        }
        if (StanceSkew.Length != 4 || StanceSkew.Any(value => !float.IsFinite(value)))
        {
            failures.Add("gait stance skew must contain exactly four finite values.");
        }
        if (!float.IsFinite(ShoulderInsetFactor) || ShoulderInsetFactor is < 0f or > 1f)
        {
            failures.Add("gait shoulder inset factor must be in [0,1].");
        }
        BehaviorConfiguration.RequirePositive(ReferencePairSpacing, "reference pair spacing", failures);
        BehaviorConfiguration.ValidateRange(MinimumPairGap, MaximumPairGap, 0.01f, "pair gap", failures);
        BehaviorConfiguration.ValidateRange(FastPairErrorMemoryDuration, SlowPairErrorMemoryDuration, 0.001f, "pair-error memory duration", failures);
        if (!float.IsFinite(MaximumPairErrorWeight) ||
            !float.IsFinite(MinimumPairErrorWeight) ||
            MaximumPairErrorWeight is < 0f or > 1f ||
            MinimumPairErrorWeight is < 0f or > 1f ||
            MathF.Abs(MaximumPairErrorWeight + MinimumPairErrorWeight - 1f) > 0.0001f)
        {
            failures.Add("pair-error weights must be in [0,1] and sum to 1.");
        }
        BehaviorConfiguration.ValidateRange(FastMinimumSupportDuration, SlowMinimumSupportDuration, 0f, "minimum support duration", failures);
        BehaviorConfiguration.RequireNonNegative(IdleSettleError, "idle settle error", failures);
        BehaviorConfiguration.RequirePositive(IdleStepDuration, "idle step duration", failures);
        BehaviorConfiguration.RequireNonNegative(IdleSupportDuration, "idle support duration", failures);
        BehaviorConfiguration.RequireNonNegative(DropStepError, "drop step error", failures);
        BehaviorConfiguration.ValidateRange(SlowStepError, FastStepError, 0f, "moving step error", failures);
        BehaviorConfiguration.ValidateRange(FastMaximumStepWait, SlowMaximumStepWait, 0f, "maximum step wait", failures);
        if (!(SoftReachRatio > 0f && SoftReachRatio < HardReachRatio && HardReachRatio <= 1f))
        {
            failures.Add("gait reach ratios must satisfy 0 < soft < hard <= 1.");
        }
        BehaviorConfiguration.RequireNonNegative(TightTurnCadenceStart, "tight-turn cadence start", failures);
        BehaviorConfiguration.RequirePositive(TightTurnCadenceRange, "tight-turn cadence range", failures);
        BehaviorConfiguration.ValidateRange(FastStepDuration, SlowStepDuration, MinimumStepDuration, "gait step duration", failures);
        ValidateUnitFactor(TightTurnDurationFactor, "tight-turn duration factor", failures);
        BehaviorConfiguration.RequirePositive(DropStepDuration, "drop step duration", failures);
        ValidateUnitFactor(FastSCurveDurationFactor, "fast S-curve duration factor", failures);
        BehaviorConfiguration.ValidateRange(MinimumLead, MaximumLead, 0f, "gait lead", failures);
        ValidateUnitFactor(TightTurnLeadFactor, "tight-turn lead factor", failures);
        BehaviorConfiguration.ValidateRange(PairStaggerMinimum, PairStaggerMaximum, 0f, "pair stagger", failures);
        ValidateUnitFactor(TightTurnStaggerFactor, "tight-turn stagger factor", failures);
        ValidateUnitFactor(TightTurnPairGapFactor, "tight-turn pair-gap factor", failures);
        BehaviorConfiguration.RequireNonNegative(DropSupportDuration, "drop support duration", failures);
        BehaviorConfiguration.ValidateRange(TightTurnSupportFloor, NormalSupportFloor, 0f, "support floor", failures);
        BehaviorConfiguration.RequirePositive(TurnBiasRate, "turn bias rate", failures);
        ValidateUnitFactor(FrontInnerLeadReduction, "front inner-lead reduction", failures);
        ValidateUnitFactor(RearInnerLeadReduction, "rear inner-lead reduction", failures);
        BehaviorConfiguration.RequireNonNegative(FrontOuterLeadBoost, "front outer-lead boost", failures);
        BehaviorConfiguration.RequireNonNegative(RearOuterLeadBoost, "rear outer-lead boost", failures);
        BehaviorConfiguration.RequireNonNegative(TightTurnLiftStart, "tight-turn lift start", failures);
        BehaviorConfiguration.RequirePositive(TightTurnLiftRange, "tight-turn lift range", failures);
        ValidateUnitFactor(TightTurnLiftHeightFactor, "tight-turn lift-height factor", failures);
    }

    private static void ValidateUnitFactor(float value, string name, List<string> failures)
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
        {
            failures.Add($"{name} must be in [0,1].");
        }
    }
}
