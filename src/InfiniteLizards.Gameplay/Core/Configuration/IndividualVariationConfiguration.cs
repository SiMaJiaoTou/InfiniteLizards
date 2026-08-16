namespace DesktopLizard.Core;

/// <summary>
/// Controls how stable individual traits are projected onto runtime settings.
/// Amounts control overall strength; the typed coefficient groups control the
/// exact trait-to-parameter mapping without changing the trait RNG stream.
/// </summary>
internal sealed record IndividualVariationConfiguration
{
    public bool Enabled { get; init; } = true;
    public float SpeedVariation { get; init; } = 0.12f;
    public float DurationVariation { get; init; } = 0.18f;
    public float TurnVariation { get; init; } = 0.15f;
    public float GaitVariation { get; init; } = 0.10f;
    public float PhysicsVariation { get; init; } = 0.08f;
    public float ColorVariation { get; init; } = 0.08f;
    public float TransitionWeightVariation { get; init; } = 0.35f;

    /// <summary>
    /// Zero preserves the configured rest-band weights exactly. The default
    /// value of one reproduces the original individualization behavior.
    /// </summary>
    public float RestWeightVariation { get; init; } = 1f;

    public IndividualMultiplierCoefficients Multipliers { get; init; } = new();
    public IndividualBehaviorVariationCoefficients Behavior { get; init; } = new();
    public IndividualTransitionVariationCoefficients Transitions { get; init; } = new();
    public IndividualRestVariationCoefficients Rest { get; init; } = new();
    public IndividualAnimationVariationCoefficients Animation { get; init; } = new();
    public IndividualPhysicsVariationCoefficients Physics { get; init; } = new();
    public IndividualAppearanceVariationCoefficients Appearance { get; init; } = new();

    internal void Validate(List<string> failures)
    {
        ValidateAmount(SpeedVariation, nameof(SpeedVariation), 0.75f, failures);
        ValidateAmount(DurationVariation, nameof(DurationVariation), 0.75f, failures);
        ValidateAmount(TurnVariation, nameof(TurnVariation), 0.75f, failures);
        ValidateAmount(GaitVariation, nameof(GaitVariation), 0.75f, failures);
        ValidateAmount(PhysicsVariation, nameof(PhysicsVariation), 0.75f, failures);
        ValidateAmount(ColorVariation, nameof(ColorVariation), 0.75f, failures);
        ValidateAmount(TransitionWeightVariation, nameof(TransitionWeightVariation), 0.75f, failures);
        ValidateAmount(RestWeightVariation, nameof(RestWeightVariation), 1f, failures);
        Multipliers.Validate(failures);
        Behavior.Validate(failures);
        Transitions.Validate(
            failures,
            requireNormalizedGroups: Enabled && TransitionWeightVariation > 0f);
        Rest.Validate(failures);
        Animation.Validate(failures);
        Physics.Validate(failures);
        Appearance.Validate(failures);
        if (Enabled)
        {
            ValidatePositiveMultiplier(
                SpeedVariation,
                Multipliers.SpeedActivity + Multipliers.SpeedAgility,
                "speed",
                failures);
            ValidatePositiveMultiplier(
                DurationVariation,
                Multipliers.DurationCalmness + Multipliers.DurationActivity,
                "duration",
                failures);
            ValidatePositiveMultiplier(
                TurnVariation,
                Multipliers.TurnAgility + Multipliers.TurnCuriosity,
                "turn",
                failures);
            ValidatePositiveMultiplier(
                GaitVariation,
                Multipliers.GaitAgility + Multipliers.GaitBuild,
                "gait",
                failures);
            ValidatePositiveMultiplier(
                DurationVariation,
                Behavior.PointerAttentionCuriosity + Behavior.PointerAttentionBoldness,
                "pointer attention",
                failures);
        }
    }

    private static void ValidateAmount(float value, string name, float maximum, List<string> failures)
    {
        if (!float.IsFinite(value) || value < 0f || value > maximum)
        {
            failures.Add($"Individual variation {name} must be in [0,{maximum}].");
        }
    }

    internal static void ValidateCoefficient(float value, string name, List<string> failures)
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
        {
            failures.Add($"Individual variation coefficient {name} must be in [0,1].");
        }
    }

    private static void ValidatePositiveMultiplier(
        float amount,
        float maximumAbsoluteContribution,
        string name,
        List<string> failures)
    {
        if (float.IsFinite(amount) &&
            float.IsFinite(maximumAbsoluteContribution) &&
            1f - amount * maximumAbsoluteContribution <= 0f)
        {
            failures.Add(
                $"Individual variation {name} coefficients can produce a non-positive multiplier.");
        }
    }
}

