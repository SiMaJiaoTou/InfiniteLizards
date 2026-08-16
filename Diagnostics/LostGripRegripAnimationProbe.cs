using System.Numerics;
using DesktopLizard.Core;

namespace DesktopLizard.Diagnostics;

internal readonly record struct LostGripRegripProbeResult(
    bool SeekingSeen,
    bool TerminalReachSeen,
    bool ContactHoldSeen,
    bool DistinctTargetsSeen,
    int ObservedLegMask,
    int OrderingViolations,
    int DirectionViolations,
    int IkBranchViolations,
    int IkBranchTransitions,
    float MinimumIkBranchTransitionStraightness,
    float MaximumIkBranchTransitionElbowJump,
    string WorstIkBranchTransitionDetail,
    float MaximumNonBranchLimbPoseJump,
    string WorstNonBranchLimbPoseJumpDetail,
    int RequiresProjectionLegMask,
    int ProjectedLimbSamples,
    int RollbackLimbSamples,
    int ProjectionContinuityViolations,
    float MaximumProjectionLimbPoseJump,
    string WorstProjectionLimbPoseJumpDetail,
    int BodyClearanceViolations,
    int OwnEnvelopeReentryViolations,
    string WorstBodyClearanceDetail,
    string FirstOwnEnvelopeReentryDetail,
    string[] FirstOwnEnvelopeReentryDetailByLeg,
    float[] MinimumNonOwnedBodyClearanceByLeg,
    int[] OwnEnvelopeReentriesByLeg,
    int ContactStopViolations,
    int NonZeroSeekingSteps,
    float BehaviorReachPeak,
    float TargetErrorReduction,
    float MinimumFootUpwardTravel,
    float MinimumPawCatchDirectionTravel,
    float MinimumArmRadiusRetention,
    float FirstVisibleArmRetraction,
    float MinimumFrontHeadwardTargetTravel,
    float MinimumRearTailwardTargetTravel,
    float MinimumTargetSeparation,
    float MinimumNonOwnedBodyClearance,
    float ReachEntryPoseJump,
    float TerminalReachError,
    float ContactError,
    float ContactSpineDrift,
    float PostContactWindowDrift,
    float PostContactVisualCentroidDrift,
    int RecordedLostGripSteps,
    int MovingVisiblePawSteps,
    int FirstVisiblePawMotionStep,
    int FirstStationaryStep,
    int VisiblePawMotionLeadSteps,
    int MovingVisiblePawDisplayFrames60Hz,
    int VisiblePawMotionLeadDisplayFrames60Hz,
    int PreStopPawMotionViolations,
    string MotionTimingDetail,
    LostGripMotionStepSample[] MotionTrace);

/// <summary>
/// One fixed-step sample of the exact four-paw pose handed to rendering. Paw
/// displacement is shoulder-relative so host travel cannot masquerade as a
/// reach; catch advance is projected toward each paw's independent target.
/// </summary>
internal readonly record struct LostGripMotionStepSample(
    int Step,
    LostGripFallPhase BehaviorPhase,
    RegripAnimationPhase AnimationPhase,
    float WindowDeltaY,
    float[] PawRelativeDisplacements,
    float[] PawCatchAdvances,
    bool CatchTargetsAvailable);

/// <summary>
/// Observes Seeking -> ContactHold -> Recovering without reaching into mutable
/// controller state. A pass requires all four limbs to move before the host
/// stops, retain valid IK geometry, and atomically contact four distinct points.
/// </summary>
internal sealed class LostGripRegripAnimationProbe
{
    private const int LegCount = DanglingTopology2D.LegCount;
    private const int AllLegContactMask = (1 << LegCount) - 1;
    private const float ProgressEpsilon = 0.0001f;
    private const float StationaryEpsilon = 0.001f;
    private const float VisiblePawMotionScreenPixels = 0.5f;
    private const float MinimumRemoteBodyClearance = 0f;
    internal const float MinimumBranchTransitionStraightness = 0.99f;
    internal const float MaximumBranchTransitionElbowJump = 0.75f;
    internal const float MaximumProjectionLimbPoseJump = 4f;

    private readonly Vector2[] _startShoulders = new Vector2[LegCount];
    private readonly Vector2[] _startFeet = new Vector2[LegCount];
    private readonly Vector2[] _startTargets = new Vector2[LegCount];
    private readonly float[] _startTargetErrors = new float[LegCount];
    private readonly Vector2[] _startRenderedPawOffsets = new Vector2[LegCount];
    private readonly float[] _capturedBendSigns = new float[LegCount];
    private readonly float[] _lastNonZeroBendSigns = new float[LegCount];
    private readonly float[] _capturedArmLengths = new float[LegCount];
    private readonly float[] _previousArmStraightness = new float[LegCount];
    private readonly int[] _ikBranchTransitionsByLeg = new int[LegCount];
    private readonly float[] _previousReachAngles = new float[LegCount];
    private readonly float[] _cumulativeReachAngularTravel = new float[LegCount];
    private readonly float[] _previousReachTurnSigns = new float[LegCount];
    private readonly int[] _reachDirectionChanges = new int[LegCount];
    private readonly Vector2[] _terminalTargets = new Vector2[LegCount];
    private readonly List<LostGripMotionStepSample> _motionTrace = [];
    private readonly VisibleDisplayCadenceTracker[] _displayCadenceTrackers;
    private readonly string _context;
    private readonly float[] _minimumNonOwnedBodyClearanceByLeg =
        new float[LegCount];
    private readonly int[] _ownEnvelopeReentriesByLeg = new int[LegCount];

