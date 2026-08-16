using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using DesktopPet.Engine;
using InfiniteLizards.Desktop;
using InfiniteLizards.Desktop.Platform;
using InfiniteLizards.Desktop.Rendering;

var tests = new (string Name, Action Run)[]
{
    ("Win32 client rectangles convert to exact outer-window rectangles", ClientToOuterIsExact),
    ("Win32 frame insets are measured in virtual-desktop pixels", InsetsAreMeasuredExactly),
    ("Mixed-DPI drag rebases gameplay before applying physical movement", MixedDpiDragTransitionSelfTest.Run),
    ("Win32 placement rejects invalid and overflowing native geometry", InvalidGeometryIsRejected),
    ("Win32 overlay style composition survives Avalonia style rebuilds", OverlayStyleComposition),
    ("Win32 backend uses Avalonia-owned callbacks instead of a native WndProc delegate", UsesAvaloniaCallbacks),
    ("Windows production-acceptance telemetry has a strict launch-scoped protocol", WindowsAcceptanceProtocolIsStrict),
    ("Input-region contracts reject default-bypassed and non-finite primitives", InputRegionContractsAreDeeplyValidated),
    ("Win32 input-region coordinates preserve DPI and client insets", InputRegionTransformIsExact),
    ("Input-region refresh policy follows every pose and bounds failed retries", InputRegionRefreshIsBounded),
    ("Render fences coalesce to the last completed Render and retry after failure", RenderFenceStateIsRaceSafe),
    ("Rendered pose acknowledgements atomically advance bounded native placements", SurfacePlacementTransactionIsVersioned),
    ("Render-region transactions are bounded and keep committed plus pending poses", RenderRegionTransactionIsConservative),
    ("Out-of-order render acknowledgements cannot regress pose or DPI", RenderAcknowledgementsAreMonotonic),
    ("Mixed-DPI raster plans retain pose-scale correlation without narrowing early", MixedDpiRasterPlanIsCorrelated),
    ("Failed native synchronization freezes untracked visual submissions", FailedSynchronizationBackpressuresPresentation),
    ("Late compositor acknowledgements are harmless after unsubscription", LateRenderCommitAfterUnsubscribeIsHarmless),
    ("Fail-closed state is transparent until a shaped region is installed", FailClosedPolicyIsExplicit),
    ("macOS click-through state requires native getter confirmation", MacOsClickThroughRequiresReadback),
    ("macOS active-Space acceptance uses the public window list", MacOsActiveSpaceAcceptanceUsesWindowList),
    ("macOS non-activation acceptance uses the public frontmost application", MacOsFocusAcceptanceUsesFrontmostApplication),
    ("macOS click-through acceptance verifies native hit routing", MacOsHitRoutingRequiresNativeTransition),
    ("Visible geometry remains inside conservative hit dimensions", VisibleGeometryIsConservativelySized),
    ("Lizard presenter exports and hits its shadow geometry", LizardShadowGeometryIsShared),
    ("Lizard hit testing retains the compositor-committed presentation", VersionedHitTestingTracksCommittedPose),
    ("Unified Skia rendering is DPI-normalized and deterministic", RasterDpiInvarianceSelfTest.Run),
    ("Degenerate invisible ellipses are omitted from the shared pose", DegenerateInvisibleEllipsesAreSkipped),
    ("Minimum pupil radius expands input and canvas bounds", MinimumPupilRadiusIsCovered),
    ("Win32 shaped input uses SetWindowRgn without message forwarding", UsesNativeShapedInputContract),
    ("PetWindow preserves pre-Show and pointer-loss safety boundaries", PetWindowSafetyBoundaries),
    ("Diagnostic mode owns a visible lifecycle-bound Avalonia panel", DiagnosticPanelIsVisibleAndDecoupled),
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
        Console.Error.WriteLine($"      {exception}");
    }
}

Console.WriteLine();
Console.WriteLine($"{tests.Length - failureCount}/{tests.Length} desktop self-tests passed.");
return failureCount == 0 ? 0 : 1;

static void ClientToOuterIsExact()
{
    var client = new SurfacePlacement(-500, -200, 1200, 700);
    var insets = new Win32WindowInsets(11, 11, 13, 23);
    var outer = Win32ClientRectPlacement.ToOuterWindow(client, insets);

    AssertEqual(-511, outer.X, "negative virtual-desktop X");
    AssertEqual(-211, outer.Y, "negative virtual-desktop Y");
    AssertEqual(1224, outer.Width, "outer width");
    AssertEqual(734, outer.Height, "outer height");

    var borderless = Win32ClientRectPlacement.ToOuterWindow(
        new SurfacePlacement(3840, 0, 401, 301),
        new Win32WindowInsets(0, 0, 0, 0));
    AssertEqual(new Win32OuterWindowPlacement(3840, 0, 401, 301), borderless,
        "a truly borderless HWND must retain its client rectangle exactly");

    foreach (var scale in new[] { 1d, 1.25d, 1.5d, 2d })
    {
        var frame = (int)Math.Ceiling(8d * scale);
        var scaledInsets = new Win32WindowInsets(frame, frame, frame + 1, frame + 2);
        var scaledClient = new SurfacePlacement(
            scale == 1.25d ? -2400 : 1920,
            -120,
            (int)Math.Ceiling(320d * scale),
            (int)Math.Ceiling(240d * scale));
        var scaledOuter = Win32ClientRectPlacement.ToOuterWindow(scaledClient, scaledInsets);
        AssertEqual(scaledClient.X, scaledOuter.X + scaledInsets.Left,
            $"{scale:P0} client X");
        AssertEqual(scaledClient.Y, scaledOuter.Y + scaledInsets.Top,
            $"{scale:P0} client Y");
        AssertEqual(scaledClient.Width,
            scaledOuter.Width - scaledInsets.Left - scaledInsets.Right,
            $"{scale:P0} client width");
        AssertEqual(scaledClient.Height,
            scaledOuter.Height - scaledInsets.Top - scaledInsets.Bottom,
            $"{scale:P0} client height");
    }
}

static void InsetsAreMeasuredExactly()
{
    var insets = Win32ClientRectPlacement.MeasureInsets(
        new Win32PixelRect(-511, -211, 713, 523),
        new Win32PixelRect(-500, -200, 700, 500));

    AssertEqual(new Win32WindowInsets(11, 11, 13, 23), insets,
        "client-to-screen geometry must retain asymmetric frame insets");
}

static void InvalidGeometryIsRejected()
{
    AssertThrows<ArgumentOutOfRangeException>(() =>
        Win32ClientRectPlacement.ToOuterWindow(
            new SurfacePlacement(0, 0, 0, 100),
            new Win32WindowInsets(1, 1, 1, 1)));
    AssertThrows<ArgumentOutOfRangeException>(() =>
        Win32ClientRectPlacement.MeasureInsets(
            new Win32PixelRect(0, 0, 100, 100),
            new Win32PixelRect(-1, 0, 100, 100)));
    AssertThrows<OverflowException>(() =>
        Win32ClientRectPlacement.ToOuterWindow(
            new SurfacePlacement(int.MinValue, 0, 100, 100),
            new Win32WindowInsets(1, 0, 0, 0)));
}

static void OverlayStyleComposition()
{
    const uint arbitraryAvaloniaStyle = 0x00200000U;
    const uint arbitraryAvaloniaWindowStyle = 0x40000000U;
    const uint disabled = 0x08000000U;
    const uint transparent = 0x00000020U;
    const uint toolWindow = 0x00000080U;
    const uint noActivate = 0x08000000U;

    var inputDisabled = Win32OverlayStyle.ComposeWindowStyle(
        arbitraryAvaloniaWindowStyle,
        inputEnabled: false);
    AssertTrue((inputDisabled & arbitraryAvaloniaWindowStyle) != 0 &&
               (inputDisabled & disabled) != 0,
        "disabling native input must preserve Avalonia styles and add WS_DISABLED");
    var inputEnabled = Win32OverlayStyle.ComposeWindowStyle(
        inputDisabled,
        inputEnabled: true);
    AssertTrue((inputEnabled & arbitraryAvaloniaWindowStyle) != 0 &&
               (inputEnabled & disabled) == 0,
        "enabling native input must preserve Avalonia styles and clear WS_DISABLED");

    var clickThrough = Win32OverlayStyle.ComposeExtendedStyle(
        arbitraryAvaloniaStyle,
        clickThrough: true);
    AssertTrue((clickThrough & arbitraryAvaloniaStyle) != 0,
        "Avalonia-owned extended styles must be preserved");
    AssertTrue((clickThrough & (transparent | toolWindow | noActivate)) ==
               (transparent | toolWindow | noActivate),
        "production click-through mode must carry every required overlay style");

    var interactive = Win32OverlayStyle.ComposeExtendedStyle(
        clickThrough,
        clickThrough: false);
    AssertTrue((interactive & transparent) == 0,
        "interactive mode must clear WS_EX_TRANSPARENT");
    AssertTrue((interactive & (toolWindow | noActivate)) == (toolWindow | noActivate),
        "interactive mode must stay out of activation and task switching");
    AssertTrue((interactive & arbitraryAvaloniaStyle) != 0,
        "toggling hit testing must not discard Avalonia-owned styles");
}

static void UsesAvaloniaCallbacks()
{
    var fields = typeof(Win32OverlayWindowBackend).GetFields(
        BindingFlags.Instance | BindingFlags.NonPublic);
    AssertTrue(fields.Any(field =>
            field.FieldType == typeof(Win32Properties.CustomWindowStylesCallback)),
        "the backend must register Avalonia's style-rebuild callback");
    AssertTrue(fields.Any(field =>
            field.FieldType == typeof(Win32Properties.CustomWndProcHookCallback)),
        "the backend must use Avalonia's lifetime-owned WndProc hook");

    var unexpectedNativeDelegate = fields.FirstOrDefault(field =>
        typeof(Delegate).IsAssignableFrom(field.FieldType) &&
        field.FieldType != typeof(Win32Properties.CustomWindowStylesCallback) &&
        field.FieldType != typeof(Win32Properties.CustomWndProcHookCallback));
    AssertTrue(unexpectedNativeDelegate is null,
        $"a manually rooted native WndProc delegate is not allowed: {unexpectedNativeDelegate?.Name}");
}

