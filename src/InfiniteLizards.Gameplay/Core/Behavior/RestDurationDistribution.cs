namespace DesktopLizard.Core;

internal static class RestDurationDistribution
{
    public static float Sample(float sample) =>
        Sample(sample, LizardProfile.Default.Behavior.Rest);

    public static float Sample(float sample, RestConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        sample = MathEx.Clamp01(sample);
        var cumulativeWeight = 0f;
        for (var index = 0; index < configuration.Bands.Length; index++)
        {
            var band = configuration.Bands[index];
            var nextWeight = cumulativeWeight + band.Weight;
            if (sample < nextWeight || index == configuration.Bands.Length - 1)
            {
                var local = band.Weight > 0f
                    ? (sample - cumulativeWeight) / band.Weight
                    : 1f;
                return MathEx.Lerp(
                    band.MinimumDuration,
                    band.MaximumDuration,
                    MathEx.Clamp01(local));
            }

            cumulativeWeight = nextWeight;
        }

        throw new InvalidOperationException("Rest duration configuration contains no bands.");
    }
}
