namespace DesktopLizard.Core;

internal sealed partial class BehaviorController
{
    public BehaviorDebugSnapshot CaptureDebugSnapshot(FloatRect navigationArea)
    {
        var sideClearances = _boundaryNavigator.GetSideClearances(
            navigationArea,
            Position,
            Heading);

        return new BehaviorDebugSnapshot(
            State,
            LastTransitionReason,
            TransitionSerial,
            Position,
            _target,
            LookTarget,
            Heading,
            Speed,
            _desiredSpeed,
            NormalizedSpeed,
            _turnVelocity,
            _desiredTurnVelocity,
            _lookAngleOffset,
            _lookAngleTarget,
            _emotionTimer,
            _stateTimer,
            _boutTimer,
            _turnRemaining,
            _turnDirection,
            _restPending,
            _forwardExtended,
            IsPaused,
            Emotion,
            DropProgress,
            LostGripPhase,
            LostGripFallProgress,
            LostGripReachProgress,
            LostGripRegripProgress,
            LostGripVerticalVelocity,
            LostGripDistance,
            LostGripTargetDistance,
            _sCurve.Elapsed,
            _sCurve.Duration,
            _sCurve.CycleDuration,
            _sCurve.CycleCount,
            _sCurve.Progress,
            _sCurve.EasedCycleProgress,
            _sCurve.Amplitude,
            _sCurve.Direction,
            _pointerChase.AttentionTime,
            _pointerChase.LostTime,
            _pointerChase.Cooldown,
            _pointerChase.NeedsLeave,
            _pointerChase.HasTarget,
            _pointerChase.Target,
            _sprintDistance,
            _sprintTargetDistance,
            _sprintMinimumTimer,
            _motionWatchdog,
            _configuration.Boundary.LookAheadBaseDistance +
            Speed * _configuration.Boundary.LookAheadSpeedFactor,
            _boundaryNavigator.MinimumClearance(navigationArea, Position),
            sideClearances.Positive,
            sideClearances.Negative);
    }
}