static void WindowsAcceptanceProtocolIsStrict()
{
    const string suppliedNonce = "00112233445566778899AABBCCDDEEFF";
    const string canonicalNonce = "00112233445566778899aabbccddeeff";
    const string routeTag = "f0e1d2c3b4a59687";
    AssertTrue(Win32NativeAcceptanceProtocol.TryParse(
            suppliedNonce,
            "42",
            routeTag,
            out var configuration),
        "a complete nonce/parent/tag triple must enable protocol parsing");
    AssertEqual(canonicalNonce, configuration.CanonicalNonce,
        "the registered-message nonce must be canonical lowercase GUID-N text");
    AssertEqual(42, configuration.ParentProcessId,
        "the declared controller PID must retain its decimal value");
    AssertEqual(0xF0E1D2C3B4A59687UL, configuration.RouteTag,
        "the exact 64-bit SendInput tag must survive parsing");
    AssertEqual(
        "InfiniteLizards.Windows.NativeAcceptance.v1.Control",
        Win32NativeAcceptanceProtocol.ControlMessageName(canonicalNonce),
        "the controller must reuse one stable registered message instead of leaking session atoms");
    var controlToken = Win32NativeAcceptanceProtocol.DeriveControlToken(canonicalNonce);
    AssertTrue(controlToken != 0,
        "the launch nonce must derive a nonzero wParam control token");
    AssertEqual(controlToken,
        Win32NativeAcceptanceProtocol.DeriveControlToken(canonicalNonce),
        "the control token derivation must be deterministic");
    AssertTrue(controlToken != Win32NativeAcceptanceProtocol.DeriveControlToken(
            "10112233445566778899aabbccddeeff"),
        "different launch nonces must not share the same control token");
    AssertEqual(7, Win32NativeAcceptanceProtocol.SafetyReleaseUpCounter,
        "the production protocol must expose tagged emergency LEFTUP acknowledgement");
    AssertEqual(0x494C5A4453414645UL,
        Win32NativeAcceptanceProtocol.SafetyReleaseExtraInfo,
        "controller and production telemetry must share the ILZDSAFE x64 tag");
    var packedReadSafetyCounter = new nint(unchecked((long)(((ulong)7 << 32) | 3u)));
    AssertEqual(3,
        Win32NativeAcceptanceProtocol.PayloadCommand(packedReadSafetyCounter),
        "the stable message payload must carry command in its low dword");
    AssertEqual(7,
        Win32NativeAcceptanceProtocol.PayloadParameter(packedReadSafetyCounter),
        "the stable message payload must carry counter ID in its high dword");
    AssertEqual(0x494C5A4450524F44UL, Win32NativeAcceptanceProtocol.PingMagic,
        "the production HWND ping must return the fixed ILZDPROD magic");

    AssertTrue(!Win32NativeAcceptanceProtocol.TryParse(
            new string('0', 32), "42", routeTag, out _),
        "an empty GUID is not a launch nonce");
    AssertTrue(!Win32NativeAcceptanceProtocol.TryParse(
            suppliedNonce, " 42", routeTag, out _),
        "PID parsing must reject whitespace and shell-shaped values");
    AssertTrue(!Win32NativeAcceptanceProtocol.TryParse(
            suppliedNonce, "42", "0000000000000000", out _),
        "zero cannot identify injected input");
    AssertTrue(!Win32NativeAcceptanceProtocol.TryParse(
            suppliedNonce, "42", "1234", out _),
        "route tags must carry all 64 bits in a fixed-width representation");
}

static void InputRegionContractsAreDeeplyValidated()
{
    var segment = new DesktopPetBezierSegment(
        new Point(1, 2),
        new Point(3, 4),
        new Point(5, 6));
    var path = new DesktopPetBezierPath(
        new Point(0, 0),
        ImmutableArray.Create(segment));
    var region = new DesktopPetInputRegion(
        ImmutableArray.Create(path),
        ImmutableArray.Create(new DesktopPetStrokedBezierPath(path, 2.5)),
        ImmutableArray.Create(new DesktopPetRegionEllipse(new Point(4, 5), 2, 3)));
    AssertEqual(1, region.Fills.Length, "valid fill count");

    AssertThrows<ArgumentException>(() => new DesktopPetBezierPath(
        new Point(0, 0),
        ImmutableArray.Create(default(DesktopPetBezierSegment))));
    AssertThrows<ArgumentException>(() => new DesktopPetInputRegion(
        ImmutableArray.Create(default(DesktopPetBezierPath)),
        ImmutableArray<DesktopPetStrokedBezierPath>.Empty,
        ImmutableArray<DesktopPetRegionEllipse>.Empty));
    AssertThrows<ArgumentException>(() => new DesktopPetInputRegion(
        ImmutableArray<DesktopPetBezierPath>.Empty,
        ImmutableArray.Create(default(DesktopPetStrokedBezierPath)),
        ImmutableArray<DesktopPetRegionEllipse>.Empty));
    AssertThrows<ArgumentOutOfRangeException>(() => new DesktopPetInputRegion(
        ImmutableArray<DesktopPetBezierPath>.Empty,
        ImmutableArray<DesktopPetStrokedBezierPath>.Empty,
        ImmutableArray.Create(default(DesktopPetRegionEllipse))));
    AssertThrows<ArgumentOutOfRangeException>(() => new DesktopPetRegionEllipse(
        new Point(double.NaN, 0),
        1,
        1));
    AssertThrows<ArgumentException>(() => new DesktopPetInputRegion(
        ImmutableArray<DesktopPetBezierPath>.Empty,
        ImmutableArray<DesktopPetStrokedBezierPath>.Empty,
        ImmutableArray<DesktopPetRegionEllipse>.Empty));
}

static void InputRegionTransformIsExact()
{
    var transform = new Win32InputRegionTransform(
        1.25d,
        new Win32WindowInsets(2, 3, 4, 5));
    AssertEqual(new Win32RegionPoint(1, 6), transform.Point(new Point(-1.2, 2)),
        "DIP point with outer-window inset");
    AssertEqual(
        new Win32RegionBounds(13, -2, 17, 3),
        transform.EllipseBounds(new DesktopPetRegionEllipse(
            new Point(10.2, -2),
            1.2,
            2)),
        "ellipse floor/ceiling coverage");
    AssertEqual(2, transform.StrokeWidth(1.01), "stroke ceiling");
    AssertEqual(1, Win32InputRegionTransform.AntialiasDilationPixels,
        "one physical pixel of antialias coverage");

    foreach (var scale in new[] { 1d, 1.25d, 1.5d, 2d })
    {
        var scaled = new Win32InputRegionTransform(
            scale,
            new Win32WindowInsets(7, 11, 0, 0));
        var origin = scaled.Point(new Point(0, 0));
        AssertEqual(new Win32RegionPoint(7, 11), origin,
            $"{scale:P0} client origin");
        AssertEqual((int)Math.Ceiling(3.2d * scale), scaled.StrokeWidth(3.2),
            $"{scale:P0} stroke width");
    }
}

static void InputRegionRefreshIsBounded()
{
    AssertTrue(DesktopPetInputRegionRefreshPolicy.ShouldRefresh(
            false, false, false, 1, 0, 1d, 1d, 0, 0, 30d, 1000),
        "the first immutable pose must install immediately");
    AssertTrue(DesktopPetInputRegionRefreshPolicy.ShouldRefresh(
            false, true, true, 2, 1, 1d, 1d, 33, 0, 30d, 1000),
        "every newly presented pose must immediately update the clipping region");
    AssertTrue(!DesktopPetInputRegionRefreshPolicy.ShouldRefresh(
            false, true, false, 3, 2, 1d, 1d, 33, 0, 30d, 1000),
        "a new pose must not bypass throttling after native installation failed");
    AssertTrue(DesktopPetInputRegionRefreshPolicy.ShouldRefresh(
            false, true, false, 3, 2, 1d, 1d, 34, 0, 30d, 1000),
        "a failed new pose must retry once the configured interval elapses");
    AssertTrue(!DesktopPetInputRegionRefreshPolicy.ShouldRefresh(
            false, true, false, 2, 2, 1d, 1d, 67, 34, 30d, 1000),
        "failed same-pose retries remain bounded by the configured rate");
    AssertTrue(DesktopPetInputRegionRefreshPolicy.ShouldRefresh(
            false, true, false, 2, 2, 1d, 1d, 68, 34, 30d, 1000),
        "a failed pose must retry even when its version is unchanged");
    AssertTrue(!DesktopPetInputRegionRefreshPolicy.ShouldRefresh(
            false, true, true, 2, 2, 1d, 1d, 100, 34, 30d, 1000),
        "a successfully installed unchanged pose must not rebuild");
    AssertTrue(DesktopPetInputRegionRefreshPolicy.ShouldRefresh(
            false, true, true, 2, 2, 1.5d, 1d, 35, 34, 30d, 1000),
        "a mixed-DPI transition must bypass failed-retry throttling");
    AssertTrue(!DesktopPetInputRegionRefreshPolicy.ShouldRefresh(
            false, true, false, 3, 2, 1d, 1d, 1, 0, double.Epsilon, long.MaxValue),
        "an extremely small positive rate must saturate without throwing or retrying immediately");
}

static void RenderFenceStateIsRaceSafe()
{
    var coalescer = new DesktopPetRenderFenceCoalescer();
    var versionOne = new DesktopPetRenderedPoseToken(1, 1d);
    var versionTwo = new DesktopPetRenderedPoseToken(2, 1.5d);
    AssertTrue(coalescer.ObserveRender(versionOne),
        "the first completed Render must enqueue a Loaded-priority fence job");
    AssertTrue(!coalescer.ObserveRender(versionTwo),
        "a second Render before Loaded must reuse the queued job");
    AssertEqual(versionTwo, coalescer.TakeLatestForFence(),
        "the queued job must fence the last Render, not its stale enqueue-time token");
    AssertThrows<InvalidOperationException>(() => coalescer.TakeLatestForFence());

    var attempts = new DesktopPetRenderFenceAttemptTracker();
    AssertTrue(attempts.TryBegin(versionTwo, out var firstSequence),
        "the coalesced render must start a composition marker");
    AssertTrue(!attempts.TryBegin(versionTwo, out _),
        "an in-flight token must not create duplicate marker batches");
    AssertEqual(1, attempts.InFlightCount,
        "the compositor fence tracker must expose a hard one-task upper bound");

    DesktopPetRenderedPoseToken latestDuringStall = default;
    for (var version = 3L; version <= 1002L; version++)
    {
        latestDuringStall = new DesktopPetRenderedPoseToken(version, 2d);
        AssertTrue(!attempts.TryBegin(latestDuringStall, out _),
            "renders observed during a compositor stall must be deferred, not started");
    }
    AssertTrue(attempts.InFlightCount == 1 && attempts.HasDeferredToken,
        "a thousand stalled drag renders must retain one task plus one latest token only");
    AssertTrue(attempts.TryFinish(
                   versionTwo,
                   firstSequence,
                   out var deferredLatest) &&
               deferredLatest == latestDuringStall,
        "finishing the sole marker must return only the newest coalesced render");
    AssertEqual(0, attempts.InFlightCount,
        "finishing a marker must release capacity before the caller starts the latest token");

    AssertTrue(attempts.TryBegin(latestDuringStall, out var latestSequence) &&
               latestSequence > firstSequence,
        "the deferred latest render must start under a newer monotonic sequence");
    AssertTrue(!attempts.TryFinish(versionTwo, firstSequence, out _),
        "a late older completion must not reset the newer in-flight token");
    AssertTrue(attempts.TryFinish(latestDuringStall, latestSequence, out var none) &&
               none is null,
        "the latest marker must finish without manufacturing more work");
    AssertTrue(attempts.TryBegin(latestDuringStall, out var retrySequence) &&
               retrySequence > latestSequence,
        "a failed marker can retry the same version under a newer sequence after release");

    var dpiAttempts = new DesktopPetRenderFenceAttemptTracker();
    var scaleA = new DesktopPetRenderedPoseToken(50, 1d);
    var scaleB = new DesktopPetRenderedPoseToken(50, 2d);
    AssertTrue(dpiAttempts.TryBegin(scaleA, out var dpiSequence),
        "the first raster scale must start the sole marker");
    AssertTrue(!dpiAttempts.TryBegin(scaleB, out _) &&
               dpiAttempts.HasDeferredToken,
        "a same-version scale change must replace the deferred latest raster state");
    AssertTrue(!dpiAttempts.TryBegin(scaleA, out _) &&
               !dpiAttempts.HasDeferredToken,
        "an A-to-B-to-A DPI round-trip must clear stale deferred B");
    AssertTrue(dpiAttempts.TryFinish(scaleA, dpiSequence, out var staleScale) &&
               staleScale is null,
        "finishing A after the DPI round-trip must not fence obsolete scale B");
}

