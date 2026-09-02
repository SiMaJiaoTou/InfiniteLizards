using InfiniteLizards.Gameplay.Genetics;
using InfiniteLizards.Gameplay.Phenotypes;

namespace DesktopLizard.Core;

/// <summary>
/// Projects a compiled breeding phenotype onto existing, validated runtime
/// configuration. Every multiplier is bounded here; genes never replace
/// solver epsilons, iteration limits, safety areas or legal state edges.
/// </summary>
internal static class BreedablePhenotypeProfileResolver
{
    private const float ValidatedDurationFloor = 0.05f;
    private const float MinimumFallPreparationStepCount = 3f;

    internal static IndividualTraits ResolveTraits(
        IndividualTraits identity,
        LizardPhenotype phenotype)
    {
        ArgumentNullException.ThrowIfNull(phenotype);
        var agility = Average(
            BreedablePhenotypeCompiler.Normalized(
                phenotype,
                "locomotion.turn-agility"),
            BreedablePhenotypeCompiler.Normalized(
                phenotype,
                "locomotion.balance"));
        var build = Average(
            BreedablePhenotypeCompiler.Normalized(
                phenotype,
                DefaultLizardTraitIds.BodyWidth),
            BreedablePhenotypeCompiler.Normalized(
                phenotype,
                "body.height"));
        return identity with
        {
            Activity = Trait01(phenotype, "temperament.activity"),
            Curiosity = Trait01(phenotype, "temperament.curiosity"),
            Playfulness = Trait01(phenotype, "temperament.playfulness"),
            Boldness = Trait01(phenotype, "temperament.boldness"),
            Agility = agility,
            Calmness = Trait01(phenotype, "temperament.calmness"),
            Build = build
        };
    }