    private Vector2 _previousWindowPosition;
    private JointPose _previousPose;
    private RenderedPaws _previousRenderedPaws;
    private RegripAnimationPhase _previousAnimationPhase;
    private float _previousBehaviorReachProgress;
    private float _previousAnimationReachProgress;
    private bool _hasBehaviorReachSample;
    private bool _seekingSeen;
    private bool _terminalReachSeen;
    private bool _contactHoldSeen;
    private bool _distinctTargetsSeen;
    private int _observedLegMask;
    private Vector2 _terminalWindowPosition;
    private Vector2 _contactWindowPosition;
    private Vector2 _contactVisualCenter;
    private int _orderingViolations;
    private int _directionViolations;
    private int _ikBranchViolations;
    private int _ikBranchTransitions;
    private float _minimumIkBranchTransitionStraightness =
        float.PositiveInfinity;
    private float _maximumIkBranchTransitionElbowJump;
    private string _worstIkBranchTransitionDetail = "none";
    private float _maximumNonBranchLimbPoseJump;
    private string _worstNonBranchLimbPoseJumpDetail = "none";
    private int _requiresProjectionLegMask;
    private int _projectedLimbSamples;
    private int _rollbackLimbSamples;
    private int _projectionContinuityViolations;
    private float _maximumProjectionLimbPoseJump;
    private string _worstProjectionLimbPoseJumpDetail = "none";
    private int _bodyClearanceViolations;
    private int _ownEnvelopeReentryViolations;
    private string _worstBodyClearanceDetail = "none";
    private string _firstOwnEnvelopeReentryDetail = "none";
    private readonly string[] _firstOwnEnvelopeReentryDetailByLeg =
        Enumerable.Repeat("none", LegCount).ToArray();
    private int _contactStopViolations;
    private int _nonZeroSeekingSteps;
    private float _behaviorReachPeak;
    private float _targetErrorReduction;
    private float _minimumFootUpwardTravel;
    private float _minimumPawCatchDirectionTravel;
    private float _minimumArmRadiusRetention = 1f;
    private float _firstVisibleArmRetraction;
    private bool _firstVisibleReachSampled;
    private float _minimumFrontHeadwardTargetTravel = float.PositiveInfinity;
    private float _minimumRearTailwardTargetTravel = float.PositiveInfinity;
    private float _minimumTargetSeparation = float.PositiveInfinity;
    private float _minimumNonOwnedBodyClearance = float.PositiveInfinity;
    private float _reachEntryPoseJump;
    private float _terminalReachError;
    private float _contactError;
    private float _contactSpineDrift;
    private float _postContactWindowDrift;
    private float _postContactVisualCentroidDrift;
    private int _lostGripStep;
    private int _movingVisiblePawSteps;
    private int _firstVisiblePawMotionStep = -1;
    private int _firstStationaryStep = -1;

    public LostGripRegripAnimationProbe(
        Vector2 initialWindowPosition,
        ProceduralLizard lizard,
        string context = "unspecified")
    {
        _context = context;
        Array.Fill(
            _minimumNonOwnedBodyClearanceByLeg,
            float.PositiveInfinity);
        _previousWindowPosition = initialWindowPosition;
        _previousPose = JointPose.Capture(lizard);
        _previousRenderedPaws = RenderedPaws.Capture(lizard.CaptureRenderFrame());
        _displayCadenceTrackers =
        [
            new VisibleDisplayCadenceTracker(0),
            new VisibleDisplayCadenceTracker(1)
        ];
    }

    public void Observe(
        BehaviorController behavior,
        ProceduralLizard lizard,
        Vector2 visualCenter,
        LizardRenderFrame renderFrame)
    {
        var pose = JointPose.Capture(lizard);
        if (behavior.LostGripPhase == LostGripFallPhase.Falling)
        {
            var behaviorReach = behavior.LostGripReachProgress;
            if (_hasBehaviorReachSample &&
                behaviorReach + ProgressEpsilon < _previousBehaviorReachProgress)
            {
                _orderingViolations++;
            }
            _hasBehaviorReachSample = true;
            _previousBehaviorReachProgress = behaviorReach;
            _behaviorReachPeak = Math.Max(_behaviorReachPeak, behaviorReach);
        }

        switch (lizard.RegripAnimationPhase)
        {
            case RegripAnimationPhase.None:
                break;
            case RegripAnimationPhase.Seeking:
                ObserveSeeking(behavior, lizard, pose);
                break;
            case RegripAnimationPhase.ContactHold:
                ObserveContactHold(behavior, lizard, visualCenter);
                break;
            case RegripAnimationPhase.Recovering:
                ObserveRecovering(behavior, lizard, visualCenter);
                break;
            default:
                _orderingViolations++;
                break;
        }

        ObserveFixedStepMotion(behavior, lizard, renderFrame);

        _previousWindowPosition = behavior.Position;
        _previousPose = pose;
        _previousRenderedPaws = RenderedPaws.Capture(renderFrame);
        _previousAnimationPhase = lizard.RegripAnimationPhase;
    }

    public LostGripRegripProbeResult Complete()
    {
        var leadSteps =
            _firstVisiblePawMotionStep >= 0 && _firstStationaryStep >= 0
                ? _firstStationaryStep - _firstVisiblePawMotionStep
                : 0;
        var cadenceResults = _displayCadenceTrackers
            .Select(tracker => tracker.Complete())
            .ToArray();
        var movingDisplayFrames = cadenceResults.Min(
            result => result.MovingVisibleReachFrames);
        var displayLeadFrames = cadenceResults.Min(
            result => result.VisibleReachLeadFrames);
        var preStopPawMotionViolations = 0;
        if (_seekingSeen)
        {
            if (_movingVisiblePawSteps < 2)
            {
                preStopPawMotionViolations++;
            }
            if (movingDisplayFrames < 6 || displayLeadFrames < 6)
            {
                preStopPawMotionViolations++;
            }
            if (_firstVisiblePawMotionStep < 0 ||
                _firstStationaryStep < 0 ||
                _firstVisiblePawMotionStep >= _firstStationaryStep)
            {
                preStopPawMotionViolations++;
            }
        }

        return new LostGripRegripProbeResult(
            _seekingSeen,
            _terminalReachSeen,
            _contactHoldSeen,
            _distinctTargetsSeen,
            _observedLegMask,
            _orderingViolations,
            _directionViolations,
            _ikBranchViolations,
            _ikBranchTransitions,
            FiniteOrZero(_minimumIkBranchTransitionStraightness),
            _maximumIkBranchTransitionElbowJump,
            _worstIkBranchTransitionDetail,
            _maximumNonBranchLimbPoseJump,
            _worstNonBranchLimbPoseJumpDetail,
            _requiresProjectionLegMask,
            _projectedLimbSamples,
            _rollbackLimbSamples,
            _projectionContinuityViolations,
            _maximumProjectionLimbPoseJump,
            _worstProjectionLimbPoseJumpDetail,
            _bodyClearanceViolations,
            _ownEnvelopeReentryViolations,
            _worstBodyClearanceDetail,
            _firstOwnEnvelopeReentryDetail,
            _firstOwnEnvelopeReentryDetailByLeg.ToArray(),
            _minimumNonOwnedBodyClearanceByLeg
                .Select(FiniteOrZero)
                .ToArray(),
            _ownEnvelopeReentriesByLeg.ToArray(),
            _contactStopViolations,
            _nonZeroSeekingSteps,
            _behaviorReachPeak,
            _targetErrorReduction,
            _minimumFootUpwardTravel,
            _minimumPawCatchDirectionTravel,
            _minimumArmRadiusRetention,
            _firstVisibleArmRetraction,
            FiniteOrZero(_minimumFrontHeadwardTargetTravel),
            FiniteOrZero(_minimumRearTailwardTargetTravel),
            FiniteOrZero(_minimumTargetSeparation),
            FiniteOrZero(_minimumNonOwnedBodyClearance),
            _reachEntryPoseJump,
            _terminalReachError,
            _contactError,
            _contactSpineDrift,
            _postContactWindowDrift,
            _postContactVisualCentroidDrift,
            _motionTrace.Count,
            _movingVisiblePawSteps,
            _firstVisiblePawMotionStep,
            _firstStationaryStep,
            leadSteps,
            movingDisplayFrames,
            displayLeadFrames,
            preStopPawMotionViolations,
            $"{_context},fixed-moving={_movingVisiblePawSteps}," +
            $"first-motion={_firstVisiblePawMotionStep}," +
            $"first-stop={_firstStationaryStep}," +
            $"60hz-moving={cadenceResults[0].MovingVisibleReachFrames}/" +
            $"{cadenceResults[1].MovingVisibleReachFrames}," +
            $"60hz-lead={cadenceResults[0].VisibleReachLeadFrames}/" +
            $"{cadenceResults[1].VisibleReachLeadFrames}," +
            $"first-retraction={_firstVisibleArmRetraction:F3}," +
            $"geometry={_bodyClearanceViolations}/" +
            $"{_ownEnvelopeReentryViolations}/" +
            $"{FiniteOrZero(_minimumNonOwnedBodyClearance):F3}," +
            $"paths={DescribeReachPaths()}",
            _motionTrace.ToArray());
    }