static void SurfacePlacementTransactionIsVersioned()
{
    static DesktopPetSurfacePresentation Pose(float centerX, int deviceX) => new(
        new System.Numerics.Vector2(centerX, 200f),
        new SurfacePlacement(deviceX, 100, 320, 180),
        2d);

    var transaction = new DesktopPetSurfacePlacementTransaction(
        maximumPendingPresentations: 3);
    var initial = Pose(100f, -60);
    transaction.Initialize(9, initial);
    AssertEqual(initial, transaction.Committed,
        "the pre-Show position must be immediately available without a render ack");

    // A UI tick can merge several fixed simulation steps into one presenter
    // pose.  Only that final center is staged under the resulting version.
    var mergedFixedSteps = Pose(130f, 0);
    transaction.Stage(10, mergedFixedSteps);
    AssertEqual(initial, transaction.Committed,
        "successful Present must not move native pixels before its compositor ack");
    AssertThrows<NativeOverlaySafetyException>(() =>
        transaction.TryAcknowledge(
            1,
            10,
            _ => throw new NativeOverlaySafetyException("synthetic native failure")));
    AssertEqual(initial, transaction.Committed,
        "a failed native placement must not claim the pending pose as visible");
    AssertEqual(1, transaction.PendingPresentationCount,
        "a failed native placement must retain the exact version for retry");
    var firstCommit = default(DesktopPetSurfacePresentation);
    AssertTrue(transaction.TryAcknowledge(
                   2,
                   10,
                   presentation => firstCommit = presentation) &&
               firstCommit == mergedFixedSteps,
        "the version ack must recover the exact final center of the merged fixed steps");
    AssertEqual(mergedFixedSteps, transaction.Committed,
        "the acknowledged presentation must become the visible baseline");

    var intermediate = Pose(145f, 30);
    var latest = Pose(170f, 80);
    transaction.Stage(11, intermediate);
    transaction.Stage(12, latest);
    var coalesced = default(DesktopPetSurfacePresentation);
    AssertTrue(transaction.TryAcknowledge(
                   4,
                   12,
                   presentation => coalesced = presentation) &&
               coalesced == latest,
        "a fence for the newest retained pose must skip coalesced intermediate versions");
    AssertEqual(0, transaction.PendingPresentationCount,
        "acknowledging a newer pose must retire all older pending placements");
    AssertTrue(!transaction.TryAcknowledge(3, 11, _ => { }),
        "an out-of-order older sequence must not regress the native placement");
    AssertEqual(latest, transaction.Committed,
        "out-of-order acknowledgement must preserve the latest visible center");

    transaction.Stage(13, Pose(180f, 100));
    transaction.Stage(14, Pose(190f, 120));
    transaction.Stage(15, Pose(200f, 140));
    AssertTrue(!transaction.CanSubmitPresentation,
        "the pending placement queue must apply deterministic backpressure");
    AssertThrows<InvalidOperationException>(() =>
        transaction.Stage(16, Pose(210f, 160)));

    var rebased = Pose(500f, 900);
    transaction.ResetTo(rebased);
    AssertEqual(0, transaction.PendingPresentationCount,
        "a topology or direct-manipulation reset must isolate its old device generation");
    AssertTrue(!transaction.TryAcknowledge(5, 15, _ => { }),
        "a late ack from the discarded generation must not replay an old device placement");
    AssertEqual(rebased, transaction.Committed,
        "discarded-generation acknowledgements must leave the immediate rebase intact");

    transaction.Stage(16, Pose(510f, 920));
    AssertTrue(transaction.TryAcknowledge(6, 16, _ => { }),
        "the next version after a generation reset must remain committable");
    transaction.Close();
    AssertTrue(!transaction.CanSubmitPresentation &&
               !transaction.TryAcknowledge(7, 16, _ => { }),
        "close must clear pending work and turn late compositor completions into no-ops");
    AssertThrows<ObjectDisposedException>(() =>
        transaction.Stage(17, Pose(520f, 940)));

    var previousDisplay = new MappedDisplay(
        "display",
        new DeviceRect(-1000d, -500d, 1000d, 500d),
        new DeviceRect(-1000d, -500d, 1000d, 500d),
        new WorldRectD(0d, 0d, 2000d, 1000d),
        new WorldRectD(0d, 0d, 2000d, 1000d),
        1d,
        true);
    var currentDisplay = new MappedDisplay(
        "display",
        new DeviceRect(-1000d, -500d, 1000d, 500d),
        new DeviceRect(-1000d, -500d, 1000d, 500d),
        new WorldRectD(0d, 0d, 1000d, 500d),
        new WorldRectD(0d, 0d, 1000d, 500d),
        2d,
        true);
    var committedCenter = new System.Numerics.Vector2(250f, 200f);
    var newerRuntimeCenter = new System.Numerics.Vector2(700f, 300f);
    var rebasedCommitted = DesktopPetSurfaceCoordinateMapper.RebaseCenter(
        previousDisplay,
        currentDisplay,
        committedCenter);
    var rebasedRuntime = DesktopPetSurfaceCoordinateMapper.RebaseCenter(
        previousDisplay,
        currentDisplay,
        newerRuntimeCenter);
    AssertEqual(new System.Numerics.Vector2(125f, 100f), rebasedCommitted,
        "a 1x-to-2x topology refresh must preserve the committed center's device point");
    AssertTrue(rebasedCommitted != rebasedRuntime,
        "topology refresh must not replace a trailing visible center with newer simulation state");

    var negativePlacement125 = new DesktopPetSurfacePresentation(
        new System.Numerics.Vector2(-300f, -100f),
        new SurfacePlacement(-501, -203, 320, 180),
        1.25d);
    var local125 = DesktopPetSurfaceCoordinateMapper.DeviceToView(
        negativePlacement125,
        new DevicePoint(-376d, -78d));
    AssertNear(100d, local125.X, 0d,
        "negative 1.25x device X must map from the rounded committed top-left");
    AssertNear(100d, local125.Y, 0d,
        "negative 1.25x device Y must map from the rounded committed top-left");
    var negativePlacement150 = new DesktopPetSurfacePresentation(
        new System.Numerics.Vector2(-300f, -100f),
        new SurfacePlacement(-751, -305, 480, 270),
        1.5d);
    var local150 = DesktopPetSurfaceCoordinateMapper.DeviceToView(
        negativePlacement150,
        new DevicePoint(-601d, -155d));
    AssertNear(100d, local150.X, 0d,
        "negative 1.5x device X must map exactly to committed view coordinates");
    AssertNear(100d, local150.Y, 0d,
        "negative 1.5x device Y must map exactly to committed view coordinates");

    var lifecycle = new DesktopPetSurfacePlacementTransaction();
    lifecycle.Initialize(0, initial);
    lifecycle.Close();
    AssertTrue(!lifecycle.TryAcknowledge(99, 0, _ =>
            throw new InvalidOperationException("late close callback must not run")),
        "the initial v0 acknowledgement must become a no-op after Close");
}

static void RenderRegionTransactionIsConservative()
{
    var initial = CreateEllipseRegion(10);
    var next = CreateEllipseRegion(30);
    var transaction = new DesktopPetInputRegionTransaction(maximumPendingPoses: 2);

    var firstInstallation = transaction.Stage(0, initial, 1d, 1d);
    AssertEqual(1, firstInstallation.Poses.Length,
        "the first pre-Show pose must install immediately");
    AssertTrue(!firstInstallation.Poses[0].IsAcknowledged &&
               ReferenceEquals(initial, firstInstallation.Poses[0].Region),
        "the first pose remains pending until the compositor rasterizes it");

    var secondInstallation = transaction.Stage(1, next, 1d, 1.25d);
    AssertEqual(2, secondInstallation.Poses.Length,
        "every unacknowledged pose that could reach backing pixels must be retained");
    AssertTrue(!transaction.CanSubmitPose,
        "the configured pending-pose bound must apply deterministic backpressure");
    AssertThrows<InvalidOperationException>(() =>
        transaction.Stage(2, CreateEllipseRegion(50), 1.25d, 1.25d));

    AssertTrue(transaction.TryAcknowledge(1, 1, 1d, out var committed),
        "acknowledging the latest rendered pose must retire older pending poses");
    AssertEqual(0, transaction.PendingPoseCount,
        "all poses at or before the acknowledged version must be retired");
    AssertTrue(transaction.CanSubmitPose &&
               committed.Poses.Length == 1 &&
               committed.Poses[0].IsAcknowledged &&
               ReferenceEquals(next, committed.Poses[0].Region),
        "the exact acknowledged pose must become the sole committed baseline");
}

static void RenderAcknowledgementsAreMonotonic()
{
    var transaction = new DesktopPetInputRegionTransaction();
    var pose = CreateEllipseRegion(20);
    transaction.Stage(0, pose, 1d, 2d);

    AssertTrue(transaction.TryAcknowledge(2, 0, 2d, out _),
        "a newer same-pose DPI render acknowledgement must commit");
    AssertTrue(!transaction.TryAcknowledge(1, 0, 1d, out _),
        "an older completion delivered later must be ignored by sequence");
    AssertEqual(
        BitConverter.DoubleToInt64Bits(2d),
        BitConverter.DoubleToInt64Bits(
            transaction.Current.Poses.Single().RasterScales.Single()),
        "the committed raster scale must not regress from 2x to 1x");

    var newerPose = CreateEllipseRegion(40);
    transaction.Stage(1, newerPose, 2d, 2d);
    AssertTrue(transaction.TryAcknowledge(4, 1, 2d, out _),
        "the newer pose must commit under the newer fence sequence");
    AssertTrue(!transaction.TryAcknowledge(3, 0, 1d, out _),
        "a late older-pose completion must not replace the committed pose");
    AssertTrue(ReferenceEquals(
            newerPose,
            transaction.Current.Poses.Single().Region),
        "out-of-order completion must preserve the newest committed geometry");
}

