using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DesktopLizard.Core;

internal sealed class ProceduralLizard
{
    private const float RasterContainmentMargin = 2f;
    private const float RegripCompletionEpsilon = 0.0001f;
    private readonly LizardProfile _profile;
    private readonly GaitConfiguration _gait;
    private readonly PhysicsConfiguration _physics;
    private readonly AppearanceConfiguration _appearance;
    private readonly RenderingConfiguration _rendering;
    private readonly SecondaryMotionConfiguration _secondaryMotion;
    private readonly Vector2 _canvasCenter;
    private readonly float[] _bodyWidths;
    private readonly LegRig[] _legs;
    private readonly DiagonalGaitController _gaitController;
    private readonly DanglingRig2D _danglingRig;
    private readonly RegripPoseController _regripPoseController;
    private readonly SecondaryMotionController _secondaryMotionController;
    private float _heading;
    private Vector2 _grabAnchor;
    private float _releasePoseBlend;
    private readonly Vector2[] _releaseSpinePose;
    private readonly Vector2[] _releaseBlendPose;
    private readonly Vector2[] _releaseElbows = new Vector2[4];
    private readonly Vector2[] _releaseFeet = new Vector2[4];
    private bool _freeFallMetricsActive;
    private bool _freeFallContactAuthorized;
    private float _freeFallContainmentCorrectionTotal;
    private float _freeFallContainmentCorrectionMaximum;

    public Chain Spine { get; }
    public IReadOnlyList<LegRig> Legs => _legs;
    public float Heading => _heading;
    public float BlinkAmount => _secondaryMotionController.BlinkAmount;
    public float BodyBob => _secondaryMotionController.BodyBob;
    public float TailSwayOffset => _secondaryMotionController.TailSwayOffset;
    public float BodyWidthScale => _secondaryMotionController.BodyWidthScale;
    public EmotionBlend Emotion { get; private set; }
    internal LizardProfile Profile => _profile;
    internal float DanglingGrabError => _danglingRig.GrabConstraintError;
    internal float DanglingConstraintError => _danglingRig.MaximumConstraintError;
    internal float DanglingSpineError => _danglingRig.SpineConstraintError;
    internal float DanglingUpperLegError => _danglingRig.UpperLegConstraintError;
    internal float DanglingLowerLegError => _danglingRig.LowerLegConstraintError;
    internal float FreeFallContainmentCorrectionTotal =>
        _freeFallContainmentCorrectionTotal;
    internal float FreeFallContainmentCorrectionMaximum =>
        _freeFallContainmentCorrectionMaximum;
    internal float FreeFallReferenceCorrectionTotal =>
        _danglingRig.FreeFallReferenceCorrectionTotal;
    internal float FreeFallReferenceCorrectionMaximum =>
        _danglingRig.FreeFallReferenceCorrectionMaximum;
    internal float FreeFallReferenceCenterError =>
        _danglingRig.FreeFallReferenceCenterError;
    internal LizardPoseMode CurrentPoseMode { get; private set; }
    internal RegripAnimationPhase RegripAnimationPhase =>
        _regripPoseController.Phase;
    internal bool RegripReachActive => _regripPoseController.ReachActive;
    internal float RegripReachProgress => _regripPoseController.ReachProgress;
    internal bool RegripReachContactSafe =>
        _danglingRig.FreeFallReachContactSafe;
    internal int RegripReachRequiresProjectionLegMask =>
        _danglingRig.FreeFallReachRequiresProjectionLegMask;
    internal int RegripReachProjectedLegMaskThisStep =>
        _danglingRig.FreeFallReachProjectedLegMaskThisStep;
    internal int RegripReachRollbackLegMaskThisStep =>
        _danglingRig.FreeFallReachRollbackLegMaskThisStep;
    internal int RegripReachProjectionCount =>
        _danglingRig.FreeFallReachProjectionCount;
    internal int RegripReachRollbackCount =>
        _danglingRig.FreeFallReachRollbackCount;
    internal Vector2 RegripFrontContactTarget0 => RegripContactTarget(0);
    internal Vector2 RegripFrontContactTarget1 => RegripContactTarget(1);
    internal Vector2 RegripRearContactTarget0 => RegripContactTarget(2);
    internal Vector2 RegripRearContactTarget1 => RegripContactTarget(3);
    internal int RegripContactLegMask => _regripPoseController.ContactLegMask;
    internal bool RegripContacted => _regripPoseController.Contacted;
    internal float RegripContactError => _regripPoseController.ContactError;
    internal float RegripContactSpineDrift =>
        _regripPoseController.ContactSpineDrift;