    private void ObserveFixedStepMotion(
        BehaviorController behavior,
        ProceduralLizard lizard,
        LizardRenderFrame renderFrame)
    {
        if (behavior.State != RoamingState.LostGripFall)
        {
            return;
        }

        var renderedPaws = RenderedPaws.Capture(renderFrame);
        var windowDeltaY = behavior.Position.Y - _previousWindowPosition.Y;
        var targetAvailable =
            lizard.RegripAnimationPhase == RegripAnimationPhase.Seeking;
        var relativeDisplacements = new float[LegCount];
        var catchAdvances = new float[LegCount];

        for (var index = 0; index < LegCount; index++)
        {
            var previousRelative =
                _previousRenderedPaws.Feet[index] -
                _previousRenderedPaws.Shoulders[index];
            var currentRelative =
                renderedPaws.Feet[index] - renderedPaws.Shoulders[index];
            var relativeDelta = currentRelative - previousRelative;
            relativeDisplacements[index] = relativeDelta.Length();

            if (!targetAvailable)
            {
                continue;
            }

            var targetRelative =
                ContactTarget(lizard, index) - renderedPaws.Shoulders[index];
            var catchDirection = MathEx.SafeNormalize(
                targetRelative - previousRelative,
                -Vector2.UnitY);
            catchAdvances[index] = Vector2.Dot(relativeDelta, catchDirection);
        }

        _motionTrace.Add(new LostGripMotionStepSample(
            _lostGripStep,
            behavior.LostGripPhase,
            lizard.RegripAnimationPhase,
            windowDeltaY,
            relativeDisplacements,
            catchAdvances,
            targetAvailable));

        foreach (var tracker in _displayCadenceTrackers)
        {
            tracker.Observe(_lostGripStep, behavior, lizard, renderedPaws);
        }

        if (_seekingSeen &&
            _firstStationaryStep < 0 &&
            windowDeltaY <= StationaryEpsilon)
        {
            _firstStationaryStep = _lostGripStep;
        }

        if (targetAvailable)
        {
            var visualScale = lizard.Profile.Appearance.VisualScale;
            if (!_firstVisibleReachSampled)
            {
                var allPawsVisiblyDeparted = true;
                var maximumRetraction = 0f;
                for (var index = 0; index < LegCount; index++)
                {
                    var currentOffset =
                        renderedPaws.Feet[index] - renderedPaws.Shoulders[index];
                    var visibleDeparture = Vector2.Distance(
                        _startRenderedPawOffsets[index],
                        currentOffset) * visualScale;
                    allPawsVisiblyDeparted &=
                        visibleDeparture >= VisiblePawMotionScreenPixels;
                    maximumRetraction = Math.Max(
                        maximumRetraction,
                        (_startRenderedPawOffsets[index].Length() -
                         currentOffset.Length()) * visualScale);
                }
                if (allPawsVisiblyDeparted)
                {
                    _firstVisibleReachSampled = true;
                    _firstVisibleArmRetraction = Math.Max(0f, maximumRetraction);
                }
            }

            var allPawsVisiblyAdvance =
                relativeDisplacements.Min() * visualScale >=
                    VisiblePawMotionScreenPixels &&
                catchAdvances.Min() * visualScale >=
                    VisiblePawMotionScreenPixels;
            if (allPawsVisiblyAdvance)
            {
                if (_firstVisiblePawMotionStep < 0)
                {
                    _firstVisiblePawMotionStep = _lostGripStep;
                }
                if (windowDeltaY > StationaryEpsilon)
                {
                    _movingVisiblePawSteps++;
                }
            }
        }

        _lostGripStep++;
    }

    private void ObserveSeeking(
        BehaviorController behavior,
        ProceduralLizard lizard,
        JointPose pose)
    {
        if (!_seekingSeen)
        {
            BeginSeeking(lizard, pose);
        }

        ObserveSeekingGeometry(lizard);

        if (!lizard.RegripReachActive ||
            lizard.CurrentPoseMode != LizardPoseMode.FreeFall)
        {
            _orderingViolations++;
        }
        if (lizard.RegripReachProgress + ProgressEpsilon <
            _previousAnimationReachProgress)
        {
            _orderingViolations++;
        }
        _previousAnimationReachProgress = lizard.RegripReachProgress;
        if (lizard.RegripReachProgress > ProgressEpsilon)
        {
            _nonZeroSeekingSteps++;
        }

        var movedDown = behavior.Position.Y >
                        _previousWindowPosition.Y + StationaryEpsilon;
        if (lizard.RegripReachProgress < 1f - ProgressEpsilon)
        {
            if (behavior.LostGripPhase != LostGripFallPhase.Falling ||
                behavior.LostGripVerticalVelocity <= 0f ||
                !movedDown ||
                lizard.RegripContacted ||
                lizard.RegripContactLegMask != 0)
            {
                _contactStopViolations++;
            }
            return;
        }

        if (!_terminalReachSeen)
        {
            CaptureTerminalReach(behavior, lizard);
        }

        // The final nonzero host delta commits all four contacts atomically.
        // The next fixed step is the first stationary ContactHold frame.
        if (behavior.LostGripPhase != LostGripFallPhase.Regripping ||
            behavior.LostGripVerticalVelocity != 0f ||
            !movedDown ||
            lizard.RegripReachProgress < 1f - ProgressEpsilon ||
            !lizard.RegripContacted ||
            lizard.RegripContactLegMask != AllLegContactMask ||
            MaximumTargetError(lizard) > RegripPoseController.ContactTolerance)
        {
            _contactStopViolations++;
        }
    }

