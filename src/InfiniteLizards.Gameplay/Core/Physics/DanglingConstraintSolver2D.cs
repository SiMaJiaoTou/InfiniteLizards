using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// Projects particle poses onto the shared spine and limb constraints. Held
/// poses additionally apply a material grab constraint; free-fall poses use
/// the same articulated solve with no world-space pin.
/// </summary>
internal static class DanglingConstraintSolver2D
{
    public static float SolveIteration(
        ProceduralLizard lizard,
        PhysicsConfiguration configuration,
        PhysicsParticle2D[] spine,
        PhysicsParticle2D[] elbows,
        PhysicsParticle2D[] feet,
        ReadOnlySpan<float> spineLengths,
        ReadOnlySpan<float> upperLegLengths,
        ReadOnlySpan<float> lowerLegLengths,
        GrabBinding grabBinding,
        Vector2 anchor,
        float upperIterationBlend,
        float lowerIterationBlend,
        float currentGrabConstraintError)
    {
        var grabConstraintError = PinGrab(
            lizard,
            configuration,
            spine,
            elbows,
            feet,
            grabBinding,
            anchor,
            currentGrabConstraintError);

        SolveSkeletonIteration(
            lizard,
            configuration,
            spine,
            elbows,
            feet,
            spineLengths,
            upperLegLengths,
            lowerLegLengths,
            upperIterationBlend,
            lowerIterationBlend,
            grabBinding,
            isFreeFall: false);

        return PinGrab(
            lizard,
            configuration,
            spine,
            elbows,
            feet,
            grabBinding,
            anchor,
            grabConstraintError);
    }

    /// <summary>
    /// Solves the same articulated skeleton without a material point fixed to
    /// the window. Screen-space inertia and gravity may therefore move every
    /// particle during an accidental free fall.
    /// </summary>
    public static void SolveFreeFallIteration(
        ProceduralLizard lizard,
        PhysicsConfiguration configuration,
        PhysicsParticle2D[] spine,
        PhysicsParticle2D[] elbows,
        PhysicsParticle2D[] feet,
        ReadOnlySpan<float> spineLengths,
        ReadOnlySpan<float> upperLegLengths,
        ReadOnlySpan<float> lowerLegLengths,
        float upperIterationBlend,
        float lowerIterationBlend) =>
        SolveSkeletonIteration(
            lizard,
            configuration,
            spine,
            elbows,
            feet,
            spineLengths,
            upperLegLengths,
            lowerLegLengths,
            upperIterationBlend,
            lowerIterationBlend,
            grabBinding: null,
            isFreeFall: true);

