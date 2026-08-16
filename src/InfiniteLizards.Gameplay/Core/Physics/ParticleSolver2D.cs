using System.Numerics;

namespace DesktopLizard.Core;

internal struct PhysicsParticle2D
{
    public Vector2 Position;
    public Vector2 Previous;
    public Vector2 Velocity;
}

/// <summary>
/// Stateless integration and distance-constraint primitives. The dangling
/// rig owns topology and grab semantics; this module owns particle math only.
/// </summary>
internal static class ParticleSolver2D
{
    public static PhysicsParticle2D Create(Vector2 position) => new()
    {
        Position = position,
        Previous = position,
        Velocity = Vector2.Zero
    };

    public static void Integrate(
        PhysicsParticle2D[] particles,
        float dt,
        PhysicsConfiguration configuration)
    {
        var damping = MathF.Exp(-configuration.LinearDrag * dt);
        for (var index = 0; index < particles.Length; index++)
        {
            particles[index].Previous = particles[index].Position;
            particles[index].Velocity.Y += configuration.Gravity * dt;
            particles[index].Velocity *= damping;
            particles[index].Velocity = Vector2.Clamp(
                particles[index].Velocity,
                new Vector2(-configuration.MaximumSpeed),
                new Vector2(configuration.MaximumSpeed));
            particles[index].Position += particles[index].Velocity * dt;
        }
    }

    public static void UpdateVelocities(
        PhysicsParticle2D[] particles,
        float dt,
        float response,
        float maximumSpeed)
    {
        for (var index = 0; index < particles.Length; index++)
        {
            var solvedVelocity =
                (particles[index].Position - particles[index].Previous) / dt;
            particles[index].Velocity = Vector2.Clamp(
                Vector2.Lerp(particles[index].Velocity, solvedVelocity, response),
                new Vector2(-maximumSpeed),
                new Vector2(maximumSpeed));
        }
    }

    public static void SolveDistance(
        ref PhysicsParticle2D a,
        ref PhysicsParticle2D b,
        float length,
        bool pinA = false,
        bool pinB = false,
        float stiffness = 1f)
    {
        var delta = b.Position - a.Position;
        var distance = delta.Length();
        if (distance < 0.0001f)
        {
            delta = Vector2.UnitY;
            distance = 1f;
        }
        var correction = delta / distance * ((distance - length) * stiffness);
        if (pinA && pinB)
        {
            return;
        }
        if (pinA)
        {
            b.Position -= correction;
        }
        else if (pinB)
        {
            a.Position += correction;
        }
        else
        {
            a.Position += correction * 0.5f;
            b.Position -= correction * 0.5f;
        }
    }

    public static void SolvePinnedDistance(
        ref PhysicsParticle2D particle,
        Vector2 anchor,
        float length)
    {
        var delta = particle.Position - anchor;
        particle.Position = anchor + MathEx.SafeNormalize(delta, Vector2.UnitY) * length;
    }

    public static void Shift(ref PhysicsParticle2D particle, Vector2 screenDelta)
    {
        particle.Position -= screenDelta;
        particle.Previous -= screenDelta;
    }

    public static void TranslatePosition(ref PhysicsParticle2D particle, Vector2 delta) =>
        particle.Position += delta;

    public static void Translate(ref PhysicsParticle2D particle, Vector2 delta)
    {
        particle.Position += delta;
        particle.Previous += delta;
    }

    public static void ClearVelocities(PhysicsParticle2D[] particles)
    {
        for (var index = 0; index < particles.Length; index++)
        {
            particles[index].Previous = particles[index].Position;
            particles[index].Velocity = Vector2.Zero;
        }
    }

    public static bool IsFinite(PhysicsParticle2D particle, float maximumCoordinate) =>
        float.IsFinite(particle.Position.X) &&
        float.IsFinite(particle.Position.Y) &&
        float.IsFinite(particle.Velocity.X) &&
        float.IsFinite(particle.Velocity.Y) &&
        MathF.Abs(particle.Position.X) < maximumCoordinate &&
        MathF.Abs(particle.Position.Y) < maximumCoordinate;
}
