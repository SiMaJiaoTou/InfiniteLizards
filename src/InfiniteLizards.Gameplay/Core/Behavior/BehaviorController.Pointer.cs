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

        // Pointer response is proximity-driven, not timer-driven. Chase stays
        // active while the cursor remains nearby; avoidance ends only after
        // the configured safe separation has actually been reached.
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

        var observedPointerDistance = 0f;
        var hasFinitePointerDistance = pointer.IsAvailable &&
                                       TryGetFiniteDistance(
                                           Position,
                                           pointer.Position,
                                           out observedPointerDistance);
        if (pointerConfiguration.ResponseMode == PointerResponseMode.Avoid &&
            hasFinitePointerDistance &&
            observedPointerDistance >= pointerConfiguration.AvoidanceDistance)
        {
            // FinishMouseChase keeps NeedsLeave latched. Because rearm lies
            // outside both the avoidance and trigger distances, a stationary
            // cursor cannot immediately restart this response.
            FinishMouseChase(area);
            return false;
        }

        var maximumInset = Math.Max(0f, Math.Min(area.Width, area.Height) * 0.5f - 1f);
        var targetArea = area.Inset(Math.Min(pointerConfiguration.TargetInset, maximumInset));
        var trackedTarget = IsFinite(_pointerChase.Target)
            ? _pointerChase.Target
            : Position;
        var safeTarget = pointerConfiguration.ResponseMode == PointerResponseMode.Avoid
            ? ResolveAvoidanceTarget(
                targetArea,
                trackedTarget,
                pointerConfiguration.AvoidanceDistance)
            : targetArea.Clamp(trackedTarget);
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

        var mouseDistance = hasFinitePointerDistance
            ? observedPointerDistance
            : targetDistance;
        var responseAmount = pointerConfiguration.ResponseMode == PointerResponseMode.Avoid
            ? AvoidanceSpeedAmount(mouseDistance, pointerConfiguration.AvoidanceDistance)
            : ChaseSpeedAmount(mouseDistance, pointerConfiguration);
        var facing = MathEx.Lerp(
            pointerConfiguration.MinimumFacingSpeedFactor,
            1f,
            MathEx.Clamp01(
                1f - MathF.Abs(headingError) /
                (MathF.PI * pointerConfiguration.FacingAngleFactor)));
        UpdateSpeed(_desiredSpeed * responseAmount * facing, dt);
        Position += MathEx.FromAngle(Heading) * Speed * dt;
        return true;
    }

    private Vector2 ResolveAvoidanceTarget(
        FloatRect targetArea,
        Vector2 trackedPointer,
        float avoidanceDistance)
    {
        var away = Position - trackedPointer;
        var awayDirection = FiniteDirectionOrFallback(away, Heading);
        var candidate = Position + awayDirection * avoidanceDistance;
        if (!IsFinite(candidate))
        {
            candidate = Position;
        }
        return targetArea.Clamp(candidate);
    }

    private static Vector2 FiniteDirectionOrFallback(Vector2 value, float heading)
    {
        if (IsFinite(value))
        {
            var lengthSquared = value.LengthSquared();
            if (float.IsFinite(lengthSquared) && lengthSquared > 0.000001f)
            {
                return value / MathF.Sqrt(lengthSquared);
            }
        }

        var headingDirection = MathEx.FromAngle(heading);
        return IsFinite(headingDirection)
            ? headingDirection
            : Vector2.UnitX;
    }

    private static float AvoidanceSpeedAmount(float distance, float avoidanceDistance)
    {
        var urgency = MathEx.Clamp01(
            (avoidanceDistance - distance) / avoidanceDistance);
        urgency = urgency * urgency * (3f - 2f * urgency);
        return MathEx.Lerp(0.45f, 1f, urgency);
    }

    private static float ChaseSpeedAmount(
        float distance,
        PointerChaseConfiguration configuration)
    {
        var approach = MathEx.Clamp01(
            (distance - configuration.StopDistance) /
            configuration.ApproachDistance);
        return approach * approach * (3f - 2f * approach);
    }

    private static bool TryGetFiniteDistance(
        Vector2 first,
        Vector2 second,
        out float distance)
    {
        distance = Vector2.Distance(first, second);
        return float.IsFinite(distance);
    }

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

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
