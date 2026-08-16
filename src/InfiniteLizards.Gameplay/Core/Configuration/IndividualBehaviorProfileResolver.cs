namespace DesktopLizard.Core;

internal static class IndividualBehaviorProfileResolver
{
    internal static BehaviorConfiguration Resolve(
        BehaviorConfiguration sourceBehavior,
        IndividualTraits traits,
        IndividualVariationConfiguration variation,
        IndividualVariationContext context)
    {
        var coefficients = variation.Behavior;
        var effectiveSpeedMultiplier = Math.Min(
            context.SpeedMultiplier,
            sourceBehavior.Speed.MaximumCrawl /
            sourceBehavior.Speed.ReferenceMaximumCrawl);
        var speed = sourceBehavior.Speed with
        {
            ReferenceMinimumCrawl =
                sourceBehavior.Speed.ReferenceMinimumCrawl * effectiveSpeedMultiplier,
            ReferenceMaximumCrawl =
                sourceBehavior.Speed.ReferenceMaximumCrawl * effectiveSpeedMultiplier
        };

        var pointerSpan =
            sourceBehavior.Pointer.TriggerMaximumDistance -
            sourceBehavior.Pointer.TriggerMinimumDistance;
        var pointerSpanMultiplier =
            1f + variation.DurationVariation * context.Curiosity *
            coefficients.PointerTriggerCuriosity;
        var resolvedTriggerMaximum =
            sourceBehavior.Pointer.TriggerMinimumDistance +
            pointerSpan * pointerSpanMultiplier;
        var triggerDelta =
            resolvedTriggerMaximum - sourceBehavior.Pointer.TriggerMaximumDistance;
        var pointer = sourceBehavior.Pointer with
        {
            TriggerMaximumDistance = resolvedTriggerMaximum,
            RearmDistance = sourceBehavior.Pointer.RearmDistance + triggerDelta,
            LostDistance = sourceBehavior.Pointer.LostDistance + triggerDelta,
            AttentionDuration = sourceBehavior.Pointer.AttentionDuration *
                (1f - variation.DurationVariation *
                 (context.Curiosity * coefficients.PointerAttentionCuriosity +
                  context.Boldness * coefficients.PointerAttentionBoldness)),
            LostGraceDuration = sourceBehavior.Pointer.LostGraceDuration *
                                (1f + variation.DurationVariation * context.Curiosity *
                                 coefficients.PointerLostGraceCuriosity)
        };

        var normalCurve = sourceBehavior.SCurve.Normal with
        {
            MinimumCycleDuration = sourceBehavior.SCurve.Normal.MinimumCycleDuration * context.DurationMultiplier,
            MaximumCycleDuration = sourceBehavior.SCurve.Normal.MaximumCycleDuration * context.DurationMultiplier,
            MinimumAmplitude = sourceBehavior.SCurve.Normal.MinimumAmplitude * context.TurnMultiplier,
            MaximumAmplitude = sourceBehavior.SCurve.Normal.MaximumAmplitude * context.TurnMultiplier
        };
        var fastCurve = sourceBehavior.SCurve.Fast with
        {
            MinimumCycleDuration = sourceBehavior.SCurve.Fast.MinimumCycleDuration *
                                   MathEx.Lerp(
                                       1f,
                                       context.DurationMultiplier,
                                       coefficients.FastCurveDurationBlend),
            MaximumCycleDuration = sourceBehavior.SCurve.Fast.MaximumCycleDuration *
                                   MathEx.Lerp(
                                       1f,
                                       context.DurationMultiplier,
                                       coefficients.FastCurveDurationBlend),
            MinimumAmplitude = sourceBehavior.SCurve.Fast.MinimumAmplitude * context.TurnMultiplier,
            MaximumAmplitude = sourceBehavior.SCurve.Fast.MaximumAmplitude * context.TurnMultiplier
        };
        var transitionMatrix = ReweightTransitions(
            sourceBehavior.TransitionMatrix,
            traits,
            variation.TransitionWeightVariation,
            variation.Transitions);
        var rest = sourceBehavior.Rest with
        {
            Bands = ReweightRestBands(
                sourceBehavior.Rest.Bands,
                traits,
                variation.RestWeightVariation,
                variation.Rest)
        };
        var fallDistanceMultiplier = MathEx.Lerp(
            1f,
            context.DurationMultiplier,
            coefficients.LostGripDistanceBlend);
        var fallVelocityMultiplier = MathEx.Lerp(
            1f,
            context.SpeedMultiplier,
            coefficients.LostGripVelocityBlend);
        var fallGravityMultiplier = MathEx.Lerp(
            1f,
            context.PhysicsMultiplier,
            coefficients.LostGripGravityBlend);
        var fallRecoveryDurationMultiplier = MathEx.Lerp(
            1f,
            context.DurationMultiplier,
            coefficients.LostGripRecoveryDurationBlend);
        var lostGripFall = sourceBehavior.LostGripFall with
        {
            MinimumDistance = sourceBehavior.LostGripFall.MinimumDistance * fallDistanceMultiplier,
            ReachLeadDistance =
                sourceBehavior.LostGripFall.ReachLeadDistance * fallDistanceMultiplier,
            MinimumInitialVelocity =
                sourceBehavior.LostGripFall.MinimumInitialVelocity * fallVelocityMultiplier,
            MaximumInitialVelocity =
                sourceBehavior.LostGripFall.MaximumInitialVelocity * fallVelocityMultiplier,
            Gravity = sourceBehavior.LostGripFall.Gravity * fallGravityMultiplier,
            MaximumFallVelocity =
                sourceBehavior.LostGripFall.MaximumFallVelocity * fallVelocityMultiplier,
            RegripDuration =
                sourceBehavior.LostGripFall.RegripDuration * fallRecoveryDurationMultiplier,
            ResumeIdleDuration =
                sourceBehavior.LostGripFall.ResumeIdleDuration * fallRecoveryDurationMultiplier,
            PointerSuppressionDuration =
                sourceBehavior.LostGripFall.PointerSuppressionDuration *
                fallRecoveryDurationMultiplier
        };
        var behavior = sourceBehavior with
        {
            Speed = speed,
            Pointer = pointer,
            SCurve = sourceBehavior.SCurve with { Normal = normalCurve, Fast = fastCurve },
            FastForward = sourceBehavior.FastForward with
            {
                MinimumDuration = sourceBehavior.FastForward.MinimumDuration *
                                  MathEx.Lerp(
                                      1f,
                                      context.DurationMultiplier,
                                      coefficients.FastForwardDurationBlend),
                MaximumDuration = sourceBehavior.FastForward.MaximumDuration *
                                  MathEx.Lerp(
                                      1f,
                                      context.DurationMultiplier,
                                      coefficients.FastForwardDurationBlend)
            },
            Rest = rest,
            LostGripFall = lostGripFall,
            TransitionMatrix = transitionMatrix,
            WalkBoutMinimumDuration = sourceBehavior.WalkBoutMinimumDuration * context.DurationMultiplier,
            WalkBoutMaximumDuration = sourceBehavior.WalkBoutMaximumDuration * context.DurationMultiplier
        };
        return behavior;
    }

