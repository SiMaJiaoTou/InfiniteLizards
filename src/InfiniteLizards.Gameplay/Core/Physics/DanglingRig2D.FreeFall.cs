using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// Unpinned free-fall mode for the shared articulated particle rig. Keeping
/// this mode separate prevents accidental-fall policy from leaking into the
/// mouse-held simulation path.
/// </summary>
internal sealed partial class DanglingRig2D
{
    private const float RegripEscapeLateralWeight = 0.62f;
    private const float MinimumRegripEscapeBlend = 0.58f;
    private const float RegripEscapeBlendRange = 0.35f;
    private const float MinimumAlignedEscapeBlend = 0.24f;
    private const float MinimumRegripUpwardComponent = 0.12f;
    private const float MinimumRegripTerminalUpwardTravel = 4f;
    private const float MinimumRegripTargetDisplacement = 14f;
    private const float MinimumRegripLongitudinalTravel = 1.5f;
    private const float MinimumRegripOutwardTravel = 1.5f;

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
        FreeFallReachProjectedLegMaskThisStep = 0;
        FreeFallReachRollbackLegMaskThisStep = 0;

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

        PreserveFreeFallLimbBodySafety(lizard);
        ApplyCatchPreparation(lizard, catchPreparationProgress);
        UpdateParticleVelocities(dt);
        StabilizeFreeFallReferenceFrame();
        ApplyTo(lizard);
        if (ValidateAppliedFreeFallReachSafety(lizard))
        {
            UpdateFreeFallReachError();
        }
        if (!IsValid(lizard))
        {
            RestoreLastValidFreeFallPose(lizard);
            IsValid(lizard);
        }
        else
        {
            SaveValidPose();
        }
        ApplyTo(lizard);
    }

    /// <summary>
    /// During the last part of a fall, guide all four paws toward independent
    /// points above and outside their shoulders. The endpoints are authored inside the particle
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

        UpdateFreeFallReachFrame(lizard);

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

        var pathProgress = MathEx.Lerp(
            FreeFallReachProgress,
            SmoothStep(FreeFallReachProgress),
            0.5f);
        for (var legIndex = 0; legIndex < LegCount; legIndex++)
        {
            var resolvedPathProgress = ResolveFreeFallReachPathAmount(
                legIndex,
                pathProgress);
            _freeFallReachResolvedPathAmounts[legIndex] = resolvedPathProgress;
            var bodyIndex = DanglingTopology2D.GetBodyIndex(legIndex);
            var shoulder = GetShoulderPosition(lizard, legIndex, bodyIndex);
            var desiredFoot = InterpolateReachFootAroundShoulder(
                lizard,
                shoulder,
                shoulder + _freeFallReachStartOffsets[legIndex],
                _freeFallReachTargets[legIndex],
                resolvedPathProgress,
                legIndex);
            var foot = ConstrainReachFoot(shoulder, desiredFoot, legIndex);
            var bendSign = ResolveFreeFallReachBendSign(legIndex);
            var elbow = SolveReachElbow(
                shoulder,
                foot,
                legIndex,
                bendSign);
            _feet[legIndex].Position = foot;
            _elbows[legIndex].Position = elbow;
            UpdateFreeFallReachExtensionReady(legIndex, shoulder);
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
        FreeFallReachContactSafe = true;

        for (var legIndex = 0; legIndex < LegCount; legIndex++)
        {
            var bodyIndex = DanglingTopology2D.GetBodyIndex(legIndex);
            var shoulder = GetShoulderPosition(lizard, legIndex, bodyIndex);
            var elbow = _elbows[legIndex].Position;
            var foot = _feet[legIndex].Position;
            _freeFallReachStartFeet[legIndex] = foot;
            _freeFallReachStartOffsets[legIndex] = foot - shoulder;
            var currentSafe = IsFreeFallLimbBodySafe(
                lizard,
                legIndex,
                bodyIndex,
                shoulder,
                elbow,
                foot);
            if (currentSafe)
            {
                CommitFreeFallReachSafePose(
                    legIndex,
                    shoulder,
                    elbow,
                    foot);
            }

            var side = DanglingTopology2D.GetSide(legIndex);
            var isFront = legIndex < 2;
            var authoredReachLength =
                (_upperLegLengths[legIndex] + _lowerLegLengths[legIndex]) *
                (isFront
                    ? _configuration.RegripFrontReachLengthFactor
                    : _configuration.RegripRearReachLengthFactor);
            var minimumReachLength =
                MathF.Abs(_upperLegLengths[legIndex] - _lowerLegLengths[legIndex]) +
                0.001f;
            var maximumReachLength =
                _upperLegLengths[legIndex] + _lowerLegLengths[legIndex] -
                0.001f;
            var startOffset = foot - shoulder;
            var requiredUpwardMagnitude = Math.Max(
                0f,
                MinimumRegripTerminalUpwardTravel - startOffset.Y);
            var minimumReadableReachLength = startOffset.Y < 0f
                ? MathF.Sqrt(
                    requiredUpwardMagnitude * requiredUpwardMagnitude +
                    MinimumRegripTargetDisplacement *
                    MinimumRegripTargetDisplacement)
                : minimumReachLength;
            // Keep enough captured extension to avoid a folded reach, while
            // ensuring a paw that already hangs above its shoulder can still
            // extend visibly upward during the catch.
            var reachLength = Math.Clamp(
                Math.Max(
                    authoredReachLength,
                    minimumReadableReachLength),
                minimumReachLength,
                Math.Max(minimumReachLength, maximumReachLength));
            var outwardWeight = isFront
                ? _configuration.RegripFrontReachOutwardWeight
                : _configuration.RegripRearReachOutwardWeight;
            var shoulderToFoot = MathEx.SafeNormalize(
                foot - shoulder,
                -Vector2.UnitY);
            var bendSide = Vector2.Dot(
                elbow - shoulder,
                MathEx.Perpendicular(shoulderToFoot));
            _freeFallReachBendSigns[legIndex] = MathF.Abs(bendSide) > 0.0001f
                ? MathF.Sign(bendSide)
                : side;
            _freeFallReachStartBendSigns[legIndex] =
                _freeFallReachBendSigns[legIndex];
            _freeFallReachExtensionReady[legIndex] = false;
            _freeFallReachBendTransitioned[legIndex] = false;
            if (inputProgress >= 1f)
            {
                _freeFallReachTargets[legIndex] = foot;
                _freeFallReachTargetOffsets[legIndex] = foot - shoulder;
                _freeFallReachPathPlans[legIndex] = default;
                _freeFallReachProjectionEligible[legIndex] = currentSafe;
                FreeFallReachContactSafe &= currentSafe;
                continue;
            }
            var maySwitchAtFullExtension =
                inputProgress < 1f;
            if (maySwitchAtFullExtension)
            {
                _freeFallReachBendSigns[legIndex] =
                    -_freeFallReachStartBendSigns[legIndex];
            }
            var reachDirection = CreateFreeFallReachDirection(
                lizard,
                legIndex,
                bodyIndex,
                shoulder,
                foot,
                reachLength,
                _freeFallReachStartBendSigns[legIndex],
                _freeFallReachBendSigns[legIndex],
                outwardWeight);
            // If the lead interval collapsed to one fixed step there is no
            // continuous anticipation window. Preserve the visible paw as the
            // contact point instead of popping it to a newly invented target.
            _freeFallReachTargets[legIndex] =
                shoulder + reachDirection * reachLength;
            _freeFallReachTargetOffsets[legIndex] =
                _freeFallReachTargets[legIndex] - shoulder;

            var targetElbow = SolveReachElbow(
                shoulder,
                _freeFallReachTargets[legIndex],
                legIndex,
                _freeFallReachBendSigns[legIndex]);
            var targetReadable =
                Vector2.Distance(foot, _freeFallReachTargets[legIndex]) >=
                    MinimumRegripTargetDisplacement - ReachPathGeometryEpsilon &&
                foot.Y - _freeFallReachTargets[legIndex].Y >=
                    1.1f - ReachPathGeometryEpsilon;
            var endpointSafe = IsFreeFallLimbBodySafe(
                lizard,
                legIndex,
                bodyIndex,
                shoulder,
                targetElbow,
                _freeFallReachTargets[legIndex]);
            _freeFallReachPathPlans[legIndex] = SelectFreeFallReachPath(
                lizard,
                legIndex,
                bodyIndex,
                shoulder,
                foot,
                _freeFallReachTargets[legIndex],
                _freeFallReachStartBendSigns[legIndex],
                _freeFallReachBendSigns[legIndex],
                out var pathSafe);
            var prefixSafe = IsFreeFallReachPathProjectionPrefixSafe(
                _freeFallReachPathPlans[legIndex],
                lizard,
                legIndex,
                bodyIndex,
                shoulder,
                foot,
                _freeFallReachTargets[legIndex],
                _freeFallReachStartBendSigns[legIndex],
                _freeFallReachBendSigns[legIndex]);
            var projectionEligible =
                currentSafe &&
                targetReadable &&
                endpointSafe &&
                (_freeFallReachPathPlans[legIndex].FlipsBend
                    ? prefixSafe
                    : pathSafe);
            _freeFallReachProjectionEligible[legIndex] = projectionEligible;
            _freeFallReachRequiresProjection[legIndex] =
                projectionEligible && !pathSafe;
            if (_freeFallReachRequiresProjection[legIndex])
            {
                FreeFallReachRequiresProjectionLegMask |= 1 << legIndex;
            }
            FreeFallReachContactSafe &= projectionEligible;
            UpdateFreeFallReachExtensionReady(legIndex, shoulder);
        }
        UpdateFreeFallReachError();
    }

    private void UpdateFreeFallReachFrame(ProceduralLizard lizard)
    {
        for (var legIndex = 0; legIndex < LegCount; legIndex++)
        {
            var bodyIndex = DanglingTopology2D.GetBodyIndex(legIndex);
            var shoulder = GetShoulderPosition(lizard, legIndex, bodyIndex);
            _freeFallReachStartFeet[legIndex] =
                shoulder + _freeFallReachStartOffsets[legIndex];
            _freeFallReachTargets[legIndex] =
                shoulder + _freeFallReachTargetOffsets[legIndex];
        }
    }

    /// <summary>
    /// Sends front paws around the head end and rear paws around the tail end
    /// before mixing in screen-up. This keeps a rear reach from sweeping
    /// forward through the torso while still making all four catches read as
    /// grabbing something above the falling animal.
    /// </summary>
    private Vector2 CreateFreeFallReachDirection(
        ProceduralLizard lizard,
        int legIndex,
        int bodyIndex,
        Vector2 shoulder,
        Vector2 startFoot,
        float reachLength,
        float startBendSign,
        float bendSign,
        float escapeWeight)
    {
        var previousIndex = Math.Max(0, bodyIndex - 1);
        var bodyForward = MathEx.SafeNormalize(
            _spine[previousIndex].Position - _spine[bodyIndex].Position,
            MathEx.FromAngle(lizard.Spine.Angles[bodyIndex]));
        var side = DanglingTopology2D.GetSide(legIndex);
        var bodyOutward = MathEx.SafeNormalize(
            shoulder - _spine[bodyIndex].Position,
            MathEx.Perpendicular(bodyForward) * side);
        var longitudinalEscape = legIndex < 2 ? bodyForward : -bodyForward;
        var escapeDirection = MathEx.SafeNormalize(
            longitudinalEscape + bodyOutward * RegripEscapeLateralWeight,
            longitudinalEscape);
        var configuredEscape = MathEx.Clamp01(escapeWeight);
        var bodyOutwardSupportsScreenUp =
            Vector2.Dot(bodyOutward, -Vector2.UnitY) > 0f;
        var blend = bodyOutwardSupportsScreenUp
            ? Math.Clamp(
                configuredEscape,
                MinimumAlignedEscapeBlend,
                MinimumRegripEscapeBlend)
            : Math.Clamp(
                MinimumRegripEscapeBlend +
                configuredEscape * RegripEscapeBlendRange,
                MinimumRegripEscapeBlend,
                MinimumRegripEscapeBlend + RegripEscapeBlendRange);
        var direction = MathEx.SafeNormalize(
            -Vector2.UnitY * (1f - blend) + escapeDirection * blend,
            -Vector2.UnitY);

        // Highly rotated custom poses can point their head/tail escape partly
        // downward. Reproject only the vertical component so contact remains
        // visibly above the shoulder without discarding longitudinal spread.
        if (direction.Y > -MinimumRegripUpwardComponent)
        {
            var horizontalSign = MathF.Sign(direction.X);
            if (horizontalSign == 0f)
            {
                horizontalSign = MathF.Sign(longitudinalEscape.X);
            }
            if (horizontalSign == 0f)
            {
                horizontalSign = 1;
            }
            direction = new Vector2(
                horizontalSign * MathF.Sqrt(
                    1f - MinimumRegripUpwardComponent *
                    MinimumRegripUpwardComponent),
                -MinimumRegripUpwardComponent);
        }
        return SelectReadableReachDirection(
            lizard,
            legIndex,
            bodyIndex,
            shoulder,
            direction,
            longitudinalEscape,
            bodyOutward,
            startFoot - shoulder,
            reachLength,
            startBendSign,
            bendSign);
    }

    /// <summary>
    /// Keeps the semantic up/head-tail/outward target while guaranteeing that
    /// an already-outward free-fall paw still has visible travel left. Sampling
    /// the unit circle avoids fragile component clamps when the body is nearly
    /// vertical and screen-up conflicts with its longitudinal axis.
    /// </summary>
    private Vector2 SelectReadableReachDirection(
        ProceduralLizard lizard,
        int legIndex,
        int bodyIndex,
        Vector2 shoulder,
        Vector2 authoredDirection,
        Vector2 longitudinalEscape,
        Vector2 bodyOutward,
        Vector2 startOffset,
        float reachLength,
        float startBendSign,
        float bendSign)
    {
        var hasStrictDirection = TrySelectReadableReachDirection(
            lizard,
            legIndex,
            bodyIndex,
            shoulder,
            authoredDirection,
            longitudinalEscape,
            bodyOutward,
            startOffset,
            reachLength,
            startBendSign,
            bendSign,
            MinimumRegripTerminalUpwardTravel,
            MinimumRegripTargetDisplacement,
            out var strictDirection,
            out var strictContactSafe);
        if (hasStrictDirection && strictContactSafe)
        {
            return strictDirection;
        }

        var hasRelaxedDirection = TrySelectReadableReachDirection(
            lizard,
            legIndex,
            bodyIndex,
            shoulder,
            authoredDirection,
            longitudinalEscape,
            bodyOutward,
            startOffset,
            reachLength,
            startBendSign,
            bendSign,
            1.1f,
            MinimumRegripTargetDisplacement,
            out var relaxedDirection,
            out var relaxedContactSafe);
        if (hasRelaxedDirection && relaxedContactSafe)
        {
            return relaxedDirection;
        }
        if (hasStrictDirection)
        {
            return strictDirection;
        }
        if (hasRelaxedDirection)
        {
            return relaxedDirection;
        }

        var authoredTargetOffset = authoredDirection * reachLength;
        if (Vector2.Distance(startOffset, authoredTargetOffset) >=
                MinimumRegripTargetDisplacement &&
            startOffset.Y - authoredTargetOffset.Y >= 1.1f &&
            Vector2.Dot(authoredDirection, bodyOutward) * reachLength >=
                MinimumRegripOutwardTravel)
        {
            return authoredDirection;
        }

        // A custom pose can invalidate every endpoint once body capsules are
        // considered, but an Update step must never turn that into a process
        // crash. Preserve the strict visible-distance/upward contract and pick
        // the best deterministic kinematic direction; the runtime safety gate
        // can still decline contact if the pose cannot be completed safely.
        const int FallbackSamples = 180;
        var hasFallback = false;
        var fallbackDirection = authoredDirection;
        var fallbackScore = float.NegativeInfinity;
        for (var sample = 0; sample < FallbackSamples; sample++)
        {
            var candidate = MathEx.FromAngle(
                -MathF.PI + ReachPathTwoPi * sample / FallbackSamples);
            var candidateOffset = candidate * reachLength;
            if (candidate.Y > -MinimumRegripUpwardComponent ||
                startOffset.Y - candidateOffset.Y < 1.1f ||
                Vector2.Distance(startOffset, candidateOffset) <
                    MinimumRegripTargetDisplacement)
            {
                continue;
            }
            var score =
                Vector2.Dot(candidate, authoredDirection) * 4f +
                Vector2.Dot(candidate, bodyOutward) * 0.75f +
                Vector2.Dot(candidate, longitudinalEscape) * 0.35f;
            if (score <= fallbackScore)
            {
                continue;
            }
            hasFallback = true;
            fallbackScore = score;
            fallbackDirection = candidate;
        }
        return hasFallback ? fallbackDirection : authoredDirection;
    }

    private bool TrySelectReadableReachDirection(
        ProceduralLizard lizard,
        int legIndex,
        int bodyIndex,
        Vector2 shoulder,
        Vector2 authoredDirection,
        Vector2 longitudinalEscape,
        Vector2 bodyOutward,
        Vector2 startOffset,
        float reachLength,
        float startBendSign,
        float bendSign,
        float minimumUpwardTravel,
        float minimumDisplacement,
        out Vector2 selectedDirection,
        out bool selectedContactSafe)
    {
        const int DirectionSamples = 180;
        var maximumLongitudinalTravel = float.NegativeInfinity;
        for (var sample = 0; sample < DirectionSamples; sample++)
        {
            var candidate = MathEx.FromAngle(
                -MathF.PI + ReachPathTwoPi * sample / DirectionSamples);
            if (candidate.Y > -MinimumRegripUpwardComponent ||
                startOffset.Y - candidate.Y * reachLength < 1.1f)
            {
                continue;
            }
            maximumLongitudinalTravel = Math.Max(
                maximumLongitudinalTravel,
                Vector2.Dot(candidate, longitudinalEscape) * reachLength);
        }

        // A vertical custom pose can make its head/tail axis point screen-down,
        // in which case the upward contract legitimately wins. Otherwise keep
        // a positive body-longitudinal margin so ordinary poses always spread
        // front and rear targets around opposite ends of the torso.
        var requiredLongitudinalTravel = maximumLongitudinalTravel >= 0.55f
            ? Math.Min(
                MinimumRegripLongitudinalTravel,
                maximumLongitudinalTravel - 0.01f)
            : float.NegativeInfinity;
        var hasBest = false;
        var bestDirection = authoredDirection;
        var bestScore = float.NegativeInfinity;
        var bestEvaluation = default(ReachPathEvaluation);
        var upperLength = _upperLegLengths[legIndex];
        var lowerLength = _lowerLegLengths[legIndex];
        var physicalMinimum =
            MathF.Abs(upperLength - lowerLength) + 0.001f;
        var physicalMaximum = upperLength + lowerLength - 0.001f;
        var startRadius = Math.Clamp(
            startOffset.Length(),
            physicalMinimum,
            physicalMaximum);
        var startAngle = MathF.Atan2(startOffset.Y, startOffset.X);
        for (var sample = 0; sample < DirectionSamples; sample++)
        {
            var candidate = MathEx.FromAngle(
                -MathF.PI + ReachPathTwoPi * sample / DirectionSamples);
            if (candidate.Y > -MinimumRegripUpwardComponent ||
                startOffset.Y - candidate.Y * reachLength <
                    minimumUpwardTravel ||
                Vector2.Distance(
                    startOffset,
                    candidate * reachLength) < minimumDisplacement)
            {
                continue;
            }
            var longitudinalTravel =
                Vector2.Dot(candidate, longitudinalEscape) * reachLength;
            if (longitudinalTravel < Math.Min(0.51f, requiredLongitudinalTravel))
            {
                continue;
            }
            if (Vector2.Dot(candidate, bodyOutward) * reachLength <
                MinimumRegripOutwardTravel)
            {
                continue;
            }

            var targetFoot = shoulder + candidate * reachLength;
            var targetElbow = SolveReachElbow(
                shoulder,
                targetFoot,
                legIndex,
                bendSign);
            if (!IsFreeFallLimbBodySafe(
                    lizard,
                    legIndex,
                    bodyIndex,
                    shoulder,
                    targetElbow,
                    targetFoot))
            {
                continue;
            }

            var targetAngle = MathF.Atan2(candidate.Y, candidate.X);
            var pathPlan = SelectFreeFallReachPath(
                lizard,
                legIndex,
                bodyIndex,
                shoulder,
                shoulder + startOffset,
                targetFoot,
                startBendSign,
                bendSign);
            var evaluation = EvaluateReachPathGeometry(
                pathPlan,
                lizard,
                legIndex,
                bodyIndex,
                shoulder,
                startAngle,
                targetAngle,
                startRadius,
                reachLength,
                physicalMinimum,
                physicalMaximum,
                startBendSign,
                bendSign);

            var score =
                Vector2.Dot(candidate, authoredDirection) * 4f +
                Vector2.Dot(candidate, bodyOutward) * 0.75f +
                Vector2.Dot(candidate, longitudinalEscape) * 0.35f;
            var betterGeometry =
                !hasBest || IsBetterReachPath(evaluation, bestEvaluation);
            var equivalentGeometry =
                hasBest &&
                !betterGeometry &&
                !IsBetterReachPath(bestEvaluation, evaluation);
            if (!betterGeometry &&
                (!equivalentGeometry || score <= bestScore))
            {
                continue;
            }
            hasBest = true;
            bestScore = score;
            bestEvaluation = evaluation;
            bestDirection = candidate;
        }

        selectedDirection = bestDirection;
        selectedContactSafe = hasBest && IsReachPathContactSafe(bestEvaluation);
        return hasBest;
    }

    /// <summary>
    /// Sweeps an extended paw around its shoulder instead of linearly crossing
    /// the joint. Cartesian interpolation between opposite-side endpoints
    /// collapses the arm near mid-reach, which reads as "stop, then extend"
    /// even though the foot is numerically moving toward the catch point.
    /// </summary>
    private Vector2 InterpolateReachFootAroundShoulder(
        ProceduralLizard lizard,
        Vector2 shoulder,
        Vector2 startFoot,
        Vector2 targetFoot,
        float progress,
        int legIndex)
    {
        var amount = MathEx.Clamp01(progress);
        if (amount <= 0f)
        {
            return startFoot;
        }
        if (amount >= 1f)
        {
            return targetFoot;
        }

        var upperLength = _upperLegLengths[legIndex];
        var lowerLength = _lowerLegLengths[legIndex];
        var physicalMinimum = MathF.Abs(upperLength - lowerLength) + 0.001f;
        var physicalMaximum = upperLength + lowerLength - 0.001f;
        var fallbackDirection = MathEx.SafeNormalize(
            targetFoot - shoulder,
            -Vector2.UnitY);
        var startOffset = startFoot - shoulder;
        var targetOffset = targetFoot - shoulder;
        var startRadius = Math.Clamp(
            startOffset.Length(),
            physicalMinimum,
            Math.Max(physicalMinimum, physicalMaximum));
        var targetRadius = Math.Clamp(
            targetOffset.Length(),
            physicalMinimum,
            Math.Max(physicalMinimum, physicalMaximum));
        var startDirection = MathEx.SafeNormalize(
            startOffset,
            fallbackDirection);
        var targetDirection = MathEx.SafeNormalize(
            targetOffset,
            fallbackDirection);
        // The radial interpolation already stays between its endpoints. The
        // explicit retention floor makes the no-collapse invariant robust to
        // a moving shoulder and near-degenerate custom limb proportions.
        const float MinimumCapturedRadiusRetention = 0.72f;
        var retentionFloor = Math.Min(
            targetRadius,
            Math.Max(
                physicalMinimum,
                startRadius * MinimumCapturedRadiusRetention));
        var plan = _freeFallReachPathPlans[legIndex];
        var waypointDirection = ResolveFreeFallReachWaypointDirection(
            lizard,
            legIndex,
            shoulder,
            plan);
        var startAngle = MathF.Atan2(startDirection.Y, startDirection.X);
        var targetAngle = MathF.Atan2(targetDirection.Y, targetDirection.X);
        var (angle, plannedRadius) = EvaluateFreeFallReachPath(
            plan,
            waypointDirection,
            amount,
            startAngle,
            targetAngle,
            startRadius,
            targetRadius,
            physicalMinimum,
            physicalMaximum);
        var radius = Math.Max(retentionFloor, plannedRadius);
        return shoulder + MathEx.FromAngle(angle) * radius;
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

        FreeFallReachTargetError = 0f;
        for (var legIndex = 0; legIndex < LegCount; legIndex++)
        {
            FreeFallReachTargetError = Math.Max(
                FreeFallReachTargetError,
                Vector2.Distance(
                    _feet[legIndex].Position,
                    _freeFallReachTargets[legIndex]));
        }
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

        for (var index = 0; index < LegCount; index++)
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
        FreeFallReachContactSafe = false;
        FreeFallReachRequiresProjectionLegMask = 0;
        FreeFallReachProjectedLegMaskThisStep = 0;
        FreeFallReachRollbackLegMaskThisStep = 0;
        FreeFallReachProjectionCount = 0;
        FreeFallReachRollbackCount = 0;
        Array.Clear(_freeFallReachStartFeet);
        Array.Clear(_freeFallReachStartOffsets);
        Array.Clear(_freeFallReachTargets);
        Array.Clear(_freeFallReachTargetOffsets);
        Array.Clear(_freeFallReachStartBendSigns);
        Array.Clear(_freeFallReachBendSigns);
        Array.Clear(_freeFallReachExtensionReady);
        Array.Clear(_freeFallReachBendTransitioned);
        Array.Clear(_freeFallReachResolvedPathAmounts);
        Array.Clear(_freeFallReachCommittedElbowOffsets);
        Array.Clear(_freeFallReachCommittedFootOffsets);
        Array.Clear(_freeFallReachCommittedPoseInitialized);
        Array.Clear(_freeFallReachProjectionEligible);
        Array.Clear(_freeFallReachRequiresProjection);
        Array.Clear(_freeFallReachPathPlans);
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

    private void RestoreLastValidFreeFallPose(ProceduralLizard lizard)
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
        if (_freeFallReachCaptured)
        {
            for (var legIndex = 0; legIndex < LegCount; legIndex++)
            {
                var bodyIndex = DanglingTopology2D.GetBodyIndex(legIndex);
                var shoulder = GetShoulderPosition(
                    lizard,
                    legIndex,
                    bodyIndex);
                CommitFreeFallReachSafePose(
                    legIndex,
                    shoulder,
                    _elbows[legIndex].Position,
                    _feet[legIndex].Position);
            }
        }
        UpdateFreeFallReachError();
    }
}