    internal static BehaviorConfiguration ResolveBehavior(
        BehaviorConfiguration source,
        LizardPhenotype phenotype,
        float simulationRate)
    {
        var speedMultiplier = Trait(
            phenotype,
            DefaultLizardTraitIds.MaximumSpeed,
            1f,
            0.55f,
            1.8f);
        speedMultiplier = Math.Min(
            speedMultiplier,
            source.Speed.MaximumCrawl / source.Speed.ReferenceMaximumCrawl);
        var accelerationMultiplier = Trait(
            phenotype,
            "locomotion.acceleration",
            1f,
            0.65f,
            1.5f);
        var speed = source.Speed with
        {
            ReferenceMinimumCrawl = source.Speed.ReferenceMinimumCrawl * speedMultiplier,
            ReferenceMaximumCrawl = source.Speed.ReferenceMaximumCrawl * speedMultiplier,
            CrawlAcceleration = source.Speed.CrawlAcceleration * accelerationMultiplier,
            FastCrawlAcceleration = source.Speed.FastCrawlAcceleration * accelerationMultiplier
        };

        var disposition = Trait(
            phenotype,
            DefaultLizardTraitIds.PointerDisposition,
            0f,
            -1f,
            1f);
        var disposition01 = (disposition + 1f) * 0.5f;
        var responseMode = disposition < 0f
            ? PointerResponseMode.Avoid
            : PointerResponseMode.Chase;
        var attentionRadius = Trait(
            phenotype,
            "pointer.attention-radius",
            1f,
            0.45f,
            1.9f);
        var avoidanceRadius = Trait(
            phenotype,
            "pointer.avoidance-radius",
            1f,
            0.35f,
            1.8f);
        var triggerSpan = source.Pointer.TriggerMaximumDistance -
                          source.Pointer.TriggerMinimumDistance;
        var responseRadiusScale = responseMode == PointerResponseMode.Avoid
            ? avoidanceRadius
            : MathEx.Lerp(0.72f, 1.22f, disposition01);
        var triggerSpanMultiplier = Math.Clamp(
            attentionRadius * responseRadiusScale,
            0.4f,
            2.1f);
        var triggerMaximum = source.Pointer.TriggerMinimumDistance +
                             triggerSpan * triggerSpanMultiplier;
        var avoidanceDistance = source.Pointer.AvoidanceDistance;
        if (responseMode == PointerResponseMode.Avoid)
        {
            var minimumAvoidance = StrictlyGreater(
                source.Pointer.TriggerMinimumDistance,
                0.01f);
            var desiredAvoidance = (double)source.Pointer.StopDistance +
                                   source.Pointer.ApproachDistance;
            desiredAvoidance *= avoidanceRadius;
            avoidanceDistance = double.IsFinite(desiredAvoidance)
                ? Math.Max(minimumAvoidance, (float)desiredAvoidance)
                : minimumAvoidance;
            triggerMaximum = Math.Max(
                triggerMaximum,
                StrictlyGreater(avoidanceDistance, 0.01f));
        }
        var rearmGap = Math.Max(
            0.01f,
            source.Pointer.RearmDistance - source.Pointer.TriggerMaximumDistance);
        var lostGap = Math.Max(
            0.01f,
            source.Pointer.LostDistance - source.Pointer.TriggerMaximumDistance);
        var reactionDelay = Trait(
            phenotype,
            "pointer.reaction-delay",
            source.Pointer.AttentionDuration,
            0.04f,
            1.4f);
        var persistence = Trait01(phenotype, "pointer.chase-persistence");
        var recovery = Trait(
            phenotype,
            "pointer.recovery",
            1f,
            0.2f,
            1.8f);
        var observeToChase = TransitionFactor(
            phenotype,
            "behavior.transition.observe-to-chase");
        var cooldownScale = Math.Clamp(1f / recovery, 0.65f, 1.6f);
        var pointer = source.Pointer with
        {
            ResponseMode = responseMode,
            TriggerMaximumDistance = triggerMaximum,
            AvoidanceDistance = avoidanceDistance,
            RearmDistance = StrictlyGreater(triggerMaximum, rearmGap),
            LostDistance = StrictlyGreater(triggerMaximum, lostGap),
            AttentionDuration = Math.Max(
                0.02f,
                reactionDelay *
                MathEx.Lerp(1.25f, 0.72f, disposition01) *
                MathEx.Lerp(1.45f, 0.65f, FactorToUnit(observeToChase))),
            StopDistance = source.Pointer.StopDistance *
                           MathEx.Lerp(1.22f, 0.78f, disposition01),
            LostGraceDuration = source.Pointer.LostGraceDuration *
                                MathEx.Lerp(0.6f, 1.55f, persistence),
            MinimumCooldown = source.Pointer.MinimumCooldown * cooldownScale,
            MaximumCooldown = source.Pointer.MaximumCooldown * cooldownScale
        };

        var idleToExplore = TransitionFactor(
            phenotype,
            "behavior.transition.idle-to-explore");
        var exploreToIdle = TransitionFactor(
            phenotype,
            "behavior.transition.explore-to-idle");
        var endurance = Trait01(phenotype, "locomotion.endurance");
        var fallControl = Trait01(phenotype, "locomotion.fall-control");
        var swimAffinity = Trait01(phenotype, "locomotion.swim-affinity");
        var patience = Trait01(phenotype, "temperament.patience");
        var sleepiness = Trait01(phenotype, "temperament.sleepiness");
        var foodDrive = Trait01(phenotype, "temperament.food-drive");
        var clickSensitivity = Trait01(phenotype, "pointer.click-sensitivity");
        var dragTolerance = Trait01(phenotype, "pointer.drag-tolerance");
        var restToLongRest = FactorToUnit(TransitionFactor(
            phenotype,
            "behavior.transition.rest-to-long-rest"));
        var decisions = source.Decisions with
        {
            ObserveChanceBase = Math.Clamp(
                source.Decisions.ObserveChanceBase *
                MathEx.Lerp(0.72f, 1.32f, FactorToUnit(idleToExplore)),
                source.Decisions.ObserveChanceMinimum,
                source.Decisions.ObserveChanceMaximum),
            RestChanceBase = Math.Clamp(
                source.Decisions.RestChanceBase *
                MathEx.Lerp(0.78f, 1.22f, FactorToUnit(exploreToIdle)) *
                MathEx.Lerp(1.22f, 0.72f, endurance) *
                MathEx.Lerp(0.72f, 1.28f, sleepiness),
                0f,
                1f - source.Decisions.RestChanceCalmFactor),
            WalkPaceBase = Math.Clamp(
                source.Decisions.WalkPaceBase +
                MathEx.Lerp(-0.05f, 0.22f, foodDrive),
                0f,
                1f)
        };

        var timing = source.Timing with
        {
            Observe = ScaleDurationRange(
                source.Timing.Observe,
                MathEx.Lerp(0.65f, 1.65f, patience)),
            ReleaseSettleDuration = source.Timing.ReleaseSettleDuration *
                                    MathEx.Lerp(1.35f, 0.70f, dragTolerance)
        };
        var escapeScale = MathEx.Lerp(0.76f, 1.28f, clickSensitivity) *
                          MathEx.Lerp(1.28f, 0.72f, dragTolerance);
        var escapeSprint = source.EscapeSprint with
        {
            MinimumDistance = source.EscapeSprint.MinimumDistance * escapeScale,
            MaximumDistance = source.EscapeSprint.MaximumDistance * escapeScale,
            Acceleration = source.EscapeSprint.Acceleration *
                           MathEx.Lerp(0.82f, 1.22f, clickSensitivity)
        };
        var fallVelocityScale = MathEx.Lerp(1.12f, 0.80f, fallControl);
        var maximumFallVelocity = Math.Max(
            source.LostGripFall.MaximumInitialVelocity,
            ScaleFinitePositive(
                source.LostGripFall.MaximumFallVelocity,
                fallVelocityScale));
        var requiredPreparationDistance =
            MinimumFallPreparationStepCount *
            maximumFallVelocity /
            simulationRate;
        var normalizedPreparationDistance = MathF.BitIncrement(
            requiredPreparationDistance);
        if (!float.IsFinite(normalizedPreparationDistance))
        {
            normalizedPreparationDistance = requiredPreparationDistance;
        }
        var lostGripFall = source.LostGripFall with
        {
            MinimumDistance = Math.Max(
                source.LostGripFall.MinimumDistance,
                normalizedPreparationDistance),
            ReachLeadDistance = Math.Max(
                source.LostGripFall.ReachLeadDistance,
                normalizedPreparationDistance),
            Gravity = ScaleFinitePositive(
                source.LostGripFall.Gravity,
                MathEx.Lerp(1.18f, 0.72f, fallControl)),
            MaximumFallVelocity = maximumFallVelocity,
            RegripDuration = ScaleFinitePositive(
                source.LostGripFall.RegripDuration,
                MathEx.Lerp(1.18f, 0.78f, fallControl))
        };
        var waveAmplitude = MathEx.Lerp(0.72f, 1.38f, swimAffinity);
        var waveDuration = MathEx.Lerp(1.22f, 0.76f, swimAffinity);
        var normalCycleDuration = ScaleDurationRange(
            new DurationRangeConfiguration(
                source.SCurve.Normal.MinimumCycleDuration,
                source.SCurve.Normal.MaximumCycleDuration),
            waveDuration);
        var fastCycleDuration = ScaleDurationRange(
            new DurationRangeConfiguration(
                source.SCurve.Fast.MinimumCycleDuration,
                source.SCurve.Fast.MaximumCycleDuration),
            waveDuration);
        var sCurve = source.SCurve with
        {
            Normal = source.SCurve.Normal with
            {
                MinimumAmplitude = source.SCurve.Normal.MinimumAmplitude * waveAmplitude,
                MaximumAmplitude = source.SCurve.Normal.MaximumAmplitude * waveAmplitude,
                MinimumCycleDuration = normalCycleDuration.Minimum,
                MaximumCycleDuration = normalCycleDuration.Maximum
            },
            Fast = source.SCurve.Fast with
            {
                MinimumAmplitude = source.SCurve.Fast.MinimumAmplitude * waveAmplitude,
                MaximumAmplitude = source.SCurve.Fast.MaximumAmplitude * waveAmplitude,
                MinimumCycleDuration = fastCycleDuration.Minimum,
                MaximumCycleDuration = fastCycleDuration.Maximum
            }
        };
        var enduranceScale = MathEx.Lerp(0.62f, 1.75f, endurance);
        var walkBoutDuration = ScaleDurationRange(
            new DurationRangeConfiguration(
                source.WalkBoutMinimumDuration,
                source.WalkBoutMaximumDuration),
            enduranceScale);
        // Individual variation runs before genome projection and can already
        // shrink this legal 0.05-floor range. Normalize it here even though no
        // breeding locus adds another fast-forward duration multiplier.
        var fastForwardDuration = ScaleDurationRange(
            new DurationRangeConfiguration(
                source.FastForward.MinimumDuration,
                source.FastForward.MaximumDuration),
            1f);

        return source with
        {
            Speed = speed,
            Pointer = pointer,
            Decisions = decisions,
            Timing = timing,
            EscapeSprint = escapeSprint,
            LostGripFall = lostGripFall,
            SCurve = sCurve,
            FastForward = source.FastForward with
            {
                MinimumDuration = fastForwardDuration.Minimum,
                MaximumDuration = fastForwardDuration.Maximum
            },
            Rest = ResolveRest(source.Rest, sleepiness, restToLongRest),
            WalkBoutMinimumDuration = walkBoutDuration.Minimum,
            WalkBoutMaximumDuration = walkBoutDuration.Maximum,
            TransitionMatrix = ResolveTransitions(source.TransitionMatrix, phenotype)
        };
    }