    private static TransitionMatrixConfiguration ReweightTransitions(
        TransitionMatrixConfiguration source,
        IndividualTraits traits,
        float amount,
        IndividualTransitionVariationCoefficients coefficients) => new()
    {
        AfterForward = ReweightRow(source.AfterForward, traits, amount, coefficients),
        AfterCurve = ReweightRow(source.AfterCurve, traits, amount, coefficients),
        AfterSCurve = ReweightRow(source.AfterSCurve, traits, amount, coefficients),
        AfterFast = ReweightRow(source.AfterFast, traits, amount, coefficients)
    };

    private static TransitionRowConfiguration ReweightRow(
        TransitionRowConfiguration source,
        IndividualTraits traits,
        float amount,
        IndividualTransitionVariationCoefficients coefficients)
    {
        if (amount <= 0f)
        {
            return source with { Entries = source.Entries.ToArray() };
        }

        var entries = new WeightedTransitionConfiguration[source.Entries.Length];
        var total = 0f;
        for (var index = 0; index < source.Entries.Length; index++)
        {
            var entry = source.Entries[index];
            var personality = entry.Action switch
            {
                AutonomousAction.Forward =>
                    coefficients.ForwardActivity * traits.Activity +
                    coefficients.ForwardCalmness * traits.Calmness,
                AutonomousAction.ForwardExtension =>
                    coefficients.ForwardExtensionActivity * traits.Activity +
                    coefficients.ForwardExtensionBoldness * traits.Boldness,
                AutonomousAction.Curve =>
                    coefficients.CurveCuriosity * traits.Curiosity +
                    coefficients.CurveAgility * traits.Agility,
                AutonomousAction.SCurve =>
                    coefficients.SCurveCuriosity * traits.Curiosity +
                    coefficients.SCurveAgility * traits.Agility +
                    coefficients.SCurvePlayfulness * traits.Playfulness,
                AutonomousAction.FastForward =>
                    coefficients.FastForwardActivity * traits.Activity +
                    coefficients.FastForwardBoldness * traits.Boldness +
                    coefficients.FastForwardPlayfulness * traits.Playfulness,
                AutonomousAction.FastSCurve =>
                    coefficients.FastSCurveActivity * traits.Activity +
                    coefficients.FastSCurveAgility * traits.Agility +
                    coefficients.FastSCurvePlayfulness * traits.Playfulness,
                AutonomousAction.TurnAround =>
                    coefficients.TurnAroundCuriosity * traits.Curiosity +
                    coefficients.TurnAroundBoldness * traits.Boldness,
                AutonomousAction.LostGripFall =>
                    coefficients.LostGripPlayfulness * traits.Playfulness +
                    coefficients.LostGripLowAgility * (1f - traits.Agility),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(entry.Action),
                    entry.Action,
                    "Unsupported autonomous action.")
            };
            var multiplier = MathEx.Lerp(1f - amount, 1f + amount, personality);
            var weight = entry.Weight > 0f
                ? entry.Weight * multiplier
                : 0f;
            entries[index] = entry with { Weight = weight };
            total += weight;
        }

