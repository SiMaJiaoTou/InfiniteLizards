using System.Collections.Immutable;
using System.Text.Json;
using InfiniteLizards.Gameplay.Application;
using InfiniteLizards.Gameplay.Breeding;
using InfiniteLizards.Gameplay.Genetics;

internal static class BreedingDomainSelfTest
{
    public static BreedingDomainSelfTestResult Run()
    {
        try
        {
            var assertions = 0;
            RegistryIsExtensibleAndPlayerVisible(ref assertions);
            DefaultV1RemovalUpgradePreservesAlleles(ref assertions);
            InheritanceAndMutationAreDiploid(ref assertions);
            LifecycleAndBreedingGatesAreTransactional(ref assertions);
            LifecycleTraitRulesAreDeterministicAndSymmetric(ref assertions);
            MarketPricesAreExact(ref assertions);
            SeedAndRestoredContinuationAreDeterministic(ref assertions);
            CompatibleRegistrySnapshotsUpgradeWithoutRerolling(ref assertions);
            IdentityExhaustionIsTransactionallyInert(ref assertions);
            AcceptedExtremeRulesRemainExecutable(ref assertions);
            ExtremeCombinationsStayFinite(ref assertions);
            return new BreedingDomainSelfTestResult(
                true,
                assertions,
                LizardTraitRegistry.Default.Descriptors.Length,
                "Genetics, lifecycle, breeding, economy and persistence contracts passed.");
        }
        catch (Exception exception)
        {
            return new BreedingDomainSelfTestResult(
                false,
                0,
                LizardTraitRegistry.Default.Descriptors.Length,
                exception.Message);
        }
    }

    private static void RegistryIsExtensibleAndPlayerVisible(ref int assertions)
    {
        var registry = LizardTraitRegistry.Default;
        Assert(registry.Descriptors.Length >= 100,
            "Default registry must expose at least 100 independently heritable axes.",
            ref assertions);
        Assert(registry.Validate().Count == 0,
            "Default registry validation must be clean.",
            ref assertions);
        Assert(
            registry.Descriptors.Select(descriptor => descriptor.Category).Distinct().Count() ==
            Enum.GetValues<TraitCategory>().Length,
            "Every player-facing trait category needs at least one descriptor.",
            ref assertions);
        Assert(registry.Descriptors.All(descriptor =>
                descriptor.PlayerVisible &&
                !string.IsNullOrWhiteSpace(descriptor.DisplayName) &&
                !string.IsNullOrWhiteSpace(descriptor.Description)),
            "Every default trait must explain a perceptible player-facing difference.",
            ref assertions);

        var phenotype = registry.Express(registry.CreateFounder(0xCAFEUL));
        Assert(phenotype.Traits.Length == registry.Descriptors.Length,
            "Every registered trait must compile into the phenotype/detail model.",
            ref assertions);
        Assert(phenotype.Traits.All(trait =>
                double.IsFinite(trait.Value) &&
                double.IsFinite(trait.NormalizedValue) &&
                !string.IsNullOrWhiteSpace(trait.DisplayValue)),
            "Every phenotype value must be finite and formatted for the detail panel.",
            ref assertions);
        Assert(phenotype.GetValue(DefaultLizardTraitIds.LegPairCount) is >= 1d and <= 5d,
            "Leg-pair count must be visibly expressible from one to five pairs.",
            ref assertions);
        Assert(phenotype.GetValue(DefaultLizardTraitIds.LegJointCount) is >= 1d and <= 5d,
            "Joint count must be visibly expressible from one to five joints.",
            ref assertions);

        var hueRegistry = new LizardTraitRegistry(
            "hue-circle-test.v1",
            [
                new TraitDescriptor(
                    "hue",
                    "环形色相",
                    "色相遗传必须跨越零度而不是绕远路。",
                    TraitCategory.Pigmentation,
                    TraitValueKind.Hue,
                    0d,
                    360d,
                    mutationRate: 0d)
            ]);
        var hueGenome = new LizardGenome(
            LizardGenome.CurrentSchemaVersion,
            hueRegistry.RegistryId,
            [new GenePair("hue", 359d / 360d, 1d / 360d)]);
        var inheritedHue = hueRegistry.Express(hueGenome).GetValue("hue");
        Assert(inheritedHue <= 1.1d || inheritedHue >= 358.9d,
            "359° and 1° alleles must express near 0°, never 180°.",
            ref assertions);

        var baseDescriptors = new[]
        {
            new TraitDescriptor(
                "trait.b",
                "旧维度 B",
                "用于验证新增维度不会重掷旧血统。",
                TraitCategory.Body,
                TraitValueKind.Continuous,
                mutationRate: 0.8d,
                mutationScale: 0.4d),
            new TraitDescriptor(
                "trait.c",
                "旧维度 C",
                "用于验证稳定 trait 子随机流。",
                TraitCategory.Body,
                TraitValueKind.Continuous,
                mutationRate: 0.8d,
                mutationScale: 0.4d)
        };
        var baseRegistry = new LizardTraitRegistry("extension-base.v1", baseDescriptors);
        var extendedRegistry = new LizardTraitRegistry(
            "extension-plus.v1",
            [
                new TraitDescriptor(
                    "trait.a",
                    "新插入维度",
                    "排序在旧维度之前的新扩展。",
                    TraitCategory.Body,
                    TraitValueKind.Continuous),
                .. baseDescriptors
            ]);
        var baseFirst = baseRegistry.CreateFounder(4455UL);
        var extendedFirst = extendedRegistry.CreateFounder(4455UL);
        var baseSecond = baseRegistry.CreateFounder(6677UL);
        var extendedSecond = extendedRegistry.CreateFounder(6677UL);
        foreach (var oldTraitId in new[] { "trait.b", "trait.c" })
        {
            Assert(baseFirst.GetGene(oldTraitId) == extendedFirst.GetGene(oldTraitId) &&
                   baseSecond.GetGene(oldTraitId) == extendedSecond.GetGene(oldTraitId),
                $"Adding an earlier descriptor must not reroll founder trait {oldTraitId}.",
                ref assertions);
        }
        var baseChild = baseRegistry.CreateOffspring(baseFirst, baseSecond, 8899UL);
        var extendedChild = extendedRegistry.CreateOffspring(
            extendedFirst,
            extendedSecond,
            8899UL);
        foreach (var oldTraitId in new[] { "trait.b", "trait.c" })
        {
            Assert(baseChild.GetGene(oldTraitId) == extendedChild.GetGene(oldTraitId),
                $"Adding an earlier descriptor must not change inherited/mutated trait {oldTraitId}.",
                ref assertions);
        }

        var service = new LizardBreedingService(
            BreedingSimulation.Create(91UL, initialCoins: 5));
        var bought = service.Buy("detail-pen", "参数样本");
        Assert(bought.Succeeded && bought.LizardId is not null,
            "Detail-panel sample must be purchasable.",
            ref assertions);
        var details = service.GetLizardDetails(bought.LizardId!);
        Assert(details.Traits.Length == registry.Descriptors.Length &&
               details.PortraitKey.StartsWith("portrait-", StringComparison.Ordinal) &&
               details.SellPrice == 1,
            "Application details must expose every gene plus a stable portrait key and sell price.",
            ref assertions);
    }

