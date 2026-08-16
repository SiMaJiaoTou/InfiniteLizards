using System.Collections.Immutable;
using System.Numerics;
using DesktopPet.Engine;
using DesktopLizard.AppRuntime;
using DesktopLizard.Core;

namespace InfiniteLizards.Gameplay;

/// <summary>
/// Stable, immutable diagnostic seam for platform hosts. The bridge deliberately
/// contains no display cadence, backing-scale, pointer, window, or renderer
/// data; those values belong to the desktop host that samples them.
/// </summary>
internal interface ILizardPortableDebugBridge
{
    PortableDebugPathPolicy PathPolicy { get; }

    bool TryCapture(out PortableLizardDebugSnapshot snapshot);

    PortableDebugPlaybackResult TryPlay(PortableDebugAction action);

    float GetActionProbability(
        PortableDebugTransitionRow row,
        PortableDebugAction action,
        bool forwardExtended);

    void RequestPathReset();
}

internal enum PortableLizardState
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

internal enum PortableDebugTransitionReason
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

internal enum PortableDebugLostGripPhase
{
    None,
    Falling,
    Regripping
}

internal enum PortableDebugLostGripCatchReason
{
    None,
    ReachedTarget,
    SafetyForced
}

internal enum PortableDebugTransitionRow
{
    None,
    AfterForward,
    AfterCurve,
    AfterSCurve,
    AfterFast
}

internal enum PortableDebugAction
{
    Forward,
    ForwardExtension,
    Curve,
    SCurve,
    FastForward,
    FastSCurve,
    TurnAround,
    LostGripFall
}

internal enum PortableDebugPlaybackStatus
{
    Started,
    RedirectedToEdgeTurn,
    BlockedByPause,
    BlockedByProtectedState,
    UnsupportedAction,
    EnvironmentUnavailable
}

internal readonly record struct PortableDebugPathPolicy(
    float SampleInterval,
    float MinimumDistance,
    int Capacity);

internal readonly record struct PortableDebugPlaybackResult(
    PortableDebugPlaybackStatus Status,
    PortableDebugAction RequestedAction,
    PortableLizardState State)
{
    public bool Accepted => Status is
        PortableDebugPlaybackStatus.Started or
        PortableDebugPlaybackStatus.RedirectedToEdgeTurn;
}

internal readonly record struct PortableLizardLegDebugSnapshot(
    int Index,
    int Pair,
    Vector2 Shoulder,
    Vector2 Elbow,
    Vector2 Foot,
    Vector2 StepTo,
    bool IsStepping,
    float MaximumReach);

/// <summary>
/// Gameplay-only values used by both the WPF and Avalonia diagnostic panels.
/// A host may combine this with its own FPS/pointer/display information, but
/// cannot use the snapshot to mutate the simulation.
/// </summary>
internal sealed record PortableLizardDebugSnapshot(
    PortableLizardState State,
    PortableDebugTransitionReason LastTransitionReason,
    int TransitionSerial,
    Vector2 Position,
    Vector2 Target,
    Vector2 LookTarget,
    float StateTimeRemaining,
    float BoutTimeRemaining,
    float Speed,
    float DesiredSpeed,
    float TurnVelocity,
    float DesiredTurnVelocity,
    bool IsPaused,
    bool ForwardExtended,
    PortableDebugLostGripPhase LostGripPhase,
    PortableDebugLostGripCatchReason LostGripCatchReason,
    float LostGripReachProgress,
    float LostGripRegripProgress,
    float LostGripVerticalVelocity,
    float LostGripDistance,
    float LostGripTargetDistance,
    float SCurveElapsed,
    float SCurveDuration,
    float SCurveCycleDuration,
    int SCurveCycleCount,
    float SCurveProgress,
    float SCurveAmplitude,
    float LastGaitDisplaySpeed,
    int NextPair,
    ImmutableArray<Vector2> SpineJoints,
    ImmutableArray<PortableLizardLegDebugSnapshot> Legs,
    float LizardHeading,
    PortableDebugTransitionRow TransitionRow,
    bool PlaybackAvailable,
    int SimulationResetGeneration,
    int PathResetGeneration,
    Vector2 CumulativeWorldRebase);

internal sealed class LizardPortableDebugBridge : ILizardPortableDebugBridge
{
    private readonly PetSimulationSession _session;
    private readonly LizardProfile _profile;
    private WorldRect _navigationArea;
    private SafetyArea _fullRenderSafety;
    private bool _hasEnvironment;
    private int _pathResetGeneration;

    public PortableDebugPathPolicy PathPolicy { get; }