    private static void SolveSkeletonIteration(
        ProceduralLizard lizard,
        PhysicsConfiguration configuration,
        PhysicsParticle2D[] spine,
        PhysicsParticle2D[] elbows,
        PhysicsParticle2D[] feet,
        ReadOnlySpan<float> spineLengths,
        ReadOnlySpan<float> upperLegLengths,
        ReadOnlySpan<float> lowerLegLengths,
        float upperIterationBlend,
        float lowerIterationBlend,
        GrabBinding? grabBinding,
        bool isFreeFall)
    {
        static bool IsHeldSpineJoint(GrabBinding? binding, int index) =>
            binding is { } value &&
            DanglingTopology2D.IsHeldSpineJoint(value, index);

        for (var index = 0; index < spine.Length - 1; index++)
        {
            ParticleSolver2D.SolveDistance(
                ref spine[index],
                ref spine[index + 1],
                spineLengths[index],
                IsHeldSpineJoint(grabBinding, index),
                IsHeldSpineJoint(grabBinding, index + 1));
        }

        // A weak skip constraint keeps the silhouette supple without allowing
        // alternating joints to fold into a knot.
        for (var index = 0; index < spine.Length - 2; index++)
        {
            ParticleSolver2D.SolveDistance(
                ref spine[index],
                ref spine[index + 2],
                (spineLengths[index] + spineLengths[index + 1]) *
                configuration.SpineSkipLengthFactor,
                IsHeldSpineJoint(grabBinding, index),
                IsHeldSpineJoint(grabBinding, index + 2),
                stiffness: configuration.SpineSkipStiffness);
        }

        for (var legIndex = 0; legIndex < DanglingTopology2D.LegCount; legIndex++)
        {
            var bodyIndex = DanglingTopology2D.GetBodyIndex(legIndex);
            var shoulder = DanglingTopology2D.GetShoulderPosition(
                spine,
                legIndex,
                bodyIndex,
                lizard.Spine.Angles[bodyIndex],
                lizard.GetBodyWidth(bodyIndex),
                configuration.ShoulderAttachmentRadiusFactor);
            var upperLength = upperLegLengths[legIndex];
            var lowerLength = lowerLegLengths[legIndex];
            var side = DanglingTopology2D.GetSide(legIndex);
            var legUpperIterationBlend = upperIterationBlend;
            var legLowerIterationBlend = lowerIterationBlend;
            // Weak angular springs keep paired limbs readable instead of
            // collapsing into the body's silhouette. Gravity still owns the
            // vertical component and inertia still drives the swing.
            Vector2 upperDirection;
            Vector2 lowerDirection;
            if (isFreeFall)
            {
                // Screen-horizontal bias can send a rotated rear leg through
                // the head (and a front leg through the tail). In free fall,
                // author the spring in the current body frame instead: the
                // main component always exits the attachment silhouette,
                // while only the tangential part of gravity makes it dangle.
                var headward = MathEx.SafeNormalize(
                    spine[Math.Max(0, bodyIndex - 1)].Position -
                    spine[bodyIndex].Position,
                    MathEx.FromAngle(lizard.Spine.Angles[bodyIndex]));
                var bodyOutward = MathEx.SafeNormalize(
                    shoulder - spine[bodyIndex].Position,
                    MathEx.Perpendicular(headward) * side);
                var longitudinal = legIndex < 2 ? headward : -headward;
                // Keep the ordinary falling pose bent and below the shoulder.
                // If a paw is already almost fully extended screen-up, there
                // is no geometrically honest room for a visible upward catch.
                // Solving a compact desired IK pose here leaves that room while
                // still keeping each limb on its own head/tail end.
                var desiredFootDirection = legIndex == 3
                    ? MathEx.SafeNormalize(
                        Vector2.UnitY * 1.20f +
                        bodyOutward * 0.80f -
                        longitudinal * 0.80f,
                        bodyOutward)
                    : MathEx.SafeNormalize(
                        Vector2.UnitY * 1.35f +
                        bodyOutward * 0.90f +
                        longitudinal * 0.55f,
                        bodyOutward);
                var desiredFootRadius = Math.Clamp(
                    (upperLength + lowerLength) * 0.70f,
                    MathF.Abs(upperLength - lowerLength) + 0.001f,
                    upperLength + lowerLength - 0.001f);
                var desiredAlong =
                    (upperLength * upperLength - lowerLength * lowerLength +
                     desiredFootRadius * desiredFootRadius) /
                    (2f * desiredFootRadius);
                var desiredBendHeight = MathF.Sqrt(Math.Max(
                    0f,
                    upperLength * upperLength - desiredAlong * desiredAlong));
                var desiredElbowA =
                    desiredFootDirection * desiredAlong +
                    MathEx.Perpendicular(desiredFootDirection) *
                    desiredBendHeight;
                var desiredElbowB =
                    desiredFootDirection * desiredAlong -
                    MathEx.Perpendicular(desiredFootDirection) *
                    desiredBendHeight;
                var desiredElbow =
                    Vector2.Dot(desiredElbowA, bodyOutward) >=
                    Vector2.Dot(desiredElbowB, bodyOutward)
                        ? desiredElbowA
                        : desiredElbowB;
                upperDirection = MathEx.SafeNormalize(
                    desiredElbow,
                    bodyOutward);
                lowerDirection = MathEx.SafeNormalize(
                    desiredFootDirection * desiredFootRadius - desiredElbow,
                    bodyOutward);

                // Free fall has only a short pre-catch window. Accelerate the
                // same gradual spring (rather than snapping at reach capture)
                // so the pose has already cleared the body when Seeking starts.
                const float FreeFallSpringBoost = 4f;
                legUpperIterationBlend = MathEx.Clamp01(
                    upperIterationBlend * FreeFallSpringBoost);
                legLowerIterationBlend = MathEx.Clamp01(
                    lowerIterationBlend * FreeFallSpringBoost);
            }
            else
            {
                upperDirection = Vector2.Normalize(new Vector2(
                    side * configuration.UpperLimbHorizontalBias,
                    1f));
                lowerDirection = Vector2.Normalize(new Vector2(
                    side * configuration.LowerLimbHorizontalBias,
                    1f));
            }
            elbows[legIndex].Position = Vector2.Lerp(
                elbows[legIndex].Position,
                shoulder + upperDirection * upperLength,
                legUpperIterationBlend);
            ParticleSolver2D.SolvePinnedDistance(
                ref elbows[legIndex],
                shoulder,
                upperLength);
            feet[legIndex].Position = Vector2.Lerp(
                feet[legIndex].Position,
                elbows[legIndex].Position + lowerDirection * lowerLength,
                legLowerIterationBlend);
            ParticleSolver2D.SolveDistance(
                ref elbows[legIndex],
                ref feet[legIndex],
                lowerLength);
        }
    }

