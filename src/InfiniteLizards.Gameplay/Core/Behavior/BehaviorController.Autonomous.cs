using System.Numerics;

namespace DesktopLizard.Core;

internal sealed partial class BehaviorController
{
    private void PickNextAction(FloatRect area)
    {
        var decisions = _configuration.Decisions;
        var observeChance = Math.Clamp(
            decisions.ObserveChanceBase +
            Emotion.Curious * decisions.ObserveChanceCuriousFactor +
            Emotion.Wary * decisions.ObserveChanceWaryFactor,
            decisions.ObserveChanceMinimum,
            decisions.ObserveChanceMaximum);
        if (_random.NextDouble() < observeChance)
        {
            EnterObserve();
            return;
        }

        BeginWalk(area);
    }

    private void BeginWalk(FloatRect area)
    {
        DropProgress = 0f;
        ClearLostGripFallState();
        _idleMayObserve = true;
        if (_boundaryNavigator.IsNearEdge(area, Position, _configuration.Boundary.WalkStartMargin))
        {
            BeginEdgeTurn(area);
            return;
        }

        _cruiseSpeed = PickWalkSpeed();
        _desiredSpeed = _cruiseSpeed;
        _boutTimer = MathEx.Lerp(
            _configuration.WalkBoutMinimumDuration,
            _configuration.WalkBoutMaximumDuration,
            (float)_random.NextDouble());
        _restPending = false;
        _lookAngleTarget = 0f;
        BeginForwardState(
            _configuration.Timing.InitialForward.Minimum,
            _configuration.Timing.InitialForward.Maximum);
    }

    private void UpdateLocomotionState(float dt, FloatRect area)
    {
        _stateTimer -= dt;
        _boutTimer -= dt;
        if (_boutTimer <= 0f)
        {
            _restPending = true;
        }

        var forward = MathEx.FromAngle(Heading);
        var boundary = _configuration.Boundary;
        var lookAheadDistance = boundary.LookAheadBaseDistance + Speed * boundary.LookAheadSpeedFactor;
        var lookAhead = Position + forward * lookAheadDistance;
        var edgeMargin = Math.Max(boundary.MinimumEdgeMargin, Speed * boundary.EdgeMarginSpeedFactor);
        if (!area.Contains(lookAhead) || _boundaryNavigator.IsNearEdge(area, Position, edgeMargin))
        {
            BeginEdgeTurn(area);
            return;
        }

        if (State.IsSCurveCrawl())
        {
            _desiredTurnVelocity = _sCurve.Advance(dt);
        }

        var previousHeading = Heading;
        var steeringResponse = State switch
        {
            RoamingState.TurnAround => _configuration.Locomotion.TurnAroundSteeringResponse,
            RoamingState.SCurveCrawl => _configuration.SCurve.Normal.SteeringResponse,
            RoamingState.FastSCurveCrawl => _configuration.SCurve.Fast.SteeringResponse,
            _ => _configuration.Locomotion.DefaultSteeringResponse
        };
        _turnVelocity = MathEx.Lerp(
            _turnVelocity,
            _desiredTurnVelocity,
            MathEx.ExpLerpFactor(steeringResponse, dt));
        Heading = MathEx.SimplifyAngle(Heading + _turnVelocity * dt);

        var phaseSpeed = State switch
        {
            RoamingState.CurveCrawl => _configuration.Locomotion.CurveSpeedFactor,
            RoamingState.SCurveCrawl => _configuration.SCurve.Normal.SpeedFactor,
            RoamingState.FastSCurveCrawl => _configuration.SCurve.Fast.SpeedFactor,
            RoamingState.TurnAround => _configuration.Locomotion.TurnAroundSpeedFactor,
            _ => 1f
        };
        var steeringSlowdown = State.IsFastCrawl()
            ? 1f
            : MathEx.Lerp(
                1f,
                _configuration.Locomotion.SteeringSlowdownMinimumFactor,
                MathEx.Clamp01(
                    MathF.Abs(_turnVelocity) /
                    _configuration.Locomotion.SteeringSlowdownTurnRate));
        var targetSpeed = _desiredSpeed * phaseSpeed * steeringSlowdown;
        if (State.IsFastCrawl())
        {
            // Match the physical acceleration to ProceduralLizard's gait-speed
            // tracker. If the HWND outruns that tracker, an old planted pair is
            // dragged to the reach limit before the scheduler can renew it.
            UpdateSpeed(
                targetSpeed,
                dt,
                _configuration.Speed.FastCrawlAcceleration,
                _configuration.Speed.CrawlDeceleration);
        }
        else
        {
            UpdateSpeed(targetSpeed, dt);
        }
        Position += MathEx.FromAngle(Heading) * Speed * dt;

        if (State == RoamingState.TurnAround)
        {
            var directedTurn = MathEx.DeltaAngle(previousHeading, Heading) * _turnDirection;
            if (directedTurn > 0f)
            {
                _turnRemaining = Math.Max(0f, _turnRemaining - directedTurn);
            }

            if (_turnRemaining <= _configuration.Locomotion.TurnAroundCompletionTolerance ||
                _stateTimer <= 0f)
            {
                BeginForwardState(
                    _configuration.Timing.AfterTurnAround.Minimum,
                    _configuration.Timing.AfterTurnAround.Maximum);
            }
            return;
        }

        if (_stateTimer > 0f)
        {
            return;
        }

        if (State == RoamingState.ForwardCrawl)
        {
            if (_restPending)
            {
                BeginRest();
            }
            else
            {
                ChooseAfterForward(area);
            }
            return;
        }

        if (State == RoamingState.FastForwardCrawl)
        {
            if (_restPending)
            {
                BeginRest();
            }
            else
            {
                ChooseAfterFastForward(area);
            }
            return;
        }

        if (State == RoamingState.CurveCrawl)
        {
            if (_restPending)
            {
                BeginRest();
            }
            else
            {
                ChooseAfterCurve(area);
            }
            return;
        }

        if (State == RoamingState.SCurveCrawl)
        {
            if (_restPending)
            {
                BeginRest();
            }
            else
            {
                ChooseAfterSCurve(area);
            }
            return;
        }

        if (State == RoamingState.FastSCurveCrawl)
        {
            if (_restPending)
            {
                BeginRest();
            }
            else
            {
                ChooseAfterFastSCurve(area);
            }
        }
    }