        for (var index = 0; index < entries.Length; index++)
        {
            entries[index] = entries[index] with { Weight = entries[index].Weight / total };
        }
        return new TransitionRowConfiguration { Entries = entries };
    }

    private static RestBandConfiguration[] ReweightRestBands(
        RestBandConfiguration[] source,
        IndividualTraits traits,
        float amount,
        IndividualRestVariationCoefficients coefficients)
    {
        if (amount <= 0f)
        {
            return source.ToArray();
        }

        var result = new RestBandConfiguration[source.Length];
        var total = 0f;
        for (var index = 0; index < source.Length; index++)
        {
            var longness = source.Length <= 1 ? 0f : index / (float)(source.Length - 1);
            var preference = MathEx.Lerp(
                coefficients.ShortRestBase + traits.Activity * coefficients.ShortRestActivity,
                coefficients.LongRestBase + traits.Calmness * coefficients.LongRestCalmness,
                longness);
            if (amount < 1f)
            {
                preference = MathEx.Lerp(1f, preference, amount);
            }
            var weight = source[index].Weight > 0f
                ? source[index].Weight * preference
                : 0f;
            result[index] = source[index] with { Weight = weight };
            total += weight;
        }
        if (total <= 0f)
        {
            return source.ToArray();
        }
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = result[index] with { Weight = result[index].Weight / total };
        }
        return result;
    }
}