    internal static GaitConfiguration ResolveGait(
        GaitConfiguration source,
        LizardPhenotype phenotype)
    {
        var legLength = Trait(phenotype, "limbs.length", 1f, 0.82f, 1.18f);
        var frontRear = Trait(
            phenotype,
            "limbs.front-rear-ratio",
            1f,
            0.9f,
            1.1f);
        var stride = Trait(phenotype, "locomotion.stride", 1f, 0.78f, 1.22f);
        var stepHeight = Trait(
            phenotype,
            "locomotion.step-height",
            1f,
            0.7f,
            1.32f);
        var gaitKind = TraitChoice(phenotype, "locomotion.gait", 0, 5);
        var gaitProfile = gaitKind switch
        {
            0 => (Cadence: 1.00f, Stagger: 0.72f, Lift: 0.92f, Reach: 1.00f, Skew: 0.82f),
            1 => (Cadence: 0.92f, Stagger: 0.48f, Lift: 1.00f, Reach: 1.00f, Skew: 1.00f),
            2 => (Cadence: 1.14f, Stagger: 1.34f, Lift: 0.86f, Reach: 1.08f, Skew: 1.12f),
            3 => (Cadence: 0.76f, Stagger: 0.32f, Lift: 1.42f, Reach: 0.88f, Skew: 0.68f),
            4 => (Cadence: 1.08f, Stagger: 1.08f, Lift: 0.82f, Reach: 1.34f, Skew: 1.28f),
            _ => (Cadence: 0.88f, Stagger: 1.62f, Lift: 1.08f, Reach: 1.12f, Skew: 1.18f)
        };
        var asymmetry = Trait(
            phenotype,
            "limbs.left-right-asymmetry",
            0f,
            0f,
            0.28f);
        var stanceSkew = source.StanceSkew
            .Select((value, index) =>
                value * gaitProfile.Skew +
                (index % 2 == 0 ? 1f : -1f) * asymmetry * 18f)
            .ToArray();
        var fastStepDuration = Math.Max(
            source.MinimumStepDuration,
            source.FastStepDuration * gaitProfile.Cadence);
        var slowStepDuration = Math.Max(
            fastStepDuration,
            source.SlowStepDuration * gaitProfile.Cadence);
        var pairStaggerMinimum = source.PairStaggerMinimum * gaitProfile.Stagger;
        var pairStaggerMaximum = Math.Max(
            pairStaggerMinimum,
            source.PairStaggerMaximum * gaitProfile.Stagger);
        return source with
        {
            FrontLegLinkLength = source.FrontLegLinkLength * legLength * frontRear,
            RearLegLinkLength = source.RearLegLinkLength * legLength / frontRear,
            StepHeightBase = source.StepHeightBase * stepHeight * gaitProfile.Lift,
            MinimumStepHeight = source.MinimumStepHeight * stepHeight * gaitProfile.Lift,
            MaximumStepHeight = source.MaximumStepHeight * stepHeight * gaitProfile.Lift,
            MinimumLead = source.MinimumLead * stride,
            MaximumLead = source.MaximumLead * stride,
            FrontLateralReach = source.FrontLateralReach * legLength * gaitProfile.Reach,
            RearLateralReach = source.RearLateralReach * legLength * gaitProfile.Reach,
            StanceSkew = stanceSkew,
            SlowStepDuration = slowStepDuration,
            FastStepDuration = fastStepDuration,
            PairStaggerMinimum = pairStaggerMinimum,
            PairStaggerMaximum = pairStaggerMaximum
        };
    }

