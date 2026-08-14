using System.Numerics;

namespace DesktopLizard.Core;

internal readonly record struct PointerObservation(
    Vector2 Position,
    bool IsAvailable,
    bool IsInteractionBlocked = false);

/// <summary>
/// Carries the host's exact full-render-safe center area into the behavior
/// layer. <see cref="IsAvailable"/> is deliberately separate from the
/// fallback rectangle: when a work area is too small, callers still provide
/// the best clamp area but autonomous lost-grip entry must be rejected.
/// </summary>
internal readonly record struct LostGripSafetyContext(
    FloatRect SafeArea,
    bool IsAvailable);

internal readonly record struct FloatRect(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;
    public Vector2 Center => new((Left + Right) * 0.5f, (Top + Bottom) * 0.5f);

    public FloatRect Inset(float amount) => new(
        Left + amount,
        Top + amount,
        Right - amount,
        Bottom - amount);

    public Vector2 Clamp(Vector2 point) => new(
        Math.Clamp(point.X, Left, Right),
        Math.Clamp(point.Y, Top, Bottom));

    public bool Contains(Vector2 point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
}

internal readonly record struct EmotionBlend(float Calm, float Curious, float Playful, float Wary)
{
    public static EmotionBlend Normalize(EmotionBlend value)
    {
        var calm = Math.Max(0.001f, value.Calm);
        var curious = Math.Max(0.001f, value.Curious);
        var playful = Math.Max(0.001f, value.Playful);
        var wary = Math.Max(0.001f, value.Wary);
        var sum = calm + curious + playful + wary;
        return new EmotionBlend(calm / sum, curious / sum, playful / sum, wary / sum);
    }

    public static EmotionBlend Lerp(EmotionBlend from, EmotionBlend to, float t) => Normalize(new EmotionBlend(
        MathEx.Lerp(from.Calm, to.Calm, t),
        MathEx.Lerp(from.Curious, to.Curious, t),
        MathEx.Lerp(from.Playful, to.Playful, t),
        MathEx.Lerp(from.Wary, to.Wary, t)));
}
