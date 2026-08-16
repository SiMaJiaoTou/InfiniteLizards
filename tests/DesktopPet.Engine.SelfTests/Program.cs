using DesktopPet.Engine;
using System.Numerics;

var tests = new (string Name, Action Run)[]
{
    ("Device/world round-trip is stable at 100%, 125%, 150%, and 200%", DeviceWorldRoundTrips),
    ("Mixed-DPI display transitions preserve the tracked device position", MixedDpiTransitionIsContinuous),
    ("Negative and beyond-Int16 device coordinates keep full precision", LargeAndNegativeCoordinates),
    ("Surface placement floor/ceil enclosure never crops its world rectangle", SurfacePlacementNeverCrops),
    ("Topology replacement preserves device position and survives display removal", TopologyReplacementAndHotPlug),
    ("Malformed and ambiguous display descriptors cannot contaminate topology", InvalidTopologyDataIsContained),
    ("Randomized multi-display mappings retain round-trip and transition invariants", RandomizedTopologyInvariants),
    ("Fixed-step simulation is render-cadence independent", FixedStepIsCadenceIndependent),
    ("Fixed-step overload clipping is observable in its result", FixedStepOverloadIsDiagnosable),
    ("Engine runtime owns identical 50-240 Hz fixed-step cadence", RuntimeOwnsCadence),
    ("Engine runtime clips overload and reuses zero-step snapshots", RuntimeOverloadAndSnapshotReuse),
    ("Engine runtime contains invalid plugin state and host geometry", RuntimeRejectsInvalidBoundaries),
};

var failureCount = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failureCount++;
        Console.Error.WriteLine($"FAIL  {test.Name}");
        Console.Error.WriteLine($"      {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"{tests.Length - failureCount}/{tests.Length} engine self-tests passed.");
return failureCount == 0 ? 0 : 1;

static void DeviceWorldRoundTrips()
{
    foreach (var scale in new[] { 1d, 1.25d, 1.5d, 2d })
    {
        var bounds = new DeviceRect(-640.5d, 120.25d, 4480.5d, 3000.25d);
        var topology = OneDisplay(bounds, scale);
        var display = topology.Primary;

        var devicePoints = new[]
        {
            new DevicePoint(bounds.Left, bounds.Top),
            new DevicePoint(-123.125d, 777.875d),
            new DevicePoint(bounds.Right - 0.25d, bounds.Bottom - 0.75d),
        };
        foreach (var source in devicePoints)
        {
            var roundTrip = topology.WorldToDevice(topology.DeviceToWorld(source));
            AssertNear(source.X, roundTrip.X, 1e-9,
                $"scale {scale}: device X round-trip for {source}");
            AssertNear(source.Y, roundTrip.Y, 1e-9,
                $"scale {scale}: device Y round-trip for {source}");
        }

        var worldPoints = new[]
        {
            new WorldPoint(display.WorldBounds.Left, display.WorldBounds.Top),
            new WorldPoint(17.2d, 301.6d),
            new WorldPoint(display.WorldBounds.Right - 0.1d, display.WorldBounds.Bottom - 0.2d),
        };
        foreach (var source in worldPoints)
        {
            var roundTrip = topology.DeviceToWorld(topology.WorldToDevice(source));
            AssertNear(source.X, roundTrip.X, 1e-9,
                $"scale {scale}: world X round-trip for {source}");
            AssertNear(source.Y, roundTrip.Y, 1e-9,
                $"scale {scale}: world Y round-trip for {source}");
        }
    }
}

static void MixedDpiTransitionIsContinuous()
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
        new DisplayDescriptor(
            "left-125",
            new DeviceRect(-2400d, 0d, 0d, 1350d),
            new DeviceRect(-2400d, 0d, 0d, 1300d),
            1.25d),
    });

    var primary = topology.Displays.Single(display => display.Id == "primary-100");
    var right = topology.Displays.Single(display => display.Id == "right-200");
    var left = topology.Displays.Single(display => display.Id == "left-125");

    AssertNear(primary.WorldBounds.Right, right.WorldBounds.Left, 1e-12,
        "primary/right logical edges must coincide");
    AssertNear(left.WorldBounds.Right, primary.WorldBounds.Left, 1e-12,
        "left/primary logical edges must coincide");

    // The same raw Y cannot be divided by two different scales without a
    // logical jump. ActiveDisplaySpace makes that transition explicit and
    // rebases the tracked pet through device space instead.
    var space = new ActiveDisplaySpace(topology, primary);
    var trackedBefore = primary.DeviceToWorld(new DevicePoint(1800d, 1000d));
    var deviceBefore = space.WorldToDevice(trackedBefore);
    var pointerBefore = space.DeviceToWorld(new DevicePoint(1919d, 1000d));
    var pointerAcrossWhileActive = space.DeviceToWorld(new DevicePoint(1920d, 1000d));
    AssertNear(1d, pointerAcrossWhileActive.X - pointerBefore.X, 1e-12,
        "pointer sampling in the active frame must not jump at the raw seam");
    AssertNear(0d, pointerAcrossWhileActive.Y - pointerBefore.Y, 1e-12,
        "raw horizontal seam crossing must retain pointer Y before switching");

    var transition = space.SwitchForDevicePoint(
        new DevicePoint(1920d, 1000d),
        trackedBefore);
    AssertTrue(transition.Changed, "entering the right display must produce a transition");
    AssertEqual("right-200", transition.Current.Id,
        "the right display must become the active logical frame");
    var deviceAfter = space.WorldToDevice(transition.RebasedWorldPoint);
    AssertNear(deviceBefore.X, deviceAfter.X, 1e-12,
        "rebasing must preserve the pet's physical X");
    AssertNear(deviceBefore.Y, deviceAfter.Y, 1e-12,
        "rebasing must preserve the pet's physical Y");

    var leftSpace = new ActiveDisplaySpace(topology, primary);
    var leftTrackedBefore = primary.DeviceToWorld(new DevicePoint(100d, 500d));
    var leftDeviceBefore = leftSpace.WorldToDevice(leftTrackedBefore);
    var leftTransition = leftSpace.SwitchForDevicePoint(
        new DevicePoint(-1d, 500d),
        leftTrackedBefore);
    AssertTrue(leftTransition.Changed, "entering the left display must produce a transition");
    AssertEqual("left-125", leftTransition.Current.Id,
        "the left display must become the active logical frame");
    var leftDeviceAfter = leftSpace.WorldToDevice(leftTransition.RebasedWorldPoint);
    AssertNear(leftDeviceBefore.X, leftDeviceAfter.X, 1e-12,
        "left transition must preserve physical X");
    AssertNear(leftDeviceBefore.Y, leftDeviceAfter.Y, 1e-12,
        "left transition must preserve physical Y");
}