    internal Vector2 RegripContactTarget(int legIndex) =>
        _regripPoseController.ContactTarget(legIndex);

    public ProceduralLizard(LizardProfile? profile = null)
    {
        _profile = profile ?? LizardProfile.Default;
        _gait = _profile.Gait;
        _physics = _profile.Physics;
        _appearance = _profile.Appearance;
        _rendering = _profile.Rendering;
        _secondaryMotion = _profile.SecondaryMotion;
        _bodyWidths = (float[])_appearance.BodyWidths.Clone();
        _canvasCenter = new Vector2(
            _appearance.RenderCanvasSize * 0.5f,
            _appearance.RenderCanvasSize * 0.5f);
        _grabAnchor = _canvasCenter;
        _releaseSpinePose = new Vector2[_bodyWidths.Length];
        _releaseBlendPose = new Vector2[_bodyWidths.Length];

        Spine = new Chain(
            _canvasCenter,
            _bodyWidths.Length,
            _appearance.SpineLinkLength,
            _appearance.SpineMaximumBend);
        Spine.Reset(_canvasCenter + new Vector2(_appearance.HeadAnchorOffset, 0f), 0f);
        Spine.ApplyRestCurve(_appearance.SpineRestCurveAmplitude);
        _legs = Enumerable.Range(0, 4)
            .Select(index => new LegRig(index, _canvasCenter, _gait))
            .ToArray();
        _gaitController = new DiagonalGaitController(
            _legs,
            _gait,
            _profile.Behavior.Speed,
            _appearance.VisualScale,
            GetFootHome,
            GetLegBodyAngle);
        _danglingRig = new DanglingRig2D(
            _physics,
            _bodyWidths.Length,
            _appearance.RenderCanvasSize * _physics.MaximumCoordinateCanvasFactor);
        _regripPoseController = new RegripPoseController(_bodyWidths.Length);
        _secondaryMotionController = new SecondaryMotionController(
            _secondaryMotion,
            _profile.Traits.Seed);
        ResetLegs();
    }

    public LizardDebugSnapshot CaptureDebugSnapshot()
    {
        var spineJoints = Spine.Joints.ToImmutableArray();
        var spineAngles = Spine.Angles.ToImmutableArray();
        var legs = new LegDebugSnapshot[_legs.Length];
        for (var index = 0; index < _legs.Length; index++)
        {
            var leg = _legs[index];
            legs[index] = new LegDebugSnapshot(
                leg.Index,
                leg.Pair,
                leg.Side,
                leg.BodyIndex,
                leg.IsFront,
                leg.Shoulder,
                leg.Elbow,
                leg.Foot,
                leg.StepFrom,
                leg.StepTo,
                leg.StepNormal,
                leg.IsStepping,
                leg.IsSwingMoving,
                leg.StepProgress,
                leg.StepDelayRemaining,
                leg.Lift,
                leg.StepDuration,
                leg.StepSpan,
                leg.StepHeight,
                leg.MaximumReach,
                leg.LastReachCorrection,
                leg.MaximumKinematicCorrection,
                leg.StepSerial,
                leg.ReachProjectionSerial,
                leg.StepTargetClampSerial,
                leg.SwingClampSerial);
        }

        return new LizardDebugSnapshot(
            spineJoints,
            spineAngles,
            legs.ToImmutableArray(),
            _heading,
            BlinkAmount,
            BodyBob,
            TailSwayOffset,
            BodyWidthScale,
            _gaitController.NextPair,
            _gaitController.SupportTimeRemaining,
            _gaitController.AllFeetDownAge,
            _gaitController.StepWaitTime,
            _gaitController.PairError0,
            _gaitController.PairError1,
            _gaitController.LastGaitDisplaySpeed,
            _danglingRig.IsActive,
            DanglingGrabError,
            DanglingConstraintError,
            DanglingSpineError,
            DanglingUpperLegError,
            DanglingLowerLegError);
    }