    private static void InheritanceAndMutationAreDiploid(ref int assertions)
    {
        var registry = LizardTraitRegistry.Default;
        var firstGenes = registry.Descriptors
            .OrderBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .Select(descriptor => new GenePair(descriptor.Id, 0.1d, 0.2d))
            .ToImmutableArray();
        var secondGenes = registry.Descriptors
            .OrderBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .Select(descriptor => new GenePair(descriptor.Id, 0.8d, 0.9d))
            .ToImmutableArray();
        var first = new LizardGenome(
            LizardGenome.CurrentSchemaVersion,
            registry.RegistryId,
            firstGenes);
        var second = new LizardGenome(
            LizardGenome.CurrentSchemaVersion,
            registry.RegistryId,
            secondGenes);
        var child = registry.CreateOffspring(
            first,
            second,
            1234UL,
            new GeneticBreedingOptions(0d, 0d));
        Assert(child.Genes.All(gene =>
                gene.FirstAllele is 0.1d or 0.2d &&
                gene.SecondAllele is 0.8d or 0.9d),
            "Each child locus must choose exactly one allele from each parent when mutation is disabled.",
            ref assertions);

        var dormantRegistry = new LizardTraitRegistry(
            "atavism-test.v1",
            [
                new TraitDescriptor(
                    "part.present",
                    "隐藏部件",
                    "两份高值等位基因同时出现时才表达。",
                    TraitCategory.Appendages,
                    TraitValueKind.Toggle,
                    expressionMode: AlleleExpressionMode.DominantLow,
                    mutationRate: 0d),
                new TraitDescriptor(
                    "part.size",
                    "隐藏部件大小",
                    "部件返祖后立即带来可见尺寸差异。",
                    TraitCategory.Appendages,
                    TraitValueKind.Continuous,
                    activationConditions:
                    [
                        new TraitActivationCondition(
                            "part.present",
                            TraitActivationComparison.AtLeast,
                            0.5d)
                    ],
                    mutationRate: 0d)
            ]);
        var dormantGenes = ImmutableArray.Create(
            new GenePair("part.present", 0d, 1d),
            new GenePair("part.size", 0.3d, 0.9d));
        var dormantParent = new LizardGenome(
            LizardGenome.CurrentSchemaVersion,
            dormantRegistry.RegistryId,
            dormantGenes);
        Assert(!dormantRegistry.Express(dormantParent).HasFeature("part.present"),
            "A recessive structural allele must remain dormant in a carrier.",
            ref assertions);
        var returned = false;
        for (ulong seed = 1UL; seed <= 128UL && !returned; seed++)
        {
            var descendant = dormantRegistry.CreateOffspring(
                dormantParent,
                dormantParent,
                seed,
                new GeneticBreedingOptions(0d, 0d));
            var expressed = dormantRegistry.Express(descendant);
            returned = expressed.HasFeature("part.present") &&
                       expressed.GetTrait("part.size").IsExpressed;
        }
        Assert(returned,
            "Two dormant carriers must be able to produce a visibly expressed atavistic part.",
            ref assertions);

        var mutationRegistry = new LizardTraitRegistry(
            "mutation-test.v1",
            [
                new TraitDescriptor(
                    "feature.present",
                    "新结构",
                    "突变可让原本不存在的结构出现。",
                    TraitCategory.Appendages,
                    TraitValueKind.Toggle,
                    defaultNormalized: 0d,
                    founderVariation: 0d,
                    mutationRate: 1d,
                    mutationScale: 1d,
                    mutationBurstChance: 0d)
            ]);
        var absent = mutationRegistry.CreateFounder(4UL);
        var mutated = mutationRegistry.CreateOffspring(absent, absent, 5UL);
        Assert(mutationRegistry.Express(mutated).HasFeature("feature.present"),
            "A guaranteed toggle mutation must create a new visible structure.",
            ref assertions);
    }

    private static void DefaultV1RemovalUpgradePreservesAlleles(
        ref int assertions)
    {
        var registry = LizardTraitRegistry.Default;
        var predecessor = registry.CompatiblePredecessors.Single(
            item => string.Equals(
                item.RegistryId,
                LizardTraitRegistry.PreviousDefaultRegistryId,
                StringComparison.Ordinal));
        var oldGenes = predecessor.TraitIds
            .Select((traitId, index) =>
            {
                var first = (index + 1d) / (predecessor.TraitIds.Length + 2d);
                return new GenePair(traitId, first, 1d - first);
            })
            .ToImmutableArray();
        var oldGenome = new LizardGenome(
            LizardGenome.CurrentSchemaVersion,
            predecessor.RegistryId,
            oldGenes);
        var upgraded = registry.UpgradeGenome(oldGenome);

        Assert(registry.RegistryId == LizardTraitRegistry.DefaultRegistryId &&
               registry.RegistryId == "infinite-lizards.genetics.v2" &&
               registry.Descriptors.Length == 137 &&
               registry.CanUpgradeFrom(LizardTraitRegistry.PreviousDefaultRegistryId),
            "Default genetics v2 does not expose the explicit v1 removal migration.",
            ref assertions);
        Assert(oldGenome.Genes.Any(gene =>
                   gene.TraitId == DefaultTraitEffectCoverage.RemovedLongevityTraitId) &&
               upgraded.Genes.All(gene =>
                   gene.TraitId != DefaultTraitEffectCoverage.RemovedLongevityTraitId) &&
               upgraded.Genes.Length == registry.Descriptors.Length,
            "The v1 longevity locus was not removed exactly once during upgrade.",
            ref assertions);
        foreach (var descriptor in registry.Descriptors)
        {
            Assert(upgraded.GetGene(descriptor.Id) == oldGenome.GetGene(descriptor.Id),
                $"Default v1 to v2 migration changed retained alleles for {descriptor.Id}.",
                ref assertions);
        }

        var damaged = oldGenome with
        {
            Genes = oldGenome.Genes.RemoveAt(oldGenome.Genes.Length - 1)
        };
        try
        {
            _ = registry.UpgradeGenome(damaged);
        }
        catch (ArgumentException)
        {
            assertions++;
            return;
        }

        throw new InvalidOperationException(
            "Default v1 migration accepted a partial predecessor genome.");
    }