static void LargeAndNegativeCoordinates()
{
    var topology = new DisplayTopology(new[]
    {
        new DisplayDescriptor(
            "primary",
            new DeviceRect(0d, 0d, 3840d, 2160d),
            new DeviceRect(0d, 0d, 3840d, 2080d),
            2d,
            IsPrimary: true),
        new DisplayDescriptor(
            "far-left",
            new DeviceRect(-40000d, 0d, 0d, 1800d),
            new DeviceRect(-40000d, 0d, 0d, 1760d),
            1.25d),
        new DisplayDescriptor(
            "far-right",
            new DeviceRect(3840d, 0d, 50000d, 2160d),
            new DeviceRect(3840d, 0d, 50000d, 2080d),
            1.5d),
    });

    foreach (var source in new[]
             {
                 new DevicePoint(-39000.75d, 1000.5d),
                 new DevicePoint(45000.25d, 1700.75d),
             })
    {
        AssertTrue(source.X < short.MinValue || source.X > short.MaxValue,
            $"test precondition: {source.X} must exceed Int16 range");
        var display = topology.FindByDevicePoint(source);
        var roundTrip = topology.WorldToDevice(topology.DeviceToWorld(source, display), display);
        AssertNear(source.X, roundTrip.X, 1e-8,
            $"large coordinate X must not wrap or truncate for {source}");
        AssertNear(source.Y, roundTrip.Y, 1e-8,
            $"large coordinate Y must retain precision for {source}");
    }

    AssertEqual("far-left", topology.FindByDevicePoint(new DevicePoint(-39000d, 10d)).Id,
        "negative point must select the left display");
    AssertEqual("far-right", topology.FindByDevicePoint(new DevicePoint(45000d, 10d)).Id,
        "large positive point must select the right display");
}

