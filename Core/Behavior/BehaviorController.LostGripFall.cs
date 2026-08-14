using System.Numerics;

namespace DesktopLizard.Core;

internal sealed partial class BehaviorController
{
    private void TryBeginLostGripFall(FloatRect area)
    {
        var fall = _configuration.LostGripFall;
        if (!TryGetLostGripFullRenderSafeArea(area, out var fullRenderSafeArea))
        {
            // SafeInset normally preserves a usable rectangle by reducing an
            // oversized request. That fallback is unsuitable for starting a
            // full-canvas fall: the host surface would still extend beyond the
            // physical work area even though its center looked clamped.
            BeginEdgeTurn(area);
            return;
        }
        if (!fullRenderSafeArea.Contains(Position))
        {
            // The falling pose can use the full transparent render surface,
            // not only the compact normal-pose envelope. Refuse to enter near
            // any physical screen edge where that larger surface would clip.
            BeginEdgeTurn(area);
            return;
        }

        var safeBottom = fullRenderSafeArea.Bottom;
        var availableDistance = Math.Max(0f, safeBottom - Position.Y);
        if (availableDistance + 0.001f < fall.MinimumDistance)
        {
            // Do not turn a near-edge clamp into a fake long fall. Boundary
            // recovery is the safe continuation when there is no visible room.
            BeginEdgeTurn(area);
            return;
        }

        ClearLostGripFallState();
        TransitionTo(RoamingState.LostGripFall, StateTransitionReason.Choice);
        SuppressMouseChase(fall.PointerSuppressionDuration, requireLeave: false);

        _lostGripStartY = Position.Y;
        // One endpoint sample is retained before the initial-velocity sample,
        // preserving the behavior RNG draw count and ordering of schema 4.
        // Unlike the old fixed maximum, the live safe bottom lets a lizard
        // higher on the desktop produce a visibly longer fall without ever
        // moving the full render surface outside the work area.
        _lostGripTargetDistance = MathEx.Lerp(
            fall.MinimumDistance,
            availableDistance,
            (float)_random.NextDouble());
        _lostGripTargetY = _lostGripStartY + _lostGripTargetDistance;
        _lostGripVerticalVelocity = MathEx.Lerp(
            fall.MinimumInitialVelocity,
            fall.MaximumInitialVelocity,
            (float)_random.NextDouble());
        LostGripPhase = LostGripFallPhase.Falling;

        Speed = 0f;
        _desiredSpeed = 0f;
        _turnVelocity = 0f;
        _desiredTurnVelocity = 0f;
        _stateTimer = 0f;
        _restPending = false;
        _forwardExtended = false;
        _lookAngleTarget = 0f;
        _target = new Vector2(Position.X, _lostGripTargetY);
    }

    private void UpdateLostGripFall(float dt, FloatRect area)
    {
        var hasExactFullRenderArea = TryGetLostGripFullRenderSafeArea(
            area,
            out var fullRenderSafeArea);
        var constrainedPosition = fullRenderSafeArea.Clamp(Position);
        var safetyAdjustedThisStep = false;
        if (Vector2.DistanceSquared(constrainedPosition, Position) > 0.000001f)
        {
            // Display/taskbar geometry may change while this short event is
            // active. Move the host center into the new full-render-safe area
            // immediately; the animation rig consumes the same screen delta.
            Position = constrainedPosition;
            _target = new Vector2(Position.X, _target.Y);
            safetyAdjustedThisStep = true;
        }

        if (!hasExactFullRenderArea && LostGripPhase == LostGripFallPhase.Falling)
        {
            // A work-area shrink can make fitting the full transparent window
            // mathematically impossible. Clamp to the best available center
            // and catch immediately instead of extending a fall that can no
            // longer satisfy its screen-safety contract.
            _lostGripTargetY = Position.Y;
            _lostGripTargetDistance = Math.Max(0f, Position.Y - _lostGripStartY);
            _target = Position;
            BeginLostGripRegrip(
                Position.Y,
                LostGripCatchReason.SafetyForced);
            return;
        }

        switch (LostGripPhase)
        {
            case LostGripFallPhase.Falling:
                UpdateLostGripFreeFall(
                    dt,
                    fullRenderSafeArea.Bottom,
                    safetyAdjustedThisStep);
                break;

            case LostGripFallPhase.Regripping:
                UpdateLostGripRegrip(dt);
                break;

            default:
                throw new InvalidOperationException(
                    "LostGripFall state requires a falling or regripping phase.");
        }
    }

