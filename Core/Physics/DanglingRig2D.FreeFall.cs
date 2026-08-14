using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// Unpinned free-fall mode for the shared articulated particle rig. Keeping
/// this mode separate prevents accidental-fall policy from leaking into the
/// mouse-held simulation path.
/// </summary>
internal sealed partial class DanglingRig2D
{
    /// <summary>
    /// Advances an unpinned fall pose. Window movement is consumed in model
    /// space before the common particle translation is separated from the
    /// articulated motion. No particle is interpreted as a mouse grab point.
    /// </summary>
    public void UpdateFreeFall(
        ProceduralLizard lizard,
        float dt,
        Vector2 screenDelta)
    {
        if (!_active || _mode != SimulationMode.FreeFall)
        {
            BeginFreeFall(lizard);
        }

        dt = PrepareSimulationStep(
            dt,
            screenDelta,
            out var upperIterationBlend,
            out var lowerIterationBlend);
        GrabConstraintError = 0f;

        for (var iteration = 0; iteration < _configuration.ConstraintIterations; iteration++)
        {
            DanglingConstraintSolver2D.SolveFreeFallIteration(
                lizard,
                _configuration,
                _spine,
                _elbows,
                _feet,
                _spineLengths,
                _upperLegLengths,
                _lowerLegLengths,
                upperIterationBlend,
                lowerIterationBlend);
        }

        UpdateParticleVelocities(dt);
        StabilizeFreeFallReferenceFrame();
        if (!IsValid(lizard))
        {
            RestoreLastValidFreeFallPose();
            IsValid(lizard);
        }
        else
        {
            SaveValidPose();
        }

        ApplyTo(lizard);
    }

    /// <summary>
    /// The host-window center is the authoritative world-space fall path.
    /// Remove only the particle system's common translation so gravity and
    /// window inertia cannot make the entire local pose drift toward a canvas
    /// edge. Relative particle velocities are retained, so limbs and spine
    /// still articulate and swing naturally inside that moving reference
    /// frame.
    /// </summary>
    private void StabilizeFreeFallReferenceFrame()
    {
        var center = GetSpineCenter();
        var correction = _freeFallReferenceSpineCenter - center;
        TranslateParticles(correction);

        var commonVelocity = GetSpineVelocity();
        RemoveCommonVelocity(_spine, commonVelocity);
        RemoveCommonVelocity(_elbows, commonVelocity);
        RemoveCommonVelocity(_feet, commonVelocity);

        var correctionDistance = correction.Length();
        FreeFallReferenceCorrectionTotal += correctionDistance;
        FreeFallReferenceCorrectionMaximum = Math.Max(
            FreeFallReferenceCorrectionMaximum,
            correctionDistance);
    }

    private Vector2 GetSpineCenter()
    {
        var sum = Vector2.Zero;
        for (var index = 0; index < _spineCount; index++)
        {
            sum += _spine[index].Position;
        }
        return sum / _spineCount;
    }

    private Vector2 GetSpineVelocity()
    {
        var sum = Vector2.Zero;
        for (var index = 0; index < _spineCount; index++)
        {
            sum += _spine[index].Velocity;
        }
        return sum / _spineCount;
    }

    private void TranslateParticles(Vector2 correction)
    {
        for (var index = 0; index < _spineCount; index++)
        {
            ParticleSolver2D.Translate(ref _spine[index], correction);
        }
        for (var index = 0; index < LegCount; index++)
        {
            ParticleSolver2D.Translate(ref _elbows[index], correction);
            ParticleSolver2D.Translate(ref _feet[index], correction);
        }
    }

    private static void RemoveCommonVelocity(
        PhysicsParticle2D[] particles,
        Vector2 commonVelocity)
    {
        for (var index = 0; index < particles.Length; index++)
        {
            particles[index].Velocity -= commonVelocity;
        }
    }

    /// <summary>
    /// Applies a rigid model-space correction to an active pose. Translating
    /// current and previous particle positions together preserves velocity.
    /// </summary>
    public void TranslateActivePose(ProceduralLizard lizard, Vector2 correction)
    {
        if (!_active || !IsFinite(correction))
        {
            return;
        }

        TranslateParticles(correction);
        SaveValidPose();
        ApplyTo(lizard);
    }

    private void RestoreLastValidFreeFallPose()
    {
        for (var i = 0; i < _spineCount; i++)
        {
            _spine[i] = ParticleSolver2D.Create(_lastValidSpine[i]);
        }
        for (var i = 0; i < LegCount; i++)
        {
            _elbows[i] = ParticleSolver2D.Create(_lastValidElbows[i]);
            _feet[i] = ParticleSolver2D.Create(_lastValidFeet[i]);
        }
        GrabConstraintError = 0f;
    }
}
