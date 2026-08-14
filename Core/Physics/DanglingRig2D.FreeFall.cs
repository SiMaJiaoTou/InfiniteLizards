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
        Vector2 screenDelta,
        float catchPreparationProgress)
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

        ApplyCatchPreparation(lizard, catchPreparationProgress);
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
    /// During the last part of a fall, guide the two front paws toward stable
    /// points above the body. The endpoints are authored inside the particle
    /// rig so the final falling pose becomes the first stationary contact pose
    /// without a render-only correction or a synthetic mouse pin.
    /// </summary>
    private void ApplyCatchPreparation(
        ProceduralLizard lizard,
        float catchPreparationProgress)
    {
        var inputProgress = float.IsFinite(catchPreparationProgress)
            ? MathEx.Clamp01(catchPreparationProgress)
            : 0f;
        if (inputProgress <= 0f)
        {
            FreeFallReachProgress = 0f;
            FreeFallReachTargetError = 0f;
            return;
        }

        var capturedThisStep = !_freeFallReachCaptured;
        if (capturedThisStep)
        {
            CaptureFreeFallReach(lizard, inputProgress);
        }

        if (capturedThisStep && inputProgress >= 1f)
        {
            FreeFallReachProgress = 1f;
            UpdateFreeFallReachError();
            return;
        }

        // Capture the first signalled pose exactly. Progress is then remapped
        // over the remaining lead interval so entering Seeking cannot pop a
        // paw, while the final moving frame still reaches the contact points.
        var progressSpan = 1f - _freeFallReachCaptureInputProgress;
        FreeFallReachProgress = progressSpan > 0.0001f
            ? MathEx.Clamp01(
                (inputProgress - _freeFallReachCaptureInputProgress) /
                progressSpan)
            : inputProgress >= 1f
                ? 1f
                : 0f;
        if (FreeFallReachProgress <= 0f)
        {
            UpdateFreeFallReachError();
            return;
        }

        var pathProgress = SmoothStep(FreeFallReachProgress);
        for (var legIndex = 0; legIndex < 2; legIndex++)
        {
            var bodyIndex = DanglingTopology2D.GetBodyIndex(legIndex);
            var shoulder = GetShoulderPosition(lizard, legIndex, bodyIndex);
            var desiredFoot = Vector2.Lerp(
                _freeFallReachStartFeet[legIndex],
                _freeFallReachTargets[legIndex],
                pathProgress);
            var foot = ConstrainReachFoot(shoulder, desiredFoot, legIndex);
            var elbow = SolveReachElbow(
                shoulder,
                foot,
                legIndex,
                _freeFallReachBendSigns[legIndex]);
            _feet[legIndex].Position = foot;
            _elbows[legIndex].Position = elbow;
        }
        UpdateFreeFallReachError();
    }

    private void CaptureFreeFallReach(
        ProceduralLizard lizard,
        float inputProgress)
    {
        _freeFallReachCaptured = true;
        _freeFallReachCaptureInputProgress = inputProgress;
        FreeFallReachProgress = 0f;

        for (var legIndex = 0; legIndex < 2; legIndex++)
        {
            var bodyIndex = DanglingTopology2D.GetBodyIndex(legIndex);
            var shoulder = GetShoulderPosition(lizard, legIndex, bodyIndex);
            var elbow = _elbows[legIndex].Position;
            var foot = _feet[legIndex].Position;
            _freeFallReachStartFeet[legIndex] = foot;

            var side = DanglingTopology2D.GetSide(legIndex);
            var outwardWeight = _configuration.RegripFrontReachOutwardWeight;
            var reachDirection = MathEx.SafeNormalize(
                new Vector2(side * outwardWeight, -(1f - outwardWeight)),
                -Vector2.UnitY);
            var reachLength =
                (_upperLegLengths[legIndex] + _lowerLegLengths[legIndex]) *
                _configuration.RegripFrontReachLengthFactor;
            // A user-configured lead can be shorter than one fixed step. In
            // that degenerate case there is no time interval in which an
            // upward reach can be animated without violating continuity.
            // Treat the already visible paw as the contact point: this keeps
            // the stop physically continuous and degrades only anticipation.
            _freeFallReachTargets[legIndex] = inputProgress >= 1f
                ? foot
                : shoulder + reachDirection * reachLength;

            var shoulderToFoot = MathEx.SafeNormalize(
                foot - shoulder,
                reachDirection);
            var bendSide = Vector2.Dot(
                elbow - shoulder,
                MathEx.Perpendicular(shoulderToFoot));
            _freeFallReachBendSigns[legIndex] = MathF.Abs(bendSide) > 0.0001f
                ? MathF.Sign(bendSide)
                : side;
        }
        UpdateFreeFallReachError();
    }

    private Vector2 ConstrainReachFoot(
        Vector2 shoulder,
        Vector2 desiredFoot,
        int legIndex)
    {
        var upperLength = _upperLegLengths[legIndex];
        var lowerLength = _lowerLegLengths[legIndex];
        var minimumDistance = MathF.Abs(upperLength - lowerLength) + 0.001f;
        var maximumDistance = upperLength + lowerLength - 0.001f;
        var fromShoulder = desiredFoot - shoulder;
        var direction = MathEx.SafeNormalize(fromShoulder, -Vector2.UnitY);
        var distance = Math.Clamp(
            fromShoulder.Length(),
            minimumDistance,
            Math.Max(minimumDistance, maximumDistance));
        return shoulder + direction * distance;
    }

    private Vector2 SolveReachElbow(
        Vector2 shoulder,
        Vector2 foot,
        int legIndex,
        float bendSign)
    {
        var upperLength = _upperLegLengths[legIndex];
        var lowerLength = _lowerLegLengths[legIndex];
        var shoulderToFoot = foot - shoulder;
        var distance = Math.Max(0.0001f, shoulderToFoot.Length());
        var direction = shoulderToFoot / distance;
        var along = Math.Clamp(
            (upperLength * upperLength - lowerLength * lowerLength +
             distance * distance) /
            (2f * distance),
            0f,
            upperLength);
        var perpendicularDistance = MathF.Sqrt(Math.Max(
            0f,
            upperLength * upperLength - along * along));
        return shoulder +
               direction * along +
               MathEx.Perpendicular(direction) *
               (perpendicularDistance * bendSign);
    }

    private void UpdateFreeFallReachError()
    {
        if (!_freeFallReachCaptured)
        {
            FreeFallReachTargetError = 0f;
            return;
        }

        FreeFallReachTargetError = Math.Max(
            Vector2.Distance(_feet[0].Position, _freeFallReachTargets[0]),
            Vector2.Distance(_feet[1].Position, _freeFallReachTargets[1]));
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
        TranslateFreeFallReachFrame(correction);
    }

    private void TranslateFreeFallReachFrame(Vector2 correction)
    {
        if (!_freeFallReachCaptured)
        {
            return;
        }

        for (var index = 0; index < 2; index++)
        {
            _freeFallReachStartFeet[index] += correction;
            _freeFallReachTargets[index] += correction;
        }
    }

    private void ResetFreeFallReach()
    {
        _freeFallReachCaptured = false;
        _freeFallReachCaptureInputProgress = 0f;
        FreeFallReachProgress = 0f;
        FreeFallReachTargetError = 0f;
        Array.Clear(_freeFallReachStartFeet);
        Array.Clear(_freeFallReachTargets);
        Array.Clear(_freeFallReachBendSigns);
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
        UpdateFreeFallReachError();
    }
}