static void SurfacePlacementNeverCrops()
{
    foreach (var scale in new[] { 1d, 1.25d, 1.5d, 2d })
    {
        var topology = OneDisplay(
            new DeviceRect(-50000d, -40000d, 50000d, 40000d),
            scale);
        var display = topology.Primary;
        var center = display.DeviceToWorld(new DevicePoint(-39000.375d, -33000.625d));
        var sizeWorld = new WorldSize(101.3f, 49.7f);
        var placement = SurfacePlacement.FromCenter(
            topology,
            center,
            sizeWorld);

        var cornerA = display.WorldToDevice(new WorldPoint(
            center.X - sizeWorld.Width * 0.5d,
            center.Y - sizeWorld.Height * 0.5d));
        var cornerB = display.WorldToDevice(new WorldPoint(
            center.X + sizeWorld.Width * 0.5d,
            center.Y + sizeWorld.Height * 0.5d));
        var expectedLeft = Math.Min(cornerA.X, cornerB.X);
        var expectedTop = Math.Min(cornerA.Y, cornerB.Y);
        var expectedRight = Math.Max(cornerA.X, cornerB.X);
        var expectedBottom = Math.Max(cornerA.Y, cornerB.Y);

        AssertEqual((int)Math.Floor(expectedLeft), placement.X,
            $"scale {scale}: surface left must use floor");
        AssertEqual((int)Math.Floor(expectedTop), placement.Y,
            $"scale {scale}: surface top must use floor");
        AssertEqual((int)Math.Ceiling(expectedRight), placement.X + placement.Width,
            $"scale {scale}: surface right must use ceil");
        AssertEqual((int)Math.Ceiling(expectedBottom), placement.Y + placement.Height,
            $"scale {scale}: surface bottom must use ceil");
        AssertTrue(placement.X <= expectedLeft && placement.X + placement.Width >= expectedRight,
            $"scale {scale}: horizontal world extent must be fully enclosed");
        AssertTrue(placement.Y <= expectedTop && placement.Y + placement.Height >= expectedBottom,
            $"scale {scale}: vertical world extent must be fully enclosed");
    }
}

static void TopologyReplacementAndHotPlug()
{
    var originalTopology = new DisplayTopology(new[]
    {
        new DisplayDescriptor(
            "primary",
            new DeviceRect(0d, 0d, 1920d, 1080d),
            new DeviceRect(0d, 0d, 1920d, 1040d),
            1d,
            IsPrimary: true),
        new DisplayDescriptor(
            "active",
            new DeviceRect(1920d, 0d, 5760d, 2160d),
            new DeviceRect(1920d, 0d, 5760d, 2080d),
            2d),
    });
    var trackedDevicePoint = new DevicePoint(4321.25d, 1333.75d);
    var space = new ActiveDisplaySpace(originalTopology, trackedDevicePoint);
    var trackedWorldPoint = space.DeviceToWorld(trackedDevicePoint);

    var changedTopology = new DisplayTopology(new[]
    {
        new DisplayDescriptor(
            "primary",
            new DeviceRect(0d, 0d, 2560d, 1440d),
            new DeviceRect(0d, 0d, 2560d, 1390d),
            1.25d,
            IsPrimary: true),
        new DisplayDescriptor(
            "active",
            new DeviceRect(2560d, -300d, 7680d, 2580d),
            new DeviceRect(2560d, -300d, 7680d, 2520d),
            1.5d),
    });
    var changed = space.ReplaceTopology(changedTopology, trackedWorldPoint);
    var deviceAfterChange = space.WorldToDevice(changed.RebasedWorldPoint);

    AssertTrue(changed.Changed,
        "changing bounds and scale for the active display must report a transition");
    AssertEqual("active", changed.Previous.Id,
        "transition must report the original active display");
    AssertEqual("active", changed.Current.Id,
        "same-ID replacement must remain the active display");
    AssertEqual("active", space.Display.Id,
        "active space must adopt the same-ID display from the replacement topology");
    AssertNear(trackedDevicePoint.X, deviceAfterChange.X, 1e-9,
        "same-ID topology replacement must preserve tracked device X");
    AssertNear(trackedDevicePoint.Y, deviceAfterChange.Y, 1e-9,
        "same-ID topology replacement must preserve tracked device Y");

    var unpluggedTopology = new DisplayTopology(new[]
    {
        new DisplayDescriptor(
            "new-primary",
            new DeviceRect(-3440d, -900d, 0d, 1260d),
            new DeviceRect(-3440d, -860d, 0d, 1260d),
            2d,
            IsPrimary: true),
    });
    var unplugged = space.ReplaceTopology(
        unpluggedTopology,
        changed.RebasedWorldPoint);
    var deviceAfterUnplug = space.WorldToDevice(unplugged.RebasedWorldPoint);

    AssertTrue(unplugged.Changed,
        "removing the active display must report a transition");
    AssertEqual("active", unplugged.Previous.Id,
        "display-removal transition must identify the removed display");
    AssertEqual(unpluggedTopology.Primary.Id, unplugged.Current.Id,
        "a missing active display must fall back to the replacement primary");
    AssertEqual(unpluggedTopology.Primary.Id, space.Display.Id,
        "active space must retain the replacement primary after fallback");
    AssertTrue(double.IsFinite(unplugged.RebasedWorldPoint.X) &&
               double.IsFinite(unplugged.RebasedWorldPoint.Y),
        "fallback rebased world position must remain finite");
    AssertNear(trackedDevicePoint.X, deviceAfterUnplug.X, 1e-9,
        "display-removal fallback must preserve tracked device X");
    AssertNear(trackedDevicePoint.Y, deviceAfterUnplug.Y, 1e-9,
        "display-removal fallback must preserve tracked device Y");
}