    private void BeginSeeking(ProceduralLizard lizard, JointPose pose)
    {
        _seekingSeen = true;
        _reachEntryPoseJump = _previousPose.MaximumDistance(pose);
        for (var index = 0; index < LegCount; index++)
        {
            var leg = lizard.Legs[index];
            var bodyIndex = DanglingTopology2D.GetBodyIndex(index);
            var target = ContactTarget(lizard, index);
            _startShoulders[index] = leg.Shoulder;
            _startFeet[index] = leg.Foot;
            _startTargets[index] = target;
            _startTargetErrors[index] = Vector2.Distance(leg.Foot, target);
            _startRenderedPawOffsets[index] = leg.Foot - leg.Shoulder;
            _previousReachAngles[index] = MathF.Atan2(
                _startRenderedPawOffsets[index].Y,
                _startRenderedPawOffsets[index].X);
            _observedLegMask |= 1 << index;

            var shoulderToFoot = MathEx.SafeNormalize(
                leg.Foot - leg.Shoulder,
                -Vector2.UnitY);
            var bend = Vector2.Dot(
                leg.Elbow - leg.Shoulder,
                MathEx.Perpendicular(shoulderToFoot));
            _capturedBendSigns[index] = MathF.Abs(bend) > ProgressEpsilon
                ? MathF.Sign(bend)
                : DanglingTopology2D.GetSide(index);
            _lastNonZeroBendSigns[index] = _capturedBendSigns[index];
            _capturedArmLengths[index] =
                Vector2.Distance(leg.Shoulder, leg.Elbow) +
                Vector2.Distance(leg.Elbow, leg.Foot);
            _previousArmStraightness[index] = ArmStraightness(
                leg,
                _capturedArmLengths[index]);

            var headward = MathEx.SafeNormalize(
                lizard.Spine.Joints[Math.Max(0, bodyIndex - 1)] -
                lizard.Spine.Joints[bodyIndex],
                Vector2.UnitX);
            var targetOffset = target - leg.Shoulder;
            if (index < 2)
            {
                _minimumFrontHeadwardTargetTravel = Math.Min(
                    _minimumFrontHeadwardTargetTravel,
                    Vector2.Dot(targetOffset, headward));
            }
            else
            {
                _minimumRearTailwardTargetTravel = Math.Min(
                    _minimumRearTailwardTargetTravel,
                    Vector2.Dot(targetOffset, -headward));
            }
            if (target.Y >= leg.Shoulder.Y - ProgressEpsilon)
            {
                _directionViolations++;
            }
        }

        for (var first = 0; first < LegCount; first++)
        {
            for (var second = first + 1; second < LegCount; second++)
            {
                _minimumTargetSeparation = Math.Min(
                    _minimumTargetSeparation,
                    Vector2.Distance(_startTargets[first], _startTargets[second]));
            }
        }
        _distinctTargetsSeen = _minimumTargetSeparation > 1f;
    }

    private void ObserveSeekingGeometry(ProceduralLizard lizard)
    {
        _requiresProjectionLegMask |=
            lizard.RegripReachRequiresProjectionLegMask;
        var projectedMask = lizard.RegripReachProjectedLegMaskThisStep;
        var rollbackMask = lizard.RegripReachRollbackLegMaskThisStep;
        _projectedLimbSamples += BitOperations.PopCount(
            (uint)(projectedMask & AllLegContactMask));
        _rollbackLimbSamples += BitOperations.PopCount(
            (uint)(rollbackMask & AllLegContactMask));
        for (var index = 0; index < LegCount; index++)
        {
            var leg = lizard.Legs[index];
            var currentOffset = leg.Foot - leg.Shoulder;
            var currentAngle = MathF.Atan2(currentOffset.Y, currentOffset.X);
            var angleDelta = MathEx.DeltaAngle(
                _previousReachAngles[index],
                currentAngle);
            if (MathF.Abs(angleDelta) > ProgressEpsilon)
            {
                var turnSign = MathF.Sign(angleDelta);
                if (_previousReachTurnSigns[index] != 0f &&
                    turnSign != _previousReachTurnSigns[index])
                {
                    _reachDirectionChanges[index]++;
                }
                _previousReachTurnSigns[index] = turnSign;
                _cumulativeReachAngularTravel[index] += MathF.Abs(angleDelta);
            }
            _previousReachAngles[index] = currentAngle;
            var startRadius = Vector2.Distance(
                _startFeet[index],
                _startShoulders[index]);
            var currentRadius = Vector2.Distance(leg.Foot, leg.Shoulder);
            if (startRadius > ProgressEpsilon)
            {
                _minimumArmRadiusRetention = Math.Min(
                    _minimumArmRadiusRetention,
                    currentRadius / startRadius);
            }

            var shoulderToFoot = MathEx.SafeNormalize(
                leg.Foot - leg.Shoulder,
                -Vector2.UnitY);
            var bendHeight = Vector2.Dot(
                leg.Elbow - leg.Shoulder,
                MathEx.Perpendicular(shoulderToFoot));
            var currentBendSign = MathF.Abs(bendHeight) > ProgressEpsilon
                ? MathF.Sign(bendHeight)
                : 0f;
            var currentStraightness = ArmStraightness(
                leg,
                _capturedArmLengths[index]);
            var isBranchTransition =
                currentBendSign != 0f &&
                currentBendSign != _lastNonZeroBendSigns[index];
            var previousShoulder = _previousPose.Shoulders[index];
            var elbowPoseJump = Vector2.Distance(
                _previousPose.Elbows[index] - previousShoulder,
                leg.Elbow - leg.Shoulder);
            var footPoseJump = Vector2.Distance(
                _previousPose.Feet[index] - previousShoulder,
                leg.Foot - leg.Shoulder);
            var limbPoseJump = Math.Max(elbowPoseJump, footPoseJump);
            if (!isBranchTransition)
            {
                if (limbPoseJump > _maximumNonBranchLimbPoseJump)
                {
                    _maximumNonBranchLimbPoseJump = limbPoseJump;
                    _worstNonBranchLimbPoseJumpDetail =
                        $"{_context},step={_lostGripStep}," +
                        $"progress={lizard.RegripReachProgress:F4}," +
                        $"leg={index},elbow={elbowPoseJump:F4}," +
                        $"foot={footPoseJump:F4}," +
                        $"shoulder={Format(leg.Shoulder)}," +
                        $"elbow-position={Format(leg.Elbow)}," +
                        $"foot-position={Format(leg.Foot)}";
                }
            }
            if ((projectedMask & (1 << index)) != 0)
            {
                if (limbPoseJump > _maximumProjectionLimbPoseJump)
                {
                    _maximumProjectionLimbPoseJump = limbPoseJump;
                    _worstProjectionLimbPoseJumpDetail =
                        $"{_context},step={_lostGripStep}," +
                        $"progress={lizard.RegripReachProgress:F4}," +
                        $"leg={index},elbow={elbowPoseJump:F4}," +
                        $"foot={footPoseJump:F4}," +
                        $"requires={Convert.ToString(_requiresProjectionLegMask, 2)}," +
                        $"projected={Convert.ToString(projectedMask, 2)}," +
                        $"rollback={Convert.ToString(rollbackMask, 2)}";
                }
                if (limbPoseJump >
                    MaximumProjectionLimbPoseJump + ProgressEpsilon)
                {
                    _projectionContinuityViolations++;
                }
            }
            if (isBranchTransition)
            {
                var transitionStraightness = Math.Min(
                    _previousArmStraightness[index],
                    currentStraightness);
                var elbowJump = Vector2.Distance(
                    _previousPose.Elbows[index],
                    leg.Elbow);
                var transitionsBefore = _ikBranchTransitionsByLeg[index];
                var allowed = IsNearStraightIkBranchTransitionAllowed(
                    transitionsBefore,
                    _previousArmStraightness[index],
                    currentStraightness,
                    elbowJump);
                _ikBranchTransitionsByLeg[index]++;
                _ikBranchTransitions++;
                _minimumIkBranchTransitionStraightness = Math.Min(
                    _minimumIkBranchTransitionStraightness,
                    transitionStraightness);
                _maximumIkBranchTransitionElbowJump = Math.Max(
                    _maximumIkBranchTransitionElbowJump,
                    elbowJump);
                if (!allowed)
                {
                    _ikBranchViolations++;
                }
                if (_worstIkBranchTransitionDetail == "none" || !allowed)
                {
                    _worstIkBranchTransitionDetail =
                        $"{_context},step={_lostGripStep}," +
                        $"progress={lizard.RegripReachProgress:F4}," +
                        $"leg={index},switch={transitionsBefore + 1}," +
                        $"straight={transitionStraightness:F5}," +
                        $"elbow-jump={elbowJump:F4},allowed={allowed}," +
                        $"shoulder={Format(leg.Shoulder)}," +
                        $"elbow={Format(leg.Elbow)},foot={Format(leg.Foot)}";
                }
                _lastNonZeroBendSigns[index] = currentBendSign;
            }
            _previousArmStraightness[index] = currentStraightness;

            ObserveNonOwnedBodyClearance(lizard, index);
        }
    }

