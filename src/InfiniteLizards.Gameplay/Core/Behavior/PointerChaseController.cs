using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// Owns the persistent pointer-attention lifecycle without owning behavior
/// transitions, random sampling, or locomotion. Keeping those responsibilities
/// in <see cref="BehaviorController"/> preserves the seeded state-machine order.
/// </summary>
internal sealed class PointerChaseController
{
    private readonly PointerChaseConfiguration _configuration;

    public PointerChaseController(PointerChaseConfiguration? configuration = null)
    {
        _configuration = configuration ?? new PointerChaseConfiguration();
    }

    public Vector2 Target { get; private set; }
    public float AttentionTime { get; private set; }
    public float LostTime { get; private set; }
    public float Cooldown { get; private set; }
    public bool NeedsLeave { get; private set; }
    public bool HasTarget { get; private set; }

    /// <summary>
    /// Returns whether the autonomous state permits a pointer interruption.
    /// This is deliberately independent of the pointer lifecycle state.
    /// </summary>
    public static bool CanStartFrom(RoamingState state) => state is
        RoamingState.Idle or
        RoamingState.Observe or
        RoamingState.ForwardCrawl or
        RoamingState.FastForwardCrawl or
        RoamingState.CurveCrawl or
        RoamingState.SCurveCrawl or
        RoamingState.FastSCurveCrawl or
        RoamingState.TurnAround;

    public void Reset(Vector2 initialTarget)
    {
        Target = initialTarget;
        AttentionTime = 0f;
        LostTime = 0f;
        Cooldown = 0f;
        NeedsLeave = false;
        HasTarget = false;
    }

    /// <summary>
    /// Translates the persistent target into a replacement world-coordinate
    /// frame without changing pointer attention, cooldown, or chase state.
    /// </summary>
    public void TranslateWorld(Vector2 delta)
    {
        if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                "The world translation must be finite.");
        }
        if (delta == Vector2.Zero)
        {
            return;
        }

        var translated = Target + delta;
        if (!float.IsFinite(translated.X) || !float.IsFinite(translated.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                "The translated pointer target must remain finite.");
        }

        Target = translated;
    }

    /// <summary>
    /// Advances the cooldown and rearms pursuit only after the pointer has
    /// moved far enough away from the lizard.
    /// </summary>
    public void UpdateArmState(float dt, Vector2 position, PointerObservation pointer)
    {
        Cooldown = Math.Max(0f, Cooldown - dt);
        if (!pointer.IsAvailable || !NeedsLeave)
        {
            return;
        }

        if (Vector2.Distance(position, pointer.Position) >= _configuration.RearmDistance)
        {
            NeedsLeave = false;
        }
    }

    /// <summary>
    /// Tracks a pointer in the trigger annulus and reports when its attention
    /// dwell has completed. Invalid input clears only the pending attention.
    /// </summary>
    public bool UpdateAttention(float dt, Vector2 position, PointerObservation pointer)
    {
        if (!CanAttend(position, pointer))
        {
            ClearAttention();
            return false;
        }

        Track(pointer.Position, dt);
        AttentionTime += dt;
        return AttentionTime >= _configuration.AttentionDuration;
    }

    /// <summary>
    /// Updates the smoothed chase target and lost-target grace timer.
    /// </summary>
    public void UpdateChaseTracking(float dt, Vector2 position, PointerObservation pointer)
    {
        if (pointer.IsAvailable)
        {
            Track(pointer.Position, dt);
            LostTime = Vector2.Distance(position, pointer.Position) > _configuration.LostDistance
                ? LostTime + dt
                : 0f;
        }
        else
        {
            LostTime += dt;
        }
    }

    public bool HasLostTarget => LostTime >= _configuration.LostGraceDuration;

    public void BeginChase(Vector2 pointerPosition)
    {
        Target = pointerPosition;
        HasTarget = true;
        AttentionTime = 0f;
        LostTime = 0f;
    }

    public void Suppress(float cooldown, bool requireLeave)
    {
        AttentionTime = 0f;
        LostTime = 0f;
        Cooldown = Math.Max(Cooldown, cooldown);
        NeedsLeave |= requireLeave;
        HasTarget = false;
    }

    public void ClearAttention()
    {
        AttentionTime = 0f;
        HasTarget = false;
    }

    private bool CanAttend(Vector2 position, PointerObservation pointer)
    {
        if (!pointer.IsAvailable || pointer.IsInteractionBlocked || Cooldown > 0f || NeedsLeave)
        {
            return false;
        }

        var distance = Vector2.Distance(position, pointer.Position);
        return distance >= _configuration.TriggerMinimumDistance &&
               distance <= _configuration.TriggerMaximumDistance;
    }

    private void Track(Vector2 position, float dt)
    {
        if (!HasTarget)
        {
            Target = position;
            HasTarget = true;
            return;
        }

        Target = Vector2.Lerp(
            Target,
            position,
            MathEx.ExpLerpFactor(_configuration.TargetTrackingResponse, dt));
    }
}