    public LizardRenderFrame CaptureRenderFrame()
    {
        // The arrays become owned by ImmutableArray without another copy. A
        // display frame therefore allocates only two compact buffers and the
        // renderer never receives a reference to the mutable rig itself.
        var outline = new Vector2[Spine.Joints.Count * 2 + 1];
        var writeIndex = 0;
        for (var index = 0; index < Spine.Joints.Count; index++)
        {
            outline[writeIndex++] = GetVisualBodyPoint(index, 1f);
        }

        for (var index = Spine.Joints.Count - 1; index >= 0; index--)
        {
            outline[writeIndex++] = GetVisualBodyPoint(index, -1f);
        }

        outline[writeIndex] = GetHeadNose();

        var legs = new LizardRenderLegPose[_legs.Length];
        for (var index = 0; index < _legs.Length; index++)
        {
            var leg = _legs[index];
            legs[index] = new LizardRenderLegPose(
                leg.Shoulder,
                leg.Elbow,
                leg.Foot,
                leg.IsFront,
                leg.Lift);
        }

        return new LizardRenderFrame(
            ImmutableCollectionsMarshal.AsImmutableArray(outline),
            ImmutableCollectionsMarshal.AsImmutableArray(legs),
            GetHeadNose(),
            GetEyeCenter(-1),
            GetEyeCenter(1),
            _heading,
            BlinkAmount);
    }

    public void BeginGrab(Vector2 modelPoint)
    {
        if (!float.IsFinite(modelPoint.X) || !float.IsFinite(modelPoint.Y))
        {
            modelPoint = Spine.Joints[0];
        }
        _grabAnchor = new Vector2(
            Math.Clamp(modelPoint.X, 0f, _appearance.RenderCanvasSize),
            Math.Clamp(modelPoint.Y, 0f, _appearance.RenderCanvasSize));
        if (_danglingRig.IsActive)
        {
            _danglingRig.End();
        }
        _freeFallContactAuthorized = false;
        _regripPoseController.Reset();
        _gaitController.ResetTracking();
    }