static void MixedDpiRasterPlanIsCorrelated()
{
    var committedRegion = CreateEllipseRegion(10);
    var pendingRegion = CreateEllipseRegion(50);
    var transaction = new DesktopPetInputRegionTransaction();
    transaction.Stage(0, committedRegion, 1d, 1d);
    transaction.TryAcknowledge(1, 0, 1d, out _);
    var installation = transaction.Stage(1, pendingRegion, 1.25d, 1.5d);

    var plan = Win32InputRegionRasterPlan.Create(
        installation,
        targetDisplayScale: 2d,
        currentRenderScale: 2d);
    AssertRasterization(plan, 0, committedRegion, 1d, expected: true,
        "committed pose retains its acknowledged raster scale");
    AssertRasterization(plan, 0, committedRegion, 2d, expected: true,
        "committed pose also covers a live DPI resize before the latest Render");
    AssertRasterization(plan, 0, committedRegion, 1.25d, expected: false,
        "a pending pose's historical submission scale must not leak to the committed pose");
    AssertRasterization(plan, 1, pendingRegion, 1.25d, expected: true,
        "pending pose retains its own submission RenderScaling");
    AssertRasterization(plan, 1, pendingRegion, 1.5d, expected: true,
        "pending pose retains its own submission target scale");
    AssertRasterization(plan, 1, pendingRegion, 1d, expected: false,
        "committed historical scale must not leak to unrelated pending geometry");
    AssertRasterization(plan, 1, pendingRegion, 2d, expected: true,
        "pending pose covers the live target/current scale");

    var dpiTransaction = new DesktopPetInputRegionTransaction();
    dpiTransaction.Stage(0, committedRegion, 1d, 2d);
    dpiTransaction.TryAcknowledge(1, 0, 1d, out var oldScaleCommit);
    var unsettled = Win32InputRegionRasterPlan.Create(oldScaleCommit, 2d, 2d);
    AssertEqual(2, unsettled.Rasterizations.Length,
        "a 1x acknowledgement while the window is at 2x must not narrow early");
    dpiTransaction.TryAcknowledge(2, 0, 2d, out var newScaleCommit);
    var settled = Win32InputRegionRasterPlan.Create(newScaleCommit, 2d, 2d);
    AssertEqual(1, settled.Rasterizations.Length,
        "a newer 2x acknowledgement may narrow to the exact 2x silhouette");

    AssertThrows<ArgumentOutOfRangeException>(() =>
        Win32InputRegionRasterPlan.Create(
            newScaleCommit,
            targetDisplayScale: 2d,
            currentRenderScale: double.NaN));
}

static void FailedSynchronizationBackpressuresPresentation()
{
    var transaction = new DesktopPetInputRegionTransaction(maximumPendingPoses: 1);
    transaction.Stage(0, CreateEllipseRegion(10), 1d, 1d);
    AssertTrue(!DesktopPetPresentationSubmissionPolicy.CanSubmit(
            transaction,
            hasAttemptedInputRegion: true,
            lastInputRegionAttemptSucceeded: true),
        "a full pending queue must freeze presentation until an acknowledgement");

    transaction.TryAcknowledge(1, 0, 1d, out _);
    AssertTrue(!DesktopPetPresentationSubmissionPolicy.CanSubmit(
            transaction,
            hasAttemptedInputRegion: true,
            lastInputRegionAttemptSucceeded: false),
        "native capture/install failure must freeze the already tracked presenter version");
    AssertTrue(DesktopPetPresentationSubmissionPolicy.CanSubmit(
            transaction,
            hasAttemptedInputRegion: true,
            lastInputRegionAttemptSucceeded: true),
        "successful same-version retry may release presentation backpressure");

    var lateAck = new DesktopPetInputRegionTransaction();
    AssertTrue(!lateAck.TryAcknowledge(1, 7, 1.5d, out _),
        "an ack received after capture reset has no geometry to install yet");
    var recoveredLateAck = lateAck.Stage(
        7,
        CreateEllipseRegion(70),
        1.5d,
        1.5d);
    AssertTrue(recoveredLateAck.Poses.Single().IsAcknowledged &&
               lateAck.PendingPoseCount == 0,
        "same-version capture retry must consume an orphan ack without a duplicate fence");
    AssertTrue(DesktopPetPresentationSubmissionPolicy.CanSubmit(
            lateAck,
            hasAttemptedInputRegion: true,
            lastInputRegionAttemptSucceeded: true),
        "an orphan-ack recovery must release presentation backpressure");

    var resetAfterCommit = new DesktopPetInputRegionTransaction();
    resetAfterCommit.Stage(10, CreateEllipseRegion(90), 2d, 2d);
    resetAfterCommit.TryAcknowledge(10, 10, 2d, out _);
    resetAfterCommit.Reset();
    var recoveredCommitted = resetAfterCommit.Stage(
        10,
        CreateEllipseRegion(90),
        2d,
        2d);
    AssertTrue(recoveredCommitted.Poses.Single().IsAcknowledged &&
               resetAfterCommit.PendingPoseCount == 0,
        "Reset after an already-delivered ack must preserve that same-version fence fact");
}

static void LateRenderCommitAfterUnsubscribeIsHarmless()
{
    var notifier = new DesktopPetRenderCommitNotifier();
    var deliveries = 0;
    EventHandler<DesktopPetRenderCommitAcknowledgedEventArgs> handler =
        (_, _) => deliveries++;
    notifier.RenderCommitAcknowledged += handler;
    notifier.Publish(1, new DesktopPetRenderedPoseToken(0, 1d));
    AssertEqual(1, deliveries, "an active host receives the compositor acknowledgement");

    notifier.RenderCommitAcknowledged -= handler;
    notifier.Publish(2, new DesktopPetRenderedPoseToken(1, 2d));
    AssertEqual(1, deliveries,
        "an async completion after host disposal must not call the detached native host");
}

static void FailClosedPolicyIsExplicit()
{
    AssertTrue(Win32InputRegionPolicy.RequiresTransparentFallback(
            Win32InputRegionState.Unknown),
        "an uninitialized HWND must remain click-through");
    AssertTrue(Win32InputRegionPolicy.RequiresTransparentFallback(
            Win32InputRegionState.FailClosed),
        "an empty HRGN retains the redundant transparent fallback");
    AssertTrue(!Win32InputRegionPolicy.RequiresTransparentFallback(
            Win32InputRegionState.Installed),
        "an installed shaped region must not depend on WS_EX_TRANSPARENT");
}

static void MacOsClickThroughRequiresReadback()
{
    MacOsClickThroughPolicy.RequireObservedState(
        requested: true,
        observed: true,
        "test surface");
    MacOsClickThroughPolicy.RequireObservedState(
        requested: false,
        observed: false,
        "test surface");
    AssertThrows<NativeOverlaySafetyException>(() =>
        MacOsClickThroughPolicy.RequireObservedState(
            requested: true,
            observed: false,
            "unsafe fail-closed surface"));
    AssertThrows<NativeOverlaySafetyException>(() =>
        MacOsClickThroughPolicy.RequireObservedState(
            requested: false,
            observed: true,
            "non-interactive surface"));
}

static void MacOsActiveSpaceAcceptanceUsesWindowList()
{
    var bothWindows = new HashSet<nint> { new(41), new(42), new(99) };
    AssertTrue(MacOsActiveSpaceMembershipPolicy.ContainsRequiredWindows(
            new nint(41),
            new nint(42),
            bothWindows),
        "both native windows listed on the current Space must pass");
    AssertTrue(!MacOsActiveSpaceMembershipPolicy.ContainsRequiredWindows(
            new nint(41),
            new nint(43),
            bothWindows),
        "a missing anchor must fail current-Space membership");
    AssertTrue(!MacOsActiveSpaceMembershipPolicy.ContainsRequiredWindows(
            nint.Zero,
            new nint(42),
            bothWindows),
        "an invalid native surface number must fail current-Space membership");

    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    var source = File.ReadAllText(Path.Combine(
        root,
        "src",
        "InfiniteLizards.Desktop",
        "Platform",
        "MacOsOverlayWindowBackend.cs"));
    var acceptanceStart = source.IndexOf(
        "private void AdvanceNativeAcceptance()",
        StringComparison.Ordinal);
    var acceptanceEnd = source.IndexOf(
        "private static nint ReadWindowNumberAtPoint",
        acceptanceStart,
        StringComparison.Ordinal);
    var acceptanceSource = source[acceptanceStart..acceptanceEnd];
    AssertTrue(acceptanceSource.Contains(
            "ReadActiveSpaceWindowNumbers()",
            StringComparison.Ordinal) &&
        acceptanceSource.Contains(
            "MacOsActiveSpaceMembershipPolicy.ContainsRequiredWindows",
            StringComparison.Ordinal),
        "native acceptance must use NSWindow's current-Space membership list");
    AssertTrue(!acceptanceSource.Contains(
            "SendBoolResult(_avaloniaWindow, \"isOnActiveSpace\") &&",
            StringComparison.Ordinal),
        "the child-window getter is diagnostic only and must not gate acceptance");

    var membershipStart = source.IndexOf(
        "private static HashSet<nint> ReadActiveSpaceWindowNumbers()",
        StringComparison.Ordinal);
    var membershipEnd = source.IndexOf(
        "private static bool ReadEnvironmentSwitch",
        membershipStart,
        StringComparison.Ordinal);
    var membershipSource = source[membershipStart..membershipEnd];
    AssertTrue(membershipSource.Contains(
            "\"windowNumbersWithOptions:\"",
            StringComparison.Ordinal) &&
        membershipSource.Contains(
            "WindowNumberListAllApplications",
            StringComparison.Ordinal) &&
        !membershipSource.Contains(
            "WindowNumberListAllSpaces",
            StringComparison.Ordinal),
        "the public list query must include all applications but only the current Space");
}

