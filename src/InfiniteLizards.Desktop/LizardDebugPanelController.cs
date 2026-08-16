using System.Collections.Immutable;
using System.Globalization;
using InfiniteLizards.Desktop.Rendering;
using InfiniteLizards.Gameplay;

namespace InfiniteLizards.Desktop;

/// <summary>Maps the immutable Gameplay debug seam into Desktop-only views.</summary>
internal sealed class LizardDebugPanelController : IDisposable
{
    private static readonly ImmutableArray<PortableDebugAction> Actions =
        Enum.GetValues<PortableDebugAction>().ToImmutableArray();

    private readonly ILizardPortableDebugBridge _bridge;
    private readonly PetWindowHost _host;
    private readonly DiagnosticPanelWindow _panel;
    private readonly LizardView _lizardView;
    private readonly LizardDebugOverlayCoordinator _overlay;
    private readonly DebugPanelProbabilityMemory _probabilityMemory = new();
    private bool _disposed;

    public LizardDebugPanelController(
        ILizardPortableDebugBridge bridge,
        PetWindowHost host,
        DiagnosticPanelWindow panel,
        LizardView lizardView)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _lizardView = lizardView ?? throw new ArgumentNullException(nameof(lizardView));
        _overlay = new LizardDebugOverlayCoordinator(
            _bridge.PathPolicy,
            lizardView.CanvasCenterModel,
            lizardView.ModelToWorldScale);
        _host.DiagnosticSnapshotUpdated += OnHostSnapshot;
        _host.DebugOverlayEnabledChanged += OnEnabledChanged;
        _host.DebugPathResetRequested += OnPathResetRequested;
        _panel.ActionRequested += OnActionRequested;
        _host.Closed += OnHostClosed;
    }

    private void OnHostSnapshot(object? sender, DesktopPetDiagnosticSnapshot host)
    {
        if (!_bridge.TryCapture(out var frame))
        {
            return;
        }

        var probability = _probabilityMemory.Observe(
            frame.TransitionRow,
            frame.ForwardExtended);

        _panel.SetDebugPresentation(BuildPresentation(
            _bridge,
            frame,
            host,
            probability.Row,
            probability.ForwardExtended,
            probability.HasCurrentRow));
        _lizardView.SetDebugOverlay(_overlay.Capture(frame, host));
    }

    private void OnActionRequested(string id)
    {
        if (TryParseAction(id, out var action))
        {
            _ = _bridge.TryPlay(action);
        }
    }

    private void OnEnabledChanged(object? sender, bool enabled)
    {
        if (!enabled)
        {
            _lizardView.ClearDebugOverlay();
        }
        else
        {
            _bridge.RequestPathReset();
        }
    }

    private void OnPathResetRequested(object? sender, EventArgs e) =>
        _bridge.RequestPathReset();

    private void OnHostClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _host.DiagnosticSnapshotUpdated -= OnHostSnapshot;
        _host.DebugOverlayEnabledChanged -= OnEnabledChanged;
        _host.DebugPathResetRequested -= OnPathResetRequested;
        _panel.ActionRequested -= OnActionRequested;
        _host.Closed -= OnHostClosed;
        _lizardView.ClearDebugOverlay();
    }

    internal static DebugPanelPresentation BuildPresentation(
        ILizardPortableDebugBridge bridge,
        PortableLizardDebugSnapshot frame,
        DesktopPetDiagnosticSnapshot host,
        PortableDebugTransitionRow displayedRow,
        bool displayedForwardExtended,
        bool hasCurrentRow)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(frame);
        var pointerStatus = !host.PointerAvailable
            ? "不可用"
            : host.PointerBlocked ? "交互锁定" : "可追踪";
        var pointerDistance = host.PointerAvailable && float.IsFinite(host.PointerDistance)
            ? $"{host.PointerDistance:F0}px"
            : "--";
        var cycleIndex = frame.SCurveCycleCount > 0 && frame.SCurveCycleDuration > 0.01f
            ? Math.Min(frame.SCurveCycleCount,
                (int)(frame.SCurveElapsed / frame.SCurveCycleDuration) + 1)
            : 0;
        var estimatedRadius = frame.SCurveAmplitude > 0.01f
            ? Math.Max(0f, frame.DesiredSpeed) / frame.SCurveAmplitude
            : 0f;
        var sCurve = IsSCurve(frame.State)
            ? $"段{cycleIndex}/{frame.SCurveCycleCount}  周{frame.SCurveCycleDuration:F1}s  半径≈{estimatedRadius:F0}px"
            : "未激活";
        var lostGripProgress = LostGripProgressLabel(
            frame.LostGripPhase,
            frame.LostGripCatchReason,
            frame.LostGripReachProgress,
            frame.LostGripRegripProgress);

        var left = string.Join('\n',
            $"状态  {StateLabel(frame.State)}",
            $"切换  {ReasonLabel(frame.LastTransitionReason)}  #{frame.TransitionSerial}",
            $"计时  {Math.Max(0f, frame.StateTimeRemaining):F1}s / {Math.Max(0f, frame.BoutTimeRemaining):F1}s",
            $"速度  {frame.Speed:F1} → {frame.DesiredSpeed:F1}");
        var right = frame.State == PortableLizardState.LostGripFall
            ? string.Join('\n',
                $"阶段  {LostGripLabel(frame.LostGripPhase)}",
                $"{lostGripProgress}  v{frame.LostGripVerticalVelocity:F0}",
                $"下坠  {frame.LostGripDistance:F0}/{frame.LostGripTargetDistance:F0}px",
                $"步态  {frame.LastGaitDisplaySpeed:F1}  P{frame.NextPair}  帧率{host.FramesPerSecond:F0}")
            : string.Join('\n',
                $"转向  {frame.TurnVelocity:F2} → {frame.DesiredTurnVelocity:F2}",
                $"鼠标  {pointerStatus}  {pointerDistance}",
                $"S弯   {sCurve}",
                $"步态  {frame.LastGaitDisplaySpeed:F1}  P{frame.NextPair}  帧率{host.FramesPerSecond:F0}");

        var actionViews = Actions.Select(action => new DebugPanelActionPresentation(
            ActionId(action),
            ActionLabel(action),
            bridge.GetActionProbability(displayedRow, action, displayedForwardExtended),
            $"立即播放：{ActionLabel(action)}")).ToImmutableArray();
        return new DebugPanelPresentation(
            left,
            right,
            TransitionRowLabel(displayedRow),
            hasCurrentRow,
            frame.PlaybackAvailable,
            actionViews);
    }

    internal static string ActionId(PortableDebugAction action) => action switch
    {
        PortableDebugAction.Forward => "forward",
        PortableDebugAction.ForwardExtension => "forward-extension",
        PortableDebugAction.Curve => "curve",
        PortableDebugAction.SCurve => "s-curve",
        PortableDebugAction.FastForward => "fast-forward",
        PortableDebugAction.FastSCurve => "fast-s-curve",
        PortableDebugAction.TurnAround => "turn-around",
        PortableDebugAction.LostGripFall => "lost-grip-fall",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    private static bool TryParseAction(string id, out PortableDebugAction action)
    {
        foreach (var candidate in Actions)
        {
            if (string.Equals(ActionId(candidate), id, StringComparison.Ordinal))
            {
                action = candidate;
                return true;
            }
        }
        action = default;
        return false;
    }

    private static bool IsSCurve(PortableLizardState state) => state is
        PortableLizardState.SCurveCrawl or PortableLizardState.FastSCurveCrawl;

    private static string StateLabel(PortableLizardState state) => state switch
    {
        PortableLizardState.Spawn => "出场", PortableLizardState.Idle => "休息",
        PortableLizardState.Observe => "观察", PortableLizardState.ForwardCrawl => "向前爬行",
        PortableLizardState.FastForwardCrawl => "快速向前爬行", PortableLizardState.CurveCrawl => "曲线爬行",
        PortableLizardState.SCurveCrawl => "S形爬行", PortableLizardState.FastSCurveCrawl => "快速S弯爬行",
        PortableLizardState.TurnAround => "掉头", PortableLizardState.MouseChase => "追逐鼠标",
        PortableLizardState.EdgeTurn => "边缘转向", PortableLizardState.Grabbed => "被抓起",
        PortableLizardState.ReleaseSettle => "释放回稳", PortableLizardState.EscapeSprint => "释放冲刺",
        PortableLizardState.LostGripFall => "失手下坠", _ => state.ToString()
    };

    private static string ReasonLabel(PortableDebugTransitionReason reason) => reason switch
    {
        PortableDebugTransitionReason.SpawnComplete => "出场完成", PortableDebugTransitionReason.Timer => "计时结束",
        PortableDebugTransitionReason.Choice => "随机选择", PortableDebugTransitionReason.Boundary => "边缘保护",
        PortableDebugTransitionReason.Watchdog => "运动恢复", PortableDebugTransitionReason.Pointer => "鼠标触发",
        PortableDebugTransitionReason.Grab => "鼠标抓取", PortableDebugTransitionReason.Release => "鼠标释放",
        PortableDebugTransitionReason.SprintComplete => "冲刺完成", PortableDebugTransitionReason.Pause => "暂停",
        PortableDebugTransitionReason.Command => "用户命令", PortableDebugTransitionReason.FallComplete => "重新抓稳",
        _ => reason.ToString()
    };

    private static string LostGripLabel(PortableDebugLostGripPhase phase) => phase switch
    {
        PortableDebugLostGripPhase.None => "未激活", PortableDebugLostGripPhase.Falling => "自由下坠",
        PortableDebugLostGripPhase.Regripping => "重新抓稳", _ => phase.ToString()
    };

    internal static string LostGripProgressLabel(
        PortableDebugLostGripPhase phase,
        PortableDebugLostGripCatchReason catchReason,
        float reachProgress,
        float regripProgress) => phase switch
        {
            PortableDebugLostGripPhase.Falling =>
                $"伸手{FormatPercent(reachProgress)}",
            PortableDebugLostGripPhase.Regripping
                when catchReason == PortableDebugLostGripCatchReason.SafetyForced =>
                $"安全恢复{FormatPercent(regripProgress)}",
            PortableDebugLostGripPhase.Regripping =>
                $"抓稳{FormatPercent(regripProgress)}",
            _ => "等待"
        };

    private static string FormatPercent(float value) =>
        (Math.Clamp(float.IsFinite(value) ? value : 0f, 0f, 1f) * 100f)
        .ToString("F0", CultureInfo.InvariantCulture) + "%";

    private static string TransitionRowLabel(PortableDebugTransitionRow row) => row switch
    {
        PortableDebugTransitionRow.AfterForward => "前进后", PortableDebugTransitionRow.AfterCurve => "曲线后",
        PortableDebugTransitionRow.AfterSCurve => "S弯后", PortableDebugTransitionRow.AfterFast => "快速动作后",
        _ => "无随机行"
    };

    private static string ActionLabel(PortableDebugAction action) => action switch
    {
        PortableDebugAction.Forward => "前进", PortableDebugAction.ForwardExtension => "续爬",
        PortableDebugAction.Curve => "曲线", PortableDebugAction.SCurve => "S弯",
        PortableDebugAction.FastForward => "快进", PortableDebugAction.FastSCurve => "快S",
        PortableDebugAction.TurnAround => "掉头", PortableDebugAction.LostGripFall => "失手",
        _ => action.ToString()
    };
}

internal readonly record struct DebugPanelProbabilityContext(
    PortableDebugTransitionRow Row,
    bool ForwardExtended,
    bool HasCurrentRow);

/// <summary>
/// Mirrors the WPF action strip: states without a transition row keep showing
/// the most recently observed matrix row, with AfterForward as the initial
/// context, while the header switches from “next” to “recent”.
/// </summary>
internal sealed class DebugPanelProbabilityMemory
{
    private PortableDebugTransitionRow _row =
        PortableDebugTransitionRow.AfterForward;
    private bool _forwardExtended;

    public DebugPanelProbabilityContext Observe(
        PortableDebugTransitionRow currentRow,
        bool forwardExtended)
    {
        var hasCurrentRow = currentRow != PortableDebugTransitionRow.None;
        if (hasCurrentRow)
        {
            _row = currentRow;
            _forwardExtended =
                currentRow == PortableDebugTransitionRow.AfterForward &&
                forwardExtended;
        }

        return new DebugPanelProbabilityContext(
            _row,
            _forwardExtended,
            hasCurrentRow);
    }
}