    private static void LifecycleAndBreedingGatesAreTransactional(ref int assertions)
    {
        var rules = new BreedingRules
        {
            BaseIncubationDuration = TimeSpan.FromSeconds(10),
            BaseMaturationDuration = TimeSpan.FromSeconds(20),
            BreedingCooldown = TimeSpan.FromSeconds(2),
            MutationMultiplier = 0d,
            BurstMutationMultiplier = 0d
        };
        var simulation = BreedingSimulation.Create(0xBEEFUL, 10, rules);
        var first = simulation.BuyLizard("nest", "甲");
        var second = simulation.BuyLizard("nest", "乙");
        Assert(first.Succeeded && second.Succeeded && simulation.Coins == 0,
            "Two market adults must be available for the initial breeding loop.",
            ref assertions);

        var quietAdvance = simulation.Advance(TimeSpan.FromTicks(1L));
        Assert(quietAdvance.HatchedEggIds.IsEmpty &&
               quietAdvance.HatchlingLizardIds.IsEmpty &&
               quietAdvance.NewlyMaturedLizardIds.IsEmpty,
            "A normal clock tick with no lifecycle milestone must return empty results without throwing.",
            ref assertions);

        var moved = simulation.MoveLizard(second.LizardId!, "other-nest");
        Assert(moved.Succeeded, "Moving a lizard must succeed.", ref assertions);
        var beforeWrongHabitat = Serialize(simulation.CaptureSnapshot());
        var wrongHabitat = simulation.Breed(first.LizardId!, second.LizardId!);
        Assert(wrongHabitat.Status == BreedingActionStatus.DifferentHabitat &&
               Serialize(simulation.CaptureSnapshot()) == beforeWrongHabitat,
            "Different habitats must block breeding without consuming RNG, money or cooldown.",
            ref assertions);
        _ = simulation.MoveLizard(second.LizardId!, "nest");

        var bred = simulation.Breed(first.LizardId!, second.LizardId!);
        Assert(bred.Succeeded && bred.EggId is not null &&
               simulation.EggCount == 1 && simulation.Coins == 0,
            "One valid pairing must create exactly one egg and no money.",
            ref assertions);
        var beforeCooldownFailure = Serialize(simulation.CaptureSnapshot());
        var cooldown = simulation.Breed(first.LizardId!, second.LizardId!);
        Assert(cooldown.Status == BreedingActionStatus.BreedingCooldown &&
               Serialize(simulation.CaptureSnapshot()) == beforeCooldownFailure,
            "Breeding cooldown must reject atomically.",
            ref assertions);

        Assert(simulation.TryGetEgg(bred.EggId!, out var egg),
            "Created egg must be queryable.",
            ref assertions);
        _ = simulation.Advance(TimeSpan.FromTicks(egg.RequiredIncubationTicks - 1L));
        Assert(simulation.EggCount == 1 && simulation.LizardCount == 2,
            "Egg must remain intact one tick before its individual hatch time.",
            ref assertions);
        var hatch = simulation.Advance(TimeSpan.FromTicks(1L));
        Assert(hatch.HatchedEggIds.SequenceEqual([egg.Id]) &&
               hatch.HatchlingLizardIds.Length == 1 &&
               simulation.EggCount == 0 && simulation.LizardCount == 3,
            "Egg must hatch exactly on its individual incubation boundary.",
            ref assertions);
        var juvenileId = hatch.HatchlingLizardIds[0];
        Assert(simulation.TryGetLizard(juvenileId, out var juvenile) &&
               juvenile.LifeStage == BreedingLifeStage.Juvenile,
            "A newly hatched lizard must start juvenile.",
            ref assertions);
        var juvenileGate = simulation.Breed(first.LizardId!, juvenileId);
        Assert(juvenileGate.Status == BreedingActionStatus.JuvenileCannotBreed,
            "A juvenile must never pass the maturity gate.",
            ref assertions);

        var remainingMaturation =
            juvenile.RequiredMaturationTicks - juvenile.AgeTicks;
        _ = simulation.Advance(TimeSpan.FromTicks(remainingMaturation - 1L));
        Assert(simulation.TryGetLizard(juvenileId, out juvenile) &&
               juvenile.LifeStage == BreedingLifeStage.Juvenile,
            "Juvenile must remain juvenile one tick before maturity.",
            ref assertions);
        var matured = simulation.Advance(TimeSpan.FromTicks(1L));
        Assert(matured.NewlyMaturedLizardIds.Contains(juvenileId) &&
               simulation.TryGetLizard(juvenileId, out juvenile) &&
               juvenile.LifeStage == BreedingLifeStage.Mature,
            "Juvenile must become breedable exactly at its maturity boundary.",
            ref assertions);
    }

