using System.Numerics;

namespace DesktopPet.Engine;

/// <summary>
/// Canonical desktop space: 96-based logical units, top-left origin, Y down.
/// Raster/backing scale is intentionally absent from gameplay input.
/// </summary>
public readonly record struct WorldRect(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;
    public Vector2 Center => new((Left + Right) * 0.5f, (Top + Bottom) * 0.5f);
    public bool IsValid =>
        float.IsFinite(Left) &&
        float.IsFinite(Top) &&
        float.IsFinite(Right) &&
        float.IsFinite(Bottom) &&
        float.IsFinite(Width) &&
        float.IsFinite(Height) &&
        Right >= Left &&
        Bottom >= Top;

    public WorldRect Inset(float amount) => new(
        Left + amount,
        Top + amount,
        Right - amount,
        Bottom - amount);

    public Vector2 Clamp(Vector2 point) => new(
        Math.Clamp(point.X, Left, Right),
        Math.Clamp(point.Y, Top, Bottom));

    public bool Contains(Vector2 point) =>
        point.X >= Left && point.X <= Right &&
        point.Y >= Top && point.Y <= Bottom;
}

public readonly record struct PointerSample(
    Vector2 Position,
    bool IsAvailable,
    bool IsInteractionBlocked = false);

public readonly record struct SafetyArea(WorldRect Area, bool IsAvailable);

/// <summary>
/// Size in the engine's canonical 96-DPI logical coordinate space.
/// Keeping both axes explicit prevents a desktop host from silently assuming
/// that every pet renders into a square surface.
/// </summary>
public readonly record struct WorldSize
{
    public float Width { get; }
    public float Height { get; }
    public Vector2 HalfExtents => new(Width * 0.5f, Height * 0.5f);

    public WorldSize(float width, float height)
    {
        if (!float.IsFinite(width) || width <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        if (!float.IsFinite(height) || height <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        Width = width;
        Height = height;
    }

    public static WorldSize Square(float size) => new(size, size);
}

/// <summary>
/// Immutable geometry/visual contract captured by DesktopPetRuntime. Canvas
/// dimensions, model scale, and fade duration must be positive; radii and
/// safety insets may be zero but never negative.
/// </summary>
public readonly record struct DesktopPetMetrics(
    WorldSize CanvasSizeWorld,
    float NavigationRadiusWorld,
    float FullRenderRadiusWorld,
    float FullRenderSafetyInsetWorld,
    float ModelToWorldScale,
    float InitialHeading,
    float SpawnFadeDuration);

/// <summary>
/// Fixed simulation timing selected by a gameplay module and enforced by the
/// generic runtime. Display cadence never crosses the gameplay boundary.
/// </summary>
public readonly record struct DesktopPetTiming
{
    public const int MinimumSupportedDisplayRate = 30;
    public const float MinimumFrameCatchUp = 1f / MinimumSupportedDisplayRate;

    public float SimulationStep { get; }
    public float MaximumFrameCatchUp { get; }

    public DesktopPetTiming(float simulationStep, float maximumFrameCatchUp)
    {
        if (!float.IsFinite(simulationStep) || simulationStep <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(simulationStep),
                "The simulation step must be finite and positive.");
        }
        if (!float.IsFinite(maximumFrameCatchUp) || maximumFrameCatchUp <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFrameCatchUp),
                "The frame catch-up limit must be finite and positive.");
        }
        if (maximumFrameCatchUp < simulationStep)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFrameCatchUp),
                "The frame catch-up limit cannot be shorter than one simulation step.");
        }
        if (maximumFrameCatchUp < MinimumFrameCatchUp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFrameCatchUp),
                $"The frame catch-up limit must cover at least one " +
                $"{MinimumSupportedDisplayRate} Hz display frame.");
        }
        // One partial step may already be accumulated when a maximum-sized
        // frame arrives. Reserve capacity for that carry before accepting the
        // timing contract.
        var maximumSteps = Math.Ceiling(
            (double)maximumFrameCatchUp / simulationStep) + 1d;
        if (!double.IsFinite(maximumSteps) ||
            maximumSteps > FixedStepRunner.MaximumStepsPerAdvance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(simulationStep),
                $"Timing would exceed the safety limit of " +
                $"{FixedStepRunner.MaximumStepsPerAdvance} fixed steps per display frame.");
        }

        SimulationStep = simulationStep;
        MaximumFrameCatchUp = maximumFrameCatchUp;
    }

    public void EnsureValid()
    {
        _ = new DesktopPetTiming(SimulationStep, MaximumFrameCatchUp);
    }
}

public readonly record struct DesktopPetInput(
    float FrameDelta,
    WorldRect NavigationArea,
    SafetyArea FullRenderSafety,
    PointerSample Pointer,
    bool IsPrimaryInteractionActive);

/// <summary>
/// One engine-authorized fixed simulation step. StepsRemaining describes only
/// the current display batch, allowing external drag movement to be spread
/// evenly without exposing the display frame delta to gameplay.
/// </summary>
public readonly record struct DesktopPetFixedStepInput(
    float StepDelta,
    int StepsRemaining,
    WorldRect NavigationArea,
    SafetyArea FullRenderSafety,
    PointerSample Pointer,
    bool IsPrimaryInteractionActive);

public readonly record struct DesktopPetFrame<TSnapshot>(
    float FrameDelta,
    Vector2 Position,
    Vector2 LookDirection,
    TSnapshot Snapshot,
    bool IsPaused,
    int SimulationSteps);

/// <summary>
/// Gameplay plug-in contract owned by the generic desktop-pet engine. Neither
/// the host nor the engine needs to know a game's state machine or scene type.
/// </summary>
public interface IDesktopPetGame<TSnapshot>
{
    DesktopPetMetrics Metrics { get; }
    DesktopPetTiming Timing { get; }
    Vector2 Position { get; }
    Vector2 LookDirection { get; }
    bool IsPaused { get; }

    void Reset(Vector2 position);
    void AdvanceFixedStep(in DesktopPetFixedStepInput input);
    TSnapshot CaptureSnapshot();
    void BeginPrimaryInteraction(Vector2 modelPoint);
    void DragTo(Vector2 worldPosition);
    void EndPrimaryInteraction(Vector2 settledWorldPosition);
    bool TogglePaused();
    void MoveTo(Vector2 worldPosition);
    void RebaseWorldPosition(Vector2 delta);
}
