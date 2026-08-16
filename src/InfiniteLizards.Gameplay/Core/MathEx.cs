using System.Numerics;

namespace DesktopLizard.Core;

internal static class MathEx
{
    public const float TwoPi = MathF.PI * 2f;

    public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    public static float Lerp(float from, float to, float t) => from + (to - from) * t;

    public static Vector2 Lerp(Vector2 from, Vector2 to, float t) => from + (to - from) * t;

    public static float ExpLerpFactor(float response, float dt) => 1f - MathF.Exp(-response * dt);

    public static float SimplifyAngle(float angle)
    {
        angle %= TwoPi;
        return angle < 0f ? angle + TwoPi : angle;
    }

    public static float DeltaAngle(float from, float to)
    {
        var delta = SimplifyAngle(to - from);
        return delta > MathF.PI ? delta - TwoPi : delta;
    }

    public static float RotateTowards(float current, float target, float maxDelta)
    {
        var delta = DeltaAngle(current, target);
        if (MathF.Abs(delta) <= maxDelta)
        {
            return SimplifyAngle(target);
        }

        return SimplifyAngle(current + MathF.CopySign(maxDelta, delta));
    }

    public static Vector2 SafeNormalize(Vector2 value, Vector2 fallback)
    {
        var lengthSquared = value.LengthSquared();
        return lengthSquared < 0.000001f ? fallback : value / MathF.Sqrt(lengthSquared);
    }

    public static Vector2 FromAngle(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));

    public static Vector2 Perpendicular(Vector2 value) => new(-value.Y, value.X);

    public static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared < 0.000001f)
        {
            return Vector2.Distance(point, start);
        }

        var t = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, start + segment * t);
    }
}