    private static void LifecycleTraitRulesAreDeterministicAndSymmetric(
        ref int assertions)
    {
        var registry = LizardTraitRegistry.Default;
        var rules = new BreedingRules
        {
            BaseIncubationDuration = TimeSpan.FromSeconds(100),
            BaseMaturationDuration = TimeSpan.FromSeconds(200),
            BreedingCooldown = TimeSpan.FromSeconds(80),
            MutationMultiplier = 0d,
            BurstMutationMultiplier = 0d
        };
        var fixture = BreedingSimulation.Create(0x5EEDUL, 20, rules, registry);
        var boughtFirst = fixture.BuyLizard("effect-pen", "恢复快");
        var boughtSecond = fixture.BuyLizard("effect-pen", "恢复慢");
        var firstGenome = GenomeWith(
            registry,
            0.5d,
            ("temperament.sociability", 0.95d),
            ("temperament.territoriality", 0.05d),
            ("behavior.transition.social-approach", 0.95d),
            ("behavior.transition.courtship", 0.95d),
            ("behavior.transition.egg-guarding", 0.05d),
            ("lifecycle.vitality", 0.95d),
            ("lifecycle.metabolism", 0.95d));
        var secondGenome = GenomeWith(
            registry,
            0.5d,
            ("temperament.sociability", 0.05d),
            ("temperament.territoriality", 0.95d),
            ("behavior.transition.social-approach", 0.05d),
            ("behavior.transition.courtship", 0.05d),
            ("behavior.transition.egg-guarding", 0.95d),
            ("lifecycle.vitality", 0.05d),
            ("lifecycle.metabolism", 0.05d));
        var snapshot = fixture.CaptureSnapshot();
        snapshot = snapshot with
        {
            Lizards = snapshot.Lizards
                .Select(lizard => lizard.Id switch
                {
                    var id when id == boughtFirst.LizardId =>
                        lizard with { Genome = firstGenome },
                    var id when id == boughtSecond.LizardId =>
                        lizard with { Genome = secondGenome },
                    _ => lizard
                })
                .ToImmutableArray()
        };

        var firstRun = BreedingSimulation.Restore(snapshot, registry);
        var repeatedRun = BreedingSimulation.Restore(snapshot, registry);
        var swappedRun = BreedingSimulation.Restore(snapshot, registry);
        var beforeRejected = Serialize(firstRun.CaptureSnapshot());
        var rejected = firstRun.Breed(
            boughtFirst.LizardId!,
            boughtFirst.LizardId!);
        Assert(rejected.Status == BreedingActionStatus.SameParent &&
               Serialize(firstRun.CaptureSnapshot()) == beforeRejected,
            "A rejected lifecycle-aware breed command mutated state.",
            ref assertions);

        var firstResult = firstRun.Breed(
            boughtFirst.LizardId!,
            boughtSecond.LizardId!);
        var repeatedResult = repeatedRun.Breed(
            boughtFirst.LizardId!,
            boughtSecond.LizardId!);
        var swappedResult = swappedRun.Breed(
            boughtSecond.LizardId!,
            boughtFirst.LizardId!);
        Assert(firstResult.Succeeded && repeatedResult.Succeeded &&
               swappedResult.Succeeded &&
               Serialize(firstRun.CaptureSnapshot()) ==
               Serialize(repeatedRun.CaptureSnapshot()),
            "Lifecycle-aware breeding is not deterministic for the same snapshot and command.",
            ref assertions);

        if (!firstRun.TryGetEgg(firstResult.EggId!, out var firstEgg) ||
            !swappedRun.TryGetEgg(swappedResult.EggId!, out var swappedEgg) ||
            !firstRun.TryGetLizard(boughtFirst.LizardId!, out var fastParent) ||
            !firstRun.TryGetLizard(boughtSecond.LizardId!, out var slowParent) ||
            !swappedRun.TryGetLizard(
                boughtFirst.LizardId!,
                out var swappedFastParent) ||
            !swappedRun.TryGetLizard(
                boughtSecond.LizardId!,
                out var swappedSlowParent))
        {
            throw new InvalidOperationException(
                "Lifecycle timing fixture lost an egg or parent after successful breeding.");
        }
        Assert(firstEgg.RequiredIncubationTicks ==
               swappedEgg.RequiredIncubationTicks &&
               firstEgg.RequiredMaturationTicks ==
               swappedEgg.RequiredMaturationTicks,
            "Swapping parent roles changed symmetric incubation or maturation timing.",
            ref assertions);
        Assert(fastParent.BreedingCooldownTicks < slowParent.BreedingCooldownTicks &&
               fastParent.BreedingCooldownTicks ==
               swappedFastParent.BreedingCooldownTicks &&
               slowParent.BreedingCooldownTicks ==
               swappedSlowParent.BreedingCooldownTicks,
            "Individual cooldown traits are not observable or became parent-role dependent.",
            ref assertions);

        var firstPhenotype = registry.Express(firstGenome);
        var secondPhenotype = registry.Express(secondGenome);
        var offspringPhenotype = registry.Express(firstEgg.Genome);
        var incubationRate = BreedingTraitEffectResolver.ResolveIncubationRate(
            offspringPhenotype,
            firstPhenotype,
            secondPhenotype);
        var swappedRate = BreedingTraitEffectResolver.ResolveIncubationRate(
            offspringPhenotype,
            secondPhenotype,
            firstPhenotype);
        var maturationRate =
            BreedingTraitEffectResolver.ResolveMaturationRate(offspringPhenotype);
        Assert(double.IsFinite(incubationRate) && incubationRate > 0d &&
               incubationRate == swappedRate &&
               double.IsFinite(maturationRate) && maturationRate > 0d &&
               firstEgg.RequiredIncubationTicks == CalculateExpectedDurationTicks(
                   rules.BaseIncubationDuration,
                   incubationRate) &&
               firstEgg.RequiredMaturationTicks == CalculateExpectedDurationTicks(
                   rules.BaseMaturationDuration,
                   maturationRate) &&
               fastParent.BreedingCooldownTicks == CalculateExpectedDurationTicks(
                   rules.BreedingCooldown,
                   BreedingTraitEffectResolver.ResolveBreedingCooldownRate(
                       firstPhenotype)) &&
               slowParent.BreedingCooldownTicks == CalculateExpectedDurationTicks(
                   rules.BreedingCooldown,
                   BreedingTraitEffectResolver.ResolveBreedingCooldownRate(
                       secondPhenotype)),
            "Persisted lifecycle durations do not match the finite phenotype rule projections.",
            ref assertions);
    }

