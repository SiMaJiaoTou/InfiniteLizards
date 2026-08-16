using System.Numerics;
using DesktopLizard.Core;
using DesktopPet.Engine;
using InfiniteLizards.Gameplay;

/// <summary>
/// Exercises the real display mapping and gameplay adapter together. Equivalent
/// device-space input must produce the same 96-DIP world input and therefore the
/// same simulation and immutable render pose at every supported raster scale.
/// </summary>
internal static class DpiScaleInvarianceSelfTest
{
    private const int BehaviorSeed = 0x51A7E;
    private const int FrameCount = 420;
    private const float FloatTolerance = 0.000001f;
    private const double MappingTolerance = 0.000000001d;

    private static readonly float[] FrameDeltas =
    {
        1f / 60f,
        1f / 144f,
        1f / 75f,
        1f / 120f,
        1f / 50f,
        1f / 90f,
        0f,
        1f / 60f
    };

    public static DpiScaleInvarianceResult Run()
    {
        try
        {
            var scales = new[] { 1d, 1.25d, 1.5d, 2d };
            var scenarios = scales
                .Select(scale => new ScaleScenario(scale))
                .ToArray();
            var baseline = scenarios[0];

            foreach (var scenario in scenarios.Skip(1))
            {
                CompareMetrics(baseline.Runtime.Metrics, scenario.Runtime.Metrics, scenario.Scale);
                CompareRect(
                    baseline.NavigationArea,
                    scenario.NavigationArea,
                    $"{scenario.Scale:0.##}x navigation area");
                CompareRect(
                    baseline.FullRenderSafety.Area,
                    scenario.FullRenderSafety.Area,
                    $"{scenario.Scale:0.##}x full-render safety area");
                CompareRenderFrame(
                    baseline.Runtime.CurrentSnapshot,
                    scenario.Runtime.CurrentSnapshot,
                    scenario.Scale,
                    -1);
            }

            for (var frameIndex = 0; frameIndex < FrameCount; frameIndex++)
            {
                var logicalPointer = GetLogicalPointer(frameIndex);
                var mappedPointers = scenarios
                    .Select(scenario => scenario.MapEquivalentDevicePointer(logicalPointer))
                    .ToArray();

                for (var scenarioIndex = 0; scenarioIndex < scenarios.Length; scenarioIndex++)
                {
                    AssertNear(
                        (float)logicalPointer.X,
                        mappedPointers[scenarioIndex].X,
                        MappingTolerance,
                        $"frame {frameIndex} pointer X at {scenarios[scenarioIndex].Scale:0.##}x");
                    AssertNear(
                        (float)logicalPointer.Y,
                        mappedPointers[scenarioIndex].Y,
                        MappingTolerance,
                        $"frame {frameIndex} pointer Y at {scenarios[scenarioIndex].Scale:0.##}x");
                }

                if (frameIndex == 160)
                {
                    foreach (var scenario in scenarios)
                    {
                        scenario.Runtime.BeginPrimaryInteraction(Vector2.Zero);
                    }
                }

                var isDragging = frameIndex is >= 160 and < 226;
                if (isDragging)
                {
                    for (var scenarioIndex = 0; scenarioIndex < scenarios.Length; scenarioIndex++)
                    {
                        scenarios[scenarioIndex].Runtime.DragTo(mappedPointers[scenarioIndex]);
                    }
                }
                else if (frameIndex == 226)
                {
                    for (var scenarioIndex = 0; scenarioIndex < scenarios.Length; scenarioIndex++)
                    {
                        scenarios[scenarioIndex].Runtime.EndPrimaryInteraction(
                            mappedPointers[scenarioIndex]);
                    }
                }

                var frames = new DesktopPetFrame<LizardRenderFrame>[scenarios.Length];
                for (var scenarioIndex = 0; scenarioIndex < scenarios.Length; scenarioIndex++)
                {
                    var scenario = scenarios[scenarioIndex];
                    frames[scenarioIndex] = scenario.Runtime.Advance(new DesktopPetInput(
                        FrameDeltas[frameIndex % FrameDeltas.Length],
                        scenario.NavigationArea,
                        scenario.FullRenderSafety,
                        new PointerSample(mappedPointers[scenarioIndex], true),
                        isDragging));
                }

                for (var scenarioIndex = 1; scenarioIndex < scenarios.Length; scenarioIndex++)
                {
                    CompareFrame(
                        frames[0],
                        frames[scenarioIndex],
                        scenarios[scenarioIndex].Scale,
                        frameIndex);
                }
            }

            return new DpiScaleInvarianceResult(
                true,
                $"{FrameCount} frames were identical at 1x, 1.25x, 1.5x and 2x, " +
                "including device mapping and grab/drag/release geometry.");
        }
        catch (Exception exception)
        {
            return new DpiScaleInvarianceResult(false, exception.Message);
        }
    }