    public void Update(float dt, in LizardAnimationInput input)
    {
        dt = Math.Clamp(dt, 0f, 0.05f);
        Emotion = input.Emotion;
        CurrentPoseMode = input.PoseMode;

        var pointerGrabbed = input.PoseMode == LizardPoseMode.Grabbed;
        var freeFalling = input.PoseMode == LizardPoseMode.FreeFall;
        var usingParticleRig = input.PoseMode.UsesParticleRig();
        // Keep the gait active through the short braking tail when a roaming
        // phase enters rest. This prevents the body from gliding while the paws
        // have already switched to their idle logic.
        var walking = input.PoseMode.IsLocomoting() ||
                      input.NormalizedSpeed > _secondaryMotion.WalkingSpeedThreshold;
        var headingTarget = input.DesiredHeading;
        var oldHeading = _heading;
        var maxTurn = usingParticleRig
            ? _secondaryMotion.GrabbedHeadingRate
            : MathEx.Lerp(
                _secondaryMotion.MinimumHeadingRate,
                _secondaryMotion.MaximumHeadingRate,
                input.NormalizedSpeed);
        _heading = MathEx.RotateTowards(_heading, headingTarget, maxTurn * dt);
        var signedTurnAmount = MathEx.DeltaAngle(oldHeading, _heading);
        var turnAmount = MathF.Abs(signedTurnAmount);

        var forward = MathEx.FromAngle(_heading);
        var locomotionDelta = usingParticleRig ? Vector2.Zero : input.ScreenDeltaModel;
        var endingParticlePose = !usingParticleRig && _danglingRig.IsActive;

        // MouseUp can arrive between two simulation callbacks. In that case
        // the HWND has already followed the cursor while the ragdoll still
        // contains the previous local pose. The first recovery frame must
        // preserve that pose exactly; treating the pending window delta as
        // planted-foot locomotion would shift only the paws and tear the rig.
        if (endingParticlePose)
        {
            locomotionDelta = Vector2.Zero;
        }

        var groundShift = -locomotionDelta;
        foreach (var leg in _legs)
        {
            if (!usingParticleRig)
            {
                leg.ShiftGround(groundShift);
            }
        }

        if (usingParticleRig)
        {
            if (pointerGrabbed)
            {
                _freeFallMetricsActive = false;
                _freeFallContactAuthorized = false;
                _regripPoseController.Reset();
                _danglingRig.Update(this, dt, _grabAnchor, input.ScreenDeltaModel);
            }
            else if (freeFalling)
            {
                if (!_danglingRig.IsFreeFallActive)
                {
                    _freeFallContactAuthorized = false;
                }
                _freeFallContactAuthorized |=
                    input.CatchPreparationContactAllowed;
                if (!_freeFallMetricsActive)
                {
                    _freeFallContainmentCorrectionTotal = 0f;
                    _freeFallContainmentCorrectionMaximum = 0f;
                    _freeFallMetricsActive = true;
                }
                _danglingRig.UpdateFreeFall(
                    this,
                    dt,
                    input.ScreenDeltaModel,
                    input.CatchPreparationProgress);
                var containmentCorrection = KeepParticlePoseInsideRenderCanvas();
                var containmentDistance = containmentCorrection.Length();
                _freeFallContainmentCorrectionTotal += containmentDistance;
                _freeFallContainmentCorrectionMaximum = Math.Max(
                    _freeFallContainmentCorrectionMaximum,
                    containmentDistance);
                if (_danglingRig.FreeFallReachActive)
                {
                    _regripPoseController.ObserveSeeking(
                        _danglingRig.FreeFallReachProgress,
                        _danglingRig.FreeFallReachTargets,
                        _danglingRig.FreeFallReachTargetError,
                        _freeFallContactAuthorized &&
                        _danglingRig.FreeFallReachContactSafe);
                }
                else
                {
                    _regripPoseController.ObserveFreeFallWithoutReach();
                }
            }
            _releasePoseBlend = 0f;
            _secondaryMotionController.Update(
                dt,
                input.NormalizedSpeed,
                input.PoseMode,
                input.Emotion,
                input.DropProgress,
                walking);
            return;
        }

        _freeFallMetricsActive = false;

        if (endingParticlePose)
        {
            var endingFreeFallPose = _danglingRig.IsFreeFallActive;
            if (endingFreeFallPose)
            {
                _freeFallContactAuthorized |=
                    input.CatchPreparationContactAllowed;
            }
            var completedRegripReach =
                _danglingRig.FreeFallReachActive &&
                _danglingRig.FreeFallReachContactSafe &&
                _danglingRig.FreeFallReachProgress >=
                1f - RegripCompletionEpsilon &&
                _danglingRig.FreeFallReachTargetError <=
                RegripPoseController.ContactTolerance;
            Span<Vector2> regripTargets =
                stackalloc Vector2[DanglingTopology2D.LegCount];
            _danglingRig.FreeFallReachTargets.CopyTo(regripTargets);
            _danglingRig.End();
            for (var i = 0; i < Spine.Joints.Count; i++)
            {
                _releaseSpinePose[i] = Spine.Joints[i];
            }
            for (var i = 0; i < _legs.Length; i++)
            {
                _releaseElbows[i] = _legs[i].Elbow;
                _releaseFeet[i] = _legs[i].Foot;
            }
            _heading = Spine.Angles[0];
            forward = MathEx.FromAngle(_heading);
            if (endingFreeFallPose &&
                _freeFallContactAuthorized &&
                completedRegripReach &&
                input.PoseMode == LizardPoseMode.Regrip)
            {
                var contactBegan = _regripPoseController.BeginContact(
                    Spine,
                    _legs,
                    regripTargets);
                _releasePoseBlend = contactBegan ? 0f : 1f;
            }
            else
            {
                _regripPoseController.Reset();
                _releasePoseBlend = 1f;
            }
            _freeFallContactAuthorized = false;
            _gaitController.ResetSupportState();
        }
        else if (input.PoseMode != LizardPoseMode.Regrip &&
                 _regripPoseController.HasContactPose)
        {
            _regripPoseController.Reset();
        }

        var headTarget = Spine.Joints[0] + locomotionDelta;
        Spine.Resolve(headTarget);
        var visualHeadAnchor = _canvasCenter + forward * _appearance.HeadAnchorOffset;
        Spine.Translate(visualHeadAnchor - Spine.Joints[0]);

        var regripRecoveryActive =
            input.PoseMode == LizardPoseMode.Regrip &&
            _regripPoseController.HasContactPose;
        var recoveringFromGrab = _releasePoseBlend > 0f;
        var recoveryT = regripRecoveryActive
            ? _regripPoseController.PrepareRecovery(
                input.DropProgress,
                _physics.RegripContactHoldFraction)
            : 1f - SmoothStep(_releasePoseBlend);
        if (regripRecoveryActive)
        {
            _regripPoseController.ApplySpineBlend(Spine, recoveryT);
        }
        else if (recoveringFromGrab)
        {
            for (var i = 0; i < Spine.Joints.Count; i++)
            {
                _releaseBlendPose[i] = Vector2.Lerp(_releaseSpinePose[i], Spine.Joints[i], recoveryT);
            }
            Spine.SetPose(_releaseBlendPose);
        }

        UpdateLegShoulders();
        // Preserve the planted desktop position unless an extreme body turn
        // would make the two-link leg physically impossible. In that rare case
        // project the paw onto the reach boundary instead of drawing a spoke.
        if (regripRecoveryActive || recoveringFromGrab)
        {
            foreach (var leg in _legs)
            {
                var targetFoot = GetFootHome(leg, 0f);
                leg.ForceFoot(targetFoot);
                var targetElbow = leg.Elbow;
                if (regripRecoveryActive)
                {
                    _regripPoseController.ApplyLegBlend(
                        leg,
                        targetElbow,
                        targetFoot,
                        recoveryT);
                }
                else
                {
                    leg.SetDanglingPose(
                        leg.Shoulder,
                        Vector2.Lerp(_releaseElbows[leg.Index], targetElbow, recoveryT),
                        Vector2.Lerp(_releaseFeet[leg.Index], targetFoot, recoveryT));
                }
            }
            if (regripRecoveryActive)
            {
                _regripPoseController.UpdateContactMetrics(Spine, _legs);
            }
            else
            {
                _releasePoseBlend = Math.Max(
                    0f,
                    _releasePoseBlend - dt / _physics.ReleasePoseRecoveryDuration);
            }
        }
        else
        {
            var locomotionSpeed = dt > 0.0001f ? input.ScreenDeltaModel.Length() / dt : 0f;
            var signedTurnRate = dt > 0.0001f ? signedTurnAmount / dt : 0f;
            _gaitController.Update(
                dt,
                input.NormalizedSpeed,
                walking,
                input.PoseMode is LizardPoseMode.ReleaseSettle or LizardPoseMode.Regrip,
                input.PoseMode == LizardPoseMode.FastSCurve,
                locomotionSpeed,
                signedTurnRate);
            // Let the gait scheduler rescue an aging footprint before applying
            // the hard geometry guard. A paw that starts its reference step on
            // this frame must not first be slid along the desktop.
            foreach (var leg in _legs)
            {
                if (!leg.IsStepping)
                {
                    leg.ConstrainToReach();
                }
            }
        }

        // Idle life is deliberately render-only. The controller below never
        // feeds its offsets back into Chain.Resolve or the planted-foot IK.
        _secondaryMotionController.Update(
            dt,
            input.NormalizedSpeed,
            input.PoseMode,
            input.Emotion,
            input.DropProgress,
            walking);
    }