    internal static AppearanceConfiguration ResolveAppearance(
        AppearanceConfiguration source,
        BreedableVisualPhenotype visual)
    {
        var length = Math.Clamp(visual.Body.LengthRatio, 0.82f, 1.22f);
        var width = Math.Clamp(visual.Body.WidthRatio, 0.75f, 1.3f);
        var limbThickness = Math.Clamp(visual.Limbs.ThicknessRatio, 0.65f, 1.45f);
        var widths = new float[source.BodyWidths.Length];
        for (var index = 0; index < widths.Length; index++)
        {
            var normalizedIndex = widths.Length <= 1
                ? 0f
                : index / (float)(widths.Length - 1);
            var headWeight = MathEx.Clamp01(1f - normalizedIndex / 0.18f);
            var shoulderWeight = Triangle(normalizedIndex, 0.13f, 0.2f);
            var hipWeight = Triangle(normalizedIndex, 0.36f, 0.22f);
            var bellyWeight = Triangle(normalizedIndex, 0.34f, 0.34f);
            var tailWeight = MathEx.Clamp01((normalizedIndex - 0.35f) / 0.65f);
            var regional = 1f;
            regional *= MathEx.Lerp(
                1f,
                Math.Clamp(
                    visual.Body.HeadSizeRatio * visual.Body.HeadWidthRatio,
                    0.72f,
                    1.38f),
                headWeight);
            regional *= MathEx.Lerp(
                1f,
                Math.Clamp(visual.Body.ShoulderMassRatio, 0.72f, 1.35f),
                shoulderWeight);
            regional *= MathEx.Lerp(
                1f,
                Math.Clamp(visual.Body.HipMassRatio, 0.72f, 1.35f),
                hipWeight);
            regional *= MathEx.Lerp(
                1f,
                MathEx.Lerp(0.82f, 1.18f, visual.Body.BellyRoundness),
                bellyWeight);
            var tailBase = Math.Clamp(visual.Tail.BaseThicknessRatio, 0.65f, 1.4f);
            var tailTaper = MathEx.Lerp(1.12f, 0.82f, visual.Tail.Taper);
            regional *= MathEx.Lerp(
                1f,
                MathEx.Lerp(tailBase, tailTaper, tailWeight),
                tailWeight);
            widths[index] = Math.Max(0f, source.BodyWidths[index] * width * regional);
        }

        return source with
        {
            SpineLinkLength = source.SpineLinkLength * length,
            SpineMaximumBend = source.SpineMaximumBend * MathEx.Lerp(
                0.72f,
                1.38f,
                MathEx.Clamp01((visual.Body.Flexibility - 0.25f) / 0.75f)),
            HeadAnchorOffset = source.HeadAnchorOffset * length *
                               Math.Clamp(visual.Body.HeadSizeRatio, 0.8f, 1.25f) *
                               Math.Clamp(visual.Body.NeckLengthRatio, 0.72f, 1.35f),
            SpineRestCurveAmplitude = source.SpineRestCurveAmplitude * MathEx.Lerp(
                0.55f,
                1.55f,
                MathEx.Clamp01((visual.Body.SpineArch + 0.35f) / 1f)),
            BodyWidths = widths,
            BodyColor = ToRgb(visual.Palette.Primary),
            ShadowColor = Darken(visual.Palette.Primary, 0.62f),
            PupilColor = ToRgb(visual.Palette.Eye),
            ShadowOpacity = Math.Clamp(
                source.ShadowOpacity *
                MathEx.Lerp(0.78f, 1.25f, visual.Skin.Roughness) *
                MathEx.Lerp(1.18f, 0.74f, visual.Skin.Gloss),
                0f,
                1f),
            LimbWidth = source.LimbWidth * limbThickness,
            ShadowLimbWidth = source.ShadowLimbWidth * limbThickness,
            HitLimbWidth = source.HitLimbWidth * limbThickness
        };
    }

