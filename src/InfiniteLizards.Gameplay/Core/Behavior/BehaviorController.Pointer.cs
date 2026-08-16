using System.Numerics;

namespace DesktopLizard.Core;

internal sealed partial class BehaviorController
{
    private bool UpdateMouseResponse(float dt, FloatRect area, PointerObservation pointer)
    {
        if (State == RoamingState.MouseChase)
        {
            if (pointer.IsInteractionBlocked)
            {
                FinishMouseChase(area);
                return false;
            }

            return UpdateMouseChase(dt, area, pointer);
        }

        if (!PointerChaseController.CanStartFrom(State))
        {
            _pointerChase.ClearAttention();
            return false;
        }

        if (!_pointerChase.UpdateAttention(dt, Position, pointer))
        {
            return false;
        }

        BeginMouseChase(pointer.Position);
        return UpdateMouseChase(dt, area, pointer);
    }

    private bool UpdateMouseChase(float dt, FloatRect area, PointerObservation pointer)
    {
        var pointerConfiguration = _configuration.Pointer;
        _pointerChase.UpdateChaseTracking(dt, Position, pointer);

        // MouseChase is proximity-driven, not timer-driven. A cursor that
        // remains around the lizard keeps this state alive indefinitely; the
        // normal state machine resumes only after the pointer is genuinely
        // lost (or a higher-priority interaction interrupts the chase).
        if (_pointerChase.HasLostTarget)
        {
            FinishMouseChase(area);
            return false;
        }

        // Edge recovery always outranks curiosity. The chase target is also
        // clamped inward so a cursor on another monitor cannot pull the pet
        // through the current monitor boundary.
        if (_boundaryNavigator.IsNearEdge(
                area,
                Position,
                pointerConfiguration.EdgeInterruptDistance))
        {
            SuppressMouseChase(RandomMouseCooldown(), requireLeave: true);
            BeginEdgeTurn(area);
            return false;
        }

        var maximumInset = Math.Max(0f, Math.Min(area.Width, area.Height) * 0.5f - 1f);
        var targetArea = area.Inset(Math.Min(pointerConfiguration.TargetInset, maximumInset));
        var safeTarget = targetArea.Clamp(_pointerChase.Target);
        var toTarget = safeTarget - Position;
        var targetDistance = toTarget.Length();
        var targetHeading = targetDistance > 0.001f
            ? MathF.Atan2(toTarget.Y, toTarget.X)
            : Heading;
        var headingError = MathEx.DeltaAngle(Heading, targetHeading);
        _desiredTurnVelocity = Math.Clamp(
            headingError * pointerConfiguration.TurnGain,
            -pointerConfiguration.MaximumTurnRate,
            pointerConfiguration.MaximumTurnRate);
        _turnVelocity = MathEx.Lerp(
            _turnVelocity,
            _desiredTurnVelocity,
            MathEx.ExpLerpFactor(pointerConfiguration.SteeringResponse, dt));
        Heading = MathEx.SimplifyAngle(Heading + _turnVelocity * dt);

        var mouseDistance = pointer.IsAvailable
            ? Vector2.Distance(Position, pointer.Position)
            : targetDistance;
        var approach = MathEx.Clamp01(
            (mouseDistance - pointerConfiguration.StopDistance) /
            pointerConfiguration.ApproachDistance);
        approach = approach * approach * (3f - 2f * approach);
        var facing = MathEx.Lerp(
            pointerConfiguration.MinimumFacingSpeedFactor,
            1f,
            MathEx.Clamp01(
                1f - MathF.Abs(headingError) /
                (MathF.PI * pointerConfiguration.FacingAngleFactor)));
        UpdateSpeed(_desiredSpeed * approach * facing, dt);
        Position += MathEx.FromAngle(Heading) * Speed * dt;
        return true;
    }

    private void BeginMouseChase(Vector2 pointerPosition)
    {
        TransitionTo(RoamingState.MouseChase, StateTransitionReason.Pointer);
        _pointerChase.BeginChase(pointerPosition);
        _desiredSpeed = MathEx.Lerp(
            _configuration.Speed.MaximumCrawl * _configuration.Pointer.MinimumChaseSpeedFactor,
            _configuration.Speed.MaximumCrawl * _configuration.Pointer.MaximumChaseSpeedFactor,
            (float)_random.NextDouble());
        _lookAngleTarget = 0f;
        _motionWatchdog = 0f;
        _watchdogPosition = Position;
    }

    private void FinishMouseChase(FloatRect area)
    {
        SuppressMouseChase(RandomMouseCooldown(), requireLeave: true);
        BeginWalk(area);
        _motionWatchdog = 0f;
        _watchdogPosition = Position;
    }

    private void SuppressMouseChase(float cooldown, bool requireLeave)
    {
        _pointerChase.Suppress(cooldown, requireLeave);
    }

    private float RandomMouseCooldown() => MathEx.Lerp(
        _configuration.Pointer.MinimumCooldown,
        _configuration.Pointer.MaximumCooldown,
        (float)_random.NextDouble());
}