    private static void MarketPricesAreExact(ref int assertions)
    {
        var simulation = BreedingSimulation.Create(77UL, initialCoins: 10);
        var first = simulation.BuyLizard("market", "买入一号");
        Assert(first.Succeeded && simulation.Coins == 5,
            "Buying must deduct exactly five coins.",
            ref assertions);
        var second = simulation.BuyLizard("market", "买入二号");
        Assert(second.Succeeded && simulation.Coins == 0,
            "A second purchase must deduct exactly five more coins.",
            ref assertions);
        var beforeFailedBuy = Serialize(simulation.CaptureSnapshot());
        var failedBuy = simulation.BuyLizard("market");
        Assert(failedBuy.Status == BreedingActionStatus.InsufficientFunds &&
               Serialize(simulation.CaptureSnapshot()) == beforeFailedBuy,
            "Insufficient funds must not consume random stock, IDs or money.",
            ref assertions);
        var beforeStrandingSale = Serialize(simulation.CaptureSnapshot());
        var strandingSale = simulation.SellLizard(first.LizardId!);
        Assert(strandingSale.Status == BreedingActionStatus.WouldStrandCollection &&
               Serialize(simulation.CaptureSnapshot()) == beforeStrandingSale,
            "Selling either of two lizards with no egg or money for a replacement pair must be rejected atomically.",
            ref assertions);
        var beforeMissingSale = Serialize(simulation.CaptureSnapshot());
        var repeatedSale = simulation.SellLizard("missing-lizard");
        Assert(repeatedSale.Status == BreedingActionStatus.LizardNotFound &&
               Serialize(simulation.CaptureSnapshot()) == beforeMissingSale,
            "Selling a missing lizard must be transactionally inert.",
            ref assertions);

        var eggRecovery = BreedingSimulation.Create(78UL, initialCoins: 10);
        var eggFirst = eggRecovery.BuyLizard("market", "留蛋甲");
        var eggSecond = eggRecovery.BuyLizard("market", "留蛋乙");
        var egg = eggRecovery.Breed(eggFirst.LizardId!, eggSecond.LizardId!);
        var soldWithEggFirst = eggRecovery.SellLizard(eggFirst.LizardId!);
        var beforeUnsafeSecondSale = Serialize(eggRecovery.CaptureSnapshot());
        var soldWithEggSecond = eggRecovery.SellLizard(eggSecond.LizardId!);
        Assert(egg.Succeeded && soldWithEggFirst.Succeeded &&
               soldWithEggSecond.Status == BreedingActionStatus.WouldStrandCollection &&
               Serialize(eggRecovery.CaptureSnapshot()) == beforeUnsafeSecondSale &&
               eggRecovery.LizardCount == 1 && eggRecovery.EggCount == 1 &&
               eggRecovery.Coins == 1,
            "One remaining adult plus one viable egg is recoverable, but selling that adult too is not.",
            ref assertions);

        var cashRecovery = BreedingSimulation.Create(79UL, initialCoins: 14);
        var cashLizard = cashRecovery.BuyLizard("market", "现金保底");
        var soldToBuyThreshold = cashRecovery.SellLizard(cashLizard.LizardId!);
        Assert(soldToBuyThreshold.Succeeded && cashRecovery.LizardCount == 0 &&
               cashRecovery.EggCount == 0 &&
               cashRecovery.Coins == LizardMarketPrices.Buy * 2,
            "Selling the final lizard is allowed only when the resulting balance can buy a full pair.",
            ref assertions);

        var populationRecovery = BreedingSimulation.Create(80UL, initialCoins: 15);
        var populationFirst = populationRecovery.BuyLizard("market", "三只甲");
        _ = populationRecovery.BuyLizard("market", "三只乙");
        _ = populationRecovery.BuyLizard("market", "三只丙");
        var soldFromThree = populationRecovery.SellLizard(populationFirst.LizardId!);
        Assert(soldFromThree.Succeeded && populationRecovery.LizardCount == 2 &&
               populationRecovery.Coins == 1,
            "Selling from a population of three is allowed because a breeding pair remains.",
            ref assertions);
    }

    private static void SeedAndRestoredContinuationAreDeterministic(ref int assertions)
    {
        var rules = new BreedingRules
        {
            BaseIncubationDuration = TimeSpan.FromSeconds(3),
            BaseMaturationDuration = TimeSpan.FromSeconds(5),
            BreedingCooldown = TimeSpan.FromSeconds(1)
        };
        var first = BreedingSimulation.Create(0x1234_5678UL, 15, rules);
        var second = BreedingSimulation.Create(0x1234_5678UL, 15, rules);
        ApplyIdenticalScenario(first);
        ApplyIdenticalScenario(second);
        Assert(Serialize(first.CaptureSnapshot()) == Serialize(second.CaptureSnapshot()),
            "Same seed and command sequence must produce byte-identical JSON snapshots.",
            ref assertions);

        var json = Serialize(first.CaptureSnapshot());
        var roundTripped = JsonSerializer.Deserialize<BreedingWorldSnapshot>(json) ??
                           throw new InvalidOperationException(
                               "Breeding snapshot JSON round-trip returned null.");
        var restored = BreedingSimulation.Restore(roundTripped);
        _ = first.Advance(TimeSpan.FromSeconds(17));
        _ = restored.Advance(TimeSpan.FromSeconds(17));
        var firstExtra = first.BuyLizard("archive");
        var restoredExtra = restored.BuyLizard("archive");
        Assert(firstExtra.Status == restoredExtra.Status &&
               Serialize(first.CaptureSnapshot()) == Serialize(restored.CaptureSnapshot()),
            "Restoring random state must preserve all future hatches, names, IDs and genomes.",
            ref assertions);

        static void ApplyIdenticalScenario(BreedingSimulation simulation)
        {
            var first = simulation.BuyLizard("archive", "同种甲");
            var second = simulation.BuyLizard("archive", "同种乙");
            _ = simulation.Breed(first.LizardId!, second.LizardId!);
            _ = simulation.Advance(TimeSpan.FromMilliseconds(750));
        }
    }

