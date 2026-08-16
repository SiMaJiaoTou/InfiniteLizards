using System.Collections.Immutable;
using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// A coherent, immutable pose captured after the simulation has finished a
/// display frame. Rendering can safely retain this value without observing a
/// partially-updated procedural rig.
/// </summary>
internal readonly record struct LizardRenderFrame(
    ImmutableArray<Vector2> BodyOutline,
    ImmutableArray<LizardRenderLegPose> Legs,
    Vector2 HeadNose,
    Vector2 NegativeEyeCenter,
    Vector2 PositiveEyeCenter,
    float Heading,
    float BlinkAmount)
{
    public Vector2 GetEyeCenter(int side) => side < 0
        ? NegativeEyeCenter
        : side > 0
            ? PositiveEyeCenter
            : (NegativeEyeCenter + PositiveEyeCenter) * 0.5f;
}

/// <summary>
/// The small rendering subset of a leg rig. Step scheduling and IK internals
/// deliberately remain on the mutable animation model.
/// </summary>
internal readonly record struct LizardRenderLegPose(
    Vector2 Shoulder,
    Vector2 Elbow,
    Vector2 Foot,
    bool IsFront,
    float Lift);