    internal static RenderingConfiguration ResolveRendering(
        RenderingConfiguration source,
        BreedableVisualPhenotype visual)
    {
        var head = Math.Clamp(visual.Body.HeadSizeRatio, 0.72f, 1.38f);
        var eye = Math.Clamp(visual.Body.EyeSizeRatio, 0.65f, 1.48f);
        var foot = Math.Clamp(visual.Limbs.FootSizeRatio, 0.65f, 1.5f);
        var noseRadius = source.NoseRadius * head;
        var frontFootRadius = source.FrontFootRadius * foot;
        var rearFootRadius = source.RearFootRadius * foot;
        var eyeShadowRadius = source.EyeShadowRadius * eye;
        var eyeRadius = source.EyeRadius * eye;
        var pupilRadius = source.PupilRadius * eye;
        return source with
        {
            NoseRadius = noseRadius,
            FrontFootRadius = frontFootRadius,
            RearFootRadius = rearFootRadius,
            EyeShadowRadius = eyeShadowRadius,
            EyeRadius = eyeRadius,
            PupilOffset = source.PupilOffset * eye,
            PupilRadius = pupilRadius,
            MinimumPupilRadius = source.MinimumPupilRadius * eye,
            HitFootRadius = Math.Max(source.HitFootRadius, Math.Max(frontFootRadius, rearFootRadius)),
            HitEyeRadius = Math.Max(
                source.HitEyeRadius,
                Math.Max(eyeShadowRadius, eyeRadius + pupilRadius))
        };
    }