    private static void CompatibleRegistrySnapshotsUpgradeWithoutRerolling(
        ref int assertions)
    {
        var oldDescriptors = new[]
        {
            new TraitDescriptor(
                DefaultLizardTraitIds.IncubationRate,
                "旧孵化速度",
                "旧存档的孵化时间基因。",
                TraitCategory.Lifecycle,
                TraitValueKind.Continuous,
                0.65d,
                1.45d),
            new TraitDescriptor(
                DefaultLizardTraitIds.MaturationRate,
                "旧成熟速度",
                "旧存档的成熟时间基因。",
                TraitCategory.Lifecycle,
                TraitValueKind.Continuous,
                0.65d,
                1.45d),
            new TraitDescriptor(
                DefaultLizardTraitIds.MutationSensitivity,
                "旧变异敏感度",
                "旧存档的变异倍率基因。",
                TraitCategory.Lifecycle,
                TraitValueKind.Continuous,
                0.65d,
                1.55d),
            new TraitDescriptor(
                "trait.b",
                "旧体型 B",
                "必须逐等位保持的旧体型基因。",
                TraitCategory.Body,
                TraitValueKind.Continuous),
            new TraitDescriptor(
                "trait.c",
                "旧体型 C",
                "必须逐等位保持的第二个旧基因。",
                TraitCategory.Body,
                TraitValueKind.Continuous)
        };
        var oldRegistry = new LizardTraitRegistry("bloodline.snapshot.v1", oldDescriptors);
        const double newTraitDefault = 0.73d;
        var newRegistry = new LizardTraitRegistry(
            "bloodline.snapshot.v2",
            [
                new TraitDescriptor(
                    "trait.a",
                    "新增体型 A",
                    "未来版本新增且应使用 descriptor 默认值的基因。",
                    TraitCategory.Body,
                    TraitValueKind.Continuous,
                    defaultNormalized: newTraitDefault),
                .. oldDescriptors
            ],
            [
                oldRegistry.CreateCompatibilityManifest()
            ]);
        var rules = new BreedingRules
        {
            BaseIncubationDuration = TimeSpan.FromSeconds(10),
            BaseMaturationDuration = TimeSpan.FromSeconds(20),
            BreedingCooldown = TimeSpan.FromSeconds(5),
            MutationMultiplier = 0d,
            BurstMutationMultiplier = 0d
        };
        var oldSimulation = BreedingSimulation.Create(
            0xC0FFEEUL,
            initialCoins: 20,
            rules,
            oldRegistry);
        var first = oldSimulation.BuyLizard("legacy-nest", "旧甲");
        var second = oldSimulation.BuyLizard("legacy-nest", "旧乙");
        var bred = oldSimulation.Breed(first.LizardId!, second.LizardId!);
        _ = oldSimulation.Advance(TimeSpan.FromSeconds(1));
        Assert(bred.Succeeded && oldSimulation.EggCount == 1,
            "Upgrade fixture must contain parents, cooldown and an incubating egg.",
            ref assertions);

        var oldSnapshot = oldSimulation.CaptureSnapshot();
        var upgradedSimulation = BreedingSimulation.Restore(oldSnapshot, newRegistry);
        var upgradedSnapshot = upgradedSimulation.CaptureSnapshot();
        Assert(upgradedSnapshot.TraitRegistryId == newRegistry.RegistryId &&
               upgradedSnapshot.RandomState == oldSnapshot.RandomState &&
               upgradedSnapshot.NextSequence == oldSnapshot.NextSequence &&
               upgradedSnapshot.Coins == oldSnapshot.Coins &&
               upgradedSnapshot.Rules == oldSnapshot.Rules,
            "Registry upgrade must only change registry/genome shape, never RNG, IDs, economy or rules.",
            ref assertions);
        Assert(upgradedSnapshot.Lizards.Length == oldSnapshot.Lizards.Length &&
               upgradedSnapshot.Eggs.Length == oldSnapshot.Eggs.Length,
            "Registry upgrade must preserve every lizard and egg.",
            ref assertions);

        foreach (var oldLizard in oldSnapshot.Lizards)
        {
            var upgraded = upgradedSnapshot.Lizards.Single(
                candidate => candidate.Id == oldLizard.Id);
            Assert(
                upgraded.Name == oldLizard.Name &&
                upgraded.AgeTicks == oldLizard.AgeTicks &&
                upgraded.RequiredMaturationTicks == oldLizard.RequiredMaturationTicks &&
                upgraded.BreedingCooldownTicks == oldLizard.BreedingCooldownTicks &&
                upgraded.HabitatId == oldLizard.HabitatId &&
                upgraded.FirstParentId == oldLizard.FirstParentId &&
                upgraded.SecondParentId == oldLizard.SecondParentId &&
                upgraded.Generation == oldLizard.Generation &&
                upgraded.WasMarketPurchased == oldLizard.WasMarketPurchased,
                $"Lizard {oldLizard.Id} metadata changed during registry upgrade.",
                ref assertions);
            AssertUpgradedGenome(
                oldLizard.Genome,
                upgraded.Genome,
                newRegistry,
                newTraitDefault,
                ref assertions);
        }
        foreach (var oldEgg in oldSnapshot.Eggs)
        {
            var upgraded = upgradedSnapshot.Eggs.Single(
                candidate => candidate.Id == oldEgg.Id);
            Assert(
                upgraded.IncubationElapsedTicks == oldEgg.IncubationElapsedTicks &&
                upgraded.RequiredIncubationTicks == oldEgg.RequiredIncubationTicks &&
                upgraded.RequiredMaturationTicks == oldEgg.RequiredMaturationTicks &&
                upgraded.HabitatId == oldEgg.HabitatId &&
                upgraded.FirstParentId == oldEgg.FirstParentId &&
                upgraded.SecondParentId == oldEgg.SecondParentId &&
                upgraded.Generation == oldEgg.Generation,
                $"Egg {oldEgg.Id} metadata changed during registry upgrade.",
                ref assertions);
            AssertUpgradedGenome(
                oldEgg.Genome,
                upgraded.Genome,
                newRegistry,
                newTraitDefault,
                ref assertions);
        }

        var oldNext = oldSimulation.BuyLizard("legacy-nest", "续跑");
        var upgradedNext = upgradedSimulation.BuyLizard("legacy-nest", "续跑");
        Assert(oldNext.Succeeded && upgradedNext.Succeeded &&
               oldNext.LizardId == upgradedNext.LizardId &&
               oldSimulation.Coins == upgradedSimulation.Coins,
            "Upgrading must preserve the exact RNG/identity continuation.",
            ref assertions);
        var foundOldNext = oldSimulation.TryGetLizard(oldNext.LizardId!, out var oldNextLizard);
        var foundUpgradedNext = upgradedSimulation.TryGetLizard(
            upgradedNext.LizardId!,
            out var upgradedNextLizard);
        Assert(foundOldNext && foundUpgradedNext,
            "Both old and upgraded continuation lizards must exist.",
            ref assertions);
        foreach (var oldGene in oldNextLizard.Genome.Genes)
        {
            Assert(oldGene == upgradedNextLizard.Genome.GetGene(oldGene.TraitId),
                $"New descriptor insertion rerolled continuing trait {oldGene.TraitId}.",
                ref assertions);
        }

        var corruptLizard = oldSnapshot.Lizards[0];
        var withUnknown = corruptLizard.Genome.Genes
            .Add(new GenePair("unknown.legacy-trait", 0.2d, 0.8d))
            .OrderBy(gene => gene.TraitId, StringComparer.Ordinal)
            .ToImmutableArray();
        var unknownSnapshot = ReplaceFirstLizardGenome(
            oldSnapshot,
            corruptLizard.Genome with { Genes = withUnknown });
        AssertRestoreRejected(unknownSnapshot, newRegistry, "unknown old trait", ref assertions);

        var missingSnapshot = ReplaceFirstLizardGenome(
            oldSnapshot,
            corruptLizard.Genome with
            {
                Genes = corruptLizard.Genome.Genes.RemoveAt(0)
            });
        AssertRestoreRejected(missingSnapshot, newRegistry, "missing old trait", ref assertions);

        var unrelatedSnapshot = oldSnapshot with
        {
            TraitRegistryId = "completely.unrelated.v9",
            Lizards = oldSnapshot.Lizards
                .Select(lizard => lizard with
                {
                    Genome = lizard.Genome with
                    {
                        RegistryId = "completely.unrelated.v9"
                    }
                })
                .ToImmutableArray(),
            Eggs = oldSnapshot.Eggs
                .Select(egg => egg with
                {
                    Genome = egg.Genome with
                    {
                        RegistryId = "completely.unrelated.v9"
                    }
                })
                .ToImmutableArray()
        };
        AssertRestoreRejected(unrelatedSnapshot, newRegistry, "unrelated registry", ref assertions);

        static BreedingWorldSnapshot ReplaceFirstLizardGenome(
            BreedingWorldSnapshot snapshot,
            LizardGenome genome)
        {
            var lizards = snapshot.Lizards.ToBuilder();
            lizards[0] = lizards[0] with { Genome = genome };
            return snapshot with { Lizards = lizards.ToImmutable() };
        }

        static void AssertUpgradedGenome(
            LizardGenome oldGenome,
            LizardGenome upgradedGenome,
            LizardTraitRegistry newRegistry,
            double expectedDefault,
            ref int assertions)
        {
            Assert(upgradedGenome.RegistryId == newRegistry.RegistryId,
                "Upgraded genome must write the current registry ID.",
                ref assertions);
            foreach (var oldGene in oldGenome.Genes)
            {
                Assert(oldGene == upgradedGenome.GetGene(oldGene.TraitId),
                    $"Saved alleles changed for {oldGene.TraitId}.",
                    ref assertions);
            }
            var added = upgradedGenome.GetGene("trait.a");
            Assert(added.FirstAllele == expectedDefault &&
                   added.SecondAllele == expectedDefault,
                "A newly registered trait must receive two exact descriptor defaults.",
                ref assertions);
        }

        static void AssertRestoreRejected(
            BreedingWorldSnapshot snapshot,
            LizardTraitRegistry registry,
            string scenario,
            ref int assertions)
        {
            try
            {
                _ = BreedingSimulation.Restore(snapshot, registry);
            }
            catch (ArgumentException)
            {
                assertions++;
                return;
            }

            throw new InvalidOperationException(
                $"Registry upgrade accepted a {scenario} snapshot.");
        }
    }

