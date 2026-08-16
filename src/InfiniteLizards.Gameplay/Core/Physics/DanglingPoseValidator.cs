using System.Numerics;

namespace DesktopLizard.Core;

internal readonly record struct DanglingPoseValidation(
    bool IsValid,
    float MaximumConstraintError,
    float SpineConstraintError,
    float UpperLegConstraintError,
    float LowerLegConstraintError);

/// <summary>
/// Pure suspended-pose validation. It reports constraint channels separately
/// and has no recovery or mutation policy; the rig decides what to do with an
/// invalid candidate pose.
/// </summary>
internal static class DanglingPoseValidator
{
    public static DanglingPoseValidation Validate(
        ProceduralLizard lizard,
        ReadOnlySpan<PhysicsParticle2D> spine,
        ReadOnlySpan<PhysicsParticle2D> elbows,
        ReadOnlySpan<PhysicsParticle2D> feet,
        ReadOnlySpan<float> spineLengths,
        ReadOnlySpan<float> upperLegLengths,
        ReadOnlySpan<float> lowerLegLengths,
        float shoulderAttachmentRadiusFactor,
        float maximumCoordinate,
        float maximumConstraintError,
        float grabConstraintError,
        float maximumGrabError)
    {
        var spineError = 0f;
        var upperLegError = 0f;
        var lowerLegError = 0f;

        for (var index = 0; index < spine.Length; index++)
        {
            if (!ParticleSolver2D.IsFinite(spine[index], maximumCoordinate))
            {
                return InvalidBeforeAggregate(
                    spineError,
                    upperLegError,
                    lowerLegError);
            }
            if (index > 0)
            {
                spineError = Math.Max(
                    spineError,
                    MathF.Abs(
                        Vector2.Distance(
                            spine[index - 1].Position,
                            spine[index].Position) -
                        spineLengths[index - 1]));
            }
        }

        for (var legIndex = 0; legIndex < DanglingTopology2D.LegCount; legIndex++)
        {
            if (!ParticleSolver2D.IsFinite(elbows[legIndex], maximumCoordinate) ||
                !ParticleSolver2D.IsFinite(feet[legIndex], maximumCoordinate))
            {
                return InvalidBeforeAggregate(
                    spineError,
                    upperLegError,
                    lowerLegError);
            }

            var bodyIndex = DanglingTopology2D.GetBodyIndex(legIndex);
            var shoulder = DanglingTopology2D.GetShoulderPosition(
                spine,
                legIndex,
                bodyIndex,
                lizard.Spine.Angles[bodyIndex],
                lizard.GetBodyWidth(bodyIndex),
                shoulderAttachmentRadiusFactor);
            upperLegError = Math.Max(
                upperLegError,
                MathF.Abs(
                    Vector2.Distance(shoulder, elbows[legIndex].Position) -
                    upperLegLengths[legIndex]));
            lowerLegError = Math.Max(
                lowerLegError,
                MathF.Abs(
                    Vector2.Distance(
                        elbows[legIndex].Position,
                        feet[legIndex].Position) -
                    lowerLegLengths[legIndex]));
        }

        var aggregateError = Math.Max(
            spineError,
            Math.Max(upperLegError, lowerLegError));
        var isValid =
            aggregateError <= maximumConstraintError &&
            float.IsFinite(grabConstraintError) &&
            grabConstraintError <= maximumGrabError;
        return new DanglingPoseValidation(
            isValid,
            aggregateError,
            spineError,
            upperLegError,
            lowerLegError);
    }

    private static DanglingPoseValidation InvalidBeforeAggregate(
        float spineError,
        float upperLegError,
        float lowerLegError) =>
        new(
            false,
            0f,
            spineError,
            upperLegError,
            lowerLegError);
}