static void InvalidTopologyDataIsContained()
{
    var valid = new DisplayDescriptor(
        "valid",
        new DeviceRect(0d, 0d, 1920d, 1080d),
        new DeviceRect(0d, 0d, 1920d, 1040d),
        1d,
        IsPrimary: true);
    var topology = new DisplayTopology(new[]
    {
        valid,
        new DisplayDescriptor(
            "infinite",
            new DeviceRect(double.NegativeInfinity, 0d, 0d, 1080d),
            new DeviceRect(-1920d, 0d, 0d, 1040d),
            1d),
        new DisplayDescriptor(
            "outside-work-area",
            new DeviceRect(1920d, 0d, 3840d, 1080d),
            new DeviceRect(1900d, 0d, 3840d, 1040d),
            1d),
    });

    AssertEqual(1, topology.Displays.Count,
        "invalid native descriptors must be ignored instead of producing non-finite world state");
    AssertEqual("valid", topology.Primary.Id,
        "the remaining valid display must remain primary");

    AssertThrows<ArgumentException>(() => new DisplayTopology(new[]
        {
            valid,
            valid with { IsPrimary = false },
        }),
        "duplicate display IDs must be rejected because ID-based topology replacement would be ambiguous");
    AssertThrows<ArgumentException>(() => new DisplayTopology(new[]
        {
            valid,
            new DisplayDescriptor(
                "also-primary",
                new DeviceRect(1920d, 0d, 3840d, 1080d),
                new DeviceRect(1920d, 0d, 3840d, 1040d),
                1d,
                IsPrimary: true),
        }),
        "multiple primary displays must be rejected rather than selected by incidental sort order");
}

static void RandomizedTopologyInvariants()
{
    var random = new Random(0x51A7E);
    var scales = new[] { 1d, 1.25d, 1.5d, 2d };
    for (var iteration = 0; iteration < 250; iteration++)
    {
        var displayCount = random.Next(1, 5);
        var primaryIndex = random.Next(displayCount);
        var descriptors = new List<DisplayDescriptor>(displayCount);
        var left = random.Next(-50000, 5001);
        for (var index = 0; index < displayCount; index++)
        {
            var width = random.Next(800, 5001);
            var height = random.Next(600, 3001);
            var top = random.Next(-20000, 20001);
            var bottomInset = random.Next(0, Math.Min(120, height / 4) + 1);
            var bounds = new DeviceRect(left, top, left + width, top + height);
            var working = new DeviceRect(
                bounds.Left,
                bounds.Top,
                bounds.Right,
                bounds.Bottom - bottomInset);
            descriptors.Add(new DisplayDescriptor(
                $"display-{iteration}-{index}",
                bounds,
                working,
                scales[random.Next(scales.Length)],
                index == primaryIndex));
            left += width + random.Next(0, 501);
        }

        var topology = new DisplayTopology(descriptors);
        foreach (var display in topology.Displays)
        {
            var source = new DevicePoint(
                display.DeviceBounds.Left + random.NextDouble() * display.DeviceBounds.Width,
                display.DeviceBounds.Top + random.NextDouble() * display.DeviceBounds.Height);
            AssertEqual(display.Id, topology.FindByDevicePoint(source).Id,
                $"iteration {iteration}: a point inside a display must select that display");
            var roundTrip = display.WorldToDevice(display.DeviceToWorld(source));
            AssertNear(source.X, roundTrip.X, 1e-8,
                $"iteration {iteration}: device/world/device X must round-trip");
            AssertNear(source.Y, roundTrip.Y, 1e-8,
                $"iteration {iteration}: device/world/device Y must round-trip");
        }

        var space = new ActiveDisplaySpace(topology, topology.Primary);
        var trackedDevice = new DevicePoint(
            topology.Primary.DeviceBounds.CenterX,
            topology.Primary.DeviceBounds.CenterY);
        var trackedWorld = space.DeviceToWorld(trackedDevice);
        foreach (var target in topology.Displays)
        {
            var transition = space.SwitchForDevicePoint(
                new DevicePoint(target.DeviceBounds.CenterX, target.DeviceBounds.CenterY),
                trackedWorld);
            trackedWorld = transition.RebasedWorldPoint;
            var after = space.WorldToDevice(trackedWorld);
            AssertNear(trackedDevice.X, after.X, 1e-8,
                $"iteration {iteration}: display transition must preserve tracked device X");
            AssertNear(trackedDevice.Y, after.Y, 1e-8,
                $"iteration {iteration}: display transition must preserve tracked device Y");
        }
    }
}