    private static void IdentityExhaustionIsTransactionallyInert(
        ref int assertions)
    {
        var rules = new BreedingRules
        {
            BaseIncubationDuration = TimeSpan.FromSeconds(100),
            BaseMaturationDuration = TimeSpan.FromSeconds(20),
            BreedingCooldown = TimeSpan.Zero,
            MutationMultiplier = 0d,
            BurstMutationMultiplier = 0d
        };
        var fixture = BreedingSimulation.Create(0x1D5EEDUL, 20, rules);
        var first = fixture.BuyLizard("sequence-nest", "序列甲");
        var second = fixture.BuyLizard("sequence-nest", "序列乙");
        var eggResult = fixture.Breed(first.LizardId!, second.LizardId!);
        Assert(eggResult.Succeeded && eggResult.EggId is not null,
            "Identity exhaustion fixture must contain a hatchable egg.",
            ref assertions);

        var exhausted = BreedingSimulation.Restore(
            fixture.CaptureSnapshot() with
            {
                NextSequence = long.MaxValue
            });
        var beforeBuy = Serialize(exhausted.CaptureSnapshot());
        var failedBuy = exhausted.BuyLizard("sequence-nest", "不会生成");
        Assert(failedBuy.Status == BreedingActionStatus.IdentitySequenceExhausted &&
               Serialize(exhausted.CaptureSnapshot()) == beforeBuy,
            "Exhausted Buy must not consume founder RNG, money or an ID.",
            ref assertions);

        var beforeBreed = Serialize(exhausted.CaptureSnapshot());
        var failedBreed = exhausted.Breed(first.LizardId!, second.LizardId!);
        Assert(failedBreed.Status == BreedingActionStatus.IdentitySequenceExhausted &&
               Serialize(exhausted.CaptureSnapshot()) == beforeBreed,
            "Exhausted Breed must not consume offspring RNG or alter cooldown/eggs.",
            ref assertions);

        Assert(exhausted.TryGetEgg(eggResult.EggId!, out var egg),
            "Exhausted fixture egg must remain available.",
            ref assertions);
        var beforeHatch = Serialize(exhausted.CaptureSnapshot());
        var threwBeforeMutation = false;
        try
        {
            _ = exhausted.Advance(TimeSpan.FromTicks(
                egg.RequiredIncubationTicks - egg.IncubationElapsedTicks));
        }
        catch (InvalidOperationException)
        {
            threwBeforeMutation = true;
        }
        Assert(threwBeforeMutation &&
               Serialize(exhausted.CaptureSnapshot()) == beforeHatch,
            "Exhausted hatch must throw before changing age, cooldown, egg progress, RNG or IDs.",
            ref assertions);
    }