    public float GetBodyWidth(int index) => _bodyWidths[index];

    public Vector2 GetBodyPoint(int index, float side, float lengthOffset = 0f)
    {
        var angle = Spine.Angles[index] + MathF.PI * 0.5f * side;
        return Spine.Joints[index] + MathEx.FromAngle(angle) * (_bodyWidths[index] + lengthOffset);
    }

    public Vector2 GetVisualBodyPoint(int index, float side, float lengthOffset = 0f)
    {
        var forward = MathEx.FromAngle(Spine.Angles[index]);
        var sideNormal = MathEx.Perpendicular(forward) * side;

        // Breathing is concentrated through the ribcage and fades before the
        // tail. The physical shoulder locations keep using GetBodyPoint.
        var chestWeight = index <= _secondaryMotion.BreathingChestEndIndex
            ? MathF.Sin(
                MathF.PI * (index + 1f) / _secondaryMotion.BreathingChestSpan)
            : 0f;
        var radiusScale = 1f + (BodyWidthScale - 1f) * chestWeight;
        var point = Spine.Joints[index] + sideNormal * ((_bodyWidths[index] + lengthOffset) * radiusScale);

        var tailStart = Math.Min(
            _secondaryMotion.TailStartBodyIndex,
            _bodyWidths.Length - 2f);
        var tailSpan = _bodyWidths.Length - 1f - tailStart;
        var tailWeight = MathEx.Clamp01((index - tailStart) / tailSpan);
        point += MathEx.Perpendicular(forward) * (TailSwayOffset * tailWeight * tailWeight);
        return point;
    }

