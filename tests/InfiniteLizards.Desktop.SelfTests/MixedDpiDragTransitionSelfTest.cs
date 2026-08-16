using System.Numerics;
using DesktopPet.Engine;
using InfiniteLizards.Desktop;

internal static class MixedDpiDragTransitionSelfTest
{
    public static void Run()
    {
        var topology = new DisplayTopology(new[]
        {
            new DisplayDescriptor(
                "primary-100",
                new DeviceRect(0d, 0d, 1920d, 1080d),
                new DeviceRect(0d, 0d, 1920d, 1040d),
                1d,
                IsPrimary: true),
            new DisplayDescriptor(
                "right-200",
                new DeviceRect(1920d, 0d, 5760d, 2160d),
                new DeviceRect(1920d, 0d, 5760d, 2080d),
                2d),
        });
        var primary = topology.FindById("primary-100")!;
        var displaySpace = new ActiveDisplaySpace(topology, primary);

        var initialPosition = new Vector2(1800f, 450f);
        var game = new RecordingGame(initialPosition);
        var runtime = new DesktopPetRuntime<int>(game);

        // The center moves 120x50 logical units in the old frame and crosses
        // the horizontal seam. The raw Y is halved by the new 200% frame, so a
        // missing rebase would look like a large upward physical movement.
        var desiredBefore = new WorldPoint(1920d, 500d);
        var desiredDevice = displaySpace.WorldToDevice(desiredBefore);
        var transition = displaySpace.SwitchForDevicePoint(
            desiredDevice,
            desiredBefore);
        AssertTrue(transition.Changed, "test setup must cross into the 200% display");
        var pointerDevice = new DevicePoint(1960d, 520d);

        var dragOffset = DisplaySpaceDragTransition.Apply(
            runtime,
            displaySpace,
            transition,
            desiredBefore,
            pointerDevice);

        var desiredBeforeVector = ToVector(desiredBefore);
        var rebasedDesired = ToVector(transition.RebasedWorldPoint);
        var expectedRebase = rebasedDesired - desiredBeforeVector;
        AssertVector(new Vector2(0f, -250f), expectedRebase, "test rebase precondition");

        AssertEqual(2, game.Commands.Count, "exactly two spatial commands");
        AssertEqual("rebase", game.Commands[0].Name, "rebase must execute first");
        AssertVector(expectedRebase, game.Commands[0].Value, "rebase delta");
        AssertEqual("drag", game.Commands[1].Name, "drag must execute second");
        AssertVector(rebasedDesired, game.Commands[1].Value, "rebased drag target");

        var physicalDragBefore = desiredBeforeVector - initialPosition;
        var positionAfterCoordinateRebase = initialPosition + expectedRebase;
        var physicalDragAfter = rebasedDesired - positionAfterCoordinateRebase;
        AssertVector(
            physicalDragBefore,
            physicalDragAfter,
            "the coordinate rebase must not double-apply or erase physical drag movement");
        AssertVector(rebasedDesired, runtime.Position, "runtime final position");

        var pointerWorld = ToVector(displaySpace.DeviceToWorld(pointerDevice));
        AssertVector(
            pointerWorld - rebasedDesired,
            dragOffset,
            "drag offset must be recomputed in the new active display frame");
    }

    private static Vector2 ToVector(WorldPoint value) =>
        new((float)value.X, (float)value.Y);

    private static void AssertVector(Vector2 expected, Vector2 actual, string context)
    {
        AssertNear(expected.X, actual.X, context + " X");
        AssertNear(expected.Y, actual.Y, context + " Y");
    }

    private static void AssertNear(float expected, float actual, string context)
    {
        if (!float.IsFinite(expected) ||
            !float.IsFinite(actual) ||
            MathF.Abs(expected - actual) > 0.000001f)
        {
            throw new InvalidOperationException(
                $"{context}: expected {expected:R}, actual {actual:R}.");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string context)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{context}: expected {expected}, actual {actual}.");
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message + ".");
        }
    }

    private sealed class RecordingGame : IDesktopPetGame<int>
    {
        public RecordingGame(Vector2 initialPosition)
        {
            Position = initialPosition;
        }

        public List<SpatialCommand> Commands { get; } = [];
        public DesktopPetMetrics Metrics { get; } = new(
            WorldSize.Square(360f),
            100f,
            180f,
            180f,
            0.6f,
            0f,
            0.25f);
        public DesktopPetTiming Timing { get; } = new(1f / 120f, 0.25f);
        public Vector2 Position { get; private set; }
        public Vector2 LookDirection => Vector2.UnitX;
        public bool IsPaused => false;

        public void Reset(Vector2 position) => Position = position;

        public void AdvanceFixedStep(in DesktopPetFixedStepInput input)
        {
        }

        public int CaptureSnapshot() => 0;

        public void BeginPrimaryInteraction(Vector2 modelPoint)
        {
        }

        public void DragTo(Vector2 worldPosition)
        {
            Commands.Add(new SpatialCommand("drag", worldPosition));
            Position = worldPosition;
        }

        public void EndPrimaryInteraction(Vector2 settledWorldPosition)
        {
        }

        public bool TogglePaused() => false;

        public void MoveTo(Vector2 worldPosition) => Position = worldPosition;

        public void RebaseWorldPosition(Vector2 delta)
        {
            Commands.Add(new SpatialCommand("rebase", delta));
            Position += delta;
        }
    }

    private readonly record struct SpatialCommand(string Name, Vector2 Value);
}