    public static float FinalizeConstrainedPose(
        ProceduralLizard lizard,
        PhysicsConfiguration configuration,
        PhysicsParticle2D[] spine,
        PhysicsParticle2D[] elbows,
        PhysicsParticle2D[] feet,
        ReadOnlySpan<float> spineLengths,
        ReadOnlySpan<float> upperLegLengths,
        ReadOnlySpan<float> lowerLegLengths,
        GrabBinding grabBinding,
        Vector2 anchor,
        float currentGrabConstraintError)
    {
        var held = DanglingTopology2D.GetHeldSpineSegment(grabBinding);
        var heldLength = spineLengths[held.Start];
        var heldDirection = MathEx.SafeNormalize(
            spine[held.End].Position - spine[held.Start].Position,
            Vector2.UnitY);
        var grabConstraintError = currentGrabConstraintError;

        if (grabBinding.Kind == GrabBindingKind.Spine)
        {
            var heldNormal = MathEx.Perpendicular(heldDirection);
            spine[held.Start].Position =
                anchor -
                heldDirection *
                (grabBinding.T * heldLength + grabBinding.TangentOffset) -
                heldNormal * grabBinding.NormalOffset;
            spine[held.End].Position =
                spine[held.Start].Position + heldDirection * heldLength;
        }
        else
        {
            var midpoint =
                (spine[held.Start].Position + spine[held.End].Position) * 0.5f;
            spine[held.Start].Position =
                midpoint - heldDirection * (heldLength * 0.5f);
            spine[held.End].Position =
                midpoint + heldDirection * (heldLength * 0.5f);
            grabConstraintError = PinGrab(
                lizard,
                configuration,
                spine,
                elbows,
                feet,
                grabBinding,
                anchor,
                grabConstraintError);
        }

        ProjectSpineOutward(spine, spineLengths, held.Start, held.End);

        for (var legIndex = 0; legIndex < DanglingTopology2D.LegCount; legIndex++)
        {
            var bodyIndex = DanglingTopology2D.GetBodyIndex(legIndex);
            var shoulder = DanglingTopology2D.GetShoulderPosition(
                spine,
                legIndex,
                bodyIndex,
                lizard.Spine.Angles[bodyIndex],
                lizard.GetBodyWidth(bodyIndex),
                configuration.ShoulderAttachmentRadiusFactor);
            var upperDirection = MathEx.SafeNormalize(
                elbows[legIndex].Position - shoulder,
                Vector2.UnitY);
            elbows[legIndex].Position =
                shoulder + upperDirection * upperLegLengths[legIndex];
            var lowerDirection = MathEx.SafeNormalize(
                feet[legIndex].Position - elbows[legIndex].Position,
                Vector2.UnitY);
            feet[legIndex].Position =
                elbows[legIndex].Position +
                lowerDirection * lowerLegLengths[legIndex];
        }

        // A leg attachment translates its complete two-link limb and shoulder
        // segment together. Re-project the two free spine branches afterwards;
        // this leaves both the material grab point and every bone length exact.
        grabConstraintError = PinGrab(
            lizard,
            configuration,
            spine,
            elbows,
            feet,
            grabBinding,
            anchor,
            grabConstraintError);
        ProjectSpineOutward(spine, spineLengths, held.Start, held.End);
        return Vector2.Distance(
            DanglingTopology2D.GetBindingPoint(
                lizard,
                spine,
                elbows,
                feet,
                grabBinding,
                configuration.ShoulderAttachmentRadiusFactor),
            anchor);
    }