    internal static SecondaryMotionConfiguration ResolveSecondaryMotion(
        SecondaryMotionConfiguration source,
        LizardPhenotype phenotype,
        BreedableVisualPhenotype visual)
    {
        var idleSway = Trait01(phenotype, "locomotion.idle-sway");
        var tailFlexibility = MathEx.Clamp01(
            (visual.Tail.Flexibility - 0.15f) / 0.85f);
        var swayMultiplier = MathEx.Lerp(0.55f, 1.5f, idleSway) *
                             MathEx.Lerp(0.62f, 1.42f, tailFlexibility);
        return source with
        {
            IdleTailAmplitude = source.IdleTailAmplitude * swayMultiplier,
            ObserveTailAmplitude = source.ObserveTailAmplitude * swayMultiplier,
            TailPrimaryFrequency = source.TailPrimaryFrequency *
                                   MathEx.Lerp(0.72f, 1.28f, tailFlexibility),
            EyeLateralOffset = source.EyeLateralOffset *
                               Math.Clamp(visual.Body.EyeSpacingRatio, 0.72f, 1.35f),
            EyeForwardOffset = source.EyeForwardOffset *
                               Math.Clamp(visual.Body.HeadSizeRatio, 0.8f, 1.25f),
            NoseForwardOffset = source.NoseForwardOffset *
                                Math.Clamp(visual.Body.SnoutLengthRatio, 0.72f, 1.4f)
        };
    }

