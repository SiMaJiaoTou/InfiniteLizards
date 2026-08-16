using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// Safety and release reactions that pre-empt autonomous roaming.
/// </summary>
internal sealed partial class BehaviorController
{
    private void BeginEdgeTurn(FloatRect area)
    {
        var boundary = _configuration.Boundary;
        // A dropped window may start exactly on the clamp boundary. Nudge the
        // model inward before turning so forward motion cannot be rounded back
        // to the same screen pixel indefinitely.
        var recoveryArea = _boundaryNavigator.SafeInset(area, boundary.RecoveryInset);
        var recoveryInset = Math.Max(0f, recoveryArea.Left - area.Left);
        if (recoveryInset > 0f)
        {
            Position = recoveryArea.Clamp(Position);
        }

        TransitionTo(RoamingState.EdgeTurn, StateTransitionReason.Boundary);
        _target = area.Center + new Vector2(
            RandomSigned(area.Width * boundary.RecoveryTargetJitterFactor),
            RandomSigned(area.Height * boundary.RecoveryTargetJitterFactor));
        _target = area.Clamp(_target);
        _desiredSpeed = MathEx.Lerp(
            boundary.RecoveryMinimumSpeed,
            boundary.RecoveryMaximumSpeed,
            (float)_random.NextDouble()) *
            _configuration.Speed.BoundaryRecoverySpeedMultiplier;
        _stateTimer = boundary.RecoveryStateDuration;
        _restPending = false;
        _lookAngleTarget = 0f;
        _motionWatchdog = 0f;
    }

    private void UpdateEdgeTurn(float dt, FloatRect area)
    {
        var boundary = _configuration.Boundary;
        var toTarget = _target - Position;
        var targetHeading = MathF.Atan2(toTarget.Y, toTarget.X);
        var error = MathEx.DeltaAngle(Heading, targetHeading);
        var desired = Math.Clamp(
            error * boundary.HeadingGain,
            -boundary.MaximumTurnRate,
            boundary.MaximumTurnRate);
        if (MathF.Abs(error) > boundary.MinimumTurnError &&
            MathF.Abs(desired) < boundary.MinimumTurnRate)
        {
            desired = MathF.CopySign(boundary.MinimumTurnRate, error);
        }

        _desiredTurnVelocity = desired;
        _turnVelocity = MathEx.Lerp(
            _turnVelocity,
            desired,
            MathEx.ExpLerpFactor(boundary.SteeringResponse, dt));
        Heading = MathEx.SimplifyAngle(Heading + _turnVelocity * dt);
        var inwardSpeed = MathEx.Lerp(
            boundary.MinimumInwardSpeed,
            _desiredSpeed,
            MathEx.Clamp01(1f - MathF.Abs(error) / MathF.PI));
        UpdateSpeed(inwardSpeed, dt);
        Position += MathEx.FromAngle(Heading) * Speed * dt;

        var safeArea = area.Inset(Math.Min(
            boundary.SafeAreaInset,
            Math.Min(area.Width, area.Height) * boundary.SafeAreaInsetFactor));
        var safeAhead = Position + MathEx.FromAngle(Heading) * boundary.SafeAheadDistance;
        if ((safeArea.Contains(safeAhead) &&
             MathF.Abs(error) < boundary.CompletionHeadingTolerance) ||
            _stateTimer <= 0f)
        {
            _cruiseSpeed = PickWalkSpeed();
            _desiredSpeed = _cruiseSpeed;
            _boutTimer = MathEx.Lerp(
                _configuration.EdgeRecoveryBoutMinimumDuration,
                _configuration.EdgeRecoveryBoutMaximumDuration,
                (float)_random.NextDouble());
            _restPending = false;
            BeginForwardState(
                _configuration.Timing.AfterTurnAround.Minimum,
                _configuration.Timing.AfterTurnAround.Maximum);
        }
    }

