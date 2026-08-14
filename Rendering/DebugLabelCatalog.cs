using DesktopLizard.Core;

namespace DesktopLizard.Rendering;

/// <summary>Single Chinese vocabulary for every debug surface.</summary>
internal static class DebugLabelCatalog
{
    public static string State(RoamingState state) => state switch
    {
        RoamingState.Spawn => "出场",
        RoamingState.Idle => "休息",
        RoamingState.Observe => "观察",
        RoamingState.ForwardCrawl => "向前爬行",
        RoamingState.FastForwardCrawl => "快速向前爬行",
        RoamingState.CurveCrawl => "曲线爬行",
        RoamingState.SCurveCrawl => "S形爬行",
        RoamingState.FastSCurveCrawl => "快速S弯爬行",
        RoamingState.TurnAround => "掉头",
        RoamingState.MouseChase => "追逐鼠标",
        RoamingState.EdgeTurn => "边缘转向",
        RoamingState.Grabbed => "被抓起",
        RoamingState.ReleaseSettle => "释放回稳",
        RoamingState.EscapeSprint => "释放冲刺",
        RoamingState.LostGripFall => "失手下坠",
        _ => state.ToString()
    };

    public static string Action(AutonomousAction action) => action switch
    {
        AutonomousAction.Forward => "前进",
        AutonomousAction.ForwardExtension => "续爬",
        AutonomousAction.Curve => "曲线",
        AutonomousAction.SCurve => "S弯",
        AutonomousAction.FastForward => "快进",
        AutonomousAction.FastSCurve => "快S",
        AutonomousAction.TurnAround => "掉头",
        AutonomousAction.LostGripFall => "失手",
        _ => action.ToString()
    };

    public static string TransitionRow(AutonomousTransitionRow row) => row switch
    {
        AutonomousTransitionRow.AfterForward => "前进后",
        AutonomousTransitionRow.AfterCurve => "曲线后",
        AutonomousTransitionRow.AfterSCurve => "S弯后",
        AutonomousTransitionRow.AfterFast => "快速动作后",
        _ => "无随机行"
    };

    public static string LostGripPhase(LostGripFallPhase phase) => phase switch
    {
        LostGripFallPhase.None => "未激活",
        LostGripFallPhase.Falling => "自由下坠",
        LostGripFallPhase.Regripping => "重新抓稳",
        _ => phase.ToString()
    };

    public static string Reason(StateTransitionReason reason) => reason switch
    {
        StateTransitionReason.SpawnComplete => "出场完成",
        StateTransitionReason.Timer => "计时结束",
        StateTransitionReason.Choice => "随机选择",
        StateTransitionReason.Boundary => "边缘保护",
        StateTransitionReason.Watchdog => "运动恢复",
        StateTransitionReason.Pointer => "鼠标触发",
        StateTransitionReason.Grab => "鼠标抓取",
        StateTransitionReason.Release => "鼠标释放",
        StateTransitionReason.SprintComplete => "冲刺完成",
        StateTransitionReason.Pause => "暂停",
        StateTransitionReason.Command => "用户命令",
        StateTransitionReason.FallComplete => "重新抓稳",
        _ => reason.ToString()
    };
}