static void MacOsHitRoutingRequiresNativeTransition()
{
    AssertTrue(MacOsWindowHitRoutingPolicy.MatchesClickThroughTransition(
            new nint(41),
            new nint(42),
            new nint(41),
            new nint(7)),
        "interactive surface hit followed by an underlying-window hit must pass");
    AssertTrue(!MacOsWindowHitRoutingPolicy.MatchesClickThroughTransition(
            new nint(41),
            new nint(42),
            new nint(7),
            new nint(8)),
        "interactive mode must route to the surface itself");
    AssertTrue(!MacOsWindowHitRoutingPolicy.MatchesClickThroughTransition(
            new nint(41),
            new nint(42),
            new nint(41),
            new nint(41)),
        "click-through mode must bypass the surface");
    AssertTrue(!MacOsWindowHitRoutingPolicy.MatchesClickThroughTransition(
            new nint(41),
            new nint(42),
            new nint(41),
            new nint(42)),
        "the transparent anchor must never become the click-through target");
    AssertTrue(MacOsNativeAcceptanceRetryPolicy.ShouldRetryInteractiveHit(
            interactiveStateObserved: true,
            surfaceWindowNumber: new nint(41),
            interactiveHitWindowNumber: new nint(7),
            attemptNumber: 1),
        "a stale first WindowServer route must be sampled again");
    AssertTrue(MacOsNativeAcceptanceRetryPolicy.ShouldRetryInteractiveHit(
            interactiveStateObserved: true,
            surfaceWindowNumber: new nint(41),
            interactiveHitWindowNumber: new nint(7),
            attemptNumber: MacOsNativeAcceptanceRetryPolicy.MaximumInteractiveHitAttempts - 1),
        "the final permitted warm-up retry must remain available");
    AssertTrue(!MacOsNativeAcceptanceRetryPolicy.ShouldRetryInteractiveHit(
            interactiveStateObserved: true,
            surfaceWindowNumber: new nint(41),
            interactiveHitWindowNumber: new nint(7),
            attemptNumber: MacOsNativeAcceptanceRetryPolicy.MaximumInteractiveHitAttempts),
        "an unchanged native route must stop retrying at the strict bound");
    AssertTrue(!MacOsNativeAcceptanceRetryPolicy.ShouldRetryInteractiveHit(
            interactiveStateObserved: true,
            surfaceWindowNumber: new nint(41),
            interactiveHitWindowNumber: new nint(41),
            attemptNumber: 1),
        "a correct interactive route must proceed immediately");
    AssertTrue(!MacOsNativeAcceptanceRetryPolicy.ShouldRetryInteractiveHit(
            interactiveStateObserved: false,
            surfaceWindowNumber: new nint(41),
            interactiveHitWindowNumber: new nint(7),
            attemptNumber: 1),
        "a failed ignoresMouseEvents readback must remain a final failure");

    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    var source = File.ReadAllText(Path.Combine(
        root,
        "src",
        "InfiniteLizards.Desktop",
        "Platform",
        "MacOsOverlayWindowBackend.cs"));
    var beginStart = source.IndexOf(
        "private void AdvanceNativeAcceptance()",
        StringComparison.Ordinal);
    var observeStart = source.IndexOf(
        "private void ObserveInteractiveHitRouting(",
        beginStart,
        StringComparison.Ordinal);
    var completeStart = source.IndexOf(
        "private void CompleteNativeAcceptance(",
        observeStart,
        StringComparison.Ordinal);
    var scheduleStart = source.IndexOf(
        "private void ScheduleNativeAcceptanceProbe(",
        completeStart,
        StringComparison.Ordinal);
    var beginSource = source[beginStart..observeStart];
    var observeSource = source[observeStart..completeStart];
    var completeSource = source[completeStart..scheduleStart];
    AssertTrue(beginSource.Contains("SetClickThrough(false);", StringComparison.Ordinal) &&
               beginSource.Contains("ScheduleNativeAcceptanceProbe", StringComparison.Ordinal) &&
               !beginSource.Contains("ReadWindowNumberAtPoint", StringComparison.Ordinal),
        "interactive hit routing must be sampled only after a later run-loop turn");
    AssertTrue(observeSource.Contains("ReadWindowNumberAtPoint", StringComparison.Ordinal) &&
               observeSource.Contains("SetClickThrough(true);", StringComparison.Ordinal) &&
               observeSource.Contains("ScheduleNativeAcceptanceProbe", StringComparison.Ordinal),
        "the interactive phase must sample before scheduling the click-through phase");
    AssertTrue(completeSource.Contains(
            "var clickThroughHitWindowNumber = ReadWindowNumberAtPoint(hitTestPoint);",
            StringComparison.Ordinal),
        "the click-through route must be sampled on its own later run-loop turn");
    AssertTrue(source.Contains(
            "TimeSpan.FromMilliseconds(100d)",
            StringComparison.Ordinal),
        "each WindowServer routing transition must be given more than two 120 Hz frames");
    AssertTrue(observeSource.Contains(
            "checked(attemptNumber + 1)",
            StringComparison.Ordinal) &&
        completeSource.Contains(
            "hitRoutingMatches &&",
            StringComparison.Ordinal),
        "cold-start retry must be bounded without relaxing the final native route gate");
}

static void MacOsFocusAcceptanceUsesFrontmostApplication()
{
    AssertTrue(MacOsFrontmostApplicationPolicy.IsOwnedByAnotherProcess(
            new nint(123),
            new nint(456)),
        "another valid frontmost process must prove the pet did not take focus");
    AssertTrue(!MacOsFrontmostApplicationPolicy.IsOwnedByAnotherProcess(
            new nint(123),
            new nint(123)),
        "the pet process itself must fail the non-activation gate");
    AssertTrue(!MacOsFrontmostApplicationPolicy.IsOwnedByAnotherProcess(
            new nint(123),
            nint.Zero),
        "an unavailable frontmost application must fail closed");
    AssertTrue(!MacOsFrontmostApplicationPolicy.IsOwnedByAnotherProcess(
            nint.Zero,
            new nint(456)),
        "an invalid current process identifier must fail closed");
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    var source = File.ReadAllText(Path.Combine(
        root,
        "src",
        "InfiniteLizards.Desktop",
        "Platform",
        "MacOsOverlayWindowBackend.cs"));
    AssertTrue(source.Contains(
            "ReadFrontmostApplicationProcessIdentifier()",
            StringComparison.Ordinal) &&
        source.Contains(
            "\"frontmostApplication\"",
            StringComparison.Ordinal) &&
        source.Contains(
            "\"processIdentifier\"",
            StringComparison.Ordinal) &&
        source.Contains(
            "frontmostApplicationIsOther &&",
            StringComparison.Ordinal),
        "acceptance must use NSWorkspace's public frontmost process identity");
    AssertTrue(!source.Contains(
            "!ReadApplicationActive() &&",
            StringComparison.Ordinal) &&
        source.Contains(
            "var applicationActiveDiagnostic = ReadApplicationActive();",
            StringComparison.Ordinal),
        "NSApplication.isActive must be diagnostic only for an LSUIElement app");
    var programSource = File.ReadAllText(Path.Combine(
        root,
        "src",
        "InfiniteLizards.Desktop",
        "Program.cs"));
    AssertTrue(programSource.Contains(
            "ShowInDock = false",
            StringComparison.Ordinal) &&
        programSource.Contains(
            "DisableAvaloniaAppDelegate = true",
            StringComparison.Ordinal),
        "the LSUIElement host must suppress Avalonia's unconditional launch activation delegate");
}

static void VisibleGeometryIsConservativelySized()
{
    AssertEqual(100d, DesktopPetVisibleInputSizing.MainLimbThickness(1, 0.1, 100),
        "a wide visible limb cannot be clipped by a small hit configuration");
    AssertEqual(80d, DesktopPetVisibleInputSizing.MainFootRadius(1, 0.1, 80),
        "a wide visible foot cannot be clipped by a small hit configuration");
    AssertEqual(70d, DesktopPetVisibleInputSizing.MainEyeRadius(1, 0.1, 3, 50, 20, 1),
        "an offset pupil must remain inside the shaped input window");
}

static void LizardShadowGeometryIsShared()
{
    Avalonia.Skia.SkiaPlatform.Initialize();
    var gameplay = Assembly.Load("InfiniteLizards.Gameplay");
    var profileType = gameplay.GetType("DesktopLizard.Core.LizardProfile", throwOnError: true)!;
    var profile = profileType.GetProperty(
            "Default",
            BindingFlags.Public | BindingFlags.Static)!
        .GetValue(null)!;
    var gameType = gameplay.GetType(
        "InfiniteLizards.Gameplay.LizardGameModule",
        throwOnError: true)!;
    var game = Activator.CreateInstance(
        gameType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        args: [profile, 1234],
        culture: null)!;
    var frame = gameType.GetMethod(
            "CaptureSnapshot",
            BindingFlags.Instance | BindingFlags.Public)!
        .Invoke(game, null)!;
    var constructor = typeof(LizardView).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Single();
    var view = (LizardView)constructor.Invoke([profile, frame]);
    var provider = (IDesktopPetInputRegionProvider)view;
    var inputRegion = provider.CaptureInputRegion();

    AssertEqual(2, inputRegion.Fills.Length,
        "body and shadow body must both be exported");
    AssertTrue(inputRegion.Strokes.Length >= 2,
        "main hit limbs and shadow limbs must both be exported");
    AssertTrue(inputRegion.Ellipses.Length >= 2,
        "feet, face and shadow circles must be exported");
    var representativeShadowPoint = inputRegion.Ellipses[^1].Center;
    AssertTrue(view.IsPointOnLizard(representativeShadowPoint),
        "the shared presenter hit test must include exported shadow pixels");
}

static void VersionedHitTestingTracksCommittedPose()
{
    var gameplay = Assembly.Load("InfiniteLizards.Gameplay");
    var profileType = gameplay.GetType("DesktopLizard.Core.LizardProfile", throwOnError: true)!;
    var profile = profileType.GetProperty(
            "Default",
            BindingFlags.Public | BindingFlags.Static)!
        .GetValue(null)!;
    var gameType = gameplay.GetType(
        "InfiniteLizards.Gameplay.LizardGameModule",
        throwOnError: true)!;
    var game = Activator.CreateInstance(
        gameType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        args: [profile, 2468],
        culture: null)!;
    var initialFrame = gameType.GetMethod(
            "CaptureSnapshot",
            BindingFlags.Instance | BindingFlags.Public)!
        .Invoke(game, null)!;
    var constructor = typeof(LizardView).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Single();
    var view = (LizardView)constructor.Invoke([profile, initialFrame]);
    var hitTester = (IDesktopPetPresentationHitTester)view;
    var delta = new System.Numerics.Vector2(1000f, 700f);
    var translatedFrame = TranslateRenderFrame(initialFrame, delta);
    var present = typeof(LizardView).GetMethod(
        "Present",
        BindingFlags.Instance | BindingFlags.Public)!;

    present.Invoke(view, [translatedFrame, System.Numerics.Vector2.UnitX]);
    present.Invoke(view, [initialFrame, System.Numerics.Vector2.UnitX]);
    var translatedNose =
        (System.Numerics.Vector2)initialFrame.GetType().GetProperty("HeadNose")!
            .GetValue(initialFrame)! + delta;
    var translatedNoseInView = new Point(
        translatedNose.X * view.ModelToWorldScale,
        translatedNose.Y * view.ModelToWorldScale);

    hitTester.CommitPresentation(1);
    AssertTrue(hitTester.HitTestPresentation(1, translatedNoseInView),
        "the committed v1 geometry must remain hittable after a newer v2 Present");
    AssertTrue(!hitTester.HitTestPresentation(2, translatedNoseInView),
        "the same point must not be tested against the newer uncommitted v2 geometry");
    hitTester.DiscardUncommittedPresentations(1);
    AssertThrows<InvalidOperationException>(() =>
        hitTester.HitTestPresentation(2, translatedNoseInView));

    static object TranslateRenderFrame(
        object source,
        System.Numerics.Vector2 offset)
    {
        var frameType = source.GetType();
        var body = ((System.Collections.IEnumerable)frameType
                .GetProperty("BodyOutline")!
                .GetValue(source)!)
            .Cast<System.Numerics.Vector2>()
            .Select(point => point + offset)
            .ToImmutableArray();
        var sourceLegs = ((System.Collections.IEnumerable)frameType
                .GetProperty("Legs")!
                .GetValue(source)!)
            .Cast<object>()
            .ToArray();
        var legType = frameType.GetProperty("Legs")!
            .PropertyType
            .GenericTypeArguments
            .Single();
        var translatedLegArray = Array.CreateInstance(legType, sourceLegs.Length);
        for (var index = 0; index < sourceLegs.Length; index++)
        {
            var leg = sourceLegs[index];
            translatedLegArray.SetValue(Activator.CreateInstance(
                legType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args:
                [
                    (System.Numerics.Vector2)legType.GetProperty("Shoulder")!.GetValue(leg)! + offset,
                    (System.Numerics.Vector2)legType.GetProperty("Elbow")!.GetValue(leg)! + offset,
                    (System.Numerics.Vector2)legType.GetProperty("Foot")!.GetValue(leg)! + offset,
                    legType.GetProperty("IsFront")!.GetValue(leg)!,
                    legType.GetProperty("Lift")!.GetValue(leg)!
                ],
                culture: null),
                index);
        }
        var immutableLegFactory = typeof(ImmutableArray)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
                method.Name == "Create" &&
                method.IsGenericMethodDefinition &&
                method.GetParameters() is [{ ParameterType.IsArray: true }]);
        var translatedLegs = immutableLegFactory
            .MakeGenericMethod(legType)
            .Invoke(null, [translatedLegArray])!;
        var frameConstructor = frameType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 7);
        return frameConstructor.Invoke(
        [
            body,
            translatedLegs,
            (System.Numerics.Vector2)frameType.GetProperty("HeadNose")!.GetValue(source)! + offset,
            (System.Numerics.Vector2)frameType.GetProperty("NegativeEyeCenter")!.GetValue(source)! + offset,
            (System.Numerics.Vector2)frameType.GetProperty("PositiveEyeCenter")!.GetValue(source)! + offset,
            frameType.GetProperty("Heading")!.GetValue(source)!,
            frameType.GetProperty("BlinkAmount")!.GetValue(source)!
        ]);
    }
}