    private void BeginEscapeSprint(FloatRect area)
    {
        var sprint = _configuration.EscapeSprint;
        TransitionTo(RoamingState.EscapeSprint, StateTransitionReason.Release);
        SuppressMouseChase(sprint.PointerSuppressionDuration, requireLeave: true);
        DropProgress = 1f;
        _sprintDistance = 0f;
        _sprintTargetDistance = MathEx.Lerp(
            sprint.MinimumDistance,
            sprint.MaximumDistance,
            (float)_random.NextDouble());
        _sprintMinimumTimer = sprint.MinimumDuration;
        _stateTimer = sprint.MaximumDuration;
        _desiredSpeed = MathEx.Lerp(
            sprint.MinimumSpeed,
            sprint.MaximumSpeed,
            (float)_random.NextDouble());
        _lookAngleTarget = 0f;

        var maximumInset = Math.Max(0f, Math.Min(area.Width, area.Height) * 0.5f - 1f);
        var safeArea = area.Inset(Math.Min(sprint.SafeAreaInset, maximumInset));
        var selectedDirection = MathEx.SafeNormalize(
            area.Center - Position,
            MathEx.FromAngle(Heading));
        var candidates = new Vector2[sprint.DirectionCandidateCount];
        var candidateCount = 0;
        var startAngle = (float)_random.NextDouble() * MathEx.TwoPi;
        for (var index = 0; index < sprint.DirectionCandidateCount; index++)
        {
            var angle = startAngle + index * MathEx.TwoPi / sprint.DirectionCandidateCount;
            var direction = MathEx.FromAngle(angle);
            var endpoint = Position + direction * _sprintTargetDistance;
            if (!safeArea.Contains(endpoint))
            {
                continue;
            }
            candidates[candidateCount++] = direction;
        }
        if (candidateCount > 0)
        {
            selectedDirection = candidates[_random.Next(candidateCount)];
        }

        _target = safeArea.Clamp(Position + selectedDirection * _sprintTargetDistance);
        _sprintTargetDistance = Vector2.Distance(Position, _target);
    }

    private void UpdateEscapeSprint(float dt, FloatRect area)
    {
        var sprint = _configuration.EscapeSprint;
        _stateTimer -= dt;
        _sprintMinimumTimer -= dt;
        var toTarget = _target - Position;
        var remainingDistance = toTarget.Length();
        var targetHeading = remainingDistance > 0.001f
            ? MathF.Atan2(toTarget.Y, toTarget.X)
            : Heading;
        var error = MathEx.DeltaAngle(Heading, targetHeading);
        _desiredTurnVelocity = Math.Clamp(
            error * sprint.HeadingGain,
            -sprint.MaximumTurnRate,
            sprint.MaximumTurnRate);
        _turnVelocity = MathEx.Lerp(
            _turnVelocity,
            _desiredTurnVelocity,
            MathEx.ExpLerpFactor(sprint.SteeringResponse, dt));
        Heading = MathEx.SimplifyAngle(Heading + _turnVelocity * dt);

        var facing = MathEx.Lerp(
            sprint.MinimumFacingSpeedFactor,
            1f,
            MathEx.Clamp01(
                1f - MathF.Abs(error) /
                (MathF.PI * sprint.FacingAngleFactor)));
        UpdateSpeed(_desiredSpeed * facing, dt, sprint.Acceleration, sprint.Deceleration);
        var delta = MathEx.FromAngle(Heading) * Speed * dt;
        var predicted = Position + delta;
        if (!area.Contains(predicted))
        {
            BeginEdgeTurn(area);
            return;
        }

        Position = predicted;
        _sprintDistance += delta.Length();
        if ((_sprintMinimumTimer <= 0f &&
             (_sprintDistance >= _sprintTargetDistance ||
              remainingDistance <= sprint.CompletionDistance)) ||
            _stateTimer <= 0f)
        {
            BeginWalk(area);
        }
    }
}