    public Vector2 GetEyeCenter(int side)
    {
        var forward = MathEx.FromAngle(Spine.Angles[0]);
        var normal = MathEx.Perpendicular(forward);
        return Spine.Joints[0] +
               forward * _secondaryMotion.EyeForwardOffset +
               normal * (side * _secondaryMotion.EyeLateralOffset) +
               normal * (BodyBob * _secondaryMotion.EyeBobFactor);
    }

    public Vector2 GetHeadNose()
    {
        return Spine.Joints[0] +
               MathEx.FromAngle(Spine.Angles[0]) * _secondaryMotion.NoseForwardOffset;
    }

    private void ResetLegs()
    {
        _gaitController.ResetTracking();
        UpdateLegShoulders();
        foreach (var leg in _legs)
        {
            leg.Reset(leg.Shoulder, GetFootHome(leg, 0f));
        }
    }

    private void UpdateLegShoulders()
    {
        foreach (var leg in _legs)
        {
            var bodyWidth = _bodyWidths[leg.BodyIndex];
            var shoulder = GetBodyPoint(
                leg.BodyIndex,
                leg.Side,
                -bodyWidth * _gait.ShoulderInsetFactor);
            leg.SetShoulder(shoulder, MathEx.FromAngle(Spine.Angles[leg.BodyIndex]));
        }
    }