    internal static bool IsNearStraightIkBranchTransitionAllowed(
        int transitionsBefore,
        float previousStraightness,
        float currentStraightness,
        float elbowJump) =>
        transitionsBefore == 0 &&
        float.IsFinite(previousStraightness) &&
        float.IsFinite(currentStraightness) &&
        float.IsFinite(elbowJump) &&
        Math.Min(previousStraightness, currentStraightness) + ProgressEpsilon >=
            MinimumBranchTransitionStraightness &&
        elbowJump <= MaximumBranchTransitionElbowJump + ProgressEpsilon;

    private static float ArmStraightness(LegRig leg, float armLength) =>
        armLength > ProgressEpsilon
            ? Vector2.Distance(leg.Shoulder, leg.Foot) / armLength
            : 0f;

    private void ObserveNonOwnedBodyClearance(
        ProceduralLizard lizard,
        int legIndex)
    {
        var leg = lizard.Legs[legIndex];
        var bodyIndex = DanglingTopology2D.GetBodyIndex(legIndex);
        var limbRadius = lizard.Profile.Appearance.LimbWidth * 0.5f;
        Span<Vector2> limbStarts = stackalloc Vector2[2]
        {
            leg.Shoulder,
            leg.Elbow
        };
        Span<Vector2> limbEnds = stackalloc Vector2[2]
        {
            leg.Elbow,
            leg.Foot
        };

        for (var limbSegment = 0; limbSegment < limbStarts.Length; limbSegment++)
        {
            for (var spineSegment = 0;
                 spineSegment < lizard.Spine.Joints.Count - 1;
                 spineSegment++)
            {
                // Body capsules overlap farther than one 16 px spine link.
                // Treat the attachment topology neighbourhood as one owned
                // silhouette and gate only genuinely distant body capsules.
                if (Math.Abs(spineSegment - bodyIndex) <= 2)
                {
                    continue;
                }

                var bodyRadius = Math.Max(
                    lizard.GetBodyWidth(spineSegment),
                    lizard.GetBodyWidth(spineSegment + 1));
                var clearance = DistanceBetweenSegments(
                    limbStarts[limbSegment],
                    limbEnds[limbSegment],
                    lizard.Spine.Joints[spineSegment],
                    lizard.Spine.Joints[spineSegment + 1]) -
                    bodyRadius - limbRadius;
                if (clearance < _minimumNonOwnedBodyClearance)
                {
                    _minimumNonOwnedBodyClearance = clearance;
                    _worstBodyClearanceDetail =
                        $"{_context},step={_lostGripStep}," +
                        $"progress={lizard.RegripReachProgress:F4}," +
                        $"leg={legIndex}," +
                        $"limb={limbSegment},spine={spineSegment}," +
                        $"clearance={clearance:F3}," +
                        $"shoulder={Format(leg.Shoulder)}," +
                        $"elbow={Format(leg.Elbow)},foot={Format(leg.Foot)}," +
                        $"target={Format(ContactTarget(lizard, legIndex))}," +
                        $"spineA={Format(lizard.Spine.Joints[spineSegment])}," +
                        $"spineB={Format(lizard.Spine.Joints[spineSegment + 1])}";
                }
                _minimumNonOwnedBodyClearanceByLeg[legIndex] = Math.Min(
                    _minimumNonOwnedBodyClearanceByLeg[legIndex],
                    clearance);
                if (clearance < MinimumRemoteBodyClearance)
                {
                    _bodyClearanceViolations++;
                }
            }

            if (ReentersOwnedEnvelope(
                    lizard,
                    bodyIndex,
                    limbStarts[limbSegment],
                    limbEnds[limbSegment],
                    limbRadius,
                    out var reentrySample,
                    out var reentrySpineSegment,
                    out var reentryPoint))
            {
                _ownEnvelopeReentryViolations++;
                _ownEnvelopeReentriesByLeg[legIndex]++;
                var detail =
                    $"{_context},step={_lostGripStep}," +
                    $"progress={lizard.RegripReachProgress:F4}," +
                    $"leg={legIndex},limb={limbSegment}," +
                    $"sample={reentrySample}," +
                    $"own-spine={reentrySpineSegment}," +
                    $"point={Format(reentryPoint)}," +
                    $"shoulder={Format(leg.Shoulder)}," +
                    $"elbow={Format(leg.Elbow)},foot={Format(leg.Foot)}," +
                    $"target={Format(ContactTarget(lizard, legIndex))}";
                if (_firstOwnEnvelopeReentryDetailByLeg[legIndex] == "none")
                {
                    _firstOwnEnvelopeReentryDetailByLeg[legIndex] = detail;
                }
                if (_firstOwnEnvelopeReentryDetail == "none")
                {
                    _firstOwnEnvelopeReentryDetail = detail;
                }
            }
        }
    }

