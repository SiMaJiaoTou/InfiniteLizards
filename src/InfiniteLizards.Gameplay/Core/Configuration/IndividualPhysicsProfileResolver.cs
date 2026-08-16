namespace DesktopLizard.Core;

internal static class IndividualPhysicsProfileResolver
{
    internal static PhysicsConfiguration Resolve(
        PhysicsConfiguration sourcePhysics,
        IndividualVariationConfiguration variation,
        IndividualVariationContext context)
    {
        var coefficients = variation.Physics;
        var physics = sourcePhysics with
        {
            Gravity = sourcePhysics.Gravity * context.PhysicsMultiplier,
            LinearDrag = sourcePhysics.LinearDrag *
                         (1f + variation.PhysicsVariation * context.Calmness *
                          coefficients.LinearDragCalmness),
            ConstraintIterations = Math.Clamp(
                (int)MathF.Round(sourcePhysics.ConstraintIterations * context.PhysicsMultiplier),
                1,
                128),
            ConstraintVelocityResponseRate = sourcePhysics.ConstraintVelocityResponseRate *
                                             (1f + variation.PhysicsVariation * context.Calmness *
                                              coefficients.ConstraintResponseCalmness),
            MaximumSpeed = sourcePhysics.MaximumSpeed *
                           (1f + variation.PhysicsVariation * context.Agility *
                            coefficients.MaximumSpeedAgility),
            UpperLimbAngularSpring = sourcePhysics.UpperLimbAngularSpring *
                                     (1f + variation.PhysicsVariation * context.Agility *
                                      coefficients.LimbAngularSpringAgility),
            LowerLimbAngularSpring = sourcePhysics.LowerLimbAngularSpring *
                                     (1f + variation.PhysicsVariation * context.Agility *
                                      coefficients.LimbAngularSpringAgility),
            ReleasePoseRecoveryDuration = sourcePhysics.ReleasePoseRecoveryDuration * context.DurationMultiplier,
            RegripFrontReachLengthFactor = Math.Clamp(
                sourcePhysics.RegripFrontReachLengthFactor *
                (1f + variation.PhysicsVariation * context.Agility *
                 coefficients.RegripReachAgility),
                0f,
                1f),
            RegripFrontReachOutwardWeight = Math.Clamp(
                sourcePhysics.RegripFrontReachOutwardWeight *
                (1f + variation.PhysicsVariation * context.Curiosity *
                 coefficients.RegripOutwardCuriosity),
                0f,
                1f),
            RegripRearReachLengthFactor = Math.Clamp(
                sourcePhysics.RegripRearReachLengthFactor *
                (1f + variation.PhysicsVariation * context.Agility *
                 coefficients.RegripReachAgility),
                0f,
                1f),
            RegripRearReachOutwardWeight = Math.Clamp(
                sourcePhysics.RegripRearReachOutwardWeight *
                (1f + variation.PhysicsVariation * context.Curiosity *
                 coefficients.RegripOutwardCuriosity),
                0f,
                1f),
            RegripContactHoldFraction = Math.Clamp(
                sourcePhysics.RegripContactHoldFraction *
                (1f + variation.PhysicsVariation * context.Calmness *
                 coefficients.RegripHoldCalmness),
                0f,
                0.8f)
        };
        return physics;
    }
}