internal sealed record IndividualMultiplierCoefficients
{
    public float SpeedActivity { get; init; } = 0.62f;
    public float SpeedAgility { get; init; } = 0.38f;
    public float DurationCalmness { get; init; } = 0.65f;
    public float DurationActivity { get; init; } = 0.35f;
    public float TurnAgility { get; init; } = 0.70f;
    public float TurnCuriosity { get; init; } = 0.30f;
    public float GaitAgility { get; init; } = 0.65f;
    public float GaitBuild { get; init; } = 0.35f;
    public float GaitCadenceAgility { get; init; } = 0.25f;
    public float GaitHeightAgility { get; init; } = 0.45f;
    public float PhysicsBuild { get; init; } = 1f;
    public float BodyBuild { get; init; } = 0.55f;

    internal void Validate(List<string> failures)
    {
        Validate(SpeedActivity, nameof(SpeedActivity), failures);
        Validate(SpeedAgility, nameof(SpeedAgility), failures);
        Validate(DurationCalmness, nameof(DurationCalmness), failures);
        Validate(DurationActivity, nameof(DurationActivity), failures);
        Validate(TurnAgility, nameof(TurnAgility), failures);
        Validate(TurnCuriosity, nameof(TurnCuriosity), failures);
        Validate(GaitAgility, nameof(GaitAgility), failures);
        Validate(GaitBuild, nameof(GaitBuild), failures);
        Validate(GaitCadenceAgility, nameof(GaitCadenceAgility), failures);
        Validate(GaitHeightAgility, nameof(GaitHeightAgility), failures);
        Validate(PhysicsBuild, nameof(PhysicsBuild), failures);
        Validate(BodyBuild, nameof(BodyBuild), failures);
    }

    private static void Validate(float value, string name, List<string> failures) =>
        IndividualVariationConfiguration.ValidateCoefficient(value, name, failures);
}

internal sealed record IndividualBehaviorVariationCoefficients
{
    public float PointerTriggerCuriosity { get; init; } = 0.30f;
    public float PointerAttentionCuriosity { get; init; } = 0.55f;
    public float PointerAttentionBoldness { get; init; } = 0.25f;
    public float PointerLostGraceCuriosity { get; init; } = 0.25f;
    public float FastCurveDurationBlend { get; init; } = 0.45f;
    public float FastForwardDurationBlend { get; init; } = 0.35f;
    public float LostGripDistanceBlend { get; init; } = 0.45f;
    public float LostGripVelocityBlend { get; init; } = 0.35f;
    public float LostGripGravityBlend { get; init; } = 0.25f;
    public float LostGripRecoveryDurationBlend { get; init; } = 0.40f;

    internal void Validate(List<string> failures)
    {
        Validate(PointerTriggerCuriosity, nameof(PointerTriggerCuriosity), failures);
        Validate(PointerAttentionCuriosity, nameof(PointerAttentionCuriosity), failures);
        Validate(PointerAttentionBoldness, nameof(PointerAttentionBoldness), failures);
        Validate(PointerLostGraceCuriosity, nameof(PointerLostGraceCuriosity), failures);
        Validate(FastCurveDurationBlend, nameof(FastCurveDurationBlend), failures);
        Validate(FastForwardDurationBlend, nameof(FastForwardDurationBlend), failures);
        Validate(LostGripDistanceBlend, nameof(LostGripDistanceBlend), failures);
        Validate(LostGripVelocityBlend, nameof(LostGripVelocityBlend), failures);
        Validate(LostGripGravityBlend, nameof(LostGripGravityBlend), failures);
        Validate(
            LostGripRecoveryDurationBlend,
            nameof(LostGripRecoveryDurationBlend),
            failures);
    }

    private static void Validate(float value, string name, List<string> failures) =>
        IndividualVariationConfiguration.ValidateCoefficient(value, name, failures);
}

