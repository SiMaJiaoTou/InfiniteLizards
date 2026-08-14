namespace DesktopLizard.Core;

internal readonly record struct IndividualVariationContext(
    float Activity,
    float Curiosity,
    float Playfulness,
    float Boldness,
    float Agility,
    float Calmness,
    float Build,
    float SpeedMultiplier,
    float DurationMultiplier,
    float TurnMultiplier,
    float GaitMultiplier,
    float GaitCadenceMultiplier,
    float GaitHeightMultiplier,
    float PhysicsMultiplier,
    float BodyMultiplier)
{
    internal static IndividualVariationContext Create(
        IndividualTraits traits,
        IndividualVariationConfiguration variation)
    {
        var activity = Centered(traits.Activity);
        var curiosity = Centered(traits.Curiosity);
        var playfulness = Centered(traits.Playfulness);
        var boldness = Centered(traits.Boldness);
        var agility = Centered(traits.Agility);
        var calmness = Centered(traits.Calmness);
        var build = Centered(traits.Build);
        var coefficients = variation.Multipliers;

        var speedMultiplier = 1f + variation.SpeedVariation *
            (activity * coefficients.SpeedActivity + agility * coefficients.SpeedAgility);
        var durationMultiplier = 1f + variation.DurationVariation *
            (calmness * coefficients.DurationCalmness - activity * coefficients.DurationActivity);
        var turnMultiplier = 1f + variation.TurnVariation *
            (agility * coefficients.TurnAgility + curiosity * coefficients.TurnCuriosity);
        var gaitMultiplier = 1f + variation.GaitVariation *
            (agility * coefficients.GaitAgility + build * coefficients.GaitBuild);
        var gaitCadenceMultiplier = 1f - variation.GaitVariation * agility * coefficients.GaitCadenceAgility;
        var gaitHeightMultiplier = 1f + variation.GaitVariation * agility * coefficients.GaitHeightAgility;
        var physicsMultiplier = 1f + variation.PhysicsVariation * build * coefficients.PhysicsBuild;
        var bodyMultiplier = 1f + variation.GaitVariation * coefficients.BodyBuild * build;

        return new IndividualVariationContext(
            activity,
            curiosity,
            playfulness,
            boldness,
            agility,
            calmness,
            build,
            speedMultiplier,
            durationMultiplier,
            turnMultiplier,
            gaitMultiplier,
            gaitCadenceMultiplier,
            gaitHeightMultiplier,
            physicsMultiplier,
            bodyMultiplier);
    }

    private static float Centered(float value) => Math.Clamp(value, 0f, 1f) * 2f - 1f;
}