static void FixedStepIsCadenceIndependent()
{
    var results = new[] { 60, 120, 144 }
        .Select(RunFixedStepScenario)
        .ToArray();

    var baseline = results[0];
    foreach (var result in results.Skip(1))
    {
        AssertEqual(baseline.StepCount, result.StepCount,
            $"{result.RenderHz} Hz and {baseline.RenderHz} Hz must execute the same fixed-step count");
        AssertNear(baseline.Position, result.Position, 1e-12,
            $"{result.RenderHz} Hz and {baseline.RenderHz} Hz must produce the same position");
        AssertNear(baseline.Velocity, result.Velocity, 1e-12,
            $"{result.RenderHz} Hz and {baseline.RenderHz} Hz must produce the same velocity");
    }

    AssertEqual(600, baseline.StepCount,
        "five seconds at a 120 Hz simulation step must execute exactly 600 steps");
}

static FixedStepScenarioResult RunFixedStepScenario(int renderHz)
{
    const float simulationStep = 1f / 120f;
    const float maximumCatchUp = 0.25f;
    const int durationSeconds = 5;
    var runner = new FixedStepRunner();
    var position = 0d;
    var velocity = 2.5d;
    var stepCount = 0;

    for (var frame = 0; frame < renderHz * durationSeconds; frame++)
    {
        runner.Advance(1f / renderHz, simulationStep, maximumCatchUp, (delta, _) =>
        {
            velocity += 0.75d * delta;
            position += velocity * delta;
            stepCount++;
        });
    }

    return new FixedStepScenarioResult(renderHz, stepCount, position, velocity);
}

static void FixedStepOverloadIsDiagnosable()
{
    var runner = new FixedStepRunner();
    var callbackCount = 0;
    var result = runner.Advance(
        frameDelta: 0.5f,
        simulationStep: 1f / 120f,
        maximumFrameCatchUp: 0.1f,
        (_, _) => callbackCount++);

    AssertNear(0.1d, result.FrameDelta, 1e-7,
        "result.FrameDelta must expose the accepted (clipped) frame time");
    AssertTrue(result.FrameDelta < 0.5f,
        "accepted frame time must make overload clipping observable to callers");
    AssertEqual(12, result.StepCount,
        "100 ms catch-up at 120 Hz must execute 12 fixed steps");
    AssertEqual(result.StepCount, callbackCount,
        "reported step count must equal executed callbacks");
    AssertNear(0.1d, result.SimulationDelta, 1e-7,
        "reported simulated time must equal the clipped catch-up budget");
    AssertNear(result.AccumulatedTime, runner.AccumulatedTime, 1e-12,
        "result must expose the runner's post-advance accumulator for diagnostics");
    AssertTrue(result.AccumulatedTime >= 0d && result.AccumulatedTime < 1d / 120d,
        "post-overload accumulator must remain within one simulation step");
}

static void RuntimeOwnsCadence()
{
    var results = new[] { 50, 60, 120, 144, 240 }
        .Select(RunRuntimeScenario)
        .ToArray();
    var baseline = results[0];

    foreach (var result in results.Skip(1))
    {
        AssertEqual(600, result.TotalSteps,
            $"{result.RenderHz} Hz must execute 600 engine-owned steps in five seconds");
        AssertEqual(baseline.Snapshot.StepCount, result.Snapshot.StepCount,
            $"{result.RenderHz} Hz must reach the same gameplay step state");
        AssertNear(baseline.Snapshot.PositionX, result.Snapshot.PositionX, 0d,
            $"{result.RenderHz} Hz must reach the same gameplay position");
        AssertNear(baseline.Snapshot.VelocityX, result.Snapshot.VelocityX, 0d,
            $"{result.RenderHz} Hz must reach the same gameplay velocity");
    }

    AssertEqual(600, baseline.TotalSteps,
        "50 Hz baseline must execute 600 engine-owned steps in five seconds");
}

