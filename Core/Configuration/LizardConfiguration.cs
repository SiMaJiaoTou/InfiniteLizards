using System.Collections.ObjectModel;
using System.IO;

namespace DesktopLizard.Core;

/// <summary>
/// Editable species defaults. Runtime code consumes a resolved
/// <see cref="LizardProfile"/> instead of reaching for scattered constants.
/// Numerical epsilons and platform protocol values are intentionally not
/// configuration: they are algorithmic safety invariants, not pet traits.
/// </summary>
internal sealed record LizardConfiguration
{
    public const int CurrentSchemaVersion = 6;
    internal const int MaximumEscapeDirectionCandidateCount = 256;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public int? IndividualSeed { get; init; }
    public BehaviorConfiguration Behavior { get; init; } = new();
    public GaitConfiguration Gait { get; init; } = new();
    public PhysicsConfiguration Physics { get; init; } = new();
    public AppearanceConfiguration Appearance { get; init; } = new();
    public SecondaryMotionConfiguration SecondaryMotion { get; init; } = new();
    public RenderingConfiguration Rendering { get; init; } = new();
    public RuntimeConfiguration Runtime { get; init; } = new();
    public IndividualVariationConfiguration IndividualVariation { get; init; } = new();

    public static LizardConfiguration Default { get; } = new();

    public IReadOnlyList<string> Validate()
    {
        var failures = new List<string>();
        if (SchemaVersion != CurrentSchemaVersion)
        {
            failures.Add($"Unsupported configuration schema version: {SchemaVersion}.");
        }

        ValidateSection(Behavior, nameof(Behavior), failures, section => section.Validate(failures));
        ValidateSection(Gait, nameof(Gait), failures, section => section.Validate(failures));
        ValidateSection(Physics, nameof(Physics), failures, section => section.Validate(failures));
        ValidateSection(Appearance, nameof(Appearance), failures, section => section.Validate(failures));
        ValidateSection(
            SecondaryMotion,
            nameof(SecondaryMotion),
            failures,
            section => section.Validate(failures));
        ValidateSection(Rendering, nameof(Rendering), failures, section => section.Validate(failures));
        ValidateSection(Runtime, nameof(Runtime), failures, section => section.Validate(failures));
        ValidateSection(
            IndividualVariation,
            nameof(IndividualVariation),
            failures,
            section => section.Validate(failures));
        if (Appearance is not null &&
            Gait is not null &&
            SecondaryMotion is not null &&
            Rendering is not null)
        {
            var envelope = LizardGeometryEnvelope.Calculate(
                Appearance,
                Gait,
                SecondaryMotion,
                Rendering);
            if (Appearance.CreatureCanvasSize * 0.5f + 0.0001f < envelope.NormalModelRadius)
            {
                failures.Add(
                    $"creature canvas radius must be at least {envelope.NormalModelRadius:F2} model units for the configured normal pose.");
            }
            if (Appearance.RenderCanvasSize * 0.5f + 0.0001f < envelope.DanglingModelRadius)
            {
                failures.Add(
                    $"render canvas radius must be at least {envelope.DanglingModelRadius:F2} model units for the configured dangling pose.");
            }

            if (Behavior?.LostGripFall is not null && Runtime is not null)
            {
                var requiredFallSafetyInset = envelope.RequiredFallBottomSafetyInset(
                    Appearance,
                    Runtime);
                if (Behavior.LostGripFall.BottomSafetyInset + 0.0001f <
                    requiredFallSafetyInset)
                {
                    failures.Add(
                        "lost-grip bottom safety inset must be at least " +
                        $"{requiredFallSafetyInset:F2} screen pixels for the configured full render canvas.");
                }
            }
        }
        if (Behavior is not null &&
            Behavior.EscapeSprint is not null &&
            Behavior.EscapeSprint.DirectionCandidateCount > MaximumEscapeDirectionCandidateCount)
        {
            failures.Add(
                $"escape sprint direction candidate count must be in [1,{MaximumEscapeDirectionCandidateCount}].");
        }
        if (Runtime is not null && Physics is not null &&
            float.IsFinite(Runtime.SimulationRate) && Runtime.SimulationRate > 0f)
        {
            var simulationStep = 1f / Runtime.SimulationRate;
            if (float.IsFinite(Physics.MinimumSimulationStep) &&
                float.IsFinite(Physics.MaximumSimulationStep) &&
                (simulationStep < Physics.MinimumSimulationStep ||
                 simulationStep > Physics.MaximumSimulationStep))
            {
                failures.Add(
                    "runtime simulation step must be inside the dangling-physics simulation-step range.");
            }
        }
        return new ReadOnlyCollection<string>(failures);
    }

    private static void ValidateSection<T>(
        T? section,
        string name,
        List<string> failures,
        Action<T> validate)
        where T : class
    {
        if (section is null)
        {
            failures.Add($"configuration section '{name}' must not be null.");
            return;
        }

        validate(section);
    }

    public LizardConfiguration EnsureValid()
    {
        var failures = Validate();
        if (failures.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, failures));
        }

        return this;
    }
}
