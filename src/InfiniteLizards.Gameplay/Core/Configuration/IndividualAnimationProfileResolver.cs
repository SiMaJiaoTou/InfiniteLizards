namespace DesktopLizard.Core;

internal static class IndividualAnimationProfileResolver
{
    internal static GaitConfiguration ResolveGait(
        GaitConfiguration sourceGait,
        IndividualVariationContext context)
    {
        var gait = sourceGait with
        {
            FrontLegLinkLength = sourceGait.FrontLegLinkLength * context.GaitMultiplier,
            RearLegLinkLength = sourceGait.RearLegLinkLength * context.GaitMultiplier,
            MinimumStepDuration = sourceGait.MinimumStepDuration * context.GaitCadenceMultiplier,
            FrontLateralReach = sourceGait.FrontLateralReach * context.GaitMultiplier,
            RearLateralReach = sourceGait.RearLateralReach * context.GaitMultiplier,
            FrontElbowLongitudinalOffset = sourceGait.FrontElbowLongitudinalOffset * context.GaitMultiplier,
            RearElbowLongitudinalOffset = sourceGait.RearElbowLongitudinalOffset * context.GaitMultiplier,
            ElbowLateralOffset = sourceGait.ElbowLateralOffset * context.GaitMultiplier,
            FrontLongitudinalBase = sourceGait.FrontLongitudinalBase * context.GaitMultiplier,
            RearLongitudinalBase = sourceGait.RearLongitudinalBase * context.GaitMultiplier,
            StepHeightBase = sourceGait.StepHeightBase * context.GaitHeightMultiplier,
            MinimumStepHeight = sourceGait.MinimumStepHeight * context.GaitHeightMultiplier,
            MaximumStepHeight = sourceGait.MaximumStepHeight * context.GaitHeightMultiplier,
            ReferencePairSpacing = sourceGait.ReferencePairSpacing * context.GaitMultiplier,
            MinimumPairGap = sourceGait.MinimumPairGap * context.GaitCadenceMultiplier,
            MaximumPairGap = sourceGait.MaximumPairGap * context.GaitCadenceMultiplier,
            SlowPairErrorMemoryDuration = sourceGait.SlowPairErrorMemoryDuration * context.GaitCadenceMultiplier,
            FastPairErrorMemoryDuration = sourceGait.FastPairErrorMemoryDuration * context.GaitCadenceMultiplier,
            SlowMinimumSupportDuration = sourceGait.SlowMinimumSupportDuration * context.GaitCadenceMultiplier,
            FastMinimumSupportDuration = sourceGait.FastMinimumSupportDuration * context.GaitCadenceMultiplier,
            IdleSettleError = sourceGait.IdleSettleError * context.GaitMultiplier,
            IdleStepDuration = sourceGait.IdleStepDuration * context.GaitCadenceMultiplier,
            IdleSupportDuration = sourceGait.IdleSupportDuration * context.GaitCadenceMultiplier,
            DropStepError = sourceGait.DropStepError * context.GaitMultiplier,
            SlowStepError = sourceGait.SlowStepError * context.GaitMultiplier,
            FastStepError = sourceGait.FastStepError * context.GaitMultiplier,
            SlowMaximumStepWait = sourceGait.SlowMaximumStepWait * context.GaitCadenceMultiplier,
            FastMaximumStepWait = sourceGait.FastMaximumStepWait * context.GaitCadenceMultiplier,
            SlowStepDuration = sourceGait.SlowStepDuration * context.GaitCadenceMultiplier,
            FastStepDuration = sourceGait.FastStepDuration * context.GaitCadenceMultiplier,
            DropStepDuration = sourceGait.DropStepDuration * context.GaitCadenceMultiplier,
            MinimumLead = sourceGait.MinimumLead * context.GaitMultiplier,
            MaximumLead = sourceGait.MaximumLead * context.GaitMultiplier,
            PairStaggerMinimum = sourceGait.PairStaggerMinimum * context.GaitCadenceMultiplier,
            PairStaggerMaximum = sourceGait.PairStaggerMaximum * context.GaitCadenceMultiplier,
            DropSupportDuration = sourceGait.DropSupportDuration * context.GaitCadenceMultiplier,
            NormalSupportFloor = sourceGait.NormalSupportFloor * context.GaitCadenceMultiplier,
            TightTurnSupportFloor = sourceGait.TightTurnSupportFloor * context.GaitCadenceMultiplier,
            StanceSkew = sourceGait.StanceSkew.Select(value => value * context.GaitMultiplier).ToArray()
        };
        return gait;
    }

    internal static SecondaryMotionConfiguration ResolveSecondaryMotion(
        SecondaryMotionConfiguration sourceSecondaryMotion,
        IndividualVariationConfiguration variation,
        IndividualVariationContext context)
    {
        var coefficients = variation.Animation;
        var secondaryMotion = sourceSecondaryMotion with
        {
            BodyBobMinimumFrequency = sourceSecondaryMotion.BodyBobMinimumFrequency *
                                      (1f + variation.GaitVariation * context.Activity *
                                       coefficients.BodyBobActivity),
            BodyBobMaximumFrequency = sourceSecondaryMotion.BodyBobMaximumFrequency *
                                      (1f + variation.GaitVariation * context.Activity *
                                       coefficients.BodyBobActivity),
            IdleTailAmplitude = sourceSecondaryMotion.IdleTailAmplitude *
                                (1f + variation.GaitVariation * context.Playfulness *
                                 coefficients.IdleTailPlayfulness),
            ObserveTailAmplitude = sourceSecondaryMotion.ObserveTailAmplitude *
                                   (1f + variation.GaitVariation * context.Curiosity *
                                    coefficients.ObserveTailCuriosity),
            BreathingAmplitude = sourceSecondaryMotion.BreathingAmplitude *
                                 (1f + variation.GaitVariation * context.Calmness *
                                  coefficients.BreathingCalmness),
            BlinkMinimumInterval = sourceSecondaryMotion.BlinkMinimumInterval *
                                   (1f + variation.DurationVariation * context.Calmness *
                                    coefficients.BlinkCalmness),
            BlinkMaximumInterval = sourceSecondaryMotion.BlinkMaximumInterval *
                                   (1f + variation.DurationVariation * context.Calmness *
                                    coefficients.BlinkCalmness)
        };
        return secondaryMotion;
    }
}