    private static WorldPoint GetLogicalPointer(int frameIndex)
    {
        // Multiples of four remain integral device pixels at every tested scale.
        var x = 200 + frameIndex * 24 % 880;
        var phase = frameIndex * 7 % 80;
        var triangle = phase <= 40 ? phase : 80 - phase;
        var y = 180 + triangle * 8;
        return new WorldPoint(x, y);
    }

    private static void CompareFrame(
        in DesktopPetFrame<LizardRenderFrame> expected,
        in DesktopPetFrame<LizardRenderFrame> actual,
        double scale,
        int frameIndex)
    {
        var context = $"frame {frameIndex} at {scale:0.##}x";
        AssertNear(expected.FrameDelta, actual.FrameDelta, FloatTolerance, $"{context} delta");
        AssertVector(expected.Position, actual.Position, $"{context} position");
        AssertVector(expected.LookDirection, actual.LookDirection, $"{context} look");
        AssertEqual(expected.IsPaused, actual.IsPaused, $"{context} pause state");
        AssertEqual(
            expected.SimulationSteps,
            actual.SimulationSteps,
            $"{context} simulation steps");
        CompareRenderFrame(expected.Snapshot, actual.Snapshot, scale, frameIndex);
    }

    private static void CompareRenderFrame(
        in LizardRenderFrame expected,
        in LizardRenderFrame actual,
        double scale,
        int frameIndex)
    {
        var context = frameIndex < 0
            ? $"initial pose at {scale:0.##}x"
            : $"frame {frameIndex} pose at {scale:0.##}x";
        AssertEqual(
            expected.BodyOutline.Length,
            actual.BodyOutline.Length,
            $"{context} body point count");
        for (var index = 0; index < expected.BodyOutline.Length; index++)
        {
            AssertVector(
                expected.BodyOutline[index],
                actual.BodyOutline[index],
                $"{context} body[{index}]");
        }

        AssertEqual(expected.Legs.Length, actual.Legs.Length, $"{context} leg count");
        for (var index = 0; index < expected.Legs.Length; index++)
        {
            var expectedLeg = expected.Legs[index];
            var actualLeg = actual.Legs[index];
            AssertVector(expectedLeg.Shoulder, actualLeg.Shoulder, $"{context} leg[{index}] shoulder");
            AssertVector(expectedLeg.Elbow, actualLeg.Elbow, $"{context} leg[{index}] elbow");
            AssertVector(expectedLeg.Foot, actualLeg.Foot, $"{context} leg[{index}] foot");
            AssertEqual(expectedLeg.IsFront, actualLeg.IsFront, $"{context} leg[{index}] kind");
            AssertNear(expectedLeg.Lift, actualLeg.Lift, FloatTolerance, $"{context} leg[{index}] lift");
        }

        AssertVector(expected.HeadNose, actual.HeadNose, $"{context} head nose");
        AssertVector(
            expected.NegativeEyeCenter,
            actual.NegativeEyeCenter,
            $"{context} negative eye");
        AssertVector(
            expected.PositiveEyeCenter,
            actual.PositiveEyeCenter,
            $"{context} positive eye");
        AssertNear(expected.Heading, actual.Heading, FloatTolerance, $"{context} heading");
        AssertNear(expected.BlinkAmount, actual.BlinkAmount, FloatTolerance, $"{context} blink");
    }