    private static void AcceptedExtremeRulesRemainExecutable(ref int assertions)
    {
        var rules = BreedingRules.Default with
        {
            BreedingCooldown = TimeSpan.Zero,
            MutationMultiplier = double.MaxValue,
            BurstMutationMultiplier = double.MaxValue
        };
        Assert(rules.Validate().Count == 0,
            "Finite maximum mutation multipliers must remain accepted rules.",
            ref assertions);

        var simulation = BreedingSimulation.Create(
            0x5A7EUL,
            initialCoins: 10,
            rules: rules);
        var first = simulation.BuyLizard("extreme-rules-nest", "极值甲");
        var second = simulation.BuyLizard("extreme-rules-nest", "极值乙");
        var beforeBreed = simulation.CaptureSnapshot();
        var bred = simulation.Breed(first.LizardId!, second.LizardId!);
        Assert(bred.Succeeded && bred.EggId is not null &&
               simulation.EggCount == 1,
            "Every accepted finite mutation rule must execute breeding without overflow.",
            ref assertions);
        Assert(simulation.CaptureSnapshot().RandomState != beforeBreed.RandomState,
            "Successful maximum-multiplier breeding must consume its deterministic offspring root.",
            ref assertions);
        _ = BreedingSimulation.Restore(simulation.CaptureSnapshot());
        assertions++;
    }

    private static void ExtremeCombinationsStayFinite(ref int assertions)
    {
        var registry = LizardTraitRegistry.Default;
        var ordered = registry.Descriptors
            .OrderBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .ToArray();
        LizardGenome Extreme(double allele) => new(
            LizardGenome.CurrentSchemaVersion,
            registry.RegistryId,
            ordered.Select(descriptor =>
                    new GenePair(descriptor.Id, allele, allele))
                .ToImmutableArray());

        var zero = Extreme(0d);
        var one = Extreme(1d);
        AssertFinite(registry.Express(zero), registry, ref assertions);
        AssertFinite(registry.Express(one), registry, ref assertions);

        var reflectionRegistry = new LizardTraitRegistry(
            "reflection-test.v1",
            [
                new TraitDescriptor(
                    "continuous",
                    "反射边界",
                    "大幅突变仍应分布在区间内部。",
                    TraitCategory.Body,
                    TraitValueKind.Continuous,
                    defaultNormalized: 0d,
                    founderVariation: 0d,
                    mutationRate: 1d,
                    mutationScale: 5d,
                    mutationBurstChance: 0d)
            ]);
        var boundaryParent = reflectionRegistry.CreateFounder(1UL);
        var interiorAlleles = 0;
        const int reflectionSamples = 200;
        for (var sample = 0; sample < reflectionSamples; sample++)
        {
            var reflected = reflectionRegistry.CreateOffspring(
                boundaryParent,
                boundaryParent,
                (ulong)(5000 + sample));
            var gene = reflected.GetGene("continuous");
            if (gene.FirstAllele is > 0.000001d and < 0.999999d)
            {
                interiorAlleles++;
            }
            if (gene.SecondAllele is > 0.000001d and < 0.999999d)
            {
                interiorAlleles++;
            }
        }
        Assert(interiorAlleles >= reflectionSamples * 19 / 10,
            "Reflected large mutations must not accumulate at zero/one boundaries.",
            ref assertions);

        var first = zero;
        var second = one;
        for (var generation = 0; generation < 80; generation++)
        {
            var child = registry.CreateOffspring(
                first,
                second,
                (ulong)(generation + 1),
                new GeneticBreedingOptions(1000d, 1000d));
            Assert(child.Genes.All(gene =>
                    double.IsFinite(gene.FirstAllele) &&
                    double.IsFinite(gene.SecondAllele) &&
                    gene.FirstAllele is >= 0d and <= 1d &&
                    gene.SecondAllele is >= 0d and <= 1d),
                $"Generation {generation} must keep every extreme mutation finite and normalized.",
                ref assertions);
            AssertFinite(registry.Express(child), registry, ref assertions);
            first = second;
            second = child;
        }
    }

    private static void AssertFinite(
        LizardPhenotype phenotype,
        LizardTraitRegistry registry,
        ref int assertions)
    {
        foreach (var trait in phenotype.Traits)
        {
            var descriptor = registry.GetDescriptor(trait.TraitId);
            Assert(double.IsFinite(trait.Value) &&
                   double.IsFinite(trait.NormalizedValue) &&
                   trait.NormalizedValue is >= 0d and <= 1d &&
                   trait.Value >= descriptor.Minimum - 0.000001d &&
                   trait.Value <= descriptor.Maximum + 0.000001d,
                $"Extreme phenotype trait {trait.TraitId} escaped its finite descriptor range.",
                ref assertions);
        }
    }

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

    private static long CalculateExpectedDurationTicks(
        TimeSpan baseDuration,
        double rate)
    {
        var scaled = baseDuration.Ticks / Math.Clamp(rate, 0.05d, 20d);
        return Math.Max(
            1L,
            (long)Math.Round(scaled, MidpointRounding.AwayFromZero));
    }

    private static string Serialize(BreedingWorldSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot);

    private static void Assert(bool condition, string message, ref int assertions)
    {
        assertions++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal readonly record struct BreedingDomainSelfTestResult(
    bool Passed,
    int Assertions,
    int TraitCount,
    string Detail)
{
    public override string ToString() =>
        $"passed={Passed}, assertions={Assertions}, traits={TraitCount}, detail={Detail}";
}