    private static float PinGrab(
        ProceduralLizard lizard,
        PhysicsConfiguration configuration,
        PhysicsParticle2D[] spine,
        PhysicsParticle2D[] elbows,
        PhysicsParticle2D[] feet,
        GrabBinding grabBinding,
        Vector2 anchor,
        float currentGrabConstraintError)
    {
        var current = DanglingTopology2D.GetBindingPoint(
            lizard,
            spine,
            elbows,
            feet,
            grabBinding,
            configuration.ShoulderAttachmentRadiusFactor);
        if (!IsFinite(current))
        {
            return currentGrabConstraintError;
        }

        var correction = anchor - current;
        switch (grabBinding.Kind)
        {
            case GrabBindingKind.Spine:
                ParticleSolver2D.TranslatePosition(
                    ref spine[grabBinding.Index],
                    correction);
                ParticleSolver2D.TranslatePosition(
                    ref spine[grabBinding.Index + 1],
                    correction);
                break;

            case GrabBindingKind.UpperLeg:
            case GrabBindingKind.LowerLeg:
            {
                var bodyIndex = DanglingTopology2D.GetBodyIndex(grabBinding.Index);
                ParticleSolver2D.TranslatePosition(
                    ref spine[Math.Max(0, bodyIndex - 1)],
                    correction);
                ParticleSolver2D.TranslatePosition(
                    ref spine[bodyIndex],
                    correction);
                ParticleSolver2D.TranslatePosition(
                    ref elbows[grabBinding.Index],
                    correction);
                ParticleSolver2D.TranslatePosition(
                    ref feet[grabBinding.Index],
                    correction);
                break;
            }
        }

        return Vector2.Distance(
            DanglingTopology2D.GetBindingPoint(
                lizard,
                spine,
                elbows,
                feet,
                grabBinding,
                configuration.ShoulderAttachmentRadiusFactor),
            anchor);
    }

    private static void ProjectSpineOutward(
        PhysicsParticle2D[] spine,
        ReadOnlySpan<float> spineLengths,
        int heldStart,
        int heldEnd)
    {
        for (var index = heldStart - 1; index >= 0; index--)
        {
            var direction = MathEx.SafeNormalize(
                spine[index].Position - spine[index + 1].Position,
                -Vector2.UnitY);
            spine[index].Position =
                spine[index + 1].Position + direction * spineLengths[index];
        }
        for (var index = heldEnd + 1; index < spine.Length; index++)
        {
            var direction = MathEx.SafeNormalize(
                spine[index].Position - spine[index - 1].Position,
                Vector2.UnitY);
            spine[index].Position =
                spine[index - 1].Position + direction * spineLengths[index - 1];
        }
    }

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);
}