    private void ChooseAfterForward(FloatRect area)
    {
        var action = AutonomousTransitionPolicy.ChooseAfterForward(
            (float)_random.NextDouble(),
            _forwardExtended,
            _configuration.TransitionMatrix);
        BeginAutonomousAction(
            action,
            area,
            _configuration.Timing.AfterForward.Minimum,
            _configuration.Timing.AfterForward.Maximum);
    }

    private void ChooseAfterCurve(FloatRect area)
    {
        var action = AutonomousTransitionPolicy.ChooseAfterCurve(
            (float)_random.NextDouble(),
            _configuration.TransitionMatrix);
        BeginAutonomousAction(
            action,
            area,
            _configuration.Timing.AfterCurve.Minimum,
            _configuration.Timing.AfterCurve.Maximum);
    }

    private void ChooseAfterSCurve(FloatRect area)
    {
        var action = AutonomousTransitionPolicy.ChooseAfterSCurve(
            (float)_random.NextDouble(),
            _configuration.TransitionMatrix);
        BeginAutonomousAction(
            action,
            area,
            _configuration.Timing.AfterSCurve.Minimum,
            _configuration.Timing.AfterSCurve.Maximum);
    }

    private void ChooseAfterFastForward(FloatRect area)
    {
        var action = AutonomousTransitionPolicy.ChooseAfterFast(
            (float)_random.NextDouble(),
            _configuration.TransitionMatrix);
        BeginAutonomousAction(
            action,
            area,
            _configuration.Timing.AfterFast.Minimum,
            _configuration.Timing.AfterFast.Maximum);
    }

    private void ChooseAfterFastSCurve(FloatRect area)
    {
        var action = AutonomousTransitionPolicy.ChooseAfterFast(
            (float)_random.NextDouble(),
            _configuration.TransitionMatrix);
        BeginAutonomousAction(
            action,
            area,
            _configuration.Timing.AfterFast.Minimum,
            _configuration.Timing.AfterFast.Maximum);
    }

