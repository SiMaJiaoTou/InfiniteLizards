using System.Numerics;

namespace DesktopLizard.Core;

internal enum GrabBindingKind
{
    Spine,
    UpperLeg,
    LowerLeg
}

/// <summary>
/// A material-space attachment on a bone segment. Tangent and normal offsets
/// rotate with that segment, so grabbing a body edge or paw does not slide in
/// canvas coordinates while the suspended pose turns.
/// </summary>
internal readonly record struct GrabBinding(
    GrabBindingKind Kind,
    int Index,
    float T,
    float TangentOffset,
    float NormalOffset);

internal readonly record struct GrabBindingCandidate(
    GrabBinding Binding,
    float DistanceSquared);

internal static class GrabBindingGeometry
{
    public static GrabBindingCandidate CreateCandidate(
        GrabBindingKind kind,
        int index,
        Vector2 point,
        Vector2 start,
        Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        var t = lengthSquared > 0.000001f
            ? Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0f, 1f)
            : 0f;
        var closest = Vector2.Lerp(start, end, t);
        var tangent = MathEx.SafeNormalize(segment, Vector2.UnitX);
        var normal = MathEx.Perpendicular(tangent);
        var offset = point - closest;
        return new GrabBindingCandidate(
            new GrabBinding(
                kind,
                index,
                t,
                Vector2.Dot(offset, tangent),
                Vector2.Dot(offset, normal)),
            Vector2.DistanceSquared(point, closest));
    }

    public static Vector2 Evaluate(GrabBinding binding, Vector2 start, Vector2 end)
    {
        var tangent = MathEx.SafeNormalize(end - start, Vector2.UnitX);
        var normal = MathEx.Perpendicular(tangent);
        return Vector2.Lerp(start, end, binding.T) +
               tangent * binding.TangentOffset +
               normal * binding.NormalOffset;
    }
}