static RuntimeScenarioResult RunRuntimeScenario(int renderHz)
{
    var game = new RuntimeProbeGame(new DesktopPetTiming(1f / 120f, 0.25f));
    var runtime = new DesktopPetRuntime<RuntimeProbeSnapshot>(game);
    runtime.Reset(Vector2.Zero);
    var totalSteps = 0;
    var area = new WorldRect(-1000f, -1000f, 1000f, 1000f);

    for (var frameIndex = 0; frameIndex < renderHz * 5; frameIndex++)
    {
        var frame = runtime.Advance(new DesktopPetInput(
            1f / renderHz,
            area,
            new SafetyArea(area, true),
            default,
            false));
        totalSteps += frame.SimulationSteps;
    }

    return new RuntimeScenarioResult(renderHz, totalSteps, runtime.CurrentSnapshot);
}

static void RuntimeOverloadAndSnapshotReuse()
{
    AssertThrows<ArgumentOutOfRangeException>(
        () => _ = new DesktopPetTiming(float.NaN, 0.25f),
        "timing must reject non-finite simulation steps");
    AssertThrows<ArgumentOutOfRangeException>(
        () => _ = new DesktopPetTiming(1f / 120f, 0f),
        "timing must reject non-positive catch-up limits");
    AssertThrows<ArgumentOutOfRangeException>(
        () => _ = new DesktopPetTiming(1f / 120f, 1f / 240f),
        "timing must reject a catch-up limit shorter than one fixed step");
    AssertThrows<ArgumentOutOfRangeException>(
        () => _ = new DesktopPetTiming(1f / 120f, 0.01f),
        "timing must reject catch-up that cannot cover a 30 Hz display frame");
    AssertThrows<ArgumentOutOfRangeException>(
        () => _ = new DesktopPetTiming(1f / 120f, 0.005f),
        "timing must reject the known cadence-dependent 5 ms catch-up case");
    AssertThrows<ArgumentOutOfRangeException>(
        () => _ = new DesktopPetTiming(float.Epsilon, 1f / 30f),
        "timing must reject a step that would create an unbounded display-frame loop");
    AssertThrows<ArgumentOutOfRangeException>(
        () => new FixedStepRunner().Advance(
            1f,
            float.Epsilon,
            1f,
            (_, _) => throw new InvalidOperationException("must not execute")),
        "the low-level runner must reject step-count overflow before invoking gameplay");
    _ = new DesktopPetTiming(1f / 480f, 1f);

    var rawAdvance = typeof(IDesktopPetGame<>).GetMethods()
        .FirstOrDefault(method => method.Name == "Advance");
    AssertTrue(rawAdvance is null,
        "the gameplay contract must not expose a raw display-frame Advance method");

    var area = new WorldRect(-100f, -100f, 100f, 100f);
    var game = new RuntimeProbeGame(new DesktopPetTiming(1f / 120f, 0.1f));
    var runtime = new DesktopPetRuntime<RuntimeProbeSnapshot>(game);
    runtime.Reset(Vector2.Zero);
    var overloaded = runtime.Advance(new DesktopPetInput(
        0.5f,
        area,
        new SafetyArea(area, true),
        default,
        false));

    AssertNear(0.1d, overloaded.FrameDelta, 1e-7,
        "runtime frame must report the accepted overload-clipped delta");
    AssertEqual(12, overloaded.SimulationSteps,
        "runtime must execute only the clipped 100 ms fixed-step budget");
    AssertEqual(12, overloaded.Snapshot.StepCount,
        "runtime output must capture state after every clipped fixed step");
    var captured = overloaded.Snapshot;
    var captureCount = game.CaptureCount;

    var zeroStep = runtime.Advance(new DesktopPetInput(
        0f,
        area,
        new SafetyArea(area, true),
        default,
        false));
    AssertEqual(0, zeroStep.SimulationSteps,
        "zero display delta must not execute gameplay");
    AssertTrue(ReferenceEquals(captured, zeroStep.Snapshot),
        "zero-step frames must reuse the immutable cached snapshot instance");
    AssertEqual(captureCount, game.CaptureCount,
        "zero-step frames must not ask gameplay to recapture geometry");
}

