using System.Collections.Immutable;
using System.Numerics;
using DesktopLizard.Core;
using InfiniteLizards.Gameplay.Breeding;
using InfiniteLizards.Gameplay.Genetics;
using InfiniteLizards.Gameplay.Phenotypes;

internal static class BreedablePhenotypeCompilerSelfTest
{
    public static BreedablePhenotypeCompilerSelfTestResult Run()
    {
        try
        {
            var assertions = 0;
            LegacyProfileRemainsUnchanged(ref assertions);
            SameGenomeCompilesStably(ref assertions);
            DormantPartDimensionsStayHidden(ref assertions);
            ExtremeGenomesRemainFiniteAndValid(ref assertions);
            KeyTraitsChangeVisibleAndBehaviorProfiles(ref assertions);
            FormerlyReadoutOnlyTraitsHaveObservableEffects(ref assertions);
            NonzeroPairStaggerRangeSurvivesExtremeGait(ref assertions);
            NarrowPointerSpansCanResolveAvoidance(ref assertions);
            BoundaryDurationAndFallProfilesRemainValid(ref assertions);
            PointerTraitsDriveOppositeResponses(ref assertions);
            return new BreedablePhenotypeCompilerSelfTestResult(
                true,
                assertions,
                "Genome expression compiles into stable, bounded visual and runtime profiles.");
        }
        catch (Exception exception)
        {
            return new BreedablePhenotypeCompilerSelfTestResult(
                false,
                0,
                exception.Message);
        }
    }

    private static void DormantPartDimensionsStayHidden(ref int assertions)
    {
        var phenotype = new LizardPhenotype(
            "custom.hidden-dimensions.v1",
            ImmutableArray.Create(
                Trait(
                    DefaultLizardTraitIds.DorsalFinPresent,
                    TraitValueKind.Toggle,
                    1d,
                    isExpressed: true),
                Trait(
                    "appendage.dorsal-fin.height",
                    TraitValueKind.Continuous,
                    1.8d,
                    isExpressed: false),
                Trait(
                    "appendage.dorsal-fin.length",
                    TraitValueKind.Continuous,
                    1d,
                    isExpressed: false),
                Trait(
                    "appendage.dorsal-fin.shape",
                    TraitValueKind.Choice,
                    4d,
                    isExpressed: false)));
        var visual = BreedablePhenotypeCompiler.CompileVisual(phenotype);
        Assert(visual.Appendages.DorsalFin.IsPresent &&
               visual.Appendages.DorsalFin.HeightRatio == 0f &&
               visual.Appendages.DorsalFin.LengthRatio == 0f,
            "A dormant part dimension leaked into the compiled visual phenotype.",
            ref assertions);
    }

    private static void LegacyProfileRemainsUnchanged(ref int assertions)
    {
        var configuration = LizardConfiguration.Default;
        var profile = IndividualProfileFactory.Create(
            configuration,
            0,
            applyVariation: false);
        Assert(profile.VisualPhenotype is null,
            "Legacy profile unexpectedly gained a breeding phenotype.", ref assertions);
        Assert(ReferenceEquals(profile.Behavior, configuration.Behavior) &&
               ReferenceEquals(profile.Gait, configuration.Gait) &&
               ReferenceEquals(profile.Physics, configuration.Physics) &&
               ReferenceEquals(profile.Appearance, configuration.Appearance) &&
               ReferenceEquals(profile.SecondaryMotion, configuration.SecondaryMotion) &&
               ReferenceEquals(profile.Rendering, configuration.Rendering) &&
               ReferenceEquals(profile.Runtime, configuration.Runtime),
            "Variation-disabled legacy profile stopped retaining exact configured sections.",
            ref assertions);
        Assert(LizardProfile.Default.VisualPhenotype is null &&
               LizardProfile.Default.Appearance == configuration.Appearance &&
               LizardProfile.Default.Behavior == configuration.Behavior,
            "LizardProfile.Default changed after adding breedable phenotype support.",
            ref assertions);
    }

    private static void SameGenomeCompilesStably(ref int assertions)
    {
        var registry = LizardTraitRegistry.Default;
        var firstGenome = registry.CreateFounder(0xBEEFUL);
        var secondGenome = registry.CreateFounder(0xBEEFUL);
        Assert(firstGenome.SchemaVersion == secondGenome.SchemaVersion &&
               firstGenome.RegistryId == secondGenome.RegistryId &&
               firstGenome.Genes.SequenceEqual(secondGenome.Genes),
            "Founder generation is not field-stable for the same seed.", ref assertions);

        var first = IndividualProfileFactory.CreateFromGenome(
            LizardConfiguration.Default,
            firstGenome,
            registry);
        var second = IndividualProfileFactory.CreateFromGenome(
            LizardConfiguration.Default,
            secondGenome,
            registry);
        Assert(first.VisualPhenotype == second.VisualPhenotype,
            "The same genome produced a different visual phenotype.", ref assertions);
        Assert(first.Traits == second.Traits &&
               first.Behavior.Speed.ReferenceMinimumCrawl ==
               second.Behavior.Speed.ReferenceMinimumCrawl &&
               first.Behavior.Pointer.TriggerMaximumDistance ==
               second.Behavior.Pointer.TriggerMaximumDistance &&
               first.Behavior.TransitionMatrix.AfterForward.Entries.SequenceEqual(
                   second.Behavior.TransitionMatrix.AfterForward.Entries) &&
               first.Gait.FrontLegLinkLength == second.Gait.FrontLegLinkLength &&
               first.Gait.StanceSkew.SequenceEqual(second.Gait.StanceSkew) &&
               first.Physics == second.Physics &&
               first.Appearance.BodyColor == second.Appearance.BodyColor &&
               first.Appearance.SpineLinkLength == second.Appearance.SpineLinkLength,
            "The same genome produced a different runtime profile.", ref assertions);
    }