static void DegenerateInvisibleEllipsesAreSkipped()
{
    var gameplay = Assembly.Load("InfiniteLizards.Gameplay");
    var profileType = gameplay.GetType("DesktopLizard.Core.LizardProfile", throwOnError: true)!;
    var profile = profileType.GetProperty(
            "Default",
            BindingFlags.Public | BindingFlags.Static)!
        .GetValue(null)!;
    var rendering = profileType.GetProperty("Rendering")!.GetValue(profile)!;
    var degenerateRendering = CloneWithProperties(
        rendering,
        ("LiftFootContraction", 1f),
        ("MinimumBlinkScale", 0f),
        ("BlinkClosure", 1f));
    var degenerateProfile = CloneWithProperties(
        profile,
        ("Rendering", degenerateRendering));

    var gameType = gameplay.GetType(
        "InfiniteLizards.Gameplay.LizardGameModule",
        throwOnError: true)!;
    var game = Activator.CreateInstance(
        gameType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        args: [profile, 9876],
        culture: null)!;
    var sourceFrame = gameType.GetMethod(
            "CaptureSnapshot",
            BindingFlags.Instance | BindingFlags.Public)!
        .Invoke(game, null)!;
    var frameType = sourceFrame.GetType();
    var legType = frameType.GetProperty("Legs")!
        .PropertyType
        .GenericTypeArguments
        .Single();
    var fullyContractedLeg = Activator.CreateInstance(
        legType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        args:
        [
            new System.Numerics.Vector2(2000f, 2000f),
            new System.Numerics.Vector2(2010f, 2000f),
            new System.Numerics.Vector2(2020f, 2000f),
            true,
            1f
        ],
        culture: null)!;
    var legArray = Array.CreateInstance(legType, 1);
    legArray.SetValue(fullyContractedLeg, 0);
    var immutableLegFactory = typeof(ImmutableArray)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method =>
            method.Name == "Create" &&
            method.IsGenericMethodDefinition &&
            method.GetParameters() is [{ ParameterType.IsArray: true }]);
    var legs = immutableLegFactory
        .MakeGenericMethod(legType)
        .Invoke(null, [legArray])!;
    var frameConstructor = frameType.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Single(constructor => constructor.GetParameters().Length == 7);
    var degenerateFrame = frameConstructor.Invoke(
    [
        frameType.GetProperty("BodyOutline")!.GetValue(sourceFrame)!,
        legs,
        frameType.GetProperty("HeadNose")!.GetValue(sourceFrame)!,
        frameType.GetProperty("NegativeEyeCenter")!.GetValue(sourceFrame)!,
        frameType.GetProperty("PositiveEyeCenter")!.GetValue(sourceFrame)!,
        frameType.GetProperty("Heading")!.GetValue(sourceFrame)!,
        1f
    ]);

    var viewConstructor = typeof(LizardView).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Single();
    var view = (LizardView)viewConstructor.Invoke([degenerateProfile, degenerateFrame]);
    var inputRegion = ((IDesktopPetInputRegionProvider)view).CaptureInputRegion();

    AssertEqual(5, inputRegion.Ellipses.Length,
        "zero-area foot shadows and fully closed eye shadows must be omitted");
    AssertTrue(inputRegion.Ellipses.All(ellipse =>
            ellipse.RadiusX > 0d && ellipse.RadiusY > 0d),
        "the native input contract must receive only visible ellipses");
    AssertTrue(!view.IsPointOnLizard(new Point(100_000, 100_000)),
        "a fully closed eye shadow must not create a degenerate hit target");
}

static void MinimumPupilRadiusIsCovered()
{
    AssertEqual(
        150d,
        DesktopPetVisibleInputSizing.MainEyeRadius(1, 0.1, 3, 50, 20, 100),
        "the larger minimum pupil radius must bound an offset pupil");

    var gameplay = Assembly.Load("InfiniteLizards.Gameplay");
    var profileType = gameplay.GetType("DesktopLizard.Core.LizardProfile", throwOnError: true)!;
    var profile = profileType.GetProperty(
            "Default",
            BindingFlags.Public | BindingFlags.Static)!
        .GetValue(null)!;
    var rendering = profileType.GetProperty("Rendering")!.GetValue(profile)!;
    var appearance = profileType.GetProperty("Appearance")!.GetValue(profile)!;
    var gait = profileType.GetProperty("Gait")!.GetValue(profile)!;
    var secondaryMotion = profileType.GetProperty("SecondaryMotion")!.GetValue(profile)!;
    var expandedRendering = CloneWithProperties(
        rendering,
        ("MinimumPupilRadius", 100f));

    var envelopeType = gameplay.GetType(
        "DesktopLizard.Core.LizardGeometryEnvelope",
        throwOnError: true)!;
    var calculate = envelopeType.GetMethod(
        "Calculate",
        BindingFlags.Public | BindingFlags.Static)!;
    var baselineEnvelope = calculate.Invoke(
        null,
        [appearance, gait, secondaryMotion, rendering])!;
    var expandedEnvelope = calculate.Invoke(
        null,
        [appearance, gait, secondaryMotion, expandedRendering])!;
    var normalRadius = (float)envelopeType.GetProperty("NormalModelRadius")!
        .GetValue(expandedEnvelope)!;
    var baselineNormalRadius = (float)envelopeType.GetProperty("NormalModelRadius")!
        .GetValue(baselineEnvelope)!;
    var danglingRadius = (float)envelopeType.GetProperty("DanglingModelRadius")!
        .GetValue(expandedEnvelope)!;
    AssertTrue(normalRadius > baselineNormalRadius,
        "the geometry envelope must include an oversized minimum pupil radius");

    var expandedAppearance = envelopeType.GetMethod("EnsureCanvasCapacity")!
        .Invoke(expandedEnvelope, [appearance])!;
    var creatureCanvas = (float)expandedAppearance.GetType()
        .GetProperty("CreatureCanvasSize")!
        .GetValue(expandedAppearance)!;
    var renderCanvas = (float)expandedAppearance.GetType()
        .GetProperty("RenderCanvasSize")!
        .GetValue(expandedAppearance)!;
    AssertTrue(creatureCanvas * 0.5f >= normalRadius,
        "the normal-pose canvas must cover the expanded eye geometry");
    AssertTrue(renderCanvas * 0.5f >= danglingRadius,
        "the dangling-pose canvas must cover the expanded eye geometry");

    var source = profileType.GetProperty("Source")!.GetValue(profile)!;
    var invalidDefaultCanvas = CloneWithProperties(
        source,
        ("Rendering", expandedRendering));
    var validationFailures = (System.Collections.IEnumerable)invalidDefaultCanvas
        .GetType()
        .GetMethod("Validate")!
        .Invoke(invalidDefaultCanvas, null)!;
    AssertTrue(validationFailures.Cast<object>().Any(),
        "configuration validation must reject a pupil that outgrows the configured canvas");
}

static void UsesNativeShapedInputContract()
{
    AssertTrue(typeof(IOverlayInputRegionBackend).IsAssignableFrom(
            typeof(Win32OverlayWindowBackend)),
        "the Win32 backend must expose the optional shaped-region capability");
    AssertTrue(!typeof(IOverlayInputRegionBackend).IsAssignableFrom(
            typeof(MacOsOverlayWindowBackend)),
        "the optional Win32 contract must not couple the macOS backend");

    var methods = typeof(Win32OverlayWindowBackend).GetMethods(
        BindingFlags.Static | BindingFlags.Instance |
        BindingFlags.Public | BindingFlags.NonPublic);
    var setWindowRegion = methods.Single(method => method.Name == "SetWindowRgn");
    AssertTrue(setWindowRegion.GetCustomAttribute<DllImportAttribute>() is not null,
        "SetWindowRgn must be the USER32 ownership boundary");
    AssertTrue(methods.All(method =>
            method.Name is not "SendMessage" and not "PostMessage"),
        "manual cross-window message forwarding is forbidden");
    foreach (var nativeInputMethodName in new[] { "EnableWindow", "IsWindowEnabled" })
    {
        var nativeInputMethod = methods.Single(method =>
            method.Name == nativeInputMethodName);
        AssertTrue(nativeInputMethod.GetCustomAttribute<DllImportAttribute>() is not null,
            $"{nativeInputMethodName} must remain an explicit USER32 boundary");
    }
    AssertTrue(typeof(IOverlayInputRegionBackend).GetMethod("ClearInputRegion") is not null,
        "provider failures must be able to revoke an older native shape");
}

