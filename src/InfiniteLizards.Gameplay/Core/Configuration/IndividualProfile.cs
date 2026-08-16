namespace DesktopLizard.Core;

/// <summary>
/// Stable temperament values for one pet. Values are normalized to [0,1]
/// and are generated with a random stream independent from the behavior FSM.
/// </summary>
internal sealed record IndividualTraits(
    string Id,
    string Name,
    int Seed,
    float Activity,
    float Curiosity,
    float Playfulness,
    float Boldness,
    float Agility,
    float Calmness,
    float Build)
{
    public static IndividualTraits Neutral(int seed = 0) => new(
        seed.ToString("X8"),
        "标准个体",
        seed,
        0.5f,
        0.5f,
        0.5f,
        0.5f,
        0.5f,
        0.5f,
        0.5f);
}

/// <summary>
/// Fully resolved immutable settings for one lizard instance.
/// </summary>
internal sealed record LizardProfile(
    LizardConfiguration Source,
    IndividualTraits Traits,
    BehaviorConfiguration Behavior,
    GaitConfiguration Gait,
    PhysicsConfiguration Physics,
    AppearanceConfiguration Appearance,
    SecondaryMotionConfiguration SecondaryMotion,
    RenderingConfiguration Rendering,
    RuntimeConfiguration Runtime)
{
    public static LizardProfile Default { get; } = IndividualProfileFactory.Create(
        LizardConfiguration.Default,
        0,
        applyVariation: false);
}

internal static class IndividualProfileFactory
{
    private static readonly string[] NamePrefixes =
    [
        "青石", "苔影", "栗尾", "云脊", "松露", "浅墨", "雨点", "琥珀"
    ];

    private static readonly string[] NameSuffixes =
    [
        "一号", "小爪", "慢慢", "弯弯", "疾风", "团子", "阿绿", "星点"
    ];

    public static LizardProfile Create(
        LizardConfiguration configuration,
        int seed,
        bool? applyVariation = null)
    {
        configuration.EnsureValid();
        var useVariation = applyVariation ?? configuration.IndividualVariation.Enabled;
        var traits = useVariation ? CreateTraits(seed) : IndividualTraits.Neutral(seed);
        if (!useVariation)
        {
            return new LizardProfile(
                configuration,
                traits,
                configuration.Behavior,
                configuration.Gait,
                configuration.Physics,
                configuration.Appearance,
                configuration.SecondaryMotion,
                configuration.Rendering,
                configuration.Runtime);
        }

        var variation = configuration.IndividualVariation;
        var context = IndividualVariationContext.Create(traits, variation);
        var behavior = IndividualBehaviorProfileResolver.Resolve(
            configuration.Behavior,
            traits,
            variation,
            context);
        var gait = IndividualAnimationProfileResolver.ResolveGait(configuration.Gait, context);
        var physics = IndividualPhysicsProfileResolver.Resolve(configuration.Physics, variation, context);
        var appearance = IndividualAppearanceProfileResolver.ResolveAppearance(
            configuration.Appearance,
            variation,
            context);
        var secondaryMotion = IndividualAnimationProfileResolver.ResolveSecondaryMotion(
            configuration.SecondaryMotion,
            variation,
            context);
        var rendering = IndividualAppearanceProfileResolver.ResolveRendering(
            configuration.Rendering,
            context);
        var envelope = LizardGeometryEnvelope.Calculate(
            appearance,
            gait,
            secondaryMotion,
            rendering);
        appearance = envelope.EnsureCanvasCapacity(appearance);
        var requiredFallSafetyInset = envelope.RequiredFallBottomSafetyInset(
            appearance,
            configuration.Runtime);
        if (behavior.LostGripFall.BottomSafetyInset + 0.0001f <
            requiredFallSafetyInset)
        {
            // Canvas capacity and fall clearance are both hard geometry
            // invariants. A larger individual may need more clearance than
            // its species-level minimum, so raise only the resolved profile.
            behavior = behavior with
            {
                LostGripFall = behavior.LostGripFall with
                {
                    BottomSafetyInset = MathF.Ceiling(requiredFallSafetyInset)
                }
            };
        }

        var resolved = configuration with
        {
            Behavior = behavior,
            Gait = gait,
            Physics = physics,
            Appearance = appearance,
            SecondaryMotion = secondaryMotion,
            Rendering = rendering
        };
        resolved.EnsureValid();
        return new LizardProfile(
            configuration,
            traits,
            behavior,
            gait,
            physics,
            appearance,
            secondaryMotion,
            rendering,
            configuration.Runtime);
    }

    private static IndividualTraits CreateTraits(int seed)
    {
        // A separate stream guarantees that adding a trait never shifts the
        // BehaviorController's seeded action sequence.
        var random = new Random(unchecked(seed ^ (int)0x6C697A64));
        var prefix = NamePrefixes[random.Next(NamePrefixes.Length)];
        var suffix = NameSuffixes[random.Next(NameSuffixes.Length)];
        return new IndividualTraits(
            unchecked((uint)seed).ToString("X8"),
            prefix + suffix,
            seed,
            NextTrait(random),
            NextTrait(random),
            NextTrait(random),
            NextTrait(random),
            NextTrait(random),
            NextTrait(random),
            NextTrait(random));
    }

    private static float NextTrait(Random random)
    {
        // Average two uniforms to avoid a population dominated by extremes.
        return ((float)random.NextDouble() + (float)random.NextDouble()) * 0.5f;
    }

}