    private static void ExtremeGenomesRemainFiniteAndValid(ref int assertions)
    {
        var registry = LizardTraitRegistry.Default;
        foreach (var allele in new[] { 0d, 1d })
        {
            var genome = UniformGenome(registry, allele);
            var profile = IndividualProfileFactory.CreateFromGenome(
                LizardConfiguration.Default,
                genome,
                registry,
                behaviorSeed: 7319);
            var visual = profile.VisualPhenotype ??
                         throw new InvalidOperationException("Genome profile lost its visual phenotype.");
            var resolved = profile.Source with
            {
                Behavior = profile.Behavior,
                Gait = profile.Gait,
                Physics = profile.Physics,
                Appearance = profile.Appearance,
                SecondaryMotion = profile.SecondaryMotion,
                Rendering = profile.Rendering,
                Runtime = profile.Runtime
            };
            Assert(resolved.Validate().Count == 0,
                $"Extreme allele {allele} produced an invalid resolved profile.", ref assertions);
            Assert(AllFinite(profile, visual),
                $"Extreme allele {allele} produced NaN/infinity.", ref assertions);
            Assert(visual.Limbs.LegPairCount is >=
                       BreedableLimbMorphology.MinimumLegPairCount and <=
                       BreedableLimbMorphology.MaximumLegPairCount &&
                   visual.Limbs.VisibleJointCount is >=
                       BreedableLimbMorphology.MinimumVisibleJointCount and <=
                       BreedableLimbMorphology.MaximumVisibleJointCount &&
                   visual.Limbs.ToeCount is >= 2 and <= 7,
                "Compiled structural presentation counts escaped their contracts.",
                ref assertions);
            Assert(visual.Limbs.PresentationLegCount <=
                       BreedableLimbMorphology.MaximumPresentationLegCount &&
                   visual.Limbs.PresentationLegCount * visual.Limbs.VisibleJointCount <=
                       BreedableLimbMorphology.MaximumVisibleLegSegmentCount &&
                   visual.Tail.Spikes.Count <= BreedableTailSpikes.MaximumCount,
                "Compiled morphology exceeded the renderer primitive budget.",
                ref assertions);
            Assert(new ProceduralLizard(profile).Legs.Count ==
                   BreedableLimbMorphology.RuntimeSupportLegCount,
                "Presentation topology changed the proven four-support-leg rig.",
                ref assertions);
        }
    }

    private static void KeyTraitsChangeVisibleAndBehaviorProfiles(ref int assertions)
    {
        var registry = LizardTraitRegistry.Default;
        var low = GenomeWith(
            registry,
            0.5d,
            ("color.base-hue", 0.02d),
            (DefaultLizardTraitIds.BodyLength, 0.05d),
            (DefaultLizardTraitIds.BodyWidth, 0.08d),
            (DefaultLizardTraitIds.MaximumSpeed, 0.05d),
            (DefaultLizardTraitIds.PointerDisposition, 0.05d),
            ("behavior.transition.wander-to-curve", 0.05d),
            (DefaultLizardTraitIds.LegPairCount, 0d),
            (DefaultLizardTraitIds.LegJointCount, 0d));
        var high = GenomeWith(
            registry,
            0.5d,
            ("color.base-hue", 0.68d),
            (DefaultLizardTraitIds.BodyLength, 0.95d),
            (DefaultLizardTraitIds.BodyWidth, 0.92d),
            (DefaultLizardTraitIds.MaximumSpeed, 0.95d),
            (DefaultLizardTraitIds.PointerDisposition, 0.95d),
            ("behavior.transition.wander-to-curve", 0.95d),
            (DefaultLizardTraitIds.LegPairCount, 1d),
            (DefaultLizardTraitIds.LegJointCount, 1d));
        var lowProfile = IndividualProfileFactory.CreateFromGenome(
            LizardConfiguration.Default,
            low,
            registry,
            behaviorSeed: 42);
        var highProfile = IndividualProfileFactory.CreateFromGenome(
            LizardConfiguration.Default,
            high,
            registry,
            behaviorSeed: 42);
        var lowVisual = lowProfile.VisualPhenotype!;
        var highVisual = highProfile.VisualPhenotype!;

        Assert(lowVisual.Palette.Primary != highVisual.Palette.Primary &&
               lowProfile.Appearance.BodyColor != highProfile.Appearance.BodyColor,
            "Main-hue genes do not change the visible palette/profile color.",
            ref assertions);
        Assert(lowVisual.Body.LengthRatio < highVisual.Body.LengthRatio &&
               lowProfile.Appearance.SpineLinkLength < highProfile.Appearance.SpineLinkLength,
            "Body-length genes do not change visual and active-body proportions.",
            ref assertions);
        Assert(lowProfile.Behavior.Speed.ReferenceMaximumCrawl <
               highProfile.Behavior.Speed.ReferenceMaximumCrawl,
            "Maximum-speed genes do not change active locomotion.", ref assertions);
        Assert(lowProfile.Behavior.Pointer.TriggerMaximumDistance <
               highProfile.Behavior.Pointer.TriggerMaximumDistance &&
               lowProfile.Behavior.Pointer.AttentionDuration >
               highProfile.Behavior.Pointer.AttentionDuration,
            "Pointer-disposition genes do not create a perceptible mouse response.",
            ref assertions);
        Assert(Weight(lowProfile, AutonomousAction.Curve) <
               Weight(highProfile, AutonomousAction.Curve),
            "Action-preference genes do not change a legal transition weight.",
            ref assertions);
        Assert(lowVisual.Limbs.LegPairCount == 1 &&
               highVisual.Limbs.LegPairCount == 5 &&
               lowVisual.Limbs.VisibleJointCount == 1 &&
               highVisual.Limbs.VisibleJointCount == 5,
            "Structural genes do not reach the portrait/detail visual spec.",
            ref assertions);
        Assert(new ProceduralLizard(lowProfile).Legs.Count == 4 &&
               new ProceduralLizard(highProfile).Legs.Count == 4,
            "Structural presentation genes leaked into v1 physics topology.",
            ref assertions);
    }