    private static TransitionMatrixConfiguration ResolveTransitions(
        TransitionMatrixConfiguration source,
        LizardPhenotype phenotype) => new()
    {
        AfterForward = ResolveTransitionRow(source.AfterForward, phenotype),
        AfterCurve = ResolveTransitionRow(source.AfterCurve, phenotype),
        AfterSCurve = ResolveTransitionRow(source.AfterSCurve, phenotype),
        AfterFast = ResolveTransitionRow(source.AfterFast, phenotype)
    };

    private static TransitionRowConfiguration ResolveTransitionRow(
        TransitionRowConfiguration source,
        LizardPhenotype phenotype)
    {
        var entries = new WeightedTransitionConfiguration[source.Entries.Length];
        var grip = Trait01(phenotype, "locomotion.grip");
        var swimAffinity = Trait01(phenotype, "locomotion.swim-affinity");
        var foodDrive = Trait01(phenotype, "temperament.food-drive");
        var total = 0f;
        for (var index = 0; index < source.Entries.Length; index++)
        {
            var entry = source.Entries[index];
            var traitFactor = entry.Action switch
            {
                AutonomousAction.Forward => TransitionFactor(
                    phenotype,
                    "behavior.transition.idle-to-explore"),
                AutonomousAction.ForwardExtension => TransitionFactor(
                    phenotype,
                    "behavior.transition.novelty-seeking"),
                AutonomousAction.Curve => TransitionFactor(
                    phenotype,
                    "behavior.transition.wander-to-curve"),
                AutonomousAction.SCurve => TransitionFactor(
                    phenotype,
                    "behavior.transition.curve-to-s-curve"),
                AutonomousAction.FastForward or AutonomousAction.FastSCurve =>
                    TransitionFactor(phenotype, "behavior.transition.chase-to-sprint"),
                AutonomousAction.TurnAround => TransitionFactor(
                    phenotype,
                    "behavior.transition.novelty-seeking"),
                AutonomousAction.LostGripFall => TransitionFactor(
                    phenotype,
                    "behavior.transition.edge-to-panic"),
                _ => 1f
            };
            if (entry.Action == AutonomousAction.LostGripFall)
            {
                traitFactor *= MathEx.Lerp(1.75f, 0.42f, grip);
            }
            if (entry.Action is AutonomousAction.SCurve or
                AutonomousAction.FastSCurve)
            {
                traitFactor *= MathEx.Lerp(0.62f, 1.58f, swimAffinity);
            }
            if (entry.Action is AutonomousAction.FastForward or
                AutonomousAction.FastSCurve)
            {
                traitFactor *= MathEx.Lerp(0.68f, 1.48f, foodDrive);
            }
            var weight = entry.Weight > 0f ? entry.Weight * traitFactor : 0f;
            entries[index] = entry with { Weight = weight };
            total += weight;
        }

        if (!(total > 0f) || !float.IsFinite(total))
        {
            return source with { Entries = source.Entries.ToArray() };
        }
        for (var index = 0; index < entries.Length; index++)
        {
            entries[index] = entries[index] with
            {
                Weight = entries[index].Weight / total
            };
        }
        return new TransitionRowConfiguration { Entries = entries };
    }

