using System.Numerics;

namespace DesktopPet.Engine;

/// <summary>
/// Generic display-frame orchestrator. It is the sole owner of render-cadence
/// accumulation and exposes gameplay only to complete fixed simulation steps.
/// </summary>
public sealed class DesktopPetRuntime<TSnapshot>
{
    private readonly IDesktopPetGame<TSnapshot> _game;
    private readonly FixedStepRunner _fixedStepRunner = new();
    private readonly DesktopPetMetrics _metrics;
    private readonly DesktopPetTiming _timing;
    private TSnapshot _currentSnapshot;

    public DesktopPetMetrics Metrics => _metrics;
    public DesktopPetTiming Timing => _timing;
    public Vector2 Position => _game.Position;
    public bool IsPaused => _game.IsPaused;
    public TSnapshot CurrentSnapshot => _currentSnapshot;

    public DesktopPetRuntime(IDesktopPetGame<TSnapshot> game)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _timing = _game.Timing;
        _timing.EnsureValid();
        _metrics = _game.Metrics;
        EnsureValidMetrics(_metrics);
        EnsureValidGameState("construction");
        _currentSnapshot = CaptureSnapshotChecked("construction");
    }

    public void Reset(Vector2 position)
    {
        EnsureFinite(position, nameof(position));
        _fixedStepRunner.Reset();
        _game.Reset(position);
        EnsureValidGameState("reset");
        _currentSnapshot = CaptureSnapshotChecked("reset");
    }

    public DesktopPetFrame<TSnapshot> Advance(in DesktopPetInput input)
    {
        var displayInput = input;
        EnsureValidDisplayInput(displayInput);
        var result = _fixedStepRunner.Advance(
            displayInput.FrameDelta,
            _timing.SimulationStep,
            _timing.MaximumFrameCatchUp,
            (stepDelta, stepsRemaining) =>
            {
                _game.AdvanceFixedStep(new DesktopPetFixedStepInput(
                    stepDelta,
                    stepsRemaining,
                    displayInput.NavigationArea,
                    displayInput.FullRenderSafety,
                    displayInput.Pointer,
                    displayInput.IsPrimaryInteractionActive));
                // Fail at the first corrupt step instead of allowing the rest
                // of a catch-up batch to amplify invalid plugin state.
                EnsureValidGameState("fixed-step advance");
            });

        if (result.StepCount > 0)
        {
            _currentSnapshot = CaptureSnapshotChecked("fixed-step advance");
        }

        return new DesktopPetFrame<TSnapshot>(
            result.FrameDelta,
            _game.Position,
            _game.LookDirection,
            _currentSnapshot,
            _game.IsPaused,
            result.StepCount);
    }

    public void BeginPrimaryInteraction(Vector2 modelPoint)
    {
        EnsureFinite(modelPoint, nameof(modelPoint));
        _game.BeginPrimaryInteraction(modelPoint);
        EnsureValidGameState("begin interaction");
    }

    public void DragTo(Vector2 worldPosition)
    {
        EnsureFinite(worldPosition, nameof(worldPosition));
        _game.DragTo(worldPosition);
        EnsureValidGameState("drag");
    }

    public void EndPrimaryInteraction(Vector2 settledWorldPosition)
    {
        EnsureFinite(settledWorldPosition, nameof(settledWorldPosition));
        _game.EndPrimaryInteraction(settledWorldPosition);

        EnsureValidGameState("end interaction");
    }

    public bool TogglePaused()
    {
        var result = _game.TogglePaused();
        EnsureValidGameState("toggle pause");
        if (result != _game.IsPaused)
        {
            throw new InvalidOperationException(
                "Gameplay returned a pause state inconsistent with IsPaused.");
        }
        return result;
    }

    public void MoveTo(Vector2 worldPosition)
    {
        EnsureFinite(worldPosition, nameof(worldPosition));
        _game.MoveTo(worldPosition);
        EnsureValidGameState("move command");
    }

    /// <summary>
    /// Translates persistent world state by <paramref name="delta"/> without
    /// issuing a gameplay movement command or resetting fixed-step time.
    /// </summary>
    public void RebaseWorldPosition(Vector2 delta)
    {
        EnsureFinite(delta, nameof(delta));
        _game.RebaseWorldPosition(delta);

        EnsureValidGameState("world rebase");
    }

    private TSnapshot CaptureSnapshotChecked(string operation)
    {
        var snapshot = _game.CaptureSnapshot();
        if (snapshot is null)
        {
            throw new InvalidOperationException(
                $"Gameplay returned a null snapshot after {operation}.");
        }
        return snapshot;
    }

    private void EnsureValidGameState(string operation)
    {
        EnsureFinite(
            _game.Position,
            $"gameplay Position after {operation}");
        EnsureFinite(
            _game.LookDirection,
            $"gameplay LookDirection after {operation}");
    }

    private static void EnsureValidDisplayInput(in DesktopPetInput input)
    {
        EnsureUsableRect(input.NavigationArea, nameof(input.NavigationArea), true);
        EnsureUsableRect(
            input.FullRenderSafety.Area,
            nameof(input.FullRenderSafety),
            input.FullRenderSafety.IsAvailable);
        EnsureFinite(input.Pointer.Position, nameof(input.Pointer));
    }

    private static void EnsureValidMetrics(in DesktopPetMetrics metrics)
    {
        if (!float.IsFinite(metrics.CanvasSizeWorld.Width) ||
            !float.IsFinite(metrics.CanvasSizeWorld.Height) ||
            metrics.CanvasSizeWorld.Width <= 0f ||
            metrics.CanvasSizeWorld.Height <= 0f)
        {
            throw new ArgumentException(
                "Gameplay canvas dimensions must be finite and positive.",
                nameof(metrics));
        }
        EnsureFiniteNonNegative(
            metrics.NavigationRadiusWorld,
            nameof(metrics.NavigationRadiusWorld));
        EnsureFiniteNonNegative(
            metrics.FullRenderRadiusWorld,
            nameof(metrics.FullRenderRadiusWorld));
        EnsureFiniteNonNegative(
            metrics.FullRenderSafetyInsetWorld,
            nameof(metrics.FullRenderSafetyInsetWorld));
        EnsureFinitePositive(
            metrics.ModelToWorldScale,
            nameof(metrics.ModelToWorldScale));
        if (!float.IsFinite(metrics.InitialHeading))
        {
            throw new ArgumentException(
                "Gameplay initial heading must be finite.",
                nameof(metrics));
        }
        EnsureFinitePositive(
            metrics.SpawnFadeDuration,
            nameof(metrics.SpawnFadeDuration));
    }

    private static void EnsureUsableRect(
        in WorldRect value,
        string name,
        bool requirePositiveExtent)
    {
        if (!value.IsValid ||
            (requirePositiveExtent && (value.Width <= 0f || value.Height <= 0f)))
        {
            throw new ArgumentException(
                requirePositiveExtent
                    ? "The world rectangle must be finite, ordered, and have positive extent."
                    : "The world rectangle must be finite and ordered.",
                name);
        }
    }

    private static void EnsureFinite(Vector2 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentException("The vector must be finite.", name);
        }
    }

    private static void EnsureFiniteNonNegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentException(
                "The value must be finite and non-negative.",
                name);
        }
    }

    private static void EnsureFinitePositive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentException(
                "The value must be finite and positive.",
                name);
        }
    }
}