    private static void PointerTraitsDriveOppositeResponses(ref int assertions)
    {
        var registry = LizardTraitRegistry.Default;
        var avoidGenome = GenomeWith(
            registry,
            0.5d,
            (DefaultLizardTraitIds.PointerDisposition, 0d),
            ("pointer.avoidance-radius", 0.85d),
            ("pointer.reaction-delay", 0d),
            ("behavior.transition.observe-to-chase", 1d));
        var chaseGenome = GenomeWith(
            registry,
            0.5d,
            (DefaultLizardTraitIds.PointerDisposition, 1d),
            ("pointer.avoidance-radius", 0.85d),
            ("pointer.reaction-delay", 0d),
            ("behavior.transition.observe-to-chase", 1d));
        var avoidProfile = IndividualProfileFactory.CreateFromGenome(
            LizardConfiguration.Default,
            avoidGenome,
            registry,
            behaviorSeed: 71237);
        var chaseProfile = IndividualProfileFactory.CreateFromGenome(
            LizardConfiguration.Default,
            chaseGenome,
            registry,
            behaviorSeed: 71237);

        Assert(avoidProfile.Behavior.Pointer.ResponseMode == PointerResponseMode.Avoid &&
               chaseProfile.Behavior.Pointer.ResponseMode == PointerResponseMode.Chase,
            "Pointer disposition does not select visibly opposite response modes.",
            ref assertions);
        var avoid = avoidProfile.Behavior.Pointer;
        Assert(avoid.TriggerMinimumDistance < avoid.AvoidanceDistance &&
               avoid.AvoidanceDistance < avoid.TriggerMaximumDistance &&
               avoid.TriggerMaximumDistance < avoid.RearmDistance &&
               avoid.TriggerMaximumDistance < avoid.LostDistance,
            "Avoidance distance escaped the trigger/rearm/lost hysteresis contract.",
            ref assertions);

        var invalidFailures = new List<string>();
        (new PointerChaseConfiguration
        {
            ResponseMode = PointerResponseMode.Avoid,
            AvoidanceDistance = 340f
        })
            .Validate(invalidFailures);
        Assert(invalidFailures.Any(failure =>
                failure.Contains("avoidance distance", StringComparison.Ordinal)),
            "Pointer configuration accepted an avoidance distance beyond its trigger radius.",
            ref assertions);

        var chaseMotion = MeasurePointerMotion(chaseProfile);
        var avoidMotion = MeasurePointerMotion(avoidProfile);
        Assert(chaseMotion.EnteredResponse && avoidMotion.EnteredResponse &&
               chaseMotion.MinimumDistance < chaseMotion.InitialDistance - 15f &&
               avoidMotion.MaximumDistance > avoidMotion.InitialDistance + 60f,
            "The same stationary pointer input did not make chase close distance and avoidance open it.",
            ref assertions);
        Assert(avoidMotion.CompletedResponse &&
               !avoidMotion.RetriggeredWhilePointerStayed &&
               avoidMotion.MaximumDistance >= avoid.AvoidanceDistance - 1f,
            "Avoidance did not finish at safe separation or immediately retriggered on a stationary pointer.",
            ref assertions);
        Assert(chaseMotion.RemainedFinite && avoidMotion.RemainedFinite,
            "Pointer response motion produced a non-finite sample.",
            ref assertions);

        var slowGate = IndividualProfileFactory.CreateFromGenome(
            LizardConfiguration.Default,
            GenomeWith(
                registry,
                0.5d,
                (DefaultLizardTraitIds.PointerDisposition, 1d),
                ("pointer.reaction-delay", 0.5d),
                ("behavior.transition.observe-to-chase", 0d)),
            registry,
            behaviorSeed: 71237);
        var fastGate = IndividualProfileFactory.CreateFromGenome(
            LizardConfiguration.Default,
            GenomeWith(
                registry,
                0.5d,
                (DefaultLizardTraitIds.PointerDisposition, 1d),
                ("pointer.reaction-delay", 0.5d),
                ("behavior.transition.observe-to-chase", 1d)),
            registry,
            behaviorSeed: 71237);
        Assert(fastGate.Behavior.Pointer.AttentionDuration <
               slowGate.Behavior.Pointer.AttentionDuration,
            "Observe-to-chase weight does not affect the perceptible pointer attention gate.",
            ref assertions);
        Assert(AvoidanceYieldsToBoundary(avoidProfile),
            "Pointer avoidance outranked near-edge recovery.",
            ref assertions);
        Assert(CoincidentAvoidanceTargetRemainsFinite(avoidProfile),
            "Coincident avoidance target did not use a finite fallback direction.",
            ref assertions);
    }

