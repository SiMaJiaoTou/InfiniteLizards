using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// Pure topology queries for the suspended lizard rig. This is the single
/// source of truth for limb attachment indices, sides, shoulder geometry and
/// held-spine membership; it never mutates the particle pose.
/// </summary>
internal static class DanglingTopology2D
{
    public const int LegCount = 4;

    public static int GetBodyIndex(int legIndex) => legIndex < 2 ? 1 : 4;

    public static float GetSide(int legIndex) => legIndex % 2 == 0 ? 1f : -1f;

    public static Vector2 GetShoulderPosition(
        ReadOnlySpan<PhysicsParticle2D> spine,
        int legIndex,
        int bodyIndex,
        float fallbackBodyAngle,
        float bodyWidth,
        float shoulderAttachmentRadiusFactor)
    {
        // Chain.SetPose defines a body's forward direction from the current
        // joint toward the preceding (head-side) joint. Use that exact frame in
        // both the solver and renderer so the upper-leg constraint cannot pop.
        var previousIndex = Math.Max(0, bodyIndex - 1);
        var direction = MathEx.SafeNormalize(
            spine[previousIndex].Position - spine[bodyIndex].Position,
            MathEx.FromAngle(fallbackBodyAngle));
        var side = GetSide(legIndex);
        var radius = bodyWidth * shoulderAttachmentRadiusFactor;
        return spine[bodyIndex].Position + MathEx.Perpendicular(direction) * side * radius;
    }

    public static (int Start, int End) GetHeldSpineSegment(GrabBinding binding)
    {
        var start = binding.Kind == GrabBindingKind.Spine
            ? binding.Index
            : Math.Max(0, (GetBodyIndex(binding.Index)) - 1);
        return (start, start + 1);
    }

    public static bool IsHeldSpineJoint(GrabBinding binding, int index)
    {
        var held = GetHeldSpineSegment(binding);
        return index == held.Start || index == held.End;
    }

    public static GrabBinding FindClosestBinding(
        ProceduralLizard lizard,
        ReadOnlySpan<PhysicsParticle2D> spine,
        ReadOnlySpan<PhysicsParticle2D> elbows,
        ReadOnlySpan<PhysicsParticle2D> feet,
        Vector2 point,
        float shoulderAttachmentRadiusFactor)
    {
        var nearestDistance = float.MaxValue;
        var nearest = new GrabBinding(GrabBindingKind.Spine, 0, 0f, 0f, 0f);

        void Consider(GrabBindingKind kind, int index, Vector2 start, Vector2 end)
        {
            var candidate = GrabBindingGeometry.CreateCandidate(
                kind,
                index,
                point,
                start,
                end);
            if (candidate.DistanceSquared >= nearestDistance)
            {
                return;
            }

            nearestDistance = candidate.DistanceSquared;
            nearest = candidate.Binding;
        }

        for (var index = 0; index < spine.Length - 1; index++)
        {
            Consider(
                GrabBindingKind.Spine,
                index,
                spine[index].Position,
                spine[index + 1].Position);
        }

        for (var legIndex = 0; legIndex < LegCount; legIndex++)
        {
            var bodyIndex = GetBodyIndex(legIndex);
            var shoulder = GetShoulderPosition(
                spine,
                legIndex,
                bodyIndex,
                lizard.Spine.Angles[bodyIndex],
                lizard.GetBodyWidth(bodyIndex),
                shoulderAttachmentRadiusFactor);
            Consider(
                GrabBindingKind.UpperLeg,
                legIndex,
                shoulder,
                elbows[legIndex].Position);
            Consider(
                GrabBindingKind.LowerLeg,
                legIndex,
                elbows[legIndex].Position,
                feet[legIndex].Position);
        }

        return nearest;
    }

    public static Vector2 GetBindingPoint(
        ProceduralLizard lizard,
        ReadOnlySpan<PhysicsParticle2D> spine,
        ReadOnlySpan<PhysicsParticle2D> elbows,
        ReadOnlySpan<PhysicsParticle2D> feet,
        GrabBinding binding,
        float shoulderAttachmentRadiusFactor)
    {
        Vector2 start;
        Vector2 end;
        switch (binding.Kind)
        {
            case GrabBindingKind.Spine:
                start = spine[binding.Index].Position;
                end = spine[binding.Index + 1].Position;
                break;

            case GrabBindingKind.UpperLeg:
            {
                var bodyIndex = GetBodyIndex(binding.Index);
                start = GetShoulderPosition(
                    spine,
                    binding.Index,
                    bodyIndex,
                    lizard.Spine.Angles[bodyIndex],
                    lizard.GetBodyWidth(bodyIndex),
                    shoulderAttachmentRadiusFactor);
                end = elbows[binding.Index].Position;
                break;
            }

            default:
                start = elbows[binding.Index].Position;
                end = feet[binding.Index].Position;
                break;
        }

        return GrabBindingGeometry.Evaluate(binding, start, end);
    }
}