    private void BeginAutonomousAction(
        AutonomousAction action,
        FloatRect area,
        float forwardMinimumDuration,
        float forwardMaximumDuration)
    {
        switch (action)
        {
            case AutonomousAction.Forward:
                BeginForwardState(forwardMinimumDuration, forwardMaximumDuration);
                break;
            case AutonomousAction.ForwardExtension:
                BeginForwardState(
                    forwardMinimumDuration,
                    forwardMaximumDuration,
                    isExtension: true);
                break;
            case AutonomousAction.Curve:
                BeginCurveState(area);
                break;
            case AutonomousAction.SCurve:
                BeginSCurveState(area);
                break;
            case AutonomousAction.FastForward:
                BeginFastForwardState();
                break;
            case AutonomousAction.FastSCurve:
                BeginFastSCurveState(area);
                break;
            case AutonomousAction.TurnAround:
                BeginTurnAroundState(area);
                break;
            case AutonomousAction.LostGripFall:
                TryBeginLostGripFall(area);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    private void BeginForwardState(float minimum, float maximum, bool isExtension = false)
    {
        TransitionTo(RoamingState.ForwardCrawl, StateTransitionReason.Choice);
        _desiredSpeed = _cruiseSpeed;
        _forwardExtended = isExtension;
        _stateTimer = MathEx.Lerp(minimum, maximum, (float)_random.NextDouble());
        _desiredTurnVelocity = RandomSigned(_configuration.Locomotion.StraightHeadingJitter);
    }

    private void BeginCurveState(FloatRect area)
    {
        TransitionTo(RoamingState.CurveCrawl, StateTransitionReason.Choice);
        _desiredSpeed = _cruiseSpeed;
        _stateTimer = MathEx.Lerp(
            _configuration.Timing.Curve.Minimum,
            _configuration.Timing.Curve.Maximum,
            (float)_random.NextDouble());
        _desiredTurnVelocity = PickSafeTurnSign(area) *
                               MathEx.Lerp(
                                   _configuration.Locomotion.CurveMinimumTurnRate,
                                   _configuration.Locomotion.CurveMaximumTurnRate,
                                   (float)_random.NextDouble());
    }

    private void BeginSCurveState(FloatRect area)
    {
        TransitionTo(RoamingState.SCurveCrawl, StateTransitionReason.Choice);
        _desiredSpeed = _cruiseSpeed;
        var normalSCurve = _configuration.SCurve.Normal;
        var cycleCount = _random.NextDouble() < normalSCurve.ExtraCycleProbability
            ? normalSCurve.MaximumCycleCount
            : normalSCurve.MinimumCycleCount;
        var cycleDuration = MathEx.Lerp(
            normalSCurve.MinimumCycleDuration,
            normalSCurve.MaximumCycleDuration,
            (float)_random.NextDouble());
        var amplitude = MathEx.Lerp(
            normalSCurve.MinimumAmplitude,
            normalSCurve.MaximumAmplitude,
            (float)_random.NextDouble());
        var direction = PickSafeTurnSign(area);
        _sCurve.Begin(
            cycleCount,
            cycleDuration,
            amplitude,
            direction,
            _configuration.SCurve.SettleDuration);
        _stateTimer = _sCurve.Duration;
        _desiredTurnVelocity = 0f;
        _turnVelocity = Math.Clamp(
            _turnVelocity,
            -normalSCurve.MinimumAmplitude,
            normalSCurve.MinimumAmplitude);
    }

    private void BeginFastForwardState()
    {
        TransitionTo(RoamingState.FastForwardCrawl, StateTransitionReason.Choice);
        _desiredSpeed = _configuration.Speed.MaximumCrawl;
        _forwardExtended = false;
        _stateTimer = MathEx.Lerp(
            _configuration.FastForward.MinimumDuration,
            _configuration.FastForward.MaximumDuration,
            (float)_random.NextDouble());
        _desiredTurnVelocity = RandomSigned(_configuration.FastForward.MaximumHeadingJitter);
    }

    private void BeginFastSCurveState(FloatRect area)
    {
        TransitionTo(RoamingState.FastSCurveCrawl, StateTransitionReason.Choice);
        _desiredSpeed = _configuration.Speed.MaximumCrawl;
        _forwardExtended = false;
        var fastSCurve = _configuration.SCurve.Fast;
        var cycleCount = fastSCurve.MinimumCycleCount == fastSCurve.MaximumCycleCount
            ? fastSCurve.MinimumCycleCount
            : _random.NextDouble() < fastSCurve.ExtraCycleProbability
                ? fastSCurve.MaximumCycleCount
                : fastSCurve.MinimumCycleCount;
        var cycleDuration = MathEx.Lerp(
            fastSCurve.MinimumCycleDuration,
            fastSCurve.MaximumCycleDuration,
            (float)_random.NextDouble());
        var amplitude = MathEx.Lerp(
            fastSCurve.MinimumAmplitude,
            fastSCurve.MaximumAmplitude,
            (float)_random.NextDouble());
        var direction = PickSafeTurnSign(area);
        _sCurve.Begin(
            cycleCount,
            cycleDuration,
            amplitude,
            direction,
            _configuration.SCurve.SettleDuration);
        _stateTimer = _sCurve.Duration;
        _desiredTurnVelocity = 0f;
        _turnVelocity = Math.Clamp(
            _turnVelocity,
            -_configuration.SCurve.Normal.MinimumAmplitude,
            _configuration.SCurve.Normal.MinimumAmplitude);
    }

    private void BeginTurnAroundState(FloatRect area)
    {
        TransitionTo(RoamingState.TurnAround, StateTransitionReason.Choice);
        _desiredSpeed = _cruiseSpeed;
        var locomotion = _configuration.Locomotion;
        _stateTimer = locomotion.TurnAroundMaximumDuration;
        _turnDirection = PickSafeTurnSign(area);
        _turnRemaining = MathEx.Lerp(
            locomotion.TurnAroundMinimumAngle,
            locomotion.TurnAroundMaximumAngle,
            (float)_random.NextDouble());
        // A deliberate U-turn should read as a grounded pivot, not wrench the
        // planted diagonal past its two-link reach. A lower angular rate gives
        // each alternating support pair time to renew its footprint while the
        // same 156--173 degree heading change still completes deterministically.
        _desiredTurnVelocity = _turnDirection * MathEx.Lerp(
            locomotion.TurnAroundMinimumRate,
            locomotion.TurnAroundMaximumRate,
            (float)_random.NextDouble());
    }

    private void BeginRest()
    {
        _restPending = false;
        if (_random.NextDouble() <
            _configuration.Decisions.RestChanceBase +
            Emotion.Calm * _configuration.Decisions.RestChanceCalmFactor)
        {
            var duration = RestDurationDistribution.Sample(
                (float)_random.NextDouble(),
                _configuration.Rest);
            EnterIdle(duration, allowObserveAfter: false);
        }
        else
        {
            EnterObserve();
        }
    }

    private void EnterIdle(float minimum, float maximum)
    {
        var duration = MathEx.Lerp(minimum, maximum, (float)_random.NextDouble()) *
                       MathEx.Lerp(
                           _configuration.Decisions.IdleDurationMinimumFactor,
                           _configuration.Decisions.IdleDurationMaximumFactor,
                           Emotion.Calm);
        EnterIdle(duration, allowObserveAfter: true);
    }

    private void EnterIdle(float duration, bool allowObserveAfter)
    {
        TransitionTo(RoamingState.Idle, StateTransitionReason.Timer);
        _desiredSpeed = 0f;
        _desiredTurnVelocity = 0f;
        _lookAngleTarget = 0f;
        _stateTimer = Math.Clamp(duration, 0.05f, _configuration.Rest.MaximumDuration);
        _idleMayObserve = allowObserveAfter;
    }

    private void EnterObserve()
    {
        TransitionTo(RoamingState.Observe, StateTransitionReason.Choice);
        _idleMayObserve = true;
        _desiredSpeed = 0f;
        _desiredTurnVelocity = 0f;
        _stateTimer = MathEx.Lerp(
            _configuration.Timing.Observe.Minimum,
            _configuration.Timing.Observe.Maximum,
            (float)_random.NextDouble());
        var side = _random.NextDouble() < 0.5 ? -1f : 1f;
        _lookAngleTarget = side * MathEx.Lerp(
            _configuration.Decisions.ObserveMinimumLookAngle,
            _configuration.Decisions.ObserveMaximumLookAngle,
            (float)_random.NextDouble());
    }
}