    private static void FormerlyReadoutOnlyTraitsHaveObservableEffects(
        ref int assertions)
    {
        var registry = LizardTraitRegistry.Default;
        var coverage = DefaultTraitEffectCoverage.FormerlyReadoutOnly;
        var allPlayerFacing = DefaultTraitEffectCoverage.AllPlayerFacing;
        var registeredIds = registry.Descriptors
            .Select(descriptor => descriptor.Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert(registry.Descriptors.Length == 137 &&
               coverage.Length == 35 &&
               coverage.Select(effect => effect.TraitId)
                   .Distinct(StringComparer.Ordinal).Count() == coverage.Length &&
               coverage.All(effect => registeredIds.Contains(effect.TraitId)) &&
               !registeredIds.Contains(
                   DefaultTraitEffectCoverage.RemovedLongevityTraitId),
            "The v2 registry/effect manifest is not the exact 137/35 contract, or longevity still exists.",
            ref assertions);
        Assert(allPlayerFacing.Length == registry.Descriptors.Length &&
               allPlayerFacing.Select(effect => effect.TraitId)
                   .Distinct(StringComparer.Ordinal).Count() ==
               allPlayerFacing.Length &&
               allPlayerFacing.All(effect =>
                   registeredIds.Contains(effect.TraitId) &&
                   !string.IsNullOrWhiteSpace(effect.ObservableEffect)) &&
               registeredIds.SetEquals(
                   allPlayerFacing.Select(effect => effect.TraitId)),
            "AllPlayerFacing must map every registry-v2 locus exactly once with no unknown consumer entry.",
            ref assertions);
        Assert(coverage.Count(effect =>
                   effect.Surface == TraitEffectSurface.VisualPhenotype) == 15 &&
               coverage.Count(effect =>
                   effect.Surface == TraitEffectSurface.RuntimeProfile) == 11 &&
               coverage.Count(effect =>
                   effect.Surface == TraitEffectSurface.BreedingLifecycle) == 9,
            "Former readout-only effects are not partitioned into the approved 15/11/9 surfaces.",
            ref assertions);

        var companionPhenotype = registry.Express(GenomeWith(registry, 0.5d));
        foreach (var effect in coverage)
        {
            // Keep all unrelated loci identical. Values are inset from the
            // endpoints so circular hue decoding cannot wrap 360 degrees to 0.
            var lowGenome = GenomeWith(
                registry,
                0.5d,
                (effect.TraitId, 0.05d));
            var highGenome = GenomeWith(
                registry,
                0.5d,
                (effect.TraitId, 0.95d));
            var lowPhenotype = registry.Express(lowGenome);
            var highPhenotype = registry.Express(highGenome);
            var lowProfile = IndividualProfileFactory.CreateFromGenome(
                LizardConfiguration.Default,
                lowGenome,
                registry,
                behaviorSeed: 90210);
            var highProfile = IndividualProfileFactory.CreateFromGenome(
                LizardConfiguration.Default,
                highGenome,
                registry,
                behaviorSeed: 90210);
            var low = ObserveEffect(
                effect,
                lowPhenotype,
                lowProfile,
                companionPhenotype);
            var high = ObserveEffect(
                effect,
                highPhenotype,
                highProfile,
                companionPhenotype);

            Assert(low.Values.All(double.IsFinite) &&
                   high.Values.All(double.IsFinite) &&
                   ObservablyDifferent(low, high),
                $"Trait {effect.TraitId} does not change its declared observable effect: {effect.ObservableEffect}.",
                ref assertions);
        }

        var lowGuard = registry.Express(GenomeWith(
            registry,
            0.5d,
            ("behavior.transition.egg-guarding", 0.05d)));
        var highGuard = registry.Express(GenomeWith(
            registry,
            0.5d,
            ("behavior.transition.egg-guarding", 0.95d)));
        var firstOrder = BreedingTraitEffectResolver.ResolveIncubationRate(
            companionPhenotype,
            lowGuard,
            highGuard);
        var secondOrder = BreedingTraitEffectResolver.ResolveIncubationRate(
            companionPhenotype,
            highGuard,
            lowGuard);
        Assert(double.IsFinite(firstOrder) &&
               firstOrder == secondOrder,
            "Parental egg-guarding contribution is not finite and exactly symmetric.",
            ref assertions);
    }

    private static void NonzeroPairStaggerRangeSurvivesExtremeGait(
        ref int assertions)
    {
        var source = LizardConfiguration.Default with
        {
            Gait = LizardConfiguration.Default.Gait with
            {
                PairStaggerMinimum = 0.020f,
                PairStaggerMaximum = 0.025f
            }
        };
        source.EnsureValid();
        var registry = LizardTraitRegistry.Default;
        var bouncingGait = GenomeWith(
            registry,
            0.5d,
            ("locomotion.gait", 0.55d));
        var profile = IndividualProfileFactory.CreateFromGenome(
            source,
            bouncingGait,
            registry,
            behaviorSeed: 4242);
        var resolved = source with
        {
            Behavior = profile.Behavior,
            Gait = profile.Gait,
            Physics = profile.Physics,
            Appearance = profile.Appearance,
            SecondaryMotion = profile.SecondaryMotion,
            Rendering = profile.Rendering,
            Runtime = profile.Runtime
        };
        Assert(profile.Gait.PairStaggerMinimum > 0f &&
               profile.Gait.PairStaggerMaximum >=
               profile.Gait.PairStaggerMinimum &&
               resolved.Validate().Count == 0,
            "A nonzero custom pair-stagger range became inverted under the bouncing gait profile.",
            ref assertions);
    }

    private static void NarrowPointerSpansCanResolveAvoidance(
        ref int assertions)
    {
        var registry = LizardTraitRegistry.Default;
        var avoidGenome = GenomeWith(
            registry,
            0.5d,
            (DefaultLizardTraitIds.PointerDisposition, 0.05d));
        foreach (var triggerMaximum in new[] { 20f, 20.02f })
        {
            var source = LizardConfiguration.Default with
            {
                Behavior = LizardConfiguration.Default.Behavior with
                {
                    Pointer = LizardConfiguration.Default.Behavior.Pointer with
                    {
                        ResponseMode = PointerResponseMode.Chase,
                        TriggerMinimumDistance = 20f,
                        TriggerMaximumDistance = triggerMaximum,
                        RearmDistance = 120f,
                        LostDistance = 140f
                    }
                }
            };
            source.EnsureValid();
            var profile = IndividualProfileFactory.CreateFromGenome(
                source,
                avoidGenome,
                registry,
                behaviorSeed: 71238);
            var pointer = profile.Behavior.Pointer;
            var resolved = source with
            {
                Behavior = profile.Behavior,
                Gait = profile.Gait,
                Physics = profile.Physics,
                Appearance = profile.Appearance,
                SecondaryMotion = profile.SecondaryMotion,
                Rendering = profile.Rendering,
                Runtime = profile.Runtime
            };
            Assert(pointer.ResponseMode == PointerResponseMode.Avoid &&
                   pointer.TriggerMinimumDistance < pointer.AvoidanceDistance &&
                   pointer.AvoidanceDistance < pointer.TriggerMaximumDistance &&
                   pointer.TriggerMaximumDistance < pointer.RearmDistance &&
                   pointer.TriggerMaximumDistance < pointer.LostDistance &&
                   resolved.Validate().Count == 0,
                $"A legal {triggerMaximum:R}px Chase trigger span could not derive a strict finite Avoid profile.",
                ref assertions);
        }
    }

    private static void BoundaryDurationAndFallProfilesRemainValid(
        ref int assertions)
    {
        var defaults = LizardConfiguration.Default;
        var source = defaults with
        {
            Behavior = defaults.Behavior with
            {
                WalkBoutMinimumDuration = 0.05f,
                WalkBoutMaximumDuration = 0.05f,
                SCurve = defaults.Behavior.SCurve with
                {
                    Normal = defaults.Behavior.SCurve.Normal with
                    {
                        MinimumCycleDuration = 0.05f,
                        MaximumCycleDuration = 0.05f
                    },
                    Fast = defaults.Behavior.SCurve.Fast with
                    {
                        MinimumCycleDuration = 0.05f,
                        MaximumCycleDuration = 0.05f
                    }
                },
                FastForward = defaults.Behavior.FastForward with
                {
                    MinimumDuration = 0.05f,
                    MaximumDuration = 0.05f
                },
                Timing = defaults.Behavior.Timing with
                {
                    Observe = new DurationRangeConfiguration(0.05f, 0.05f)
                },
                LostGripFall = defaults.Behavior.LostGripFall with
                {
                    MinimumDistance = 3f,
                    ReachLeadDistance = 3f,
                    MaximumFallVelocity = 120f
                }
            },
            Runtime = defaults.Runtime with { SimulationRate = 120f },
            IndividualVariation = defaults.IndividualVariation with
            {
                DurationVariation = 0.75f,
                Multipliers = defaults.IndividualVariation.Multipliers with
                {
                    DurationCalmness = 0f,
                    DurationActivity = 1f
                },
                Behavior = defaults.IndividualVariation.Behavior with
                {
                    FastCurveDurationBlend = 1f,
                    FastForwardDurationBlend = 1f
                }
            }
        };
        source.EnsureValid();
        var registry = LizardTraitRegistry.Default;
        foreach (var allele in new[] { 0d, 1d })
        {
            var profile = IndividualProfileFactory.CreateFromGenome(
                source,
                UniformGenome(registry, allele),
                registry,
                behaviorSeed: 66120);
            var resolved = ResolvedConfiguration(source, profile);
            var behavior = profile.Behavior;
            var requiredPreparationDistance =
                3f * behavior.LostGripFall.MaximumFallVelocity /
                profile.Runtime.SimulationRate;
            Assert(resolved.Validate().Count == 0 &&
                   behavior.Timing.Observe.Minimum >= 0.05f &&
                   behavior.Timing.Observe.Maximum >=
                   behavior.Timing.Observe.Minimum &&
                   behavior.WalkBoutMinimumDuration >= 0.05f &&
                   behavior.WalkBoutMaximumDuration >=
                   behavior.WalkBoutMinimumDuration &&
                   behavior.SCurve.Normal.MinimumCycleDuration >= 0.05f &&
                   behavior.SCurve.Normal.MaximumCycleDuration >=
                   behavior.SCurve.Normal.MinimumCycleDuration &&
                   behavior.SCurve.Fast.MinimumCycleDuration >= 0.05f &&
                   behavior.SCurve.Fast.MaximumCycleDuration >=
                   behavior.SCurve.Fast.MinimumCycleDuration &&
                   behavior.FastForward.MinimumDuration >= 0.05f &&
                   behavior.FastForward.MaximumDuration >=
                   behavior.FastForward.MinimumDuration &&
                   Math.Min(
                       behavior.LostGripFall.MinimumDistance,
                       behavior.LostGripFall.ReachLeadDistance) + 0.0001f >=
                   requiredPreparationDistance,
                $"A legal floor-boundary configuration became invalid for uniform allele {allele}.",
                ref assertions);
        }

        var lowFallControl = IndividualProfileFactory.CreateFromGenome(
            source,
            GenomeWith(
                registry,
                0.5d,
                ("locomotion.fall-control", 0.05d)),
            registry,
            behaviorSeed: 66120);
        var lowFall = lowFallControl.Behavior.LostGripFall;
        var lowRequiredDistance =
            3f * lowFall.MaximumFallVelocity /
            lowFallControl.Runtime.SimulationRate;
        Assert(lowFall.MaximumFallVelocity > 120f &&
               lowFall.MinimumDistance >= lowRequiredDistance &&
               lowFall.ReachLeadDistance >= lowRequiredDistance &&
               ResolvedConfiguration(source, lowFallControl).Validate().Count == 0,
            "Low fall-control increased velocity without preserving three preparation steps.",
            ref assertions);
    }

    private static LizardConfiguration ResolvedConfiguration(
        LizardConfiguration source,
        LizardProfile profile) => source with
    {
        Behavior = profile.Behavior,
        Gait = profile.Gait,
        Physics = profile.Physics,
        Appearance = profile.Appearance,
        SecondaryMotion = profile.SecondaryMotion,
        Rendering = profile.Rendering,
        Runtime = profile.Runtime
    };

    private static EffectObservation ObserveEffect(
        TraitEffectCoverage effect,
        LizardPhenotype phenotype,
        LizardProfile profile,
        LizardPhenotype companionPhenotype)
    {
        var visual = profile.VisualPhenotype ??
                     throw new InvalidOperationException(
                         "Effect probe lost its visual phenotype.");
        return effect.TraitId switch
        {
            "color.iridescence" => Numbers(
                visual.Palette.Iridescence,
                visual.Palette.Secondary.Red,
                visual.Palette.Secondary.Green,
                visual.Palette.Secondary.Blue),
            "color.melanin" => Numbers(
                visual.Palette.Melanin,
                visual.Palette.Primary.Red,
                visual.Palette.Primary.Green,
                visual.Palette.Primary.Blue),
            "body.neck-length" => Numbers(
                visual.Body.NeckLengthRatio,
                profile.Appearance.HeadAnchorOffset),
            "body.belly-roundness" => Numbers(
                visual.Body.BellyRoundness,
                profile.Appearance.BodyWidths.Select(value => (double)value)),
            "body.spine-arch" => Numbers(
                visual.Body.SpineArch,
                profile.Appearance.SpineRestCurveAmplitude),
            "body.flexibility" => Numbers(
                visual.Body.Flexibility,
                profile.Appearance.SpineMaximumBend),
            "limbs.left-right-asymmetry" => Numbers(
                visual.Limbs.LeftRightAsymmetry,
                profile.Gait.StanceSkew.Select(value => (double)value)),
            "skin.scale-size" => Numbers(visual.Skin.ScaleSizeRatio),
            "skin.roughness" => Numbers(
                visual.Skin.Roughness,
                profile.Appearance.ShadowOpacity),
            "skin.gloss" => Numbers(
                visual.Skin.Gloss,
                profile.Appearance.ShadowOpacity),
            "skin.translucency" => Numbers(visual.Skin.Translucency),
            "tail.flexibility" => Numbers(
                visual.Tail.Flexibility,
                profile.SecondaryMotion.IdleTailAmplitude,
                profile.SecondaryMotion.TailPrimaryFrequency),
            "tail.segment-count" => Numbers(visual.Tail.SegmentCount),
            "tail.fork.present" => Discrete(visual.Tail.Fork.IsPresent.ToString()),
            "tail.fork.length" => Numbers(visual.Tail.Fork.LengthRatio),

            "locomotion.gait" => Numbers(
                profile.Gait.PairStaggerMaximum,
                profile.Gait.StepHeightBase,
                profile.Gait.FastStepDuration),
            "locomotion.grip" => Numbers(
                Weight(profile, AutonomousAction.LostGripFall)),
            "locomotion.endurance" => Numbers(
                profile.Behavior.WalkBoutMaximumDuration,
                profile.Behavior.Decisions.RestChanceBase),
            "locomotion.fall-control" => Numbers(
                profile.Behavior.LostGripFall.Gravity,
                profile.Behavior.LostGripFall.MaximumFallVelocity,
                profile.Behavior.LostGripFall.RegripDuration),
            "locomotion.swim-affinity" => Numbers(
                profile.Behavior.SCurve.Normal.MaximumAmplitude,
                profile.Behavior.SCurve.Normal.MaximumCycleDuration,
                Weight(profile, AutonomousAction.SCurve)),
            "temperament.patience" => Numbers(
                profile.Behavior.Timing.Observe.Minimum,
                profile.Behavior.Timing.Observe.Maximum),
            "temperament.food-drive" => Numbers(
                profile.Behavior.Decisions.WalkPaceBase,
                Weight(profile, AutonomousAction.FastForward)),
            "temperament.sleepiness" => Numbers(
                profile.Behavior.Decisions.RestChanceBase,
                profile.Behavior.Rest.Bands[^1].Weight),
            "pointer.click-sensitivity" => Numbers(
                profile.Behavior.EscapeSprint.MaximumDistance,
                profile.Behavior.EscapeSprint.Acceleration),
            "pointer.drag-tolerance" => Numbers(
                profile.Behavior.Timing.ReleaseSettleDuration,
                profile.Behavior.EscapeSprint.MaximumDistance),
            "behavior.transition.rest-to-long-rest" => Numbers(
                profile.Behavior.Rest.Bands[^1].Weight),

            "temperament.sociability" or
            "temperament.territoriality" or
            "behavior.transition.social-approach" or
            "behavior.transition.courtship" => Numbers(
                BreedingTraitEffectResolver.ResolveBreedingCooldownRate(
                    phenotype)),
            "behavior.transition.egg-guarding" => Numbers(
                BreedingTraitEffectResolver.ResolveIncubationRate(
                    companionPhenotype,
                    phenotype,
                    companionPhenotype)),
            "lifecycle.vitality" or
            "lifecycle.metabolism" => Numbers(
                BreedingTraitEffectResolver.ResolveMaturationRate(phenotype)),
            "lifecycle.egg-shell-pattern" => Discrete(
                BreedablePhenotypeCompiler.CompileEggAppearance(phenotype)
                    .Pattern.ToString()),
            "lifecycle.egg-shell-hue" => Numbers(
                BreedablePhenotypeCompiler.CompileEggAppearance(phenotype)
                    .HueDegrees),
            _ => throw new InvalidOperationException(
                $"Effect coverage has no executable probe for {effect.TraitId}.")
        };
    }

    private static EffectObservation Numbers(params double[] values) =>
        new(string.Empty, values.ToImmutableArray());

    private static EffectObservation Numbers(
        double leading,
        IEnumerable<double> trailing) =>
        new(
            string.Empty,
            ImmutableArray.Create(leading).AddRange(trailing));

    private static EffectObservation Discrete(string value) =>
        new(value, ImmutableArray<double>.Empty);

    private static bool ObservablyDifferent(
        EffectObservation first,
        EffectObservation second)
    {
        if (!string.Equals(first.DiscreteValue, second.DiscreteValue, StringComparison.Ordinal))
        {
            return true;
        }
        if (first.Values.Length != second.Values.Length)
        {
            return true;
        }
        for (var index = 0; index < first.Values.Length; index++)
        {
            if (Math.Abs(first.Values[index] - second.Values[index]) > 0.000001d)
            {
                return true;
            }
        }
        return false;
    }

    private static PointerMotionMeasurements MeasurePointerMotion(
        LizardProfile profile)
    {
        const float dt = 1f / 120f;
        var area = new FloatRect(-2000f, -1500f, 2000f, 1500f);
        var behavior = new BehaviorController(99173, profile);
        behavior.Reset(Vector2.Zero, 0f);
        AdvanceBehavior(behavior, area, 0.65f, default, dt);

        var pointerPosition = behavior.Position + new Vector2(70f, 0f);
        var initialDistance = Vector2.Distance(behavior.Position, pointerPosition);
        var minimumDistance = initialDistance;
        var maximumDistance = initialDistance;
        var entered = false;
        var completed = false;
        var remainedFinite = true;
        var stepCount = (int)MathF.Ceiling(12f / dt);
        for (var step = 0; step < stepCount; step++)
        {
            behavior.Update(
                dt,
                area,
                new PointerObservation(pointerPosition, true));
            remainedFinite &= MotionIsFinite(behavior);
            var distance = Vector2.Distance(behavior.Position, pointerPosition);
            if (float.IsFinite(distance))
            {
                minimumDistance = Math.Min(minimumDistance, distance);
                maximumDistance = Math.Max(maximumDistance, distance);
            }
            if (behavior.State == RoamingState.MouseChase)
            {
                entered = true;
            }
            else if (entered)
            {
                completed = true;
                break;
            }
        }

        var retriggered = false;
        if (completed)
        {
            var latchSteps = (int)MathF.Ceiling(0.75f / dt);
            for (var step = 0; step < latchSteps; step++)
            {
                behavior.Update(
                    dt,
                    area,
                    new PointerObservation(pointerPosition, true));
                remainedFinite &= MotionIsFinite(behavior);
                retriggered |= behavior.State == RoamingState.MouseChase;
            }
        }

        return new PointerMotionMeasurements(
            initialDistance,
            minimumDistance,
            maximumDistance,
            entered,
            completed,
            retriggered,
            remainedFinite);
    }

    private static bool AvoidanceYieldsToBoundary(LizardProfile profile)
    {
        const float dt = 1f / 120f;
        var area = new FloatRect(-500f, -400f, 500f, 400f);
        var start = new Vector2(
            area.Left + profile.Behavior.Pointer.EdgeInterruptDistance - 1f,
            area.Center.Y);
        var behavior = new BehaviorController(99173, profile);
        behavior.Reset(start, 0f);
        AdvanceBehavior(behavior, area, 0.65f, default, dt);
        var pointer = new PointerObservation(
            behavior.Position + new Vector2(60f, 0f),
            true);
        var stepCount = (int)MathF.Ceiling(0.5f / dt);
        for (var step = 0; step < stepCount; step++)
        {
            behavior.Update(dt, area, pointer);
            if (behavior.State == RoamingState.EdgeTurn &&
                behavior.LastTransitionReason == StateTransitionReason.Boundary)
            {
                return area.Contains(behavior.Position) && MotionIsFinite(behavior);
            }
        }
        return false;
    }

    private static bool CoincidentAvoidanceTargetRemainsFinite(
        LizardProfile sourceProfile)
    {
        const float dt = 1f / 120f;
        var pointer = sourceProfile.Behavior.Pointer with
        {
            TargetTrackingResponse = 100_000f
        };
        var profile = sourceProfile with
        {
            Behavior = sourceProfile.Behavior with { Pointer = pointer }
        };
        var area = new FloatRect(-2000f, -1500f, 2000f, 1500f);
        var behavior = new BehaviorController(88731, profile);
        behavior.Reset(Vector2.Zero, 0f);
        AdvanceBehavior(behavior, area, 0.65f, default, dt);
        var triggerPointer = new PointerObservation(
            behavior.Position + new Vector2(60f, 0f),
            true);
        var stepCount = (int)MathF.Ceiling(0.5f / dt);
        for (var step = 0; step < stepCount &&
             behavior.State != RoamingState.MouseChase; step++)
        {
            behavior.Update(dt, area, triggerPointer);
        }
        if (behavior.State != RoamingState.MouseChase)
        {
            return false;
        }

        behavior.Update(
            dt,
            area,
            new PointerObservation(behavior.Position, true));
        return MotionIsFinite(behavior) && area.Contains(behavior.Position);
    }

    private static void AdvanceBehavior(
        BehaviorController behavior,
        FloatRect area,
        float duration,
        PointerObservation pointer,
        float dt)
    {
        var stepCount = (int)MathF.Ceiling(duration / dt);
        for (var step = 0; step < stepCount; step++)
        {
            behavior.Update(dt, area, pointer);
        }
    }

    private static bool MotionIsFinite(BehaviorController behavior) =>
        float.IsFinite(behavior.Position.X) &&
        float.IsFinite(behavior.Position.Y) &&
        float.IsFinite(behavior.Heading) &&
        float.IsFinite(behavior.Speed);

    private readonly record struct PointerMotionMeasurements(
        float InitialDistance,
        float MinimumDistance,
        float MaximumDistance,
        bool EnteredResponse,
        bool CompletedResponse,
        bool RetriggeredWhilePointerStayed,
        bool RemainedFinite);

    private readonly record struct EffectObservation(
        string DiscreteValue,
        ImmutableArray<double> Values);

    private static LizardGenome UniformGenome(
        LizardTraitRegistry registry,
        double allele) => new(
        LizardGenome.CurrentSchemaVersion,
        registry.RegistryId,
        registry.Descriptors
            .OrderBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .Select(descriptor => new GenePair(descriptor.Id, allele, allele))
            .ToImmutableArray());

    private static LizardGenome GenomeWith(
        LizardTraitRegistry registry,
        double baseline,
        params (string Id, double Value)[] overrides)
    {
        var values = overrides.ToDictionary(
            item => item.Id,
            item => item.Value,
            StringComparer.Ordinal);
        return new LizardGenome(
            LizardGenome.CurrentSchemaVersion,
            registry.RegistryId,
            registry.Descriptors
                .OrderBy(descriptor => descriptor.Id, StringComparer.Ordinal)
                .Select(descriptor =>
                {
                    var value = values.TryGetValue(descriptor.Id, out var selected)
                        ? selected
                        : baseline;
                    return new GenePair(descriptor.Id, value, value);
                })
                .ToImmutableArray());
    }

    private static ExpressedTrait Trait(
        string id,
        TraitValueKind kind,
        double value,
        bool isExpressed) => new(
        id,
        id,
        id,
        TraitCategory.Appendages,
        kind,
        1d,
        value,
        isExpressed,
        value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static bool AllFinite(
        LizardProfile profile,
        BreedableVisualPhenotype visual) =>
        float.IsFinite(profile.Appearance.SpineLinkLength) &&
        profile.Appearance.BodyWidths.All(float.IsFinite) &&
        float.IsFinite(profile.Behavior.Speed.ReferenceMinimumCrawl) &&
        float.IsFinite(profile.Behavior.Speed.ReferenceMaximumCrawl) &&
        float.IsFinite(profile.Behavior.Pointer.TriggerMaximumDistance) &&
        float.IsFinite(profile.Behavior.Pointer.AvoidanceDistance) &&
        float.IsFinite(profile.Gait.FrontLegLinkLength) &&
        float.IsFinite(profile.Gait.RearLegLinkLength) &&
        float.IsFinite(visual.Pattern.Strength) &&
        float.IsFinite(visual.Palette.Iridescence) &&
        float.IsFinite(visual.Palette.Melanin) &&
        float.IsFinite(visual.Body.LengthRatio) &&
        float.IsFinite(visual.Body.WidthRatio) &&
        float.IsFinite(visual.Body.NeckLengthRatio) &&
        float.IsFinite(visual.Body.BellyRoundness) &&
        float.IsFinite(visual.Body.SpineArch) &&
        float.IsFinite(visual.Body.Flexibility) &&
        float.IsFinite(visual.Limbs.LengthRatio) &&
        float.IsFinite(visual.Limbs.LeftRightAsymmetry) &&
        float.IsFinite(visual.Skin.ScaleSizeRatio) &&
        float.IsFinite(visual.Skin.Roughness) &&
        float.IsFinite(visual.Skin.Gloss) &&
        float.IsFinite(visual.Skin.Translucency) &&
        float.IsFinite(visual.Tail.LengthRatio) &&
        float.IsFinite(visual.Tail.Flexibility) &&
        float.IsFinite(visual.Tail.Fork.LengthRatio) &&
        float.IsFinite(visual.EggAppearance.HueDegrees);

    private static float Weight(LizardProfile profile, AutonomousAction action) =>
        profile.Behavior.TransitionMatrix.AfterForward.Entries
            .Single(entry => entry.Action == action)
            .Weight;

    private static void Assert(bool condition, string message, ref int assertions)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
        assertions++;
    }
}

internal readonly record struct BreedablePhenotypeCompilerSelfTestResult(
    bool Passed,
    int Assertions,
    string Detail)
{
    public override string ToString() =>
        $"{Detail} Assertions={Assertions}.";
}