static void PetWindowSafetyBoundaries()
{
    var genericWindow = typeof(PetWindow<>);
    var prepare = genericWindow.GetMethod(
        "PrepareForShow",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    AssertTrue(prepare.GetMethodBody()!.ExceptionHandlingClauses.Any(clause =>
            clause.CatchType == typeof(NativeOverlaySafetyException)),
        "PrepareForShow must explicitly preserve the fatal native safety exception");

    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    var source = File.ReadAllText(Path.Combine(
        root,
        "src",
        "InfiniteLizards.Desktop",
        "PetWindow.cs"));
    var opacityIndex = source.IndexOf("_presenter.SetSpawnOpacity(0f);", StringComparison.Ordinal);
    var attachIndex = source.IndexOf("_platform.Attach(this, _diagnosticMode);", StringComparison.Ordinal);
    AssertTrue(opacityIndex >= 0 && opacityIndex < attachIndex,
        "spawn opacity must be zero before native Show preparation");
    var renderSourceSubscription = source.IndexOf(
        "if (presenter is IDesktopPetRenderCommitSource renderCommitSource)",
        StringComparison.Ordinal);
    var inputBackendSubscription = source.IndexOf(
        "if (_platform is IOverlayInputRegionBackend &&",
        StringComparison.Ordinal);
    AssertTrue(renderSourceSubscription >= 0 &&
               renderSourceSubscription < inputBackendSubscription,
        "render acknowledgements must be subscribed on macOS as well as the " +
        "Windows shaped-region backend");
    var prepareStart = source.IndexOf(
        "internal void PrepareForShow()",
        StringComparison.Ordinal);
    var prepareEnd = source.IndexOf(
        "private void OnOpened",
        prepareStart,
        StringComparison.Ordinal);
    var prepareSource = source[prepareStart..prepareEnd];
    AssertTrue(prepareSource.Contains(
            "ResetSurfacePlacement(_runtime.Position);",
            StringComparison.Ordinal),
        "the initial native position must be established synchronously before Show");
    var contextMenuStart = source.IndexOf(
        "private (ContextMenu Menu, MenuItem Pause) CreateContextMenu()",
        StringComparison.Ordinal);
    var contextMenuEnd = source.IndexOf(
        "private void OpenConfigurationFile()",
        contextMenuStart,
        StringComparison.Ordinal);
    var contextMenuSource = source[contextMenuStart..contextMenuEnd];
    AssertTrue(contextMenuSource.Contains(
            "menu.Opening +=",
            StringComparison.Ordinal) &&
        contextMenuSource.Contains(
            "SetClickThrough(false);",
            StringComparison.Ordinal) &&
        contextMenuSource.Contains(
            "menu.Closed +=",
            StringComparison.Ordinal) &&
        contextMenuSource.Contains(
            "SetClickThrough(true);",
            StringComparison.Ordinal),
        "the right-click menu must retain its bounded interactive window");
    AssertTrue(contextMenuSource.Contains(
            "desktop.Shutdown();",
            StringComparison.Ordinal),
        "the context-menu exit path must remain independent of the native AppDelegate");
    AssertTrue(source.Contains("else if (!hasPointer", StringComparison.Ordinal),
        "pointer sampling loss must immediately request click-through");
    AssertTrue(source.Contains("backend.ClearInputRegion(this);", StringComparison.Ordinal),
        "capture failures must revoke an older shaped region");
    var frameTimerStart = source.IndexOf(
        "private void OnFrameTimer",
        StringComparison.Ordinal);
    var frameTimerEnd = source.IndexOf(
        "private void RefreshTopology",
        frameTimerStart,
        StringComparison.Ordinal);
    var frameTimerSource = source[frameTimerStart..frameTimerEnd];
    var backpressureCheck = frameTimerSource.IndexOf(
        "DesktopPetPresentationSubmissionPolicy.CanSubmit",
        StringComparison.Ordinal);
    var presenterSubmit = frameTimerSource.IndexOf(
        "_presenter.Present(frame.Snapshot, frame.LookDirection);",
        StringComparison.Ordinal);
    AssertTrue(backpressureCheck >= 0 && presenterSubmit > backpressureCheck,
        "failed/full native region state must gate Present before its version can grow");
    var placementBackpressure = frameTimerSource.IndexOf(
        "_surfacePlacementTransaction?.CanSubmitPresentation",
        StringComparison.Ordinal);
    var stagePlacement = frameTimerSource.IndexOf(
        "placementTransaction.Stage(",
        StringComparison.Ordinal);
    AssertTrue(placementBackpressure >= 0 &&
               placementBackpressure < presenterSubmit &&
               stagePlacement > presenterSubmit,
        "native movement must bind only a successfully presented version and must " +
        "back-pressure before the bounded placement queue overflows");
    var placeSurfaceStart = source.IndexOf(
        "private void PlaceSurface(Vector2 center)",
        StringComparison.Ordinal);
    var placeSurfaceEnd = source.IndexOf(
        "private void UpdateClickThrough",
        placeSurfaceStart,
        StringComparison.Ordinal);
    var placeSurfaceSource = source[placeSurfaceStart..placeSurfaceEnd];
    var nativePlacement = placeSurfaceSource.IndexOf(
        "_platform.PlaceSurface(",
        StringComparison.Ordinal);
    var cachePlacement = placeSurfaceSource.IndexOf(
        "_lastPlacement = presentation.Placement;",
        StringComparison.Ordinal);
    AssertTrue(nativePlacement >= 0 && cachePlacement > nativePlacement,
        "the host must cache a placement only after the native backend accepts it");

    var acknowledgementStart = source.IndexOf(
        "private void OnRenderCommitAcknowledged",
        StringComparison.Ordinal);
    var acknowledgementEnd = source.IndexOf(
        "private void OnWindowPropertyChanged",
        acknowledgementStart,
        StringComparison.Ordinal);
    var acknowledgementSource = source[acknowledgementStart..acknowledgementEnd];
    var placementAck = acknowledgementSource.IndexOf(
        "placementTransaction.TryAcknowledge(",
        StringComparison.Ordinal);
    var acknowledgedPlacement = acknowledgementSource.IndexOf(
        "presentation => ApplyAcknowledgedSurface(",
        StringComparison.Ordinal);
    var regionAck = acknowledgementSource.IndexOf(
        "regionTransaction.TryAcknowledge(",
        StringComparison.Ordinal);
    AssertTrue(placementAck >= 0 &&
               acknowledgedPlacement > placementAck &&
               regionAck > acknowledgedPlacement &&
               source.Contains("PlaceSurface(presentation);", StringComparison.Ordinal) &&
               source.Contains("hitTester.CommitPresentation(presentationVersion);", StringComparison.Ordinal),
        "the UI-thread render ack must apply its version-bound placement before " +
        "independently narrowing the Windows input-region transaction");

    var pointerPressStart = source.IndexOf(
        "private void OnPointerPressed",
        StringComparison.Ordinal);
    var pointerPressEnd = source.IndexOf(
        "private void OnPointerMoved",
        pointerPressStart,
        StringComparison.Ordinal);
    var pointerPressSource = source[pointerPressStart..pointerPressEnd];
    AssertTrue(pointerPressSource.Contains(
            "PresentedSurfaceCenter(_runtime.Position)",
            StringComparison.Ordinal) &&
        pointerPressSource.Contains(
            "transaction.DiscardPending();",
            StringComparison.Ordinal),
        "direct manipulation must begin at the visible committed center and isolate " +
        "automatic-motion acknowledgements from the previous generation");

    var openedStart = source.IndexOf("private void OnOpened", StringComparison.Ordinal);
    var openedEnd = source.IndexOf("private void OnFrameTimer", openedStart, StringComparison.Ordinal);
    var openedSource = source[openedStart..openedEnd];
    var ensureVisible = openedSource.IndexOf("_platform.EnsureVisible();", StringComparison.Ordinal);
    var startTimer = openedSource.IndexOf("_frameTimer.Start();", StringComparison.Ordinal);
    AssertTrue(ensureVisible >= 0 && ensureVisible < startTimer,
        "native visibility safety must complete before the frame timer starts");
    AssertTrue(!openedSource.Contains("Dispatcher.UIThread.Post", StringComparison.Ordinal) &&
               !openedSource.Contains("DispatcherPriority.Background", StringComparison.Ordinal),
        "OnOpened must not defer native visibility safety to a background dispatch");

    var closedStart = source.IndexOf("private void OnClosed", StringComparison.Ordinal);
    var closedEnd = source.IndexOf(
        "private static string DisplayId",
        closedStart,
        StringComparison.Ordinal);
    var closedSource = source[closedStart..closedEnd];
    var markClosed = closedSource.IndexOf("_isClosed = true;", StringComparison.Ordinal);
    var unsubscribe = closedSource.IndexOf(
        "_renderCommitSource.RenderCommitAcknowledged -= OnRenderCommitAcknowledged;",
        StringComparison.Ordinal);
    var disposePlatform = closedSource.IndexOf("_platform.Dispose();", StringComparison.Ordinal);
    AssertTrue(markClosed >= 0 && unsubscribe > markClosed && disposePlatform > unsubscribe,
        "Close must reject and unsubscribe async render acks before native disposal");

    var scaleChangeStart = source.IndexOf(
        "private void OnWindowPropertyChanged",
        StringComparison.Ordinal);
    var scaleChangeEnd = source.IndexOf(
        "private void SynchronizeRenderScaleChange",
        scaleChangeStart,
        StringComparison.Ordinal);
    var scaleChangeSource = source[scaleChangeStart..scaleChangeEnd];
    AssertTrue(scaleChangeSource.Contains(
            "DispatcherPriority.Send",
            StringComparison.Ordinal) &&
        !scaleChangeSource.Contains(
            "SynchronizeInputRegion(force: true",
            StringComparison.Ordinal),
        "DPI changes must enqueue conservative region work ahead of Render without " +
        "reentering USER32 from the RenderScaling callback");

    var rendererSource = File.ReadAllText(Path.Combine(
        root,
        "src",
        "InfiniteLizards.Desktop",
        "Rendering",
        "LizardView.cs"));
    AssertTrue(rendererSource.Contains(
            "DispatcherPriority.Loaded",
            StringComparison.Ordinal) &&
        rendererSource.Contains(
            "batch.Rendered",
            StringComparison.Ordinal) &&
        !rendererSource.Contains(
            "SetWindowRgn",
            StringComparison.Ordinal),
        "Render must acknowledge the Avalonia composition batch without calling USER32");

    var win32Source = File.ReadAllText(Path.Combine(
        root,
        "src",
        "InfiniteLizards.Desktop",
        "Platform",
        "Win32OverlayWindowBackend.cs"));
    var refreshStart = win32Source.IndexOf(
        "private bool TryRefreshWindowHandle",
        StringComparison.Ordinal);
    var refreshEnd = win32Source.IndexOf(
        "private bool RequiresTransparentFallback",
        refreshStart,
        StringComparison.Ordinal);
    var refreshSource = win32Source[refreshStart..refreshEnd];
    var candidateRead = refreshSource.IndexOf(
        "var candidate = _windowOwner?.TryGetPlatformHandle()?.Handle",
        StringComparison.Ordinal);
    var candidateComparison = refreshSource.IndexOf(
        "if (candidate == _window)",
        StringComparison.Ordinal);
    var oldHandleFastPath = refreshSource.IndexOf(
        "if (_window != nint.Zero && IsWindow(_window))",
        StringComparison.Ordinal);
    AssertTrue(candidateRead >= 0 &&
               candidateComparison > candidateRead &&
               oldHandleFastPath > candidateComparison,
        "the current Avalonia HWND candidate must be compared before reusing an old live HWND");
    AssertTrue(win32Source.Contains(
            "Avalonia replaced the verified HWND",
            StringComparison.Ordinal),
        "an unexpected post-verification HWND replacement must fail-stop after securing the new handle");

    var macSource = File.ReadAllText(Path.Combine(
        root,
        "src",
        "InfiniteLizards.Desktop",
        "Platform",
        "MacOsOverlayWindowBackend.cs"));
    var acceptanceStart = macSource.IndexOf(
        "private void AdvanceNativeAcceptance",
        StringComparison.Ordinal);
    var acceptanceEnd = macSource.IndexOf(
        "private static bool ReadEnvironmentSwitch",
        acceptanceStart,
        StringComparison.Ordinal);
    var acceptanceSource = macSource[acceptanceStart..acceptanceEnd];
    AssertTrue(acceptanceSource.Contains(
            "PlaceSurface(owner, placement, _acceptanceDisplayScale);",
            StringComparison.Ordinal),
        "macOS acceptance must exercise the production SurfacePlacement path");
    AssertTrue(!acceptanceSource.Contains(
            "SendRectBool(_avaloniaWindow, \"setFrame:display:\"",
            StringComparison.Ordinal),
        "macOS acceptance must not bypass production placement with a direct NSWindow frame write");
}

static void DiagnosticPanelIsVisibleAndDecoupled()
{
    var configuredDebug = new DesktopPetDebugSettings(
        420,
        150,
        17,
        4d,
        30d);
    var telemetry = new DesktopPetDiagnosticTelemetry(configuredDebug);
    var snapshot = telemetry.Capture(
        1f / 60f,
        1f / 120f,
        2,
        new System.Numerics.Vector2(123.5f, -44.3f),
        "display:test",
        2d,
        pointerAvailable: true,
        pointerBlocked: false,
        pointerPosition: new System.Numerics.Vector2(150f, -40f),
        pointerDistance: 27f,
        isPaused: true,
        isDragging: false,
        wasPresented: true,
        presenterRegionVersion: 42,
        regionBackendAvailable: true,
        regionAttempted: true,
        regionSynchronized: true,
        submittedRegionVersion: 42,
        pendingRegionPoses: 1,
        acknowledgedRegionPoses: 1);
    AssertNear(30d, snapshot.FramesPerSecond, 0.001d,
        "the host FPS filter must honor the resolved profile maximum");
    var text = snapshot.FormatPanelText();
    foreach (var required in new[]
             {
                 "FPS", "Fixed step", "123.5, -44.3", "display:test",
                 "2.000×", "Paused", "Dragging", "Frame submission",
                 "Presenter region", "update accepted", "pending 1"
             })
    {
        AssertTrue(text.Contains(required, StringComparison.Ordinal),
            $"diagnostic panel must render '{required}'");
    }

    var actions = DebugPanelPresentation.DefaultActions
        .Select((action, index) => action with { Probability = index * 0.025f })
        .ToImmutableArray();
    var presentation = new DebugPanelPresentation(
        "状态  S形爬行\n切换  随机选择  #42\n计时  1.2s / 4.8s\n速度  19.5 → 24.0",
        "转向  0.12 → 0.20\n鼠标  可追踪  88px\nS弯   段2/4  周0.8s  半径≈120px\n步态  19.5  P1  帧率60",
        "S弯后",
        HasCurrentProbabilityRow: true,
        ActionsEnabled: true,
        actions);
    var view = new DebugPanelContentView();
    view.SetPresentation(presentation);
    view.ApplyDisplayScale(2d);
    AssertEqual(presentation.LeftText, view.LeftText,
        "Avalonia debug view must preserve the WPF left telemetry column");
    AssertEqual(presentation.RightText, view.RightText,
        "Avalonia debug view must preserve the WPF right telemetry column");
    AssertEqual("播放状态 · 下一动作概率：S弯后", view.HeaderText,
        "the current transition row must be shown above the action strip");
    AssertTrue(view.ActionsEnabled,
        "debug playback controls must follow the bridge enabled state");
    AssertEqual("前进 0%", view.ButtonLabelAt(0),
        "integral probabilities must match the WPF label format");
    AssertEqual("续爬 2.5%", view.ButtonLabelAt(1),
        "fractional probabilities must match the WPF label format");
    AssertNear(6d, view.PhysicalPanelMargin, 0.001d,
        "panel margin must remain six physical pixels at Retina scale");
    AssertNear(66d, view.PhysicalControlsHeight, 0.001d,
        "action strip must remain 66 physical pixels at Retina scale");
    AssertNear(13d, view.PhysicalTelemetryFontSize, 0.001d,
        "telemetry text must remain 13 physical pixels at Retina scale");
    AssertEqual(Avalonia.Media.Color.FromArgb(232, 12, 17, 19), DebugPanelContentView.PanelColor,
        "panel fill must match the WPF HUD");
    AssertEqual(Avalonia.Media.Color.FromRgb(61, 88, 72), DebugPanelContentView.BorderColor,
        "panel border must match the WPF HUD");

    var placement = DebugPanelPlacementCalculator.Calculate(
        new System.Numerics.Vector2(960f, 540f),
        new DebugPanelPlacementRect(0f, 0f, 1920f, 1080f),
        DiagnosticPanelWindow.PanelWidthPixels,
        DiagnosticPanelWindow.PanelHeightPixels,
        petRenderRadius: 160f,
        DiagnosticPanelWindow.PanelGapPixels,
        DebugPanelSide.None);
    AssertTrue(placement.IsVisible && placement.Side == DebugPanelSide.Right,
        "the Avalonia sidecar must preserve WPF right/left/below/above placement order");
    var configuredRetinaMetrics = DebugPanelCoordinateMetrics.Calculate(
        2d,
        usesCocoaPoints: true,
        ownerWidthDip: 360d,
        ownerHeightDip: 360d,
        panelWidthPixels: configuredDebug.PanelWidthPixels,
        panelHeightPixels: configuredDebug.PanelHeightPixels,
        panelGapPixels: configuredDebug.PanelGapPixels);
    AssertNear(210d, configuredRetinaMetrics.PanelWidth, 0.001d,
        "custom panel width must remain physical pixels on Retina");
    AssertNear(75d, configuredRetinaMetrics.PanelHeight, 0.001d,
        "custom panel height must remain physical pixels on Retina");
    AssertNear(8.5d, configuredRetinaMetrics.Gap, 0.001d,
        "custom panel gap must remain physical pixels on Retina");
    var windows2x = DebugPanelCoordinateMetrics.Calculate(
        2d, usesCocoaPoints: false, ownerWidthDip: 300d, ownerHeightDip: 200d);
    var mac2x = DebugPanelCoordinateMetrics.Calculate(
        2d, usesCocoaPoints: true, ownerWidthDip: 300d, ownerHeightDip: 200d);
    AssertNear(360d, windows2x.PanelWidth, 0.001d,
        "Windows placement must use physical pixels at 2x");
    AssertNear(600d, windows2x.OwnerWidth, 0.001d,
        "Windows owner DIPs must map to physical pixels at 2x");
    AssertNear(180d, mac2x.PanelWidth, 0.001d,
        "macOS placement must express 360 backing pixels as 180 Cocoa points");
    AssertNear(6.5d, mac2x.Gap, 0.001d,
        "macOS placement gap must be converted from backing pixels to points");
    AssertNear(300d, mac2x.OwnerWidth, 0.001d,
        "macOS owner bounds are already expressed in Cocoa points");

    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    var appSource = File.ReadAllText(Path.Combine(
        root, "src", "InfiniteLizards.Desktop", "App.cs"));
    AssertTrue(appSource.Contains("new DiagnosticPanelWindow(petWindow)", StringComparison.Ordinal) &&
               appSource.Contains("diagnosticPanel.ShowForOwner(petWindow)", StringComparison.Ordinal) &&
               appSource.Contains("profile.Runtime.DebugPanelWidthPixels", StringComparison.Ordinal) &&
               appSource.Contains("profile.Runtime.DebugMaximumFps", StringComparison.Ordinal),
        "the cross-platform host must own a toggleable debug panel");
    var diagnosticOptions = AppOptions.Parse(["--diagnostic"]);
    var overlayOptions = AppOptions.Parse(["--debug-overlay"]);
    AssertTrue(diagnosticOptions.DiagnosticMode && !diagnosticOptions.DebugOverlayMode &&
               !overlayOptions.DiagnosticMode && overlayOptions.DebugOverlayMode,
        "diagnostic and production-safe debug-overlay launch modes must parse independently");

    var panelSource = File.ReadAllText(Path.Combine(
        root, "src", "InfiniteLizards.Desktop", "DiagnosticPanelWindow.cs"));
    AssertTrue(panelSource.Contains("_owner.Closed += OnOwnerClosed", StringComparison.Ordinal) &&
               panelSource.Contains("Show(ownerWindow)", StringComparison.Ordinal) &&
               panelSource.Contains("e.Cancel = true", StringComparison.Ordinal) &&
               panelSource.Contains("Match the WPF fail-closed policy", StringComparison.Ordinal) &&
               !panelSource.Contains("_owner.Close()", StringComparison.Ordinal),
        "the panel must be visible and lifetime-bound to its pet window");
    AssertTrue(!panelSource.Contains("DesktopLizard.Core", StringComparison.Ordinal) &&
               !panelSource.Contains("InfiniteLizards.Gameplay", StringComparison.Ordinal),
        "diagnostic UI must consume host/engine telemetry without gameplay coupling");

    var contentSource = File.ReadAllText(Path.Combine(
        root, "src", "InfiniteLizards.Desktop", "DebugPanelContentView.cs"));
    AssertTrue(contentSource.Contains("Rows = 2, Columns = 4", StringComparison.Ordinal) &&
               contentSource.Contains("360×132 WPF panel", StringComparison.Ordinal) &&
               contentSource.Contains("PingFang SC", StringComparison.Ordinal) &&
               contentSource.Contains("Microsoft YaHei UI", StringComparison.Ordinal) &&
               contentSource.Contains("does not derive from", StringComparison.Ordinal) &&
               !contentSource.Contains("DesktopLizard.Core", StringComparison.Ordinal) &&
               !contentSource.Contains("InfiniteLizards.Gameplay", StringComparison.Ordinal),
        "the 2×4 Avalonia action strip must remain presentation-only");

    var controllerSource = File.ReadAllText(Path.Combine(
        root, "src", "InfiniteLizards.Desktop", "LizardDebugPanelController.cs"));
    AssertTrue(controllerSource.Contains(
                   "PortableDebugTransitionRow.AfterForward;",
                   StringComparison.Ordinal) &&
               controllerSource.Contains(
                   "if (hasCurrentRow)",
                   StringComparison.Ordinal) &&
               controllerSource.Contains(
                   "DebugPathResetRequested += OnPathResetRequested",
                   StringComparison.Ordinal),
        "states without a matrix row must retain the WPF probability context and path-reset seam");
    var overlaySource = File.ReadAllText(Path.Combine(
        root, "src", "InfiniteLizards.Desktop", "Rendering", "LizardDebugOverlay.cs"));
    AssertTrue(overlaySource.Contains(
                   "context.DrawEllipse(null, guidePen",
                   StringComparison.Ordinal) &&
               overlaySource.Contains(
                   "context.DrawLine(guidePen",
                   StringComparison.Ordinal),
        "stepping guides must keep the WPF normal line width while active legs are emphasized");
}

static DesktopPetInputRegion CreateEllipseRegion(double centerX) => new(
    ImmutableArray<DesktopPetBezierPath>.Empty,
    ImmutableArray<DesktopPetStrokedBezierPath>.Empty,
    ImmutableArray.Create(new DesktopPetRegionEllipse(
        new Point(centerX, 10),
        3,
        4)));

static void AssertRasterization(
    Win32InputRegionRasterPlan plan,
    long poseVersion,
    DesktopPetInputRegion region,
    double scale,
    bool expected,
    string message)
{
    var scaleBits = BitConverter.DoubleToInt64Bits(scale);
    var actual = plan.Rasterizations.Any(candidate =>
        candidate.PoseVersion == poseVersion &&
        ReferenceEquals(candidate.Region, region) &&
        BitConverter.DoubleToInt64Bits(candidate.RasterScale) == scaleBits);
    AssertEqual(expected, actual, message);
}

static object CloneWithProperties(
    object source,
    params (string Name, object Value)[] properties)
{
    var clone = typeof(object).GetMethod(
            "MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(source, null)!;
    foreach (var (name, value) in properties)
    {
        clone.GetType().GetProperty(name)!.SetValue(clone, value);
    }

    return clone;
}

static void AssertThrows<TException>(Action action)
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

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual<T>(T expected, T actual, string message)
    where T : IEquatable<T>
{
    if (!expected.Equals(actual))
    {
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }
}

static void AssertNear(double expected, double actual, double tolerance, string message)
{
    if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException(
            $"{message}: expected {expected:R} ± {tolerance:R}, actual {actual:R}");
    }
}
