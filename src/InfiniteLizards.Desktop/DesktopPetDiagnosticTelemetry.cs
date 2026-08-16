using System.Globalization;
using System.Numerics;

namespace InfiniteLizards.Desktop;

internal sealed record DesktopPetDebugSettings
{
    public static DesktopPetDebugSettings Default { get; } = new(
        PanelWidthPixels: 360,
        PanelHeightPixels: 132,
        PanelGapPixels: 13,
        FpsResponse: 4d,
        MaximumFps: 999d);

    public DesktopPetDebugSettings(
        int PanelWidthPixels,
        int PanelHeightPixels,
        int PanelGapPixels,
        double FpsResponse,
        double MaximumFps)
    {
        if (PanelWidthPixels < 80 || PanelHeightPixels < 40 || PanelGapPixels < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PanelWidthPixels),
                "Debug panel dimensions and gap are outside the supported range.");
        }
        if (!double.IsFinite(FpsResponse) || FpsResponse <= 0d ||
            !double.IsFinite(MaximumFps) || MaximumFps <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FpsResponse),
                "Debug FPS filtering values must be finite and positive.");
        }

        this.PanelWidthPixels = PanelWidthPixels;
        this.PanelHeightPixels = PanelHeightPixels;
        this.PanelGapPixels = PanelGapPixels;
        this.FpsResponse = FpsResponse;
        this.MaximumFps = MaximumFps;
    }

    public int PanelWidthPixels { get; }
    public int PanelHeightPixels { get; }
    public int PanelGapPixels { get; }
    public double FpsResponse { get; }
    public double MaximumFps { get; }
}

internal readonly record struct DesktopPetDiagnosticSnapshot(
    long DisplayFrame,
    double FramesPerSecond,
    float FrameDelta,
    float FixedStep,
    int SimulationSteps,
    Vector2 Position,
    string DisplayId,
    double DisplayScale,
    bool PointerAvailable,
    bool PointerBlocked,
    Vector2 PointerPosition,
    float PointerDistance,
    bool IsPaused,
    bool IsDragging,
    bool WasPresented,
    long PresenterRegionVersion,
    bool RegionBackendAvailable,
    bool RegionAttempted,
    bool RegionSynchronized,
    long SubmittedRegionVersion,
    int PendingRegionPoses,
    int AcknowledgedRegionPoses)
{
    public string FormatPanelText()
    {
        var culture = CultureInfo.InvariantCulture;
        var frameState = WasPresented ? "submitted" : "back-pressured";
        var regionState = !RegionBackendAvailable
            ? "presenter-only (platform shape not required)"
            : !RegionAttempted
                ? "waiting"
                : RegionSynchronized
                    ? "update accepted (interactive native shape bypassed)"
                    : "failed / fail-closed";

        return string.Join(
            Environment.NewLine,
            $"FPS                 {FramesPerSecond.ToString("F1", culture)}",
            $"Display frame       {DisplayFrame.ToString(culture)}",
            $"Frame delta         {(FrameDelta * 1000f).ToString("F2", culture)} ms",
            $"Fixed step          {(FixedStep * 1000f).ToString("F3", culture)} ms × {SimulationSteps.ToString(culture)}",
            $"Position (96-DIP)   {Position.X.ToString("F1", culture)}, {Position.Y.ToString("F1", culture)}",
            $"Display             {DisplayId}",
            $"Display scale       {DisplayScale.ToString("F3", culture)}×",
            $"Paused              {YesNo(IsPaused)}",
            $"Dragging            {YesNo(IsDragging)}",
            $"Frame submission    {frameState}",
            $"Presenter region    v{PresenterRegionVersion.ToString(culture)}",
            $"Region pipeline     {regionState}",
            $"Region transaction  submitted v{SubmittedRegionVersion.ToString(culture)}, pending {PendingRegionPoses.ToString(culture)}, acknowledged {AcknowledgedRegionPoses.ToString(culture)}");
    }

    private static string YesNo(bool value) => value ? "yes" : "no";
}

internal sealed class DesktopPetDiagnosticTelemetry
{
    private readonly double _fpsResponse;
    private readonly double _maximumFps;
    private long _displayFrame;
    private double _smoothedFps;

    public DesktopPetDiagnosticTelemetry(DesktopPetDebugSettings? settings = null)
    {
        var resolved = settings ?? DesktopPetDebugSettings.Default;
        _fpsResponse = resolved.FpsResponse;
        _maximumFps = resolved.MaximumFps;
    }

    public DesktopPetDiagnosticSnapshot Capture(
        float frameDelta,
        float fixedStep,
        int simulationSteps,
        Vector2 position,
        string displayId,
        double displayScale,
        bool pointerAvailable,
        bool pointerBlocked,
        Vector2 pointerPosition,
        float pointerDistance,
        bool isPaused,
        bool isDragging,
        bool wasPresented,
        long presenterRegionVersion,
        bool regionBackendAvailable,
        bool regionAttempted,
        bool regionSynchronized,
        long submittedRegionVersion,
        int pendingRegionPoses,
        int acknowledgedRegionPoses)
    {
        if (!float.IsFinite(frameDelta) || frameDelta < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(frameDelta));
        }
        if (!float.IsFinite(fixedStep) || fixedStep <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedStep));
        }
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(displayId);
        if (!double.IsFinite(displayScale) || displayScale <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(displayScale));
        }
        if (!float.IsFinite(pointerPosition.X) || !float.IsFinite(pointerPosition.Y) ||
            (pointerAvailable && (!float.IsFinite(pointerDistance) || pointerDistance < 0f)))
        {
            throw new ArgumentOutOfRangeException(nameof(pointerPosition));
        }
        if (simulationSteps < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(simulationSteps));
        }
        if (pendingRegionPoses < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pendingRegionPoses));
        }
        if (acknowledgedRegionPoses < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(acknowledgedRegionPoses));
        }

        _displayFrame = checked(_displayFrame + 1);
        if (frameDelta > 0f)
        {
            var instantaneous = Math.Min(_maximumFps, 1d / frameDelta);
            var blend = 1d - Math.Exp(-_fpsResponse * frameDelta);
            _smoothedFps = _smoothedFps <= 0d
                ? instantaneous
                : _smoothedFps + (instantaneous - _smoothedFps) * blend;
        }

        return new DesktopPetDiagnosticSnapshot(
            _displayFrame,
            _smoothedFps,
            frameDelta,
            fixedStep,
            simulationSteps,
            position,
            displayId,
            displayScale,
            pointerAvailable,
            pointerBlocked,
            pointerPosition,
            pointerDistance,
            isPaused,
            isDragging,
            wasPresented,
            presenterRegionVersion,
            regionBackendAvailable,
            regionAttempted,
            regionSynchronized,
            submittedRegionVersion,
            pendingRegionPoses,
            acknowledgedRegionPoses);
    }
}
