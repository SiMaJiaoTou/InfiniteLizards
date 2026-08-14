using System.Numerics;
using DesktopLizard.Core;

namespace DesktopLizard.Diagnostics;

internal readonly record struct AnimationProfileSmokeResult(
    bool Passed,
    bool ProfileApplied,
    int StepTransitions,
    int NonFiniteSamples,
    float MaximumConstraintError,
    float MaximumGrabError,
    string FailureDetail);

/// <summary>
/// Exercises the animation stack with deliberately non-default geometry,
/// cadence and dangling-physics settings. The ordinary diagnostics continue
/// to construct <see cref="ProceduralLizard"/> without a profile and therefore
/// remain the regression oracle for the neutral defaults.
/// </summary>
internal static class AnimationProfileSmokeTest
{
    public static AnimationProfileSmokeResult Run()
    {
        try
        {
            var source = LizardConfiguration.Default;
            var gait = source.Gait with
            {
                FrontLegLinkLength = 32f,
                RearLegLinkLength = 30f,
                MinimumStepDuration = 0.065f,
                StepHeightBase = 5f,
                StepHeightDistanceFactor = 0.12f,
                MinimumStepHeight = 7f,
                MaximumStepHeight = 10f,
                FrontElbowLongitudinalOffset = 5f,
                RearElbowLongitudinalOffset = -4.4f,
                ElbowLateralOffset = 2f,
                FrontLateralReach = 21.5f,
                RearLateralReach = 19f,
                FrontLongitudinalBase = 8f,
                RearLongitudinalBase = -3.5f,
                StanceSkew = [9.5f, 2.5f, -8.5f, -2.5f],
                ShoulderInsetFactor = 0.35f,
                ReferencePairSpacing = 17.5f,
                MinimumPairGap = 0.17f,
                MaximumPairGap = 0.75f,
                SlowPairErrorMemoryDuration = 0.09f,
                FastPairErrorMemoryDuration = 0.05f,
                SlowMinimumSupportDuration = 0.09f,
                FastMinimumSupportDuration = 0.065f,
                SlowStepDuration = 0.155f,
                FastStepDuration = 0.082f,
                MinimumLead = 23f,
                MaximumLead = 25.5f,
                PairStaggerMaximum = 0.025f
            };
            var physics = source.Physics with
            {
                Gravity = 690f,
                LinearDrag = 2.8f,
                ConstraintIterations = 20,
                ConstraintVelocityResponseRate = 55f,
                MaximumSpeed = 740f,
                TeleportThreshold = 160f,
                UpperLimbAngularSpring = 0.60f,
                LowerLimbAngularSpring = 0.82f,
                AngularSpringDelay = 0.05f,
                AngularSpringRampDuration = 0.30f,
                UpperLimbHorizontalBias = 0.18f,
                LowerLimbHorizontalBias = 0.10f,
                SpineSkipLengthFactor = 0.975f,
                SpineSkipStiffness = 0.19f,
                ShoulderAttachmentRadiusFactor = 0.60f,
                ReleasePoseRecoveryDuration = 0.30f
            };
            var appearance = source.Appearance with
            {
                VisualScale = 0.62f,
                SpineLinkLength = 17f,
                SpineMaximumBend = MathF.PI / 10f,
                HeadAnchorOffset = 52f,
                SpineRestCurveAmplitude = 14f,
                BodyWidths = source.Appearance.BodyWidths
                    .Select(width => width * 1.05f)
                    .ToArray()
            };
            var secondaryMotion = source.SecondaryMotion with
            {
                BodyBobMinimumFrequency = 4.8f,
                BodyBobMaximumFrequency = 9.6f,
                IdleTailAmplitude = 1.7f,
                ObserveTailAmplitude = 2.2f,
                BreathingAmplitude = 0.021f,
                BlinkMinimumInterval = 1.8f,
                BlinkMaximumInterval = 3.5f
            };
            var rendering = source.Rendering with
            {
                NoseRadius = 14.2f,
                FrontFootRadius = 11f,
                RearFootRadius = 10.6f,
                EyeRadius = 15.8f,
                PupilRadius = 12.9f,
                HitGeometryRefreshRate = 24f
            };
            // Geometry is intentionally larger than the default individual.
            // Resolve the same conservative canvas contract used by runtime
            // profiles instead of relying on a magic 600-model-unit surface.
            var geometryEnvelope = LizardGeometryEnvelope.Calculate(
                appearance,
                gait,
                secondaryMotion,
                rendering);
            appearance = geometryEnvelope.EnsureCanvasCapacity(appearance);
            var runtime = source.Runtime with
            {
                SimulationRate = 144f,
                DebugPathCapacity = 180,
                DebugPathSampleInterval = 0.08f
            };
            var lostGripFall = source.Behavior.LostGripFall with
            {
                BottomSafetyInset = MathF.Ceiling(
                    geometryEnvelope.RequiredFallBottomSafetyInset(
                        appearance,
                        runtime))
            };
            var configuration = (source with
            {
                Behavior = source.Behavior with { LostGripFall = lostGripFall },
                Gait = gait,
                Physics = physics,
                Appearance = appearance,
                SecondaryMotion = secondaryMotion,
                Rendering = rendering,
                Runtime = runtime
            }).EnsureValid();
            var profile = IndividualProfileFactory.Create(
                configuration,
                seed: 0x13572468,
                applyVariation: false);
            var lizard = new ProceduralLizard(profile);
            var expectedFrontReach = gait.FrontLegLinkLength * gait.MaximumReachFactor;
            var expectedRearReach = gait.RearLegLinkLength * gait.MaximumReachFactor;
            var expectedHeadX = appearance.RenderCanvasSize * 0.5f + appearance.HeadAnchorOffset;
            var profileApplied =
                ReferenceEquals(lizard.Profile, profile) &&
                NearlyEqual(lizard.Spine.LinkSize, appearance.SpineLinkLength) &&
                NearlyEqual(lizard.Spine.AngleConstraint, appearance.SpineMaximumBend) &&
                NearlyEqual(lizard.Spine.Joints[0].X, expectedHeadX) &&
                NearlyEqual(lizard.GetBodyWidth(2), appearance.BodyWidths[2]) &&
                NearlyEqual(lizard.Legs[0].MaximumReach, expectedFrontReach) &&
                NearlyEqual(lizard.Legs[2].MaximumReach, expectedRearReach) &&
                lizard.Profile.Physics.ConstraintIterations == physics.ConstraintIterations &&
                NearlyEqual(lizard.Profile.SecondaryMotion.IdleTailAmplitude, secondaryMotion.IdleTailAmplitude) &&
                NearlyEqual(lizard.Profile.Rendering.NoseRadius, rendering.NoseRadius) &&
                NearlyEqual(lizard.Profile.Runtime.SimulationRate, runtime.SimulationRate);

            const float dt = 1f / 120f;
            var mood = EmotionBlend.Normalize(new EmotionBlend(0.3f, 0.3f, 0.3f, 0.1f));
            var nonFiniteSamples = 0;
            var maximumConstraintError = 0f;
            var maximumGrabError = 0f;

            for (var frame = 0; frame < 1440; frame++)
            {
                var displaySpeed = 34f + MathF.Sin(frame / 190f) * 7f;
                var heading = MathF.Sin(frame / 230f) * 0.24f;
                var delta = MathEx.FromAngle(heading) *
                            (displaySpeed * dt / appearance.VisualScale);
                lizard.Update(
                    dt,
                    new LizardAnimationInput(
                        heading,
                        MathEx.Clamp01(displaySpeed / profile.Behavior.Speed.AnimationNormalization),
                        LizardPoseMode.Locomotion,
                        mood,
                        0f,
                        delta));
                if (!IsFinite(lizard)) nonFiniteSamples++;
            }

            var grabPoint = lizard.GetBodyPoint(2, 1f, -1f);
            lizard.BeginGrab(grabPoint);
            for (var frame = 0; frame < 300; frame++)
            {
                var windowDelta = new Vector2(
                    MathF.Sin(frame / 31f) * 0.22f,
                    MathF.Cos(frame / 43f) * 0.14f);
                lizard.Update(
                    dt,
                    new LizardAnimationInput(
                        lizard.Heading,
                        0f,
                        LizardPoseMode.Grabbed,
                        mood,
                        0f,
                        windowDelta));
                maximumConstraintError = Math.Max(
                    maximumConstraintError,
                    lizard.DanglingConstraintError);
                maximumGrabError = Math.Max(maximumGrabError, lizard.DanglingGrabError);
                if (!IsFinite(lizard)) nonFiniteSamples++;
            }

            for (var frame = 0; frame < 120; frame++)
            {
                var progress = frame / 119f;
                lizard.Update(
                    dt,
                    new LizardAnimationInput(
                        lizard.Heading,
                        0f,
                        LizardPoseMode.ReleaseSettle,
                        mood,
                        progress,
                        Vector2.Zero));
                if (!IsFinite(lizard)) nonFiniteSamples++;
            }

            var stepTransitions = lizard.Legs.Sum(leg => leg.StepSerial);
            var passed =
                profileApplied &&
                stepTransitions >= 8 &&
                nonFiniteSamples == 0 &&
                float.IsFinite(maximumConstraintError) &&
                maximumConstraintError <= physics.MaximumConstraintError &&
                float.IsFinite(maximumGrabError) &&
                maximumGrabError <= physics.MaximumGrabError;
            return new AnimationProfileSmokeResult(
                passed,
                profileApplied,
                stepTransitions,
                nonFiniteSamples,
                maximumConstraintError,
                maximumGrabError,
                passed ? "none" : "non-default profile failed an application or stability bound");
        }
        catch (Exception exception)
        {
            return new AnimationProfileSmokeResult(
                false,
                false,
                0,
                1,
                float.PositiveInfinity,
                float.PositiveInfinity,
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool IsFinite(ProceduralLizard lizard) =>
        lizard.Spine.Joints.All(IsFinite) &&
        lizard.Spine.Angles.All(float.IsFinite) &&
        lizard.Legs.All(leg =>
            IsFinite(leg.Shoulder) &&
            IsFinite(leg.Elbow) &&
            IsFinite(leg.Foot) &&
            float.IsFinite(leg.MaximumReach));

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool NearlyEqual(float left, float right) =>
        MathF.Abs(left - right) <= 0.0001f;
}