    internal static bool IsFourLimbBodyGeometrySafe(
        ProceduralLizard lizard,
        out float minimumRemoteClearance,
        out int ownEnvelopeReentries)
    {
        minimumRemoteClearance = float.PositiveInfinity;
        ownEnvelopeReentries = 0;
        var limbRadius = lizard.Profile.Appearance.LimbWidth * 0.5f;
        for (var legIndex = 0; legIndex < LegCount; legIndex++)
        {
            var leg = lizard.Legs[legIndex];
            var bodyIndex = DanglingTopology2D.GetBodyIndex(legIndex);
            for (var limbSegment = 0; limbSegment < 2; limbSegment++)
            {
                var limbStart = limbSegment == 0
                    ? leg.Shoulder
                    : leg.Elbow;
                var limbEnd = limbSegment == 0
                    ? leg.Elbow
                    : leg.Foot;
                for (var spineSegment = 0;
                     spineSegment < lizard.Spine.Joints.Count - 1;
                     spineSegment++)
                {
                    if (Math.Abs(spineSegment - bodyIndex) <= 2)
                    {
                        continue;
                    }
                    var bodyRadius = Math.Max(
                        lizard.GetBodyWidth(spineSegment),
                        lizard.GetBodyWidth(spineSegment + 1));
                    minimumRemoteClearance = Math.Min(
                        minimumRemoteClearance,
                        DistanceBetweenSegments(
                            limbStart,
                            limbEnd,
                            lizard.Spine.Joints[spineSegment],
                            lizard.Spine.Joints[spineSegment + 1]) -
                        bodyRadius - limbRadius);
                }
                if (ReentersOwnedEnvelope(
                        lizard,
                        bodyIndex,
                        limbStart,
                        limbEnd,
                        limbRadius,
                        out _,
                        out _,
                        out _))
                {
                    ownEnvelopeReentries++;
                }
            }
        }
        return minimumRemoteClearance >= MinimumRemoteBodyClearance &&
               ownEnvelopeReentries == 0;
    }

    internal static bool IsLimbBodyGeometrySafe(
        ProceduralLizard lizard,
        int legIndex,
        Vector2 shoulder,
        Vector2 elbow,
        Vector2 foot,
        out float minimumRemoteClearance,
        out int ownEnvelopeReentries)
    {
        minimumRemoteClearance = float.PositiveInfinity;
        ownEnvelopeReentries = 0;
        var bodyIndex = DanglingTopology2D.GetBodyIndex(legIndex);
        var limbRadius = lizard.Profile.Appearance.LimbWidth * 0.5f;
        for (var limbSegment = 0; limbSegment < 2; limbSegment++)
        {
            var limbStart = limbSegment == 0 ? shoulder : elbow;
            var limbEnd = limbSegment == 0 ? elbow : foot;
            for (var spineSegment = 0;
                 spineSegment < lizard.Spine.Joints.Count - 1;
                 spineSegment++)
            {
                if (Math.Abs(spineSegment - bodyIndex) <= 2)
                {
                    continue;
                }
                var bodyRadius = Math.Max(
                    lizard.GetBodyWidth(spineSegment),
                    lizard.GetBodyWidth(spineSegment + 1));
                minimumRemoteClearance = Math.Min(
                    minimumRemoteClearance,
                    DistanceBetweenSegments(
                        limbStart,
                        limbEnd,
                        lizard.Spine.Joints[spineSegment],
                        lizard.Spine.Joints[spineSegment + 1]) -
                    bodyRadius - limbRadius);
            }
            if (ReentersOwnedEnvelope(
                    lizard,
                    bodyIndex,
                    limbStart,
                    limbEnd,
                    limbRadius,
                    out _,
                    out _,
                    out _))
            {
                ownEnvelopeReentries++;
            }
        }
        return minimumRemoteClearance >= MinimumRemoteBodyClearance &&
               ownEnvelopeReentries == 0;
    }

    internal static string DescribeFourLimbBodyGeometry(
        ProceduralLizard lizard) => string.Join(
        "/",
        Enumerable.Range(0, LegCount).Select(legIndex =>
        {
            var leg = lizard.Legs[legIndex];
            var bodyIndex = DanglingTopology2D.GetBodyIndex(legIndex);
            var limbRadius = lizard.Profile.Appearance.LimbWidth * 0.5f;
            var minimumClearance = float.PositiveInfinity;
            var minimumLimbSegment = -1;
            var minimumSpineSegment = -1;
            var reentryDetail = "none";
            for (var limbSegment = 0; limbSegment < 2; limbSegment++)
            {
                var limbStart = limbSegment == 0
                    ? leg.Shoulder
                    : leg.Elbow;
                var limbEnd = limbSegment == 0
                    ? leg.Elbow
                    : leg.Foot;
                for (var spineSegment = 0;
                     spineSegment < lizard.Spine.Joints.Count - 1;
                     spineSegment++)
                {
                    if (Math.Abs(spineSegment - bodyIndex) <= 2)
                    {
                        continue;
                    }
                    var bodyRadius = Math.Max(
                        lizard.GetBodyWidth(spineSegment),
                        lizard.GetBodyWidth(spineSegment + 1));
                    var clearance = DistanceBetweenSegments(
                        limbStart,
                        limbEnd,
                        lizard.Spine.Joints[spineSegment],
                        lizard.Spine.Joints[spineSegment + 1]) -
                        bodyRadius - limbRadius;
                    if (clearance < minimumClearance)
                    {
                        minimumClearance = clearance;
                        minimumLimbSegment = limbSegment;
                        minimumSpineSegment = spineSegment;
                    }
                }
                if (ReentersOwnedEnvelope(
                        lizard,
                        bodyIndex,
                        limbStart,
                        limbEnd,
                        limbRadius,
                        out var sample,
                        out var reentrySpineSegment,
                        out var point))
                {
                    reentryDetail =
                        $"limb{limbSegment}:sample{sample}:" +
                        $"spine{reentrySpineSegment}:point{Format(point)}";
                }
            }
            return
                $"L{legIndex}:c{minimumClearance:F3}:" +
                $"limb{minimumLimbSegment}:spine{minimumSpineSegment}:" +
                $"reentry={reentryDetail}:S{Format(leg.Shoulder)}:" +
                $"E{Format(leg.Elbow)}:F{Format(leg.Foot)}";
        }));