internal sealed record IndividualTransitionVariationCoefficients
{
    public float ForwardActivity { get; init; } = 0.70f;
    public float ForwardCalmness { get; init; } = 0.30f;
    public float ForwardExtensionActivity { get; init; } = 0.75f;
    public float ForwardExtensionBoldness { get; init; } = 0.25f;
    public float CurveCuriosity { get; init; } = 0.65f;
    public float CurveAgility { get; init; } = 0.35f;
    public float SCurveCuriosity { get; init; } = 0.55f;
    public float SCurveAgility { get; init; } = 0.30f;
    public float SCurvePlayfulness { get; init; } = 0.15f;
    public float FastForwardActivity { get; init; } = 0.55f;
    public float FastForwardBoldness { get; init; } = 0.30f;
    public float FastForwardPlayfulness { get; init; } = 0.15f;
    public float FastSCurveActivity { get; init; } = 0.40f;
    public float FastSCurveAgility { get; init; } = 0.35f;
    public float FastSCurvePlayfulness { get; init; } = 0.25f;
    public float TurnAroundCuriosity { get; init; } = 0.55f;
    public float TurnAroundBoldness { get; init; } = 0.45f;
    public float LostGripPlayfulness { get; init; } = 0.55f;
    public float LostGripLowAgility { get; init; } = 0.45f;

    internal void Validate(List<string> failures, bool requireNormalizedGroups)
    {
        Validate(ForwardActivity, nameof(ForwardActivity), failures);
        Validate(ForwardCalmness, nameof(ForwardCalmness), failures);
        Validate(ForwardExtensionActivity, nameof(ForwardExtensionActivity), failures);
        Validate(ForwardExtensionBoldness, nameof(ForwardExtensionBoldness), failures);
        Validate(CurveCuriosity, nameof(CurveCuriosity), failures);
        Validate(CurveAgility, nameof(CurveAgility), failures);
        Validate(SCurveCuriosity, nameof(SCurveCuriosity), failures);
        Validate(SCurveAgility, nameof(SCurveAgility), failures);
        Validate(SCurvePlayfulness, nameof(SCurvePlayfulness), failures);
        Validate(FastForwardActivity, nameof(FastForwardActivity), failures);
        Validate(FastForwardBoldness, nameof(FastForwardBoldness), failures);
        Validate(FastForwardPlayfulness, nameof(FastForwardPlayfulness), failures);
        Validate(FastSCurveActivity, nameof(FastSCurveActivity), failures);
        Validate(FastSCurveAgility, nameof(FastSCurveAgility), failures);
        Validate(FastSCurvePlayfulness, nameof(FastSCurvePlayfulness), failures);
        Validate(TurnAroundCuriosity, nameof(TurnAroundCuriosity), failures);
        Validate(TurnAroundBoldness, nameof(TurnAroundBoldness), failures);
        Validate(LostGripPlayfulness, nameof(LostGripPlayfulness), failures);
        Validate(LostGripLowAgility, nameof(LostGripLowAgility), failures);
        if (requireNormalizedGroups)
        {
            ValidateGroup("Forward", failures, ForwardActivity, ForwardCalmness);
            ValidateGroup(
                "ForwardExtension",
                failures,
                ForwardExtensionActivity,
                ForwardExtensionBoldness);
            ValidateGroup("Curve", failures, CurveCuriosity, CurveAgility);
            ValidateGroup("SCurve", failures, SCurveCuriosity, SCurveAgility, SCurvePlayfulness);
            ValidateGroup(
                "FastForward",
                failures,
                FastForwardActivity,
                FastForwardBoldness,
                FastForwardPlayfulness);
            ValidateGroup(
                "FastSCurve",
                failures,
                FastSCurveActivity,
                FastSCurveAgility,
                FastSCurvePlayfulness);
            ValidateGroup("TurnAround", failures, TurnAroundCuriosity, TurnAroundBoldness);
            ValidateGroup(
                "LostGripFall",
                failures,
                LostGripPlayfulness,
                LostGripLowAgility);
        }
    }

    private static void Validate(float value, string name, List<string> failures) =>
        IndividualVariationConfiguration.ValidateCoefficient(value, name, failures);

    private static void ValidateGroup(
        string name,
        List<string> failures,
        params float[] coefficients)
    {
        if (coefficients.All(float.IsFinite) &&
            MathF.Abs(coefficients.Sum() - 1f) > 0.0001f)
        {
            failures.Add(
                $"Individual transition coefficients for {name} must sum to 1.");
        }
    }
}

internal sealed record IndividualRestVariationCoefficients
{
    public float ShortRestBase { get; init; } = 0.70f;
    public float ShortRestActivity { get; init; } = 0.60f;
    public float LongRestBase { get; init; } = 0.70f;
    public float LongRestCalmness { get; init; } = 0.80f;