    private static RestConfiguration ResolveRest(
        RestConfiguration source,
        float sleepiness,
        float restToLongRest)
    {
        var entries = new RestBandConfiguration[source.Bands.Length];
        var total = 0f;
        for (var index = 0; index < source.Bands.Length; index++)
        {
            var band = source.Bands[index];
            var longness = source.Bands.Length <= 1
                ? 0f
                : index / (float)(source.Bands.Length - 1);
            var sleepFactor = MathEx.Lerp(
                MathEx.Lerp(1.42f, 0.62f, sleepiness),
                MathEx.Lerp(0.58f, 1.78f, sleepiness),
                longness);
            var transitionFactor = MathEx.Lerp(
                MathEx.Lerp(1.32f, 0.72f, restToLongRest),
                MathEx.Lerp(0.72f, 1.62f, restToLongRest),
                longness);
            var weight = band.Weight * sleepFactor * transitionFactor;
            entries[index] = band with { Weight = weight };
            total += weight;
        }

        if (!(total > 0f) || !float.IsFinite(total))
        {
            return source with { Bands = source.Bands.ToArray() };
        }
        for (var index = 0; index < entries.Length; index++)
        {
            entries[index] = entries[index] with
            {
                Weight = entries[index].Weight / total
            };
        }
        return source with { Bands = entries };
    }

    private static DurationRangeConfiguration ScaleDurationRange(
        DurationRangeConfiguration source,
        float multiplier)
    {
        var minimum = ScaleFiniteAtLeast(
            source.Minimum,
            multiplier,
            ValidatedDurationFloor);
        var maximum = ScaleFiniteAtLeast(
            source.Maximum,
            multiplier,
            minimum);
        return new DurationRangeConfiguration(minimum, Math.Max(minimum, maximum));
    }

    private static float ScaleFinitePositive(float value, float multiplier) =>
        ScaleFiniteAtLeast(value, multiplier, float.Epsilon);

    private static float ScaleFiniteAtLeast(
        float value,
        float multiplier,
        float floor)
    {
        var scaled = (double)value * multiplier;
        return (float)Math.Clamp(
            double.IsFinite(scaled) ? scaled : float.MaxValue,
            floor,
            float.MaxValue);
    }

    private static float TransitionFactor(LizardPhenotype phenotype, string traitId) =>
        Trait(phenotype, traitId, 1f, 0.25f, 2.5f);

    private static float FactorToUnit(float factor) =>
        MathEx.Clamp01((factor - 0.25f) / 2.25f);

    private static float Trait01(LizardPhenotype phenotype, string traitId) =>
        (float)BreedablePhenotypeCompiler.Normalized(phenotype, traitId);

    private static int TraitChoice(
        LizardPhenotype phenotype,
        string traitId,
        int minimum,
        int maximum) =>
        (int)Math.Clamp(
            Math.Round(
                BreedablePhenotypeCompiler.Value(
                    phenotype,
                    traitId,
                    minimum,
                    minimum,
                    maximum),
                MidpointRounding.AwayFromZero),
            minimum,
            maximum);

    private static float Trait(
        LizardPhenotype phenotype,
        string traitId,
        float fallback,
        float minimum,
        float maximum) =>
        (float)BreedablePhenotypeCompiler.Value(
            phenotype,
            traitId,
            fallback,
            minimum,
            maximum);

    private static float Average(double first, double second) =>
        (float)Math.Clamp((first + second) * 0.5d, 0d, 1d);

    private static float StrictlyGreater(float value, float requestedGap)
    {
        var candidate = (float)((double)value + Math.Max(0.01f, requestedGap));
        if (float.IsFinite(candidate) && candidate > value)
        {
            return candidate;
        }

        var incremented = MathF.BitIncrement(value);
        return float.IsFinite(incremented) && incremented > value
            ? incremented
            : value;
    }

    private static float Triangle(float value, float center, float halfWidth) =>
        MathEx.Clamp01(1f - MathF.Abs(value - center) / halfWidth);

    private static RgbConfiguration ToRgb(BreedableColor color) =>
        new(color.Red, color.Green, color.Blue);

    private static RgbConfiguration Darken(BreedableColor color, float multiplier) =>
        new(
            Math.Clamp((int)MathF.Round(color.Red * multiplier), 0, 255),
            Math.Clamp((int)MathF.Round(color.Green * multiplier), 0, 255),
            Math.Clamp((int)MathF.Round(color.Blue * multiplier), 0, 255));
}
