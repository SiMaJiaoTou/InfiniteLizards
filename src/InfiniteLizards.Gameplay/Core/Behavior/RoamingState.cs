namespace DesktopLizard.Core;

internal enum RoamingState
{
    Spawn,
    Idle,
    Observe,
    ForwardCrawl,
    FastForwardCrawl,
    CurveCrawl,
    SCurveCrawl,
    FastSCurveCrawl,
    TurnAround,
    MouseChase,
    EdgeTurn,
    Grabbed,
    ReleaseSettle,
    EscapeSprint,
    LostGripFall
}

internal enum LostGripFallPhase
{
    None,
    Falling,
    Regripping
}

/// <summary>
/// Distinguishes a normal catch at the sampled endpoint from an emergency stop
/// caused by a live work-area change. This is transition metadata, not another
/// behavior state.
/// </summary>
internal enum LostGripCatchReason
{
    None,
    ReachedTarget,
    SafetyForced
}

internal static class RoamingStateTraits
{
    public static bool IsLocomoting(this RoamingState state) => state is
        RoamingState.ForwardCrawl or
        RoamingState.FastForwardCrawl or
        RoamingState.CurveCrawl or
        RoamingState.SCurveCrawl or
        RoamingState.FastSCurveCrawl or
        RoamingState.TurnAround or
        RoamingState.MouseChase or
        RoamingState.EscapeSprint or
        RoamingState.EdgeTurn;

    public static bool IsResting(this RoamingState state) => state is
        RoamingState.Spawn or
        RoamingState.Idle or
        RoamingState.Observe;

    public static bool IsSCurveCrawl(this RoamingState state) => state is
        RoamingState.SCurveCrawl or
        RoamingState.FastSCurveCrawl;

    public static bool IsFastCrawl(this RoamingState state) => state is
        RoamingState.FastForwardCrawl or
        RoamingState.FastSCurveCrawl;
}

internal enum StateTransitionReason
{
    SpawnComplete,
    Timer,
    Choice,
    Boundary,
    Watchdog,
    Pointer,
    Grab,
    Release,
    SprintComplete,
    Pause,
    Command,
    FallComplete
}

internal static class RoamingStateMachine
{
    public static bool IsLegal(RoamingState from, RoamingState to, StateTransitionReason reason)
    {
        if (from == to)
        {
            return true;
        }

        if (to == RoamingState.Grabbed)
        {
            return reason == StateTransitionReason.Grab;
        }

        if (to == RoamingState.LostGripFall)
        {
            return reason == StateTransitionReason.Choice && from is
                RoamingState.ForwardCrawl or
                RoamingState.FastForwardCrawl or
                RoamingState.CurveCrawl or
                RoamingState.SCurveCrawl or
                RoamingState.FastSCurveCrawl;
        }

        if (reason == StateTransitionReason.Pause)
        {
            return to == RoamingState.Idle;
        }

        if (reason == StateTransitionReason.Command)
        {
            return to == RoamingState.Idle && from != RoamingState.Grabbed;
        }

        return from switch
        {
            RoamingState.Spawn => to == RoamingState.Idle,
            RoamingState.Idle => to is RoamingState.Observe or RoamingState.ForwardCrawl or RoamingState.EdgeTurn or RoamingState.MouseChase,
            RoamingState.Observe => to is RoamingState.ForwardCrawl or RoamingState.EdgeTurn or RoamingState.MouseChase,
            RoamingState.ForwardCrawl => to is RoamingState.FastForwardCrawl or RoamingState.CurveCrawl or RoamingState.SCurveCrawl or RoamingState.FastSCurveCrawl or RoamingState.TurnAround or RoamingState.Idle or RoamingState.Observe or RoamingState.EdgeTurn or RoamingState.MouseChase,
            RoamingState.FastForwardCrawl => to is RoamingState.ForwardCrawl or RoamingState.CurveCrawl or RoamingState.SCurveCrawl or RoamingState.Idle or RoamingState.Observe or RoamingState.EdgeTurn or RoamingState.MouseChase,
            RoamingState.CurveCrawl => to is RoamingState.ForwardCrawl or RoamingState.FastForwardCrawl or RoamingState.SCurveCrawl or RoamingState.FastSCurveCrawl or RoamingState.TurnAround or RoamingState.Idle or RoamingState.Observe or RoamingState.EdgeTurn or RoamingState.MouseChase,
            RoamingState.SCurveCrawl => to is RoamingState.ForwardCrawl or RoamingState.FastForwardCrawl or RoamingState.CurveCrawl or RoamingState.FastSCurveCrawl or RoamingState.TurnAround or RoamingState.Idle or RoamingState.Observe or RoamingState.EdgeTurn or RoamingState.MouseChase,
            RoamingState.FastSCurveCrawl => to is RoamingState.ForwardCrawl or RoamingState.CurveCrawl or RoamingState.SCurveCrawl or RoamingState.Idle or RoamingState.Observe or RoamingState.EdgeTurn or RoamingState.MouseChase,
            RoamingState.TurnAround => to is RoamingState.ForwardCrawl or RoamingState.EdgeTurn or RoamingState.MouseChase,
            RoamingState.MouseChase => to is RoamingState.ForwardCrawl or RoamingState.EdgeTurn,
            RoamingState.EdgeTurn => to == RoamingState.ForwardCrawl,
            RoamingState.Grabbed => to is RoamingState.ReleaseSettle or RoamingState.Idle,
            RoamingState.ReleaseSettle => to is RoamingState.EscapeSprint or RoamingState.EdgeTurn,
            RoamingState.EscapeSprint => to is RoamingState.ForwardCrawl or RoamingState.EdgeTurn,
            RoamingState.LostGripFall =>
                to == RoamingState.Idle && reason == StateTransitionReason.FallComplete,
            _ => false
        };
    }
}