    private static bool ReentersOwnedEnvelope(
        ProceduralLizard lizard,
        int bodyIndex,
        Vector2 limbStart,
        Vector2 limbEnd,
        float limbRadius,
        out int reentrySample,
        out int reentrySpineSegment,
        out Vector2 reentryPoint)
    {
        const int SampleCount = 24;
        var hasLeftEnvelope = false;
        for (var sample = 0; sample <= SampleCount; sample++)
        {
            var point = Vector2.Lerp(
                limbStart,
                limbEnd,
                sample / (float)SampleCount);
            var inside = false;
            var insideSpineSegment = -1;
            for (var spineSegment = 0;
                 spineSegment < lizard.Spine.Joints.Count - 1;
                 spineSegment++)
            {
                if (Math.Abs(spineSegment - bodyIndex) > 2)
                {
                    continue;
                }
                var bodyRadius = Math.Max(
                    lizard.GetBodyWidth(spineSegment),
                    lizard.GetBodyWidth(spineSegment + 1));
                if (DistancePointToSegment(
                        point,
                        lizard.Spine.Joints[spineSegment],
                        lizard.Spine.Joints[spineSegment + 1]) <=
                    bodyRadius + limbRadius)
                {
                    inside = true;
                    if (insideSpineSegment < 0)
                    {
                        insideSpineSegment = spineSegment;
                    }
                }
            }

            if (!inside)
            {
                hasLeftEnvelope = true;
            }
            else if (hasLeftEnvelope)
            {
                reentrySample = sample;
                reentrySpineSegment = insideSpineSegment;
                reentryPoint = point;
                return true;
            }
        }
        reentrySample = -1;
        reentrySpineSegment = -1;
        reentryPoint = default;
        return false;
    }

    private void CaptureTerminalReach(
        BehaviorController behavior,
        ProceduralLizard lizard)
    {
        _terminalReachSeen = true;
        _terminalWindowPosition = behavior.Position;
        var minimumErrorReduction = float.PositiveInfinity;
        var minimumUpwardTravel = float.PositiveInfinity;
        var minimumCatchDirectionTravel = float.PositiveInfinity;
        var maximumError = 0f;
        for (var index = 0; index < LegCount; index++)
        {
            var leg = lizard.Legs[index];
            var target = ContactTarget(lizard, index);
            _terminalTargets[index] = target;
            var targetError = Vector2.Distance(leg.Foot, target);
            maximumError = Math.Max(maximumError, targetError);
            minimumErrorReduction = Math.Min(
                minimumErrorReduction,
                _startTargetErrors[index] - targetError);
            minimumUpwardTravel = Math.Min(
                minimumUpwardTravel,
                (_startFeet[index].Y - _startShoulders[index].Y) -
                (leg.Foot.Y - leg.Shoulder.Y));
            var catchDirection = MathEx.SafeNormalize(
                _startTargets[index] - _startFeet[index],
                -Vector2.UnitY);
            minimumCatchDirectionTravel = Math.Min(
                minimumCatchDirectionTravel,
                Vector2.Dot(leg.Foot - _startFeet[index], catchDirection));
        }
        _targetErrorReduction = minimumErrorReduction;
        _minimumFootUpwardTravel = minimumUpwardTravel;
        _minimumPawCatchDirectionTravel = minimumCatchDirectionTravel;
        _terminalReachError = Math.Max(lizard.RegripContactError, maximumError);
        if (minimumErrorReduction <= 0f ||
            minimumUpwardTravel <= 0f ||
            minimumCatchDirectionTravel <= 0f)
        {
            _directionViolations++;
        }
    }

    private void ObserveContactHold(
        BehaviorController behavior,
        ProceduralLizard lizard,
        Vector2 visualCenter)
    {
        if (!_contactHoldSeen)
        {
            _contactHoldSeen = true;
            _contactWindowPosition = behavior.Position;
            _contactVisualCenter = visualCenter;
            var targetsPreserved = true;
            for (var index = 0; index < LegCount; index++)
            {
                targetsPreserved &= Vector2.Distance(
                    _terminalTargets[index],
                    ContactTarget(lizard, index)) <= 0.01f;
            }
            if (!_terminalReachSeen ||
                _previousAnimationPhase != RegripAnimationPhase.Seeking ||
                !targetsPreserved)
            {
                _orderingViolations++;
            }
        }

        var maximumError = MaximumTargetError(lizard);
        if (lizard.CurrentPoseMode != LizardPoseMode.Regrip ||
            behavior.LostGripPhase != LostGripFallPhase.Regripping ||
            behavior.LostGripVerticalVelocity != 0f ||
            !lizard.RegripContacted ||
            lizard.RegripContactLegMask != AllLegContactMask ||
            maximumError > RegripPoseController.ContactTolerance ||
            Vector2.Distance(behavior.Position, _terminalWindowPosition) >
                StationaryEpsilon)
        {
            _contactStopViolations++;
        }
        _contactError = Math.Max(
            _contactError,
            Math.Max(lizard.RegripContactError, maximumError));
        _contactSpineDrift = Math.Max(
            _contactSpineDrift,
            lizard.RegripContactSpineDrift);
        ObservePostContactDrift(behavior, visualCenter);
    }

    private void ObserveRecovering(
        BehaviorController behavior,
        ProceduralLizard lizard,
        Vector2 visualCenter)
    {
        if (!_contactHoldSeen ||
            lizard.CurrentPoseMode != LizardPoseMode.Regrip ||
            behavior.LostGripPhase != LostGripFallPhase.Regripping ||
            behavior.LostGripVerticalVelocity != 0f ||
            lizard.RegripContacted ||
            lizard.RegripContactLegMask != 0)
        {
            _orderingViolations++;
        }
        ObservePostContactDrift(behavior, visualCenter);
    }

    private void ObservePostContactDrift(
        BehaviorController behavior,
        Vector2 visualCenter)
    {
        if (!_contactHoldSeen)
        {
            return;
        }
        _postContactWindowDrift = Math.Max(
            _postContactWindowDrift,
            Vector2.Distance(_contactWindowPosition, behavior.Position));
        _postContactVisualCentroidDrift = Math.Max(
            _postContactVisualCentroidDrift,
            Vector2.Distance(_contactVisualCenter, visualCenter));
    }

    private static float MaximumTargetError(ProceduralLizard lizard)
    {
        var maximum = 0f;
        for (var index = 0; index < LegCount; index++)
        {
            maximum = Math.Max(
                maximum,
                Vector2.Distance(
                    lizard.Legs[index].Foot,
                    ContactTarget(lizard, index)));
        }
        return maximum;
    }

    private static Vector2 ContactTarget(ProceduralLizard lizard, int index) =>
        lizard.RegripContactTarget(index);

    private static float DistanceBetweenSegments(
        Vector2 firstStart,
        Vector2 firstEnd,
        Vector2 secondStart,
        Vector2 secondEnd)
    {
        var firstDirection = firstEnd - firstStart;
        var secondDirection = secondEnd - secondStart;
        var denominator = Cross(firstDirection, secondDirection);
        if (MathF.Abs(denominator) > ProgressEpsilon)
        {
            var between = secondStart - firstStart;
            var firstAmount = Cross(between, secondDirection) / denominator;
            var secondAmount = Cross(between, firstDirection) / denominator;
            if (firstAmount is >= 0f and <= 1f &&
                secondAmount is >= 0f and <= 1f)
            {
                return 0f;
            }
        }

        return Math.Min(
            Math.Min(
                DistancePointToSegment(firstStart, secondStart, secondEnd),
                DistancePointToSegment(firstEnd, secondStart, secondEnd)),
            Math.Min(
                DistancePointToSegment(secondStart, firstStart, firstEnd),
                DistancePointToSegment(secondEnd, firstStart, firstEnd)));
    }

