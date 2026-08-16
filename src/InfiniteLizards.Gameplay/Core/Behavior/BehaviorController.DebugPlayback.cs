namespace DesktopLizard.Core;

internal enum DebugPlaybackStatus
{
    Started,
    RedirectedToEdgeTurn,
    BlockedByPause,
    BlockedByProtectedState,
    UnsupportedAction
}

internal readonly record struct DebugPlaybackResult(
    DebugPlaybackStatus Status,
    AutonomousAction RequestedAction,
    RoamingState State)
{
    public bool Accepted => Status is
        DebugPlaybackStatus.Started or
        DebugPlaybackStatus.RedirectedToEdgeTurn;
}

internal static class DebugPlaybackPolicy
{
    public static bool CanRequest(RoamingState state, bool isPaused) =>
        !isPaused && state is not (
            RoamingState.Spawn or
            RoamingState.Grabbed or
            RoamingState.ReleaseSettle or
            RoamingState.EscapeSprint or
            RoamingState.EdgeTurn or
            RoamingState.LostGripFall);
}

internal sealed partial class BehaviorController
{
    /// <summary>
    /// Plays one matrix action through its normal state-entry code. The debug
    /// command is deliberately unavailable during real grab/release and other
    /// protected recovery phases; it never writes <see cref="State"/> directly.
    /// </summary>
    public DebugPlaybackResult TryPlayDebugAction(
        AutonomousAction action,
        FloatRect area)
    {
        var safety = CreateDerivedLostGripSafety(area);
        return TryPlayDebugAction(action, area, safety);
    }

    /// <summary>
    /// Debug playback follows the same host-provided safety contract as a
    /// natural transition; it cannot force LostGripFall through an unsafe
    /// physical work area.
    /// </summary>
    public DebugPlaybackResult TryPlayDebugAction(
        AutonomousAction action,
        FloatRect area,
        LostGripSafetyContext lostGripSafety)
    {
        if (!Enum.IsDefined(action))
        {
            return Result(DebugPlaybackStatus.UnsupportedAction);
        }
        if (IsPaused)
        {
            return Result(DebugPlaybackStatus.BlockedByPause);
        }
        if (!DebugPlaybackPolicy.CanRequest(State, isPaused: false))
        {
            return Result(DebugPlaybackStatus.BlockedByProtectedState);
        }

        // The pointer used to click the nearby sidecar must not immediately
        // replace the requested state with MouseChase. Re-arm only after that
        // pointer has left the configured chase neighborhood.
        SuppressMouseChase(0f, requireLeave: true);
        ResetTransientStateForDebugPlayback();
        _lostGripSafety = lostGripSafety;

        // Use canonical legal edges instead of granting debug a universal
        // transition reason. Intermediate states exist only inside this
        // synchronous command and therefore never produce a partial frame.
        if (State != RoamingState.Idle)
        {
            TransitionTo(RoamingState.Idle, StateTransitionReason.Command);
        }

        if (action is not AutonomousAction.Forward and
            not AutonomousAction.ForwardExtension)
        {
            TransitionTo(RoamingState.ForwardCrawl, StateTransitionReason.Choice);
            _desiredSpeed = _cruiseSpeed;
            _forwardExtended = false;
        }

        BeginAutonomousAction(
            action,
            area,
            _configuration.Timing.AfterForward.Minimum,
            _configuration.Timing.AfterForward.Maximum);

        var status = State == RoamingState.EdgeTurn
            ? DebugPlaybackStatus.RedirectedToEdgeTurn
            : DebugPlaybackStatus.Started;
        return Result(status);

        DebugPlaybackResult Result(DebugPlaybackStatus status) =>
            new(status, action, State);
    }

    private void ResetTransientStateForDebugPlayback()
    {
        DropProgress = 0f;
        ClearLostGripFallState();
        _sCurve.Reset();
        _restPending = false;
        _forwardExtended = false;
        _idleMayObserve = true;
        _motionWatchdog = 0f;
        _watchdogPosition = Position;
        _sprintDistance = 0f;
        _sprintTargetDistance = 0f;
        _sprintMinimumTimer = 0f;
        _boutTimer = _configuration.WalkBoutMaximumDuration;
        _lookAngleTarget = 0f;
    }
}
