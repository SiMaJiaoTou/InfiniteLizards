using System.Collections.Immutable;

namespace InfiniteLizards.Gameplay.Genetics;

public enum TraitEffectSurface
{
    VisualPhenotype,
    RuntimeProfile,
    BreedingLifecycle
}

public sealed record TraitEffectCoverage(
    string TraitId,
    TraitEffectSurface Surface,
    string ObservableEffect);

/// <summary>
/// Auditable bindings for loci that previously appeared only as detail-panel
/// numbers. Tests vary each locus independently and verify the named output;
/// this table is documentation and a completeness gate, not the implementation.
/// </summary>
public static class DefaultTraitEffectCoverage
{
    public const string RemovedLongevityTraitId = "lifecycle.longevity";

    public static ImmutableArray<TraitEffectCoverage> FormerlyReadoutOnly { get; } =
    [
        Visual("color.iridescence", "Palette.Secondary hue and Iridescence"),
        Visual("color.melanin", "Palette lightness and Melanin"),
        Visual("body.neck-length", "Body.NeckLengthRatio and live head offset"),
        Visual("body.belly-roundness", "Body.BellyRoundness and live body widths"),
        Visual("body.spine-arch", "Body.SpineArch and live rest curve"),
        Visual("body.flexibility", "Body.Flexibility and live maximum bend"),
        Visual("limbs.left-right-asymmetry", "Limbs.LeftRightAsymmetry and stance skew"),
        Visual("skin.scale-size", "Skin.ScaleSizeRatio"),
        Visual("skin.roughness", "Skin.Roughness and live shadow texture strength"),
        Visual("skin.gloss", "Skin.Gloss and live highlight/shadow balance"),
        Visual("skin.translucency", "Skin.Translucency"),
        Visual("tail.flexibility", "Tail.Flexibility and live tail motion"),
        Visual("tail.segment-count", "Tail.SegmentCount"),
        Visual("tail.fork.present", "Tail.Fork.IsPresent"),
        Visual("tail.fork.length", "Tail.Fork.LengthRatio"),

        Runtime("locomotion.gait", "gait cadence/stagger/lift profile"),
        Runtime("locomotion.grip", "legal LostGripFall transition weight"),
        Runtime("locomotion.endurance", "walk-bout duration and rest chance"),
        Runtime("locomotion.fall-control", "bounded fall gravity/velocity/regrip duration"),
        Runtime("locomotion.swim-affinity", "desktop S-curve amplitude/cadence/weight"),
        Runtime("temperament.patience", "observe duration"),
        Runtime("temperament.food-drive", "search pace and fast-action weight"),
        Runtime("temperament.sleepiness", "rest chance and long-rest distribution"),
        Runtime("pointer.click-sensitivity", "post-drag release escape distance/acceleration"),
        Runtime("pointer.drag-tolerance", "release settle and escape distance"),
        Runtime("behavior.transition.rest-to-long-rest", "long-rest band weight"),

        Lifecycle("temperament.sociability", "individual breeding cooldown rate"),
        Lifecycle("temperament.territoriality", "individual breeding cooldown rate"),
        Lifecycle("behavior.transition.social-approach", "individual breeding cooldown rate"),
        Lifecycle("behavior.transition.courtship", "individual breeding cooldown rate"),
        Lifecycle("behavior.transition.egg-guarding", "symmetric egg incubation rate"),
        Lifecycle("lifecycle.vitality", "offspring maturation rate"),
        Lifecycle("lifecycle.metabolism", "incubation and maturation rate"),
        Lifecycle("lifecycle.egg-shell-pattern", "EggAppearance.Pattern"),
        Lifecycle("lifecycle.egg-shell-hue", "EggAppearance.HueDegrees")
    ];