    private static float DistancePointToSegment(
        Vector2 point,
        Vector2 segmentStart,
        Vector2 segmentEnd)
    {
        var direction = segmentEnd - segmentStart;
        var lengthSquared = direction.LengthSquared();
        if (lengthSquared <= ProgressEpsilon * ProgressEpsilon)
        {
            return Vector2.Distance(point, segmentStart);
        }
        var amount = MathEx.Clamp01(
            Vector2.Dot(point - segmentStart, direction) / lengthSquared);
        return Vector2.Distance(point, segmentStart + direction * amount);
    }

    private static float Cross(Vector2 first, Vector2 second) =>
        first.X * second.Y - first.Y * second.X;

    private static float FiniteOrZero(float value) =>
        float.IsFinite(value) ? value : 0f;

    private static string Format(Vector2 value) =>
        $"({value.X:F2},{value.Y:F2})";

    private string DescribeReachPaths() => string.Join(
        "/",
        Enumerable.Range(0, LegCount).Select(index =>
            $"L{index}:{Format(_startFeet[index])}->{Format(_startTargets[index])}," +
            $"angle={_cumulativeReachAngularTravel[index] * 180f / MathF.PI:F1}deg," +
            $"turns={_reachDirectionChanges[index]}"));

    private sealed record JointPose(
        Vector2[] Spine,
        Vector2[] Shoulders,
        Vector2[] Elbows,
        Vector2[] Feet)
    {
        public static JointPose Capture(ProceduralLizard lizard) => new(
            lizard.Spine.Joints.ToArray(),
            lizard.Legs.Select(leg => leg.Shoulder).ToArray(),
            lizard.Legs.Select(leg => leg.Elbow).ToArray(),
            lizard.Legs.Select(leg => leg.Foot).ToArray());

        public float MaximumDistance(JointPose other)
        {
            var maximum = 0f;
            for (var index = 0; index < Spine.Length; index++)
            {
                maximum = Math.Max(
                    maximum,
                    Vector2.Distance(Spine[index], other.Spine[index]));
            }
            for (var index = 0; index < Elbows.Length; index++)
            {
                maximum = Math.Max(
                    maximum,
                    Vector2.Distance(Elbows[index], other.Elbows[index]));
                maximum = Math.Max(
                    maximum,
                    Vector2.Distance(Feet[index], other.Feet[index]));
            }
            return maximum;
        }
    }

    private sealed record RenderedPaws(Vector2[] Shoulders, Vector2[] Feet)
    {
        public static RenderedPaws Capture(LizardRenderFrame frame)
        {
            if (frame.Legs.Length != LegCount ||
                !frame.Legs[0].IsFront ||
                !frame.Legs[1].IsFront ||
                frame.Legs[2].IsFront ||
                frame.Legs[3].IsFront)
            {
                throw new InvalidOperationException(
                    "Lost-grip probe requires front-front-rear-rear topology.");
            }

            return new RenderedPaws(
                frame.Legs.Select(leg => leg.Shoulder).ToArray(),
                frame.Legs.Select(leg => leg.Foot).ToArray());
        }
    }

    /// <summary>
    /// Samples the committed rendered pose at 60 Hz over the 120 Hz simulation.
    /// Both phase offsets are checked so an internal substep cannot make the
    /// four-paw lead pass only at one convenient compositor alignment.
    /// </summary>
    private sealed class VisibleDisplayCadenceTracker(int phaseOffset)
    {
        private Vector2 _previousWindowPosition;
        private RenderedPaws? _previousPaws;
        private int _displayFrame;
        private int _firstVisibleReachFrame = -1;
        private int _firstStationaryFrame = -1;
        private int _movingVisibleReachFrames;
        private bool _seekingSeen;

        public void Observe(
            int fixedStep,
            BehaviorController behavior,
            ProceduralLizard lizard,
            RenderedPaws renderedPaws)
        {
            if (fixedStep % 2 != phaseOffset)
            {
                return;
            }

            if (_previousPaws is not { } previousPaws)
            {
                _previousWindowPosition = behavior.Position;
                _previousPaws = renderedPaws;
                _displayFrame++;
                return;
            }

            var windowDeltaY = behavior.Position.Y - _previousWindowPosition.Y;
            var targetAvailable =
                lizard.RegripAnimationPhase == RegripAnimationPhase.Seeking;
            _seekingSeen |= targetAvailable;
            if (_seekingSeen &&
                _firstStationaryFrame < 0 &&
                windowDeltaY <= StationaryEpsilon)
            {
                _firstStationaryFrame = _displayFrame;
            }

            if (targetAvailable)
            {
                var allPawsVisiblyAdvance = true;
                for (var index = 0; index < LegCount; index++)
                {
                    var previousRelative =
                        previousPaws.Feet[index] -
                        previousPaws.Shoulders[index];
                    var currentRelative =
                        renderedPaws.Feet[index] - renderedPaws.Shoulders[index];
                    var relativeDelta = currentRelative - previousRelative;
                    var targetRelative =
                        ContactTarget(lizard, index) -
                        renderedPaws.Shoulders[index];
                    var catchDirection = MathEx.SafeNormalize(
                        targetRelative - previousRelative,
                        -Vector2.UnitY);
                    var scale = lizard.Profile.Appearance.VisualScale;
                    allPawsVisiblyAdvance &=
                        relativeDelta.Length() * scale >=
                            VisiblePawMotionScreenPixels &&
                        Vector2.Dot(relativeDelta, catchDirection) * scale >=
                            VisiblePawMotionScreenPixels;
                }

                if (allPawsVisiblyAdvance)
                {
                    if (_firstVisibleReachFrame < 0)
                    {
                        _firstVisibleReachFrame = _displayFrame;
                    }
                    if (windowDeltaY > StationaryEpsilon)
                    {
                        _movingVisibleReachFrames++;
                    }
                }
            }

            _previousWindowPosition = behavior.Position;
            _previousPaws = renderedPaws;
            _displayFrame++;
        }

        public VisibleDisplayCadenceResult Complete()
        {
            var leadFrames =
                _firstVisibleReachFrame >= 0 && _firstStationaryFrame >= 0
                    ? _firstStationaryFrame - _firstVisibleReachFrame
                    : 0;
            return new VisibleDisplayCadenceResult(
                _movingVisibleReachFrames,
                leadFrames);
        }
    }

    private readonly record struct VisibleDisplayCadenceResult(
        int MovingVisibleReachFrames,
        int VisibleReachLeadFrames);
}