    private Vector2 GetFootHome(LegRig leg, float lead)
    {
        var forward = MathEx.FromAngle(Spine.Angles[leg.BodyIndex]);
        var normal = MathEx.Perpendicular(forward) * leg.Side;
        var lateralReach = leg.IsFront ? _gait.FrontLateralReach : _gait.RearLateralReach;
        var stanceSkew = _gait.StanceSkew[leg.Index];
        var longitudinal = (leg.IsFront
            ? _gait.FrontLongitudinalBase
            : _gait.RearLongitudinalBase) + stanceSkew;
        return Spine.Joints[leg.BodyIndex] +
               normal * (_bodyWidths[leg.BodyIndex] + lateralReach) +
               forward * (longitudinal + lead);
    }

    private float GetLegBodyAngle(LegRig leg) => Spine.Angles[leg.BodyIndex];

    /// <summary>
    /// Free fall is deliberately unpinned, while its common local translation
    /// is stabilized by the particle rig. Keep this independent drawable-AABB
    /// guard as a last resort for extreme relative articulation. A rigid
    /// correction prevents clipping without altering particle velocity.
    /// </summary>
    private Vector2 KeepParticlePoseInsideRenderCanvas()
    {
        var minimum = new Vector2(float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity);
        var shadowOffset = new Vector2(
            _rendering.ShadowOffsetX,
            _rendering.ShadowOffsetY);

        void Include(Vector2 point, float radius)
        {
            var extent = new Vector2(radius + RasterContainmentMargin);
            minimum = Vector2.Min(minimum, point - extent);
            maximum = Vector2.Max(maximum, point + extent);
        }

        void IncludeWithShadow(Vector2 point, float radius, float shadowRadius)
        {
            Include(point, radius);
            Include(point + shadowOffset, shadowRadius);
        }

        for (var index = 0; index < Spine.Joints.Count; index++)
        {
            IncludeWithShadow(
                GetVisualBodyPoint(index, 1f),
                0f,
                0f);
            IncludeWithShadow(
                GetVisualBodyPoint(index, -1f),
                0f,
                0f);
        }

        IncludeWithShadow(
            GetHeadNose(),
            _rendering.NoseRadius,
            _rendering.NoseRadius);
        IncludeWithShadow(
            GetEyeCenter(-1),
            _rendering.EyeRadius,
            _rendering.EyeShadowRadius);
        IncludeWithShadow(
            GetEyeCenter(1),
            _rendering.EyeRadius,
            _rendering.EyeShadowRadius);

        var limbRadius = _appearance.LimbWidth * 0.5f;
        var shadowLimbRadius = _appearance.ShadowLimbWidth * 0.5f;
        foreach (var leg in _legs)
        {
            IncludeWithShadow(leg.Shoulder, limbRadius, shadowLimbRadius);
            IncludeWithShadow(leg.Elbow, limbRadius, shadowLimbRadius);
            var footRadius = leg.IsFront
                ? _rendering.FrontFootRadius
                : _rendering.RearFootRadius;
            IncludeWithShadow(leg.Foot, footRadius, footRadius);
        }

        var canvasSize = _appearance.RenderCanvasSize;
        var correction = new Vector2(
            GetContainmentCorrection(minimum.X, maximum.X, canvasSize),
            GetContainmentCorrection(minimum.Y, maximum.Y, canvasSize));
        if (correction.LengthSquared() > 0.000001f)
        {
            _danglingRig.TranslateActivePose(this, correction);
        }
        return correction;
    }

    private static float GetContainmentCorrection(
        float minimum,
        float maximum,
        float canvasSize)
    {
        if (maximum - minimum > canvasSize)
        {
            return canvasSize * 0.5f - (minimum + maximum) * 0.5f;
        }
        if (minimum < 0f)
        {
            return -minimum;
        }
        return maximum > canvasSize ? canvasSize - maximum : 0f;
    }

    private static float SmoothStep(float value)
    {
        var t = MathEx.Clamp01(value);
        return t * t * (3f - 2f * t);
    }
}
