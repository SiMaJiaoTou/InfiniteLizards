using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// Animation-facing pose intent. Behavior may add or rename states without
/// leaking its state-machine vocabulary into the procedural rig.
/// </summary>
internal enum LizardPoseMode
{
    Rest,
    Observe,
    Locomotion,
    FastSCurve,
    Grabbed,
    FreeFall,
    Regrip,
    ReleaseSettle
}

internal static class LizardPoseModeTraits
{
    public static bool IsLocomoting(this LizardPoseMode mode) => mode is
        LizardPoseMode.Locomotion or
        LizardPoseMode.FastSCurve;

    /// <summary>
    /// True while the pose is authored by the shared particle rig. A free
    /// fall intentionally has no material grab point even though both modes
    /// use the same integration and bone-constraint primitives.
    /// </summary>
    public static bool UsesParticleRig(this LizardPoseMode mode) => mode is
        LizardPoseMode.Grabbed or
        LizardPoseMode.FreeFall;
}

/// <summary>
/// Complete input for one procedural-animation step. Values are expressed in
/// model space and contain presentation semantics rather than behavior states.
/// DropProgress is the normalized progress of the active vertical event: the
/// release landing for ReleaseSettle, or traveled distance for FreeFall/Regrip.
/// </summary>
internal readonly record struct LizardAnimationInput(
    float DesiredHeading,
    float NormalizedSpeed,
    LizardPoseMode PoseMode,
    EmotionBlend Emotion,
    float DropProgress,
    Vector2 ScreenDeltaModel)
{
    /// <summary>
    /// Normalized anticipation of an imminent lost-grip catch. It is nonzero
    /// only for FreeFall, reaches exactly 1 on the final moving step, and is
    /// reset when Regrip begins on the following stationary step.
    /// </summary>
    public float CatchPreparationProgress { get; init; }
}