    /// <summary>
    /// Existing loci that already had a renderer, runtime-profile or lifecycle
    /// consumer before the formerly-readout-only closure. Kept explicit so a
    /// registry addition cannot silently ship with detail-panel-only semantics.
    /// </summary>
    private static ImmutableArray<TraitEffectCoverage> Established { get; } =
    [
        Visual("color.base-hue", "portrait primary hue"),
        Visual("color.saturation", "portrait palette saturation"),
        Visual("color.lightness", "portrait primary lightness"),
        Visual("color.belly-hue", "portrait belly hue"),
        Visual("color.belly-lightness", "portrait belly lightness"),
        Visual("color.accent-hue", "portrait pattern accent hue"),
        Visual("color.eye-hue", "portrait iris hue"),
        Visual("color.eye-glow", "portrait eye luminance"),

        Visual("pattern.type", "portrait pattern branch"),
        Visual("pattern.density", "portrait pattern element density"),
        Visual("pattern.contrast", "portrait pattern contrast"),
        Visual("pattern.element-size", "portrait pattern element scale"),
        Visual("pattern.stripe-width", "portrait stripe width"),
        Visual("pattern.stripe-count", "portrait stripe count"),
        Visual("pattern.symmetry", "portrait bilateral pattern symmetry"),
        Visual("pattern.edge-softness", "portrait pattern edge softness"),
        Visual("pattern.glow.present", "portrait glow layer presence"),
        Visual("pattern.glow.intensity", "portrait glow layer intensity"),

        Visual(DefaultLizardTraitIds.BodyLength, "portrait/live body length"),
        Visual(DefaultLizardTraitIds.BodyWidth, "portrait/live body width"),
        Visual("body.height", "portrait body height"),
        Visual("body.shoulder-mass", "portrait shoulder silhouette"),
        Visual("body.hip-mass", "portrait hip silhouette"),
        Visual("body.taper", "portrait torso taper"),

        Visual("head.size", "portrait head scale"),
        Visual("head.width", "portrait head width"),
        Visual("head.snout-length", "portrait snout length"),
        Visual("head.snout-shape", "portrait snout silhouette branch"),
        Visual("head.eye-size", "portrait/live eye scale"),
        Visual("head.eye-spacing", "portrait/live eye spacing"),
        Visual("head.pupil-shape", "portrait pupil silhouette branch"),
        Visual("head.horns.present", "portrait horn presence"),
        Visual("head.horns.length", "portrait horn length"),
        Visual("head.horns.curvature", "portrait horn curvature"),
        Visual("head.crest.present", "portrait crest presence"),
        Visual("head.crest.height", "portrait crest height"),

        Visual(DefaultLizardTraitIds.LegPairCount, "portrait leg-pair topology"),
        Visual(DefaultLizardTraitIds.LegJointCount, "portrait visible leg joints"),
        Visual("limbs.length", "portrait/live leg length"),
        Visual("limbs.thickness", "portrait/live leg thickness"),
        Visual("limbs.front-rear-ratio", "portrait/live front-rear proportions"),
        Visual("limbs.foot-size", "portrait/live foot scale"),
        Visual("limbs.toe-count", "portrait toe count"),
        Visual("limbs.claw-length", "portrait claw length"),
        Visual("limbs.webbing.present", "portrait webbing presence"),
        Visual("limbs.webbing.amount", "portrait webbing area"),
        Visual("limbs.grip-pad-size", "portrait adhesive-pad scale"),

        Visual(DefaultLizardTraitIds.DorsalFinPresent, "portrait dorsal-fin presence"),
        Visual("appendage.dorsal-fin.height", "portrait dorsal-fin height"),
        Visual("appendage.dorsal-fin.length", "portrait dorsal-fin coverage"),
        Visual("appendage.dorsal-fin.shape", "portrait dorsal-fin silhouette branch"),
        Visual("appendage.side-fins.present", "portrait side-fin presence"),
        Visual("appendage.side-fins.pairs", "portrait side-fin pair count"),
        Visual("appendage.side-fins.size", "portrait side-fin scale"),
        Visual("appendage.neck-frill.present", "portrait neck-frill presence"),
        Visual("appendage.neck-frill.size", "portrait neck-frill scale"),
        Visual("appendage.gill-tuft.present", "portrait gill-tuft presence"),
        Visual("appendage.gill-tuft.length", "portrait gill-tuft length"),
        Visual("appendage.whiskers.present", "portrait whisker presence"),
        Visual("appendage.whiskers.pairs", "portrait whisker pair count"),
        Visual("appendage.whiskers.length", "portrait whisker length"),

        Visual("tail.length", "portrait/live tail length"),
        Visual("tail.base-thickness", "portrait/live tail base thickness"),
        Visual("tail.taper", "portrait/live tail taper"),
        Visual("tail.sail.present", "portrait tail-sail presence"),
        Visual("tail.sail.height", "portrait tail-sail height"),
        Visual("tail.sail.length", "portrait tail-sail coverage"),
        Visual("tail.spikes.present", "portrait tail-spike presence"),
        Visual("tail.spikes.count", "portrait tail-spike count"),
        Visual("tail.spikes.size", "portrait tail-spike scale"),
        Visual(DefaultLizardTraitIds.TailClubPresent, "portrait tail-club presence"),
        Visual("tail.club.size", "portrait tail-club scale"),
        Visual("tail.club-spike-length", "portrait club-spike length"),

        Runtime(DefaultLizardTraitIds.MaximumSpeed, "live maximum crawl speed"),
        Runtime("locomotion.acceleration", "live crawl acceleration"),
        Runtime("locomotion.turn-agility", "live agility identity/profile"),
        Runtime("locomotion.stride", "live gait lead distance"),
        Runtime("locomotion.step-height", "live gait foot lift"),
        Runtime("locomotion.balance", "live agility identity/profile"),
        Runtime("locomotion.idle-sway", "live idle tail amplitude"),

        Runtime("temperament.activity", "live activity identity/decision profile"),
        Runtime("temperament.curiosity", "live curiosity identity/decision profile"),
        Runtime("temperament.playfulness", "live playfulness identity/decision profile"),
        Runtime("temperament.boldness", "live boldness identity/decision profile"),
        Runtime("temperament.calmness", "live calmness identity/decision profile"),

        Runtime(DefaultLizardTraitIds.PointerDisposition, "live Chase/Avoid pointer mode"),
        Runtime("pointer.attention-radius", "live pointer trigger radius"),
        Runtime("pointer.reaction-delay", "live pointer attention delay"),
        Runtime("pointer.chase-persistence", "live pointer lost grace"),
        Runtime("pointer.avoidance-radius", "live Avoid safe separation"),
        Runtime("pointer.recovery", "live pointer response cooldown"),

        Runtime("behavior.transition.idle-to-explore", "legal observe/explore decision weight"),
        Runtime("behavior.transition.explore-to-idle", "legal rest decision weight"),
        Runtime("behavior.transition.observe-to-chase", "live pointer attention gate"),
        Runtime("behavior.transition.chase-to-sprint", "legal fast-action weight"),
        Runtime("behavior.transition.wander-to-curve", "legal Curve transition weight"),
        Runtime("behavior.transition.curve-to-s-curve", "legal S-curve transition weight"),
        Runtime("behavior.transition.edge-to-panic", "legal LostGripFall transition weight"),
        Runtime("behavior.transition.novelty-seeking", "legal uncommon-action weight"),

        Lifecycle(DefaultLizardTraitIds.IncubationRate, "egg incubation duration"),
        Lifecycle(DefaultLizardTraitIds.MaturationRate, "juvenile maturation duration"),
        Lifecycle(DefaultLizardTraitIds.MutationSensitivity, "offspring mutation multiplier")
    ];

    /// <summary>
    /// One and only one declared gameplay consumer for every current default
    /// locus. The self-test compares this set exactly with registry v2.
    /// </summary>
    public static ImmutableArray<TraitEffectCoverage> AllPlayerFacing { get; } =
        Established.AddRange(FormerlyReadoutOnly);

    private static TraitEffectCoverage Visual(string id, string effect) =>
        new(id, TraitEffectSurface.VisualPhenotype, effect);

    private static TraitEffectCoverage Runtime(string id, string effect) =>
        new(id, TraitEffectSurface.RuntimeProfile, effect);

    private static TraitEffectCoverage Lifecycle(string id, string effect) =>
        new(id, TraitEffectSurface.BreedingLifecycle, effect);
}