static void RuntimeRejectsInvalidBoundaries()
{
    var validTiming = new DesktopPetTiming(1f / 120f, 0.25f);
    AssertThrows<ArgumentOutOfRangeException>(
        () => _ = new DesktopPetRuntime<RuntimeProbeSnapshot>(
            new RuntimeProbeGame(default)),
        "default-constructed timing must not bypass validation");

    var defaultMetrics = new RuntimeProbeGame(validTiming) { Metrics = default };
    AssertThrows<ArgumentException>(
        () => _ = new DesktopPetRuntime<RuntimeProbeSnapshot>(defaultMetrics),
        "default-constructed metrics must not bypass validation");
    AssertBadMetrics(
        validTiming,
        Metrics() with { ModelToWorldScale = float.NaN },
        "non-finite model scale");
    AssertBadMetrics(
        validTiming,
        Metrics() with { NavigationRadiusWorld = -1f },
        "negative navigation radius");
    AssertBadMetrics(
        validTiming,
        Metrics() with { InitialHeading = float.PositiveInfinity },
        "non-finite heading");
    AssertBadMetrics(
        validTiming,
        Metrics() with { SpawnFadeDuration = 0f },
        "non-positive fade duration");

    var poisonedAtConstruction = new RuntimeProbeGame(validTiming)
    {
        PoisonLookDirection = true
    };
    AssertThrows<ArgumentException>(
        () => _ = new DesktopPetRuntime<RuntimeProbeSnapshot>(poisonedAtConstruction),
        "runtime must reject a plugin with invalid initial scalar state");

    var game = new RuntimeProbeGame(validTiming);
    var runtime = new DesktopPetRuntime<RuntimeProbeSnapshot>(game);
    var cachedMetrics = runtime.Metrics;
    game.Timing = new DesktopPetTiming(1f / 60f, 0.25f);
    game.Metrics = default;
    runtime.Reset(Vector2.Zero);
    var area = new WorldRect(-100f, -100f, 100f, 100f);
    var frame = runtime.Advance(new DesktopPetInput(
        1f / 60f,
        area,
        new SafetyArea(area, true),
        default,
        false));
    AssertEqual(2, frame.SimulationSteps,
        "runtime must use the immutable timing captured at construction");
    AssertEqual(cachedMetrics, runtime.Metrics,
        "runtime must use the immutable metrics captured at construction");

    var stepCountBeforeInvalidInput = game.StepInvocationCount;
    AssertThrows<ArgumentException>(
        () => runtime.Advance(new DesktopPetInput(
            1f / 60f,
            default,
            new SafetyArea(area, true),
            default,
            false)),
        "default navigation geometry must be rejected before gameplay");
    AssertThrows<ArgumentException>(
        () => runtime.Advance(new DesktopPetInput(
            1f / 60f,
            area,
            new SafetyArea(area, true),
            new PointerSample(new Vector2(float.NaN, 0f), false),
            false)),
        "non-finite pointer geometry must be rejected even when unavailable");
    AssertEqual(stepCountBeforeInvalidInput, game.StepInvocationCount,
        "invalid host geometry must not reach gameplay");
    AssertThrows<ArgumentException>(
        () => runtime.DragTo(new Vector2(float.PositiveInfinity, 0f)),
        "invalid command vectors must be rejected before gameplay");

    var poisonGame = new RuntimeProbeGame(validTiming)
    {
        PoisonOnNextStep = true
    };
    var poisonRuntime = new DesktopPetRuntime<RuntimeProbeSnapshot>(poisonGame);
    poisonRuntime.Reset(Vector2.Zero);
    poisonGame.PoisonOnNextStep = true;
    AssertThrows<ArgumentException>(
        () => poisonRuntime.Advance(new DesktopPetInput(
            0.1f,
            area,
            new SafetyArea(area, true),
            default,
            false)),
        "invalid gameplay state must abort a catch-up batch immediately");
    AssertEqual(1, poisonGame.StepInvocationCount,
        "no later fixed steps may execute after plugin state becomes invalid");

    var commandPoisonGame = new RuntimeProbeGame(validTiming);
    var commandPoisonRuntime =
        new DesktopPetRuntime<RuntimeProbeSnapshot>(commandPoisonGame);
    commandPoisonGame.PoisonOnMove = true;
    AssertThrows<ArgumentException>(
        () => commandPoisonRuntime.MoveTo(Vector2.Zero),
        "runtime must validate plugin state immediately after a command");

    var resetPoisonGame = new RuntimeProbeGame(validTiming);
    var resetPoisonRuntime =
        new DesktopPetRuntime<RuntimeProbeSnapshot>(resetPoisonGame);
    resetPoisonGame.PoisonOnReset = true;
    AssertThrows<ArgumentException>(
        () => resetPoisonRuntime.Reset(Vector2.Zero),
        "runtime must validate plugin state immediately after reset");

    static DesktopPetMetrics Metrics() => new(
        WorldSize.Square(100f),
        10f,
        20f,
        20f,
        1f,
        0f,
        0.1f);

    static void AssertBadMetrics(
        DesktopPetTiming timing,
        DesktopPetMetrics metrics,
        string context)
    {
        var invalidGame = new RuntimeProbeGame(timing) { Metrics = metrics };
        AssertThrows<ArgumentException>(
            () => _ = new DesktopPetRuntime<RuntimeProbeSnapshot>(invalidGame),
            $"runtime must reject {context}");
    }
}