    internal void Validate(List<string> failures)
    {
        Validate(ShortRestBase, nameof(ShortRestBase), failures);
        Validate(ShortRestActivity, nameof(ShortRestActivity), failures);
        Validate(LongRestBase, nameof(LongRestBase), failures);
        Validate(LongRestCalmness, nameof(LongRestCalmness), failures);
    }

    private static void Validate(float value, string name, List<string> failures) =>
        IndividualVariationConfiguration.ValidateCoefficient(value, name, failures);
}

internal sealed record IndividualAnimationVariationCoefficients
{
    public float BodyBobActivity { get; init; } = 0.35f;
    public float IdleTailPlayfulness { get; init; } = 0.55f;
    public float ObserveTailCuriosity { get; init; } = 0.55f;
    public float BreathingCalmness { get; init; } = 0.30f;
    public float BlinkCalmness { get; init; } = 0.25f;

    internal void Validate(List<string> failures)
    {
        Validate(BodyBobActivity, nameof(BodyBobActivity), failures);
        Validate(IdleTailPlayfulness, nameof(IdleTailPlayfulness), failures);
        Validate(ObserveTailCuriosity, nameof(ObserveTailCuriosity), failures);
        Validate(BreathingCalmness, nameof(BreathingCalmness), failures);
        Validate(BlinkCalmness, nameof(BlinkCalmness), failures);
    }

    private static void Validate(float value, string name, List<string> failures) =>
        IndividualVariationConfiguration.ValidateCoefficient(value, name, failures);
}

internal sealed record IndividualPhysicsVariationCoefficients
{
    public float LinearDragCalmness { get; init; } = 0.45f;
    public float ConstraintResponseCalmness { get; init; } = 0.25f;
    public float MaximumSpeedAgility { get; init; } = 0.35f;
    public float LimbAngularSpringAgility { get; init; } = 0.30f;
    public float RegripReachAgility { get; init; } = 0.20f;
    public float RegripOutwardCuriosity { get; init; } = 0.18f;
    public float RegripHoldCalmness { get; init; } = 0.22f;

    internal void Validate(List<string> failures)
    {
        Validate(LinearDragCalmness, nameof(LinearDragCalmness), failures);
        Validate(ConstraintResponseCalmness, nameof(ConstraintResponseCalmness), failures);
        Validate(MaximumSpeedAgility, nameof(MaximumSpeedAgility), failures);
        Validate(LimbAngularSpringAgility, nameof(LimbAngularSpringAgility), failures);
        Validate(RegripReachAgility, nameof(RegripReachAgility), failures);
        Validate(RegripOutwardCuriosity, nameof(RegripOutwardCuriosity), failures);
        Validate(RegripHoldCalmness, nameof(RegripHoldCalmness), failures);
    }

    private static void Validate(float value, string name, List<string> failures) =>
        IndividualVariationConfiguration.ValidateCoefficient(value, name, failures);
}

internal sealed record IndividualAppearanceVariationCoefficients
{
    public float BodyCuriosity { get; init; } = 1f;
    public float BodyBuild { get; init; } = 1f;
    public float ShadowCuriosity { get; init; } = 0.35f;
    public float ShadowBuild { get; init; } = 1f;
    public float ShadowColorAmount { get; init; } = 0.45f;
    public float ColorDarkness { get; init; } = 0.45f;
    public float RedWarmth { get; init; } = 0.50f;
    public float GreenWarmth { get; init; } = 0.18f;
    public float BlueWarmth { get; init; } = 0.35f;

    internal void Validate(List<string> failures)
    {
        Validate(BodyCuriosity, nameof(BodyCuriosity), failures);
        Validate(BodyBuild, nameof(BodyBuild), failures);
        Validate(ShadowCuriosity, nameof(ShadowCuriosity), failures);
        Validate(ShadowBuild, nameof(ShadowBuild), failures);
        Validate(ShadowColorAmount, nameof(ShadowColorAmount), failures);
        Validate(ColorDarkness, nameof(ColorDarkness), failures);
        Validate(RedWarmth, nameof(RedWarmth), failures);
        Validate(GreenWarmth, nameof(GreenWarmth), failures);
        Validate(BlueWarmth, nameof(BlueWarmth), failures);
    }

    private static void Validate(float value, string name, List<string> failures) =>
        IndividualVariationConfiguration.ValidateCoefficient(value, name, failures);
}
