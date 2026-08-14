using System.Numerics;
using DesktopLizard.Core;

namespace DesktopLizard.Diagnostics;

internal readonly record struct LostGripRegripProbeResult(
    bool SeekingSeen,
    bool TerminalReachSeen,
    bool ContactHoldSeen,
    int OrderingViolations,
    int DirectionViolations,
    int ContactStopViolations,
    int NonZeroSeekingSteps,
    float BehaviorReachPeak,
    float TargetErrorReduction,
    float FrontFootUpwardTravel,
    float FrontPawCatchDirectionTravel,
    float ReachEntryPoseJump,
    float TerminalReachError,
    float ContactError,
    float ContactSpineDrift,
    float PostContactWindowDrift,
    float PostContactVisualCentroidDrift);

/// <summary>
/// Observes the presentation-only Seeking -> ContactHold -> Recovering
/// sequence without reaching into mutable animation implementation details.
/// Both geometry and ordering are required: a pose that eventually looks
/// correct still fails if the host stopped before the front paws arrived.
/// </summary>
internal sealed class LostGripRegripAnimationProbe
{
    private const float ProgressEpsilon = 0.0001f;
    private const float StationaryEpsilon = 0.001f;
    private readonly Vector2[] _startShoulders = new Vector2[2];
    private readonly Vector2[] _startFeet = new Vector2[2];
    private readonly Vector2[] _startTargets = new Vector2[2];
    private readonly float[] _startTargetErrors = new float[2];
    private Vector2 _previousWindowPosition;
    private JointPose _previousPose;
    private RegripAnimationPhase _previousAnimationPhase;
    private float _previousBehaviorReachProgress;
    private float _previousAnimationReachProgress;
    private bool _hasBehaviorReachSample;
    private bool _seekingSeen;
    private bool _terminalReachSeen;
    private bool _contactHoldSeen;
    private Vector2 _terminalWindowPosition;
    private Vector2 _terminalTarget0;
    private Vector2 _terminalTarget1;
    private Vector2 _contactWindowPosition;
    private Vector2 _contactVisualCenter;
    private int _orderingViolations;
    private int _directionViolations;
    private int _contactStopViolations;
    private int _nonZeroSeekingSteps;
    private float _behaviorReachPeak;
    private float _targetErrorReduction;
    private float _frontFootUpwardTravel;
    private float _frontPawCatchDirectionTravel;
    private float _reachEntryPoseJump;
    private float _terminalReachError;
    private float _contactError;
    private float _contactSpineDrift;
    private float _postContactWindowDrift;
    private float _postContactVisualCentroidDrift;

    public LostGripRegripAnimationProbe(
        Vector2 initialWindowPosition,
        ProceduralLizard lizard)
    {
        _previousWindowPosition = initialWindowPosition;
        _previousPose = JointPose.Capture(lizard);
    }

    public void Observe(
        BehaviorController behavior,
        ProceduralLizard lizard,
        Vector2 visualCenter)
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

        _previousWindowPosition = behavior.Position;
        _previousPose = pose;
        _previousAnimationPhase = lizard.RegripAnimationPhase;
    }

    public LostGripRegripProbeResult Complete() => new(
        _seekingSeen,
        _terminalReachSeen,
        _contactHoldSeen,
        _orderingViolations,
        _directionViolations,
        _contactStopViolations,
        _nonZeroSeekingSteps,
        _behaviorReachPeak,
        _targetErrorReduction,
        _frontFootUpwardTravel,
        _frontPawCatchDirectionTravel,
        _reachEntryPoseJump,
        _terminalReachError,
        _contactError,
        _contactSpineDrift,
        _postContactWindowDrift,
        _postContactVisualCentroidDrift);

    private void ObserveSeeking(
        BehaviorController behavior,
        ProceduralLizard lizard,
        JointPose pose)
    {
        if (!_seekingSeen)
        {
            _seekingSeen = true;
            _reachEntryPoseJump = _previousPose.MaximumDistance(pose);
            for (var index = 0; index < 2; index++)
            {
                _startShoulders[index] = lizard.Legs[index].Shoulder;
                _startFeet[index] = lizard.Legs[index].Foot;
                _startTargets[index] = ContactTarget(lizard, index);
                _startTargetErrors[index] = Vector2.Distance(
                    _startFeet[index],
                    _startTargets[index]);
            }
        }

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
            _terminalReachSeen = true;
            _terminalWindowPosition = behavior.Position;
            _terminalTarget0 = lizard.RegripFrontContactTarget0;
            _terminalTarget1 = lizard.RegripFrontContactTarget1;
            _terminalReachError = lizard.RegripContactError;
            var minimumErrorReduction = float.PositiveInfinity;
            var minimumUpwardTravel = float.PositiveInfinity;
            var minimumCatchDirectionTravel = float.PositiveInfinity;
            for (var index = 0; index < 2; index++)
            {
                var leg = lizard.Legs[index];
                var targetError = Vector2.Distance(
                    leg.Foot,
                    ContactTarget(lizard, index));
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
            _frontFootUpwardTravel = minimumUpwardTravel;
            _frontPawCatchDirectionTravel = minimumCatchDirectionTravel;
            if (minimumErrorReduction <= 0f ||
                minimumUpwardTravel <= 0f ||
                minimumCatchDirectionTravel <= 0f)
            {
                _directionViolations++;
            }
        }

        // The contact occurs on the final nonzero window delta. Behavior has
        // atomically reached Regripping, while application deliberately still
        // renders FreeFall+Seeking(progress=1) for this one fixed step.
        if (behavior.LostGripPhase != LostGripFallPhase.Regripping ||
            behavior.LostGripVerticalVelocity != 0f ||
            !movedDown ||
            lizard.RegripReachProgress < 1f - ProgressEpsilon ||
            !lizard.RegripContacted ||
            lizard.RegripContactLegMask != 0b0011)
        {
            _contactStopViolations++;
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
            if (!_terminalReachSeen ||
                _previousAnimationPhase != RegripAnimationPhase.Seeking ||
                Vector2.Distance(
                    _terminalTarget0,
                    lizard.RegripFrontContactTarget0) > 0.01f ||
                Vector2.Distance(
                    _terminalTarget1,
                    lizard.RegripFrontContactTarget1) > 0.01f)
            {
                _orderingViolations++;
            }
        }

        if (lizard.CurrentPoseMode != LizardPoseMode.Regrip ||
            behavior.LostGripPhase != LostGripFallPhase.Regripping ||
            behavior.LostGripVerticalVelocity != 0f ||
            !lizard.RegripContacted ||
            lizard.RegripContactLegMask != 0b0011 ||
            Vector2.Distance(behavior.Position, _terminalWindowPosition) >
                StationaryEpsilon)
        {
            _contactStopViolations++;
        }
        _contactError = Math.Max(_contactError, lizard.RegripContactError);
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

    private static Vector2 ContactTarget(ProceduralLizard lizard, int index) =>
        index == 0
            ? lizard.RegripFrontContactTarget0
            : lizard.RegripFrontContactTarget1;

    private sealed record JointPose(
        Vector2[] Spine,
        Vector2[] Elbows,
        Vector2[] Feet)
    {
        public static JointPose Capture(ProceduralLizard lizard) => new(
            lizard.Spine.Joints.ToArray(),
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
}