    private static void CompareMetrics(
        in DesktopPetMetrics expected,
        in DesktopPetMetrics actual,
        double scale)
    {
        var context = $"{scale:0.##}x metrics";
        AssertNear(
            expected.CanvasSizeWorld.Width,
            actual.CanvasSizeWorld.Width,
            FloatTolerance,
            $"{context} canvas width");
        AssertNear(
            expected.CanvasSizeWorld.Height,
            actual.CanvasSizeWorld.Height,
            FloatTolerance,
            $"{context} canvas height");
        AssertNear(
            expected.NavigationRadiusWorld,
            actual.NavigationRadiusWorld,
            FloatTolerance,
            $"{context} navigation radius");
        AssertNear(
            expected.FullRenderRadiusWorld,
            actual.FullRenderRadiusWorld,
            FloatTolerance,
            $"{context} render radius");
        AssertNear(
            expected.FullRenderSafetyInsetWorld,
            actual.FullRenderSafetyInsetWorld,
            FloatTolerance,
            $"{context} safety inset");
        AssertNear(
            expected.ModelToWorldScale,
            actual.ModelToWorldScale,
            FloatTolerance,
            $"{context} model scale");
        AssertNear(
            expected.InitialHeading,
            actual.InitialHeading,
            FloatTolerance,
            $"{context} heading");
        AssertNear(
            expected.SpawnFadeDuration,
            actual.SpawnFadeDuration,
            FloatTolerance,
            $"{context} spawn fade");
    }

    private static void CompareRect(in WorldRect expected, in WorldRect actual, string context)
    {
        AssertNear(expected.Left, actual.Left, FloatTolerance, $"{context} left");
        AssertNear(expected.Top, actual.Top, FloatTolerance, $"{context} top");
        AssertNear(expected.Right, actual.Right, FloatTolerance, $"{context} right");
        AssertNear(expected.Bottom, actual.Bottom, FloatTolerance, $"{context} bottom");
    }

    private static void AssertVector(Vector2 expected, Vector2 actual, string context)
    {
        AssertNear(expected.X, actual.X, FloatTolerance, $"{context} X");
        AssertNear(expected.Y, actual.Y, FloatTolerance, $"{context} Y");
    }

    private static void AssertNear(
        double expected,
        double actual,
        double tolerance,
        string context)
    {
        if (!double.IsFinite(expected) ||
            !double.IsFinite(actual) ||
            Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"{context}: expected {expected:R}, actual {actual:R}, " +
                $"tolerance {tolerance:R}.");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string context)
        where T : IEquatable<T>
    {
        if (!expected.Equals(actual))
        {
            throw new InvalidOperationException(
                $"{context}: expected {expected}, actual {actual}.");
        }
    }

    private sealed class ScaleScenario
    {
        private const double DeviceLeft = -320d;
        private const double DeviceTop = 180d;
        private const double WorldWidth = 1280d;
        private const double WorldHeight = 800d;

        public double Scale { get; }
        public DisplayTopology Topology { get; }
        public DesktopPetRuntime<LizardRenderFrame> Runtime { get; }
        public WorldRect NavigationArea { get; }
        public SafetyArea FullRenderSafety { get; }

        public ScaleScenario(double scale)
        {
            Scale = scale;
            Topology = new DisplayTopology(new[]
            {
                new DisplayDescriptor(
                    $"display-{scale:0.##}",
                    DeviceRectFromLogical(0d, 0d, WorldWidth, WorldHeight, scale),
                    DeviceRectFromLogical(48d, 32d, 1232d, 744d, scale),
                    scale,
                    true)
            });
            NavigationArea = ToWorldRect(Topology.Primary.WorldWorkingArea);
            FullRenderSafety = new SafetyArea(NavigationArea, true);
            Runtime = new DesktopPetRuntime<LizardRenderFrame>(
                new LizardGameModule(LizardProfile.Default, BehaviorSeed));

            var spawn = MapEquivalentDevicePointer(new WorldPoint(640d, 380d));
            Runtime.Reset(spawn);
        }

        public Vector2 MapEquivalentDevicePointer(WorldPoint logicalPoint)
        {
            var devicePoint = new DevicePoint(
                DeviceLeft + logicalPoint.X * Scale,
                DeviceTop + logicalPoint.Y * Scale);
            var mapped = Topology.DeviceToWorld(devicePoint);
            return new Vector2((float)mapped.X, (float)mapped.Y);
        }

        private static DeviceRect DeviceRectFromLogical(
            double left,
            double top,
            double right,
            double bottom,
            double scale) => new(
                DeviceLeft + left * scale,
                DeviceTop + top * scale,
                DeviceLeft + right * scale,
                DeviceTop + bottom * scale);

        private static WorldRect ToWorldRect(WorldRectD rect) => new(
            (float)rect.Left,
            (float)rect.Top,
            (float)rect.Right,
            (float)rect.Bottom);
    }
}

internal readonly record struct DpiScaleInvarianceResult(bool Passed, string Detail)
{
    public override string ToString() => Detail;
}