    private void UpdateLostGripFreeFall(
        float dt,
        float safeBottom,
        bool safetyAdjustedThisStep)
    {
        var fall = _configuration.LostGripFall;
        var safeTargetY = Math.Min(_lostGripTargetY, safeBottom);
        if (safeTargetY < _lostGripTargetY)
        {
            // The work area can change while falling (taskbar/display changes).
            // Retarget the catch point instead of ever advancing outside it.
            _lostGripTargetY = safeTargetY;
            _lostGripTargetDistance = Math.Max(0f, safeTargetY - _lostGripStartY);
            _target = new Vector2(Position.X, safeTargetY);
            safetyAdjustedThisStep = true;
        }

        var remainingDistance = Math.Max(0f, safeTargetY - Position.Y);
        if (remainingDistance <= 0.001f)
        {
            BeginLostGripRegrip(
                safeTargetY,
                safetyAdjustedThisStep
                    ? LostGripCatchReason.SafetyForced
                    : LostGripCatchReason.ReachedTarget);
            return;
        }

        var previousVelocity = _lostGripVerticalVelocity;
        _lostGripVerticalVelocity = Math.Min(
            fall.MaximumFallVelocity,
            previousVelocity + fall.Gravity * dt);
        var fallDistance = (previousVelocity + _lostGripVerticalVelocity) * 0.5f * dt;
        var appliedDistance = Math.Min(remainingDistance, fallDistance);
        Position = new Vector2(Position.X, Position.Y + appliedDistance);
        _lostGripDistance = Math.Max(0f, Position.Y - _lostGripStartY);

        if (appliedDistance + 0.001f >= remainingDistance)
        {
            BeginLostGripRegrip(
                safeTargetY,
                safetyAdjustedThisStep
                    ? LostGripCatchReason.SafetyForced
                    : LostGripCatchReason.ReachedTarget);
        }
    }

    private void BeginLostGripRegrip(
        float targetY,
        LostGripCatchReason catchReason)
    {
        Position = new Vector2(Position.X, targetY);
        _lostGripDistance = _lostGripTargetDistance;
        _lostGripVerticalVelocity = 0f;
        _lostGripRegripProgress = 0f;
        LostGripCatchReason = catchReason;
        LostGripPhase = LostGripFallPhase.Regripping;
        _stateTimer = _configuration.LostGripFall.RegripDuration;
    }

    private void UpdateLostGripRegrip(float dt)
    {
        var regripDuration = _configuration.LostGripFall.RegripDuration;
        if (_stateTimer <= 0f && _lostGripRegripProgress >= 1f)
        {
            LostGripPhase = LostGripFallPhase.None;
            LostGripCatchReason = LostGripCatchReason.None;
            TransitionTo(RoamingState.Idle, StateTransitionReason.FallComplete);
            _stateTimer = _configuration.LostGripFall.ResumeIdleDuration;
            _idleMayObserve = false;
            _desiredSpeed = 0f;
            _desiredTurnVelocity = 0f;
            return;
        }

        _stateTimer = Math.Max(0f, _stateTimer - dt);
        _lostGripRegripProgress = 1f - MathEx.Clamp01(_stateTimer / regripDuration);
    }

    private void ClearLostGripFallState()
    {
        LostGripPhase = LostGripFallPhase.None;
        _lostGripStartY = 0f;
        _lostGripTargetY = 0f;
        _lostGripDistance = 0f;
        _lostGripTargetDistance = 0f;
        _lostGripVerticalVelocity = 0f;
        _lostGripRegripProgress = 0f;
        LostGripCatchReason = LostGripCatchReason.None;
    }

    private bool TryGetLostGripFullRenderSafeArea(
        FloatRect area,
        out FloatRect safeArea)
    {
        safeArea = _lostGripSafety.SafeArea;
        return _lostGripSafety.IsAvailable;
    }

    private LostGripSafetyContext CreateDerivedLostGripSafety(FloatRect area)
    {
        var isAvailable = _boundaryNavigator.TryCreateExactInset(
            area,
            _configuration.LostGripFall.BottomSafetyInset,
            out var safeArea);
        return new LostGripSafetyContext(safeArea, isAvailable);
    }
}