static DisplayTopology OneDisplay(DeviceRect bounds, double scale) =>
    new(new[]
    {
        new DisplayDescriptor("only", bounds, bounds, scale, IsPrimary: true),
    });

static void AssertNear(double expected, double actual, double tolerance, string message)
{
    if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException(
            $"{message}. Expected {expected:R} ± {tolerance:R}, actual {actual:R}.");
    }
}

static void AssertEqual<T>(T expected, T actual, string message)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{message}. Expected {expected}, actual {actual}.");
    }
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message + ".");
    }
}

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(
        $"{message}. Expected {typeof(TException).Name}.");
}

readonly record struct FixedStepScenarioResult(
    int RenderHz,
    int StepCount,
    double Position,
    double Velocity);

readonly record struct RuntimeScenarioResult(
    int RenderHz,
    int TotalSteps,
    RuntimeProbeSnapshot Snapshot);

sealed record RuntimeProbeSnapshot(int StepCount, float PositionX, float VelocityX);

sealed class RuntimeProbeGame : IDesktopPetGame<RuntimeProbeSnapshot>
{
    private int _stepCount;
    private float _velocityX;
    private readonly float _expectedSimulationStep;

    public DesktopPetMetrics Metrics { get; set; } = new(
        WorldSize.Square(100f),
        10f,
        20f,
        20f,
        1f,
        0f,
        0.1f);
    public DesktopPetTiming Timing { get; set; }
    public Vector2 Position { get; private set; }
    public Vector2 LookDirection => PoisonLookDirection
        ? new Vector2(float.NaN, 0f)
        : Vector2.UnitX;
    public bool IsPaused { get; private set; }
    public int CaptureCount { get; private set; }
    public int StepInvocationCount { get; private set; }
    public bool PoisonLookDirection { get; set; }
    public bool PoisonOnNextStep { get; set; }
    public bool PoisonOnMove { get; set; }
    public bool PoisonOnReset { get; set; }

    public RuntimeProbeGame(DesktopPetTiming timing)
    {
        Timing = timing;
        _expectedSimulationStep = timing.SimulationStep;
    }

    public void Reset(Vector2 position)
    {
        Position = position;
        if (PoisonOnReset)
        {
            Position = new Vector2(float.NaN, 0f);
            return;
        }
        _velocityX = 2.5f;
        _stepCount = 0;
        StepInvocationCount = 0;
        IsPaused = false;
    }

    public void AdvanceFixedStep(in DesktopPetFixedStepInput input)
    {
        StepInvocationCount++;
        if (PoisonOnNextStep)
        {
            PoisonOnNextStep = false;
            Position = new Vector2(float.NaN, 0f);
            return;
        }
        if (input.StepDelta != _expectedSimulationStep)
        {
            throw new InvalidOperationException(
                "Runtime did not supply exactly the configured fixed step.");
        }
        _velocityX += 0.75f * input.StepDelta;
        Position += new Vector2(_velocityX * input.StepDelta, 0f);
        _stepCount++;
    }

    public RuntimeProbeSnapshot CaptureSnapshot()
    {
        CaptureCount++;
        return new RuntimeProbeSnapshot(_stepCount, Position.X, _velocityX);
    }

    public void BeginPrimaryInteraction(Vector2 modelPoint)
    {
    }

    public void DragTo(Vector2 worldPosition) => Position = worldPosition;

    public void EndPrimaryInteraction(Vector2 settledWorldPosition) =>
        Position = settledWorldPosition;

    public bool TogglePaused() => IsPaused = !IsPaused;

    public void MoveTo(Vector2 worldPosition) => Position = PoisonOnMove
        ? new Vector2(float.NaN, 0f)
        : worldPosition;

    public void RebaseWorldPosition(Vector2 delta) => Position += delta;
}