    public LizardPortableDebugBridge(
        PetSimulationSession session,
        LizardProfile profile)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        PathPolicy = new PortableDebugPathPolicy(
            profile.Runtime.DebugPathSampleInterval,
            profile.Runtime.DebugPathMinimumDistance,
            profile.Runtime.DebugPathCapacity);
    }

    public bool TryCapture(out PortableLizardDebugSnapshot snapshot)
    {
        if (!_hasEnvironment)
        {
            snapshot = null!;
            return false;
        }

        var behavior = _session.BehaviorForDiagnostics.CaptureDebugSnapshot(
            ToCore(_navigationArea));
        var lizard = _session.LizardForDiagnostics.CaptureDebugSnapshot();
        var legs = ImmutableArray.CreateBuilder<PortableLizardLegDebugSnapshot>(
            lizard.Legs.Length);
        foreach (var leg in lizard.Legs)
        {
            legs.Add(new PortableLizardLegDebugSnapshot(
                leg.Index,
                leg.Pair,
                leg.Shoulder,
                leg.Elbow,
                leg.Foot,
                leg.StepTo,
                leg.IsStepping,
                leg.MaximumReach));
        }
        snapshot = new PortableLizardDebugSnapshot(
            ToPortable(behavior.State),
            ToPortable(behavior.LastTransitionReason),
            behavior.TransitionSerial,
            behavior.Position,
            behavior.Target,
            behavior.LookTarget,
            behavior.StateTimeRemaining,
            behavior.BoutTimeRemaining,
            behavior.Speed,
            behavior.DesiredSpeed,
            behavior.TurnVelocity,
            behavior.DesiredTurnVelocity,
            behavior.IsPaused,
            behavior.ForwardExtended,
            ToPortable(behavior.LostGripPhase),
            ToPortable(behavior.LostGripCatchReason),
            behavior.LostGripReachProgress,
            behavior.LostGripRegripProgress,
            behavior.LostGripVerticalVelocity,
            behavior.LostGripDistance,
            behavior.LostGripTargetDistance,
            behavior.SCurveElapsed,
            behavior.SCurveDuration,
            behavior.SCurveCycleDuration,
            behavior.SCurveCycleCount,
            behavior.SCurveProgress,
            behavior.SCurveAmplitude,
            lizard.LastGaitDisplaySpeed,
            lizard.NextPair,
            lizard.SpineJoints,
            legs.MoveToImmutable(),
            lizard.Heading,
            ToPortable(AutonomousTransitionInspector.RowFor(behavior.State)),
            DebugPlaybackPolicy.CanRequest(behavior.State, behavior.IsPaused),
            _session.ResetGeneration,
            _pathResetGeneration,
            _session.CumulativeWorldRebase);
        return true;
    }

    public PortableDebugPlaybackResult TryPlay(PortableDebugAction action)
    {
        if (!Enum.IsDefined(action))
        {
            return new PortableDebugPlaybackResult(
                PortableDebugPlaybackStatus.UnsupportedAction,
                action,
                ToPortable(_session.BehaviorForDiagnostics.State));
        }
        if (!_hasEnvironment)
        {
            return new PortableDebugPlaybackResult(
                PortableDebugPlaybackStatus.EnvironmentUnavailable,
                action,
                ToPortable(_session.BehaviorForDiagnostics.State));
        }

        var result = _session.TryPlayDebugAction(
            ToCore(action),
            ToCore(_navigationArea),
            new LostGripSafetyContext(
                ToCore(_fullRenderSafety.Area),
                _fullRenderSafety.IsAvailable));
        var portable = new PortableDebugPlaybackResult(
            ToPortable(result.Status),
            action,
            ToPortable(result.State));
        if (portable.Accepted)
        {
            // The WPF panel starts every explicitly selected motion with a
            // clean path. Preserve that command semantic without owning the
            // path itself: hosts observe the generation change and clear
            // their own world-space trail.
            RequestPathReset();
        }
        return portable;
    }

    public float GetActionProbability(
        PortableDebugTransitionRow row,
        PortableDebugAction action,
        bool forwardExtended)
    {
        if (!Enum.IsDefined(row) ||
            row == PortableDebugTransitionRow.None ||
            !Enum.IsDefined(action))
        {
            return float.NaN;
        }

        return AutonomousTransitionInspector.EffectiveProbability(
            _profile.Behavior.TransitionMatrix,
            ToCore(row),
            ToCore(action),
            forwardExtended);
    }

    public void RequestPathReset() =>
        _pathResetGeneration = unchecked(_pathResetGeneration + 1);

    internal void ObserveEnvironment(in DesktopPetFixedStepInput input)
    {
        _navigationArea = input.NavigationArea;
        _fullRenderSafety = input.FullRenderSafety;
        _hasEnvironment = true;
    }

    internal void ObserveSimulationReset()
    {
        _hasEnvironment = false;
        RequestPathReset();
    }

    private static FloatRect ToCore(WorldRect value) => new(
        value.Left,
        value.Top,
        value.Right,
        value.Bottom);

    private static PortableLizardState ToPortable(RoamingState value) => value switch
    {
        RoamingState.Spawn => PortableLizardState.Spawn,
        RoamingState.Idle => PortableLizardState.Idle,
        RoamingState.Observe => PortableLizardState.Observe,
        RoamingState.ForwardCrawl => PortableLizardState.ForwardCrawl,
        RoamingState.FastForwardCrawl => PortableLizardState.FastForwardCrawl,
        RoamingState.CurveCrawl => PortableLizardState.CurveCrawl,
        RoamingState.SCurveCrawl => PortableLizardState.SCurveCrawl,
        RoamingState.FastSCurveCrawl => PortableLizardState.FastSCurveCrawl,
        RoamingState.TurnAround => PortableLizardState.TurnAround,
        RoamingState.MouseChase => PortableLizardState.MouseChase,
        RoamingState.EdgeTurn => PortableLizardState.EdgeTurn,
        RoamingState.Grabbed => PortableLizardState.Grabbed,
        RoamingState.ReleaseSettle => PortableLizardState.ReleaseSettle,
        RoamingState.EscapeSprint => PortableLizardState.EscapeSprint,
        RoamingState.LostGripFall => PortableLizardState.LostGripFall,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static PortableDebugTransitionReason ToPortable(
        StateTransitionReason value) => value switch
    {
        StateTransitionReason.SpawnComplete => PortableDebugTransitionReason.SpawnComplete,
        StateTransitionReason.Timer => PortableDebugTransitionReason.Timer,
        StateTransitionReason.Choice => PortableDebugTransitionReason.Choice,
        StateTransitionReason.Boundary => PortableDebugTransitionReason.Boundary,
        StateTransitionReason.Watchdog => PortableDebugTransitionReason.Watchdog,
        StateTransitionReason.Pointer => PortableDebugTransitionReason.Pointer,
        StateTransitionReason.Grab => PortableDebugTransitionReason.Grab,
        StateTransitionReason.Release => PortableDebugTransitionReason.Release,
        StateTransitionReason.SprintComplete => PortableDebugTransitionReason.SprintComplete,
        StateTransitionReason.Pause => PortableDebugTransitionReason.Pause,
        StateTransitionReason.Command => PortableDebugTransitionReason.Command,
        StateTransitionReason.FallComplete => PortableDebugTransitionReason.FallComplete,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static PortableDebugLostGripPhase ToPortable(
        LostGripFallPhase value) => value switch
    {
        LostGripFallPhase.None => PortableDebugLostGripPhase.None,
        LostGripFallPhase.Falling => PortableDebugLostGripPhase.Falling,
        LostGripFallPhase.Regripping => PortableDebugLostGripPhase.Regripping,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static PortableDebugLostGripCatchReason ToPortable(
        LostGripCatchReason value) => value switch
    {
        LostGripCatchReason.None => PortableDebugLostGripCatchReason.None,
        LostGripCatchReason.ReachedTarget =>
            PortableDebugLostGripCatchReason.ReachedTarget,
        LostGripCatchReason.SafetyForced =>
            PortableDebugLostGripCatchReason.SafetyForced,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static PortableDebugTransitionRow ToPortable(
        AutonomousTransitionRow value) => value switch
    {
        AutonomousTransitionRow.None => PortableDebugTransitionRow.None,
        AutonomousTransitionRow.AfterForward => PortableDebugTransitionRow.AfterForward,
        AutonomousTransitionRow.AfterCurve => PortableDebugTransitionRow.AfterCurve,
        AutonomousTransitionRow.AfterSCurve => PortableDebugTransitionRow.AfterSCurve,
        AutonomousTransitionRow.AfterFast => PortableDebugTransitionRow.AfterFast,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static PortableDebugPlaybackStatus ToPortable(
        DebugPlaybackStatus value) => value switch
    {
        DebugPlaybackStatus.Started => PortableDebugPlaybackStatus.Started,
        DebugPlaybackStatus.RedirectedToEdgeTurn =>
            PortableDebugPlaybackStatus.RedirectedToEdgeTurn,
        DebugPlaybackStatus.BlockedByPause => PortableDebugPlaybackStatus.BlockedByPause,
        DebugPlaybackStatus.BlockedByProtectedState =>
            PortableDebugPlaybackStatus.BlockedByProtectedState,
        DebugPlaybackStatus.UnsupportedAction =>
            PortableDebugPlaybackStatus.UnsupportedAction,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static AutonomousAction ToCore(PortableDebugAction value) => value switch
    {
        PortableDebugAction.Forward => AutonomousAction.Forward,
        PortableDebugAction.ForwardExtension => AutonomousAction.ForwardExtension,
        PortableDebugAction.Curve => AutonomousAction.Curve,
        PortableDebugAction.SCurve => AutonomousAction.SCurve,
        PortableDebugAction.FastForward => AutonomousAction.FastForward,
        PortableDebugAction.FastSCurve => AutonomousAction.FastSCurve,
        PortableDebugAction.TurnAround => AutonomousAction.TurnAround,
        PortableDebugAction.LostGripFall => AutonomousAction.LostGripFall,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static AutonomousTransitionRow ToCore(
        PortableDebugTransitionRow value) => value switch
    {
        PortableDebugTransitionRow.None => AutonomousTransitionRow.None,
        PortableDebugTransitionRow.AfterForward => AutonomousTransitionRow.AfterForward,
        PortableDebugTransitionRow.AfterCurve => AutonomousTransitionRow.AfterCurve,
        PortableDebugTransitionRow.AfterSCurve => AutonomousTransitionRow.AfterSCurve,
        PortableDebugTransitionRow.AfterFast => AutonomousTransitionRow.AfterFast,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };
}
