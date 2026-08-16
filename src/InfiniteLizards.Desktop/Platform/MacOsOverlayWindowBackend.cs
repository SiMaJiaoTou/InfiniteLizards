using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using DesktopPet.Engine;

namespace InfiniteLizards.Desktop.Platform;

internal static class MacOsClickThroughPolicy
{
    public static void RequireObservedState(
        bool requested,
        bool observed,
        string owner)
    {
        if (requested == observed)
        {
            return;
        }

        throw new NativeOverlaySafetyException(
            $"AppKit did not apply ignoresMouseEvents={requested} to {owner}; " +
            $"the getter reported {observed}.");
    }
}

internal static class MacOsActiveSpaceMembershipPolicy
{
    public static bool ContainsRequiredWindows(
        nint surfaceWindowNumber,
        nint anchorWindowNumber,
        IReadOnlySet<nint> activeSpaceWindowNumbers)
    {
        ArgumentNullException.ThrowIfNull(activeSpaceWindowNumbers);
        return surfaceWindowNumber > nint.Zero &&
               anchorWindowNumber > nint.Zero &&
               activeSpaceWindowNumbers.Contains(surfaceWindowNumber) &&
               activeSpaceWindowNumbers.Contains(anchorWindowNumber);
    }
}

internal static class MacOsWindowHitRoutingPolicy
{
    public static bool MatchesClickThroughTransition(
        nint surfaceWindowNumber,
        nint anchorWindowNumber,
        nint interactiveHitWindowNumber,
        nint clickThroughHitWindowNumber) =>
        surfaceWindowNumber > nint.Zero &&
        anchorWindowNumber > nint.Zero &&
        interactiveHitWindowNumber == surfaceWindowNumber &&
        clickThroughHitWindowNumber != surfaceWindowNumber &&
        clickThroughHitWindowNumber != anchorWindowNumber;
}

internal static class MacOsFrontmostApplicationPolicy
{
    public static bool IsOwnedByAnotherProcess(
        nint currentProcessIdentifier,
        nint frontmostProcessIdentifier) =>
        currentProcessIdentifier > nint.Zero &&
        frontmostProcessIdentifier > nint.Zero &&
        currentProcessIdentifier != frontmostProcessIdentifier;
}

internal static class MacOsNativeAcceptanceRetryPolicy
{
    public const int MaximumInteractiveHitAttempts = 5;

    public static bool ShouldRetryInteractiveHit(
        bool interactiveStateObserved,
        nint surfaceWindowNumber,
        nint interactiveHitWindowNumber,
        int attemptNumber) =>
        interactiveStateObserved &&
        surfaceWindowNumber > nint.Zero &&
        interactiveHitWindowNumber != surfaceWindowNumber &&
        attemptNumber >= 1 &&
        attemptNumber < MaximumInteractiveHitAttempts;
}

/// <summary>
/// AppKit overlay policy. Avalonia supplies the shared transparent Skia
/// surface; AppKit supplies the native non-blocking desktop behavior that a
/// cross-platform UI framework cannot infer from transparent pixels.
/// </summary>
internal sealed class MacOsOverlayWindowBackend : IOverlayWindowBackend
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private const string CoreGraphicsFramework =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundationFramework =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const nuint CanJoinAllSpaces = 1u << 0;
    private const nuint IgnoresCycle = 1u << 6;
    private const nuint FullScreenAuxiliary = 1u << 8;
    private const nuint CanJoinAllApplications = 1u << 18;
    private const nuint NonactivatingPanelStyle = 1u << 7;
    private const nuint BackingStoreBuffered = 2;
    private const int MainMenuWindowLevelKey = 8;
    private const int StatusWindowLevelKey = 9;
    private const nint ActivationPolicyAccessory = 1;
    private const int CombinedSessionState = 0;
    private const nint WindowAbove = 1;
    private const nuint WindowNumberListAllApplications = 1u << 0;
    private const string NonactivatingWindowClassName =
        "InfiniteLizardsNonactivatingAvnWindow";

    private static readonly WindowEligibilityCallback RejectWindowEligibility =
        static (_, _) => 0;

    private Window? _windowOwner;
    private nint _avaloniaWindow;
    private nint _panel;
    private nint _originalAvaloniaWindowClass;
    private nint _installedAvaloniaWindowClass;
    private bool _clickThrough;
    private bool _interactiveDiagnosticMode;
    private bool _writeNativeDiagnostics;
    private bool _activationPolicyFailedClosed;
    private bool _activationPolicyFailureReported;
    private bool _anchorPlaced;
    private bool _nativeAcceptanceMode;
    private nuint _anchorScreenNumber;
    private DispatcherTimer? _acceptanceTimer;
    private IDisposable? _acceptanceProbeTimer;
    private bool _acceptanceProbeInFlight;
    private SurfacePlacement? _acceptancePlacement;
    private double _acceptanceDisplayScale = 1d;
    private int _acceptanceScreenIndex = -1;
    private nuint _lastPlacementDiagnosticScreen;

    // The Avalonia NSWindow is always the real render/input surface. The panel
    // is only a cross-Space, high-level anchor; moving Avalonia's AvnView away
    // from its owning AvnWindow stops Avalonia.Native's render scheduling.
    private nint SurfaceWindow => _avaloniaWindow;

    public bool IsAnyMouseButtonPressed =>
        CGEventSourceButtonState(CombinedSessionState, 0) ||
        CGEventSourceButtonState(CombinedSessionState, 1) ||
        CGEventSourceButtonState(CombinedSessionState, 2);

    public void Attach(Window window, bool interactiveDiagnosticMode)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_windowOwner is not null)
        {
            throw new InvalidOperationException("The macOS overlay backend is already attached.");
        }

        _windowOwner = window;
        _interactiveDiagnosticMode = interactiveDiagnosticMode;
        _nativeAcceptanceMode = ReadEnvironmentSwitch(
            "INFINITE_LIZARDS_NATIVE_ACCEPTANCE");
        _writeNativeDiagnostics = interactiveDiagnosticMode ||
            _nativeAcceptanceMode ||
            ReadEnvironmentSwitch("INFINITE_LIZARDS_NATIVE_DIAGNOSTICS");
        _activationPolicyFailedClosed = false;
        _activationPolicyFailureReported = false;
        var platformHandle = window.TryGetPlatformHandle();
        // Use Avalonia's explicit macOS contract instead of relying on the
        // generic handle descriptor. Avalonia.Native returns its AvnWindow
        // subclass here; that object is a real NSWindow and accepts AppKit
        // selectors directly.
        var nativeWindow = platformHandle is IMacOSTopLevelPlatformHandle macHandle
            ? macHandle.NSWindow
            : nint.Zero;
        if (nativeWindow == nint.Zero)
        {
            if (interactiveDiagnosticMode)
            {
                return;
            }

            throw new NativeOverlaySafetyException(
                "Avalonia did not expose an NSWindow before the production overlay was shown.");
        }

        // Keep the Avalonia-owned NSWindow and AvnView together. A transparent,
        // non-activating NSPanel acts only as the cross-Space parent/anchor.
        // Its collection behavior requests ordinary Space participation; other
        // applications' native full-screen overlays are outside this contract.
        // Native parent/child ordering is retained without changing Avalonia's
        // render ownership.
        _avaloniaWindow = Retain(nativeWindow);
        InstallNonactivatingWindowSubclass();
        ApplySurfaceActivationPolicy();
        try
        {
            ConfigureTransparentWindow(_avaloniaWindow, setOverlayLevel: false);
            _panel = CreatePanel();
            if (_panel == nint.Zero)
            {
                throw new InvalidOperationException("AppKit could not create the overlay NSPanel.");
            }
            ConfigurePanel(_panel);
            SetAndVerifyIgnoresMouseEvents(
                _panel,
                ignoresMouseEvents: true,
                "the macOS anchor panel");
            AttachSurfaceToAnchor();
        }
        catch (Exception exception)
        {
            ReleaseAnchor();
            Trace.WriteLine(
                $"[InfiniteLizards] NSPanel creation failed; using NSWindow fallback: {exception}");
            ConfigureTransparentWindow(_avaloniaWindow, setOverlayLevel: true);
        }

        SetAllSpacesCollectionBehavior();

        var applicationClass = objc_getClass("NSApplication");
        var application = SendPointer(applicationClass, "sharedApplication");
        if (application != nint.Zero)
        {
            SendInteger(
                application,
                "setActivationPolicy:",
                ActivationPolicyAccessory);
        }

        if (!interactiveDiagnosticMode)
        {
            SetClickThrough(true);
        }
    }

    public bool TryGetGlobalPointer(out DevicePoint point)
    {
        var mouseEvent = CGEventCreate(nint.Zero);
        if (mouseEvent == nint.Zero)
        {
            point = default;
            return false;
        }

        try
        {
            // Quartz global coordinates are top-left/Y-down, matching
            // Avalonia's screen PixelPoint convention. On Retina these are
            // desktop points; the host normalizes each Screen.Scaling before
            // passing coordinates into the canonical engine space.
            var location = CGEventGetLocation(mouseEvent);
            if (!double.IsFinite(location.X) || !double.IsFinite(location.Y))
            {
                point = default;
                return false;
            }

            point = new DevicePoint(location.X, location.Y);
            return true;
        }
        finally
        {
            CFRelease(mouseEvent);
        }
    }

    public void SetClickThrough(bool clickThrough)
    {
        var surface = SurfaceWindow;
        if (surface == nint.Zero)
        {
            if (_interactiveDiagnosticMode)
            {
                return;
            }

            throw new NativeOverlaySafetyException(
                "The production macOS overlay lost its NSWindow while changing input state.");
        }

        // Validate the non-activation invariant before making the production
        // surface interactive. A failed invariant is latched for this native
        // window's lifetime; rendering continues, but input stays fail-closed.
        ApplySurfaceActivationPolicy();
        if (!_interactiveDiagnosticMode && _activationPolicyFailedClosed)
        {
            EnsureFailClosedClickThrough(
                "The non-activation invariant is already fail-closed.");
            return;
        }

        var observed = SendBoolResult(surface, "ignoresMouseEvents");
        if (_clickThrough == clickThrough && observed == clickThrough)
        {
            return;
        }

        try
        {
            SetAndVerifyIgnoresMouseEvents(
                surface,
                clickThrough,
                "the Avalonia-owned macOS surface");
            _clickThrough = clickThrough;
        }
        catch (Exception exception) when (!_interactiveDiagnosticMode)
        {
            EnterActivationPolicyFailClosed(
                "The native surface did not apply the requested click-through state.",
                exception);
            return;
        }
        ApplySurfaceActivationPolicy();
    }

    public void PlaceSurface(
        Window window,
        SurfacePlacement placement,
        double displayScale)
    {
        if (!ReferenceEquals(window, _windowOwner))
        {
            throw new InvalidOperationException(
                "The placement belongs to a different macOS overlay window.");
        }

        var effectivePlacement = _nativeAcceptanceMode &&
                                 _acceptancePlacement is { } acceptancePlacement
            ? acceptancePlacement
            : placement;
        if (_nativeAcceptanceMode && _acceptancePlacement is null &&
            double.IsFinite(displayScale) && displayScale > 0d)
        {
            _acceptanceDisplayScale = displayScale;
        }
        var effectiveDisplayScale = _nativeAcceptanceMode &&
                                    _acceptancePlacement is not null
            ? _acceptanceDisplayScale
            : displayScale;
        var coordinateReferenceFrame = GetCoordinateReferenceScreenFrame();
        var surfaceFrame = ToAppKitFrame(effectivePlacement, coordinateReferenceFrame);

        var targetScreen = FindScreenForFrame(surfaceFrame);
        if (_writeNativeDiagnostics &&
            _lastPlacementDiagnosticScreen != targetScreen.Number)
        {
            _lastPlacementDiagnosticScreen = targetScreen.Number;
            Console.Error.WriteLine(
                $"[InfiniteLizards] placement devicePoints=" +
                $"({effectivePlacement.X},{effectivePlacement.Y}," +
                $"{effectivePlacement.Width},{effectivePlacement.Height}), " +
                $"avaloniaScale={effectiveDisplayScale:F3}, " +
                $"referenceTop={coordinateReferenceFrame.Top:F3}, " +
                $"targetScreen={targetScreen.Number}, " +
                $"nsscreenFrame={targetScreen.Frame}, " +
                $"nsscreenBacking={targetScreen.BackingScale:F3}, " +
                $"appKitFrame={surfaceFrame}");
        }
        EnsureAnchorOnScreen(targetScreen, surfaceFrame);

        // Keep Avalonia's window state and backing-scale notifications current.
        OverlayWindowPlacement.ApplyManaged(
            window,
            effectivePlacement,
            effectiveDisplayScale);
        // One AppKit call updates position and dimensions together. This avoids
        // a visible one-frame stretch on the actual Avalonia/Skia surface.
        SendRectBool(_avaloniaWindow, "setFrame:display:", surfaceFrame, true);
    }

    public void EnsureVisible()
    {
        var surface = SurfaceWindow;
        if (surface == nint.Zero)
        {
            return;
        }

        if (_panel != nint.Zero)
        {
            AttachSurfaceToAnchor();
            SendVoid(_panel, "orderFrontRegardless");
        }

        ApplySurfaceActivationPolicy();
        if (_interactiveDiagnosticMode)
        {
            var application = SendPointer(objc_getClass("NSApplication"), "sharedApplication");
            if (application != nint.Zero)
            {
                SendPointerArgument(application, "unhide:", nint.Zero);
                SendBool(application, "activateIgnoringOtherApps:", true);
            }
            SendPointerArgument(surface, "makeKeyAndOrderFront:", nint.Zero);
        }

        SetAllSpacesCollectionBehavior();
        if (_panel == nint.Zero)
        {
            // Fallback NSWindow has no parent to bring it forward.
            SendVoid(surface, "orderFrontRegardless");
        }
        ApplySurfaceActivationPolicy();
        WriteWindowDiagnostic("visible");
        StartNativeAcceptance();
    }

    public void Dispose()
    {
        StopNativeAcceptance();
        ReleaseNativeWindows();
        _windowOwner = null;
        _interactiveDiagnosticMode = false;
        _writeNativeDiagnostics = false;
        _nativeAcceptanceMode = false;
        _activationPolicyFailedClosed = false;
        _activationPolicyFailureReported = false;
        _lastPlacementDiagnosticScreen = 0;
    }

    private static void SendBool(nint receiver, string selector, bool value) =>
        objc_msgSend_bool(receiver, sel_registerName(selector), value ? (byte)1 : (byte)0);

    private static bool SendBoolResult(nint receiver, string selector) =>
        objc_msgSend_bool_result(receiver, sel_registerName(selector)) != 0;

    private static void SendInteger(nint receiver, string selector, nint value) =>
        objc_msgSend_integer(receiver, sel_registerName(selector), value);

    private static nint SendIntegerResult(nint receiver, string selector) =>
        objc_msgSend_integer_result(receiver, sel_registerName(selector));

    private static double SendDoubleResult(nint receiver, string selector) =>
        objc_msgSend_double_result(receiver, sel_registerName(selector));

    private static void SendUnsigned(nint receiver, string selector, nuint value) =>
        objc_msgSend_unsigned(receiver, sel_registerName(selector), value);

    private static nuint SendUnsignedResult(nint receiver, string selector) =>
        objc_msgSend_unsigned_result(receiver, sel_registerName(selector));

    private static nint SendPointer(nint receiver, string selector) =>
        objc_msgSend_pointer(receiver, sel_registerName(selector));

    private static void SendVoid(nint receiver, string selector) =>
        objc_msgSend_void(receiver, sel_registerName(selector));

    private static void SendPointerArgument(nint receiver, string selector, nint value) =>
        objc_msgSend_pointer_argument(receiver, sel_registerName(selector), value);

    private static nint SendPointerArgumentResult(
        nint receiver,
        string selector,
        nint value) => objc_msgSend_pointer_argument_result(
        receiver,
        sel_registerName(selector),
        value);

    private static nint SendPointerUtf8(
        nint receiver,
        string selector,
        string value) => objc_msgSend_pointer_utf8(
        receiver,
        sel_registerName(selector),
        value);

    private static void SendPointerIntegerArgument(
        nint receiver,
        string selector,
        nint pointer,
        nint value) => objc_msgSend_pointer_integer_argument(
        receiver,
        sel_registerName(selector),
        pointer,
        value);

    private static nint SendPointerUnsigned(nint receiver, string selector, nuint value) =>
        objc_msgSend_pointer_unsigned(receiver, sel_registerName(selector), value);

    private static nint Retain(nint value)
    {
        if (value != nint.Zero)
        {
            _ = SendPointer(value, "retain");
        }
        return value;
    }

    private static void Release(nint value)
    {
        if (value != nint.Zero)
        {
            SendVoid(value, "release");
        }
    }

    private static nint CreatePanel()
    {
        var panelClass = objc_getClass("NSPanel");
        var allocatedPanel = SendPointer(panelClass, "alloc");
        if (allocatedPanel == nint.Zero)
        {
            return nint.Zero;
        }

        return objc_msgSend_init_rect(
            allocatedPanel,
            sel_registerName("initWithContentRect:styleMask:backing:defer:"),
            new NativeRect(new NativePoint(0d, 0d), new NativeSize(1d, 1d)),
            NonactivatingPanelStyle,
            BackingStoreBuffered,
            0);
    }

    private static void ConfigureTransparentWindow(nint window, bool setOverlayLevel)
    {
        SendBool(window, "setOpaque:", false);
        SendBool(window, "setHasShadow:", false);
        if (RespondsToSelector(window, "setCanHide:"))
        {
            SendBool(window, "setCanHide:", false);
        }
        var clearColor = SendPointer(objc_getClass("NSColor"), "clearColor");
        if (clearColor != nint.Zero)
        {
            SendPointerArgument(window, "setBackgroundColor:", clearColor);
        }
        if (setOverlayLevel)
        {
            SendInteger(window, "setLevel:", OverlayWindowLevel());
        }
    }

    private static void ConfigurePanel(nint panel)
    {
        SendBool(panel, "setReleasedWhenClosed:", false);
        SendBool(panel, "setFloatingPanel:", true);
        SendBool(panel, "setHidesOnDeactivate:", false);
        SendBool(panel, "setBecomesKeyOnlyIfNeeded:", true);
        SendBool(panel, "setExcludedFromWindowsMenu:", true);
        SendBool(panel, "setRestorable:", false);
        SendBool(panel, "setCanHide:", false);
        // setFloatingPanel: resets an NSPanel to NSFloatingWindowLevel. Apply
        // the status/main-menu overlay level last so it survives that policy.
        ConfigureTransparentWindow(panel, setOverlayLevel: true);
    }

    private static nint OverlayWindowLevel() => new(Math.Max(
        CGWindowLevelForKey(MainMenuWindowLevelKey),
        CGWindowLevelForKey(StatusWindowLevelKey)));

    private static NativeRect GetCoordinateReferenceScreenFrame()
    {
        // Avalonia.Native's ConvertPointY uses [[NSScreen screens] firstObject].
        // NSScreen.mainScreen follows the key window and can change in
        // diagnostic mode, which would make native Y jump between displays.
        var screens = SendPointer(objc_getClass("NSScreen"), "screens");
        var coordinateReferenceScreen = SendPointer(screens, "firstObject");
        if (coordinateReferenceScreen == nint.Zero)
        {
            throw new InvalidOperationException(
                "AppKit did not report a coordinate-reference screen.");
        }

        var selector = sel_registerName("frame");
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            objc_msgSend_rect_stret(out var result, coordinateReferenceScreen, selector);
            return result;
        }
        return objc_msgSend_rect_result(coordinateReferenceScreen, selector);
    }

    private static void SendRectBool(
        nint receiver,
        string selector,
        NativeRect value,
        bool flag) => objc_msgSend_rect_bool(
        receiver,
        sel_registerName(selector),
        value,
        flag ? (byte)1 : (byte)0);

    private void SetAllSpacesCollectionBehavior()
    {
        if (_avaloniaWindow == nint.Zero)
        {
            return;
        }

        // The SDK groups FullScreenAuxiliary (legacy full-screen behavior) and
        // CanJoinAllApplications (macOS 13 application membership) separately;
        // they are valid together. The similarly named 1 << 17 auxiliary flag
        // belongs to CanJoinAllApplications' mutually-exclusive group.
        var behavior = DesiredCollectionBehavior();

        SendUnsigned(_avaloniaWindow, "setCollectionBehavior:", behavior);
        if (_panel != nint.Zero)
        {
            SendUnsigned(_panel, "setCollectionBehavior:", behavior);
        }
    }

    private static nuint DesiredCollectionBehavior()
    {
        var behavior = CanJoinAllSpaces | IgnoresCycle | FullScreenAuxiliary;
        if (OperatingSystem.IsMacOSVersionAtLeast(13))
        {
            behavior |= CanJoinAllApplications;
        }
        return behavior;
    }

    private void WriteWindowDiagnostic(string stage)
    {
        if (!_writeNativeDiagnostics)
        {
            return;
        }

        var surface = SurfaceWindow;
        if (surface == nint.Zero)
        {
            return;
        }

        var pointerText = TryGetGlobalPointer(out var quartzPointer)
            ? quartzPointer.ToString()
            : "unavailable";
        Console.Error.WriteLine(
            $"[InfiniteLizards] NSPanel={_panel != nint.Zero}, " +
            $"surfaceWindow={SendIntegerResult(surface, "windowNumber")}, " +
            $"visible={SendBoolResult(surface, "isVisible")}, " +
            $"activeSpace={SendBoolResult(surface, "isOnActiveSpace")}, " +
            $"miniaturized={SendBoolResult(surface, "isMiniaturized")}, " +
            $"canKey={SendBoolResult(surface, "canBecomeKeyWindow")}, " +
            $"key={SendBoolResult(surface, "isKeyWindow")}, " +
            $"canMain={SendBoolResult(surface, "canBecomeMainWindow")}, " +
            $"main={SendBoolResult(surface, "isMainWindow")}, " +
            $"activationFailClosed={_activationPolicyFailedClosed}, " +
            $"surfaceClass={ReadClassName(surface)}, " +
            $"level={SendIntegerResult(surface, "level")}, " +
            $"style=0x{SendUnsignedResult(surface, "styleMask"):X}, " +
            $"collection=0x{SendUnsignedResult(surface, "collectionBehavior"):X}, " +
            $"frame={GetRectResult(surface, "frame")}, " +
            $"surfaceScreen={ReadWindowScreenNumber(surface)}, " +
            $"surfaceBacking={ReadWindowBackingScale(surface):F3}, " +
            $"quartzPointer={pointerText}, " +
            $"appActive={ReadApplicationActive()}, " +
            $"activationPolicy={ReadApplicationActivationPolicy()}");
        if (_panel != nint.Zero)
        {
            var contentView = SendPointer(_avaloniaWindow, "contentView");
            var contentFrame = GetRectResult(contentView, "frame");
            var contentBackingFrame = GetRectArgumentResult(
                contentView,
                "convertRectToBacking:",
                contentFrame);
            var subviews = SendPointer(contentView, "subviews");
            var firstSubview = SendPointer(subviews, "firstObject");
            Console.Error.WriteLine(
                $"[InfiniteLizards] stage={stage}, anchorVisible=" +
                $"{SendBoolResult(_panel, "isVisible")}, " +
                $"anchorWindow={SendIntegerResult(_panel, "windowNumber")}, " +
                $"anchorActiveSpace={SendBoolResult(_panel, "isOnActiveSpace")}, " +
                $"anchorFrame={GetRectResult(_panel, "frame")}, " +
                $"anchorScreen={ReadWindowScreenNumber(_panel)}, " +
                $"anchorBacking={ReadWindowBackingScale(_panel):F3}, " +
                $"parentMatch={SendPointer(_avaloniaWindow, "parentWindow") == _panel}, " +
                $"contentOwnerMatch={SendPointer(contentView, "window") == _avaloniaWindow}, " +
                $"surfaceIgnoresMouse={SendBoolResult(_avaloniaWindow, "ignoresMouseEvents")}, " +
                $"anchorIgnoresMouse={SendBoolResult(_panel, "ignoresMouseEvents")}, " +
                $"contentHidden={SendBoolResult(contentView, "isHidden")}, " +
                $"contentAlpha={SendDoubleResult(contentView, "alphaValue"):F3}, " +
                $"contentFrame={contentFrame}, " +
                $"contentBackingFrame={contentBackingFrame}, " +
                $"subviews={SendUnsignedResult(subviews, "count")}, " +
                $"childHidden={(firstSubview == nint.Zero ? "n/a" : SendBoolResult(firstSubview, "isHidden"))}, " +
                $"childFrame={(firstSubview == nint.Zero ? "n/a" : GetRectResult(firstSubview, "frame"))}");
            var subviewCount = string.Equals(stage, "visible", StringComparison.Ordinal)
                ? Math.Min(SendUnsignedResult(subviews, "count"), (nuint)8)
                : 0u;
            for (nuint index = 0; index < subviewCount; index++)
            {
                var subview = SendPointerUnsigned(subviews, "objectAtIndex:", index);
                var className = Marshal.PtrToStringAnsi(class_getName(object_getClass(subview))) ?? "?";
                Console.Error.WriteLine(
                    $"[InfiniteLizards] subview[{index}] class={className}, " +
                    $"hidden={SendBoolResult(subview, "isHidden")}, " +
                    $"alpha={SendDoubleResult(subview, "alphaValue"):F3}, " +
                    $"frame={GetRectResult(subview, "frame")}");
            }
        }
    }

    private void AttachSurfaceToAnchor()
    {
        if (_panel == nint.Zero || _avaloniaWindow == nint.Zero ||
            SendPointer(_avaloniaWindow, "parentWindow") == _panel)
        {
            return;
        }

        var previousParent = SendPointer(_avaloniaWindow, "parentWindow");
        if (previousParent != nint.Zero)
        {
            SendPointerArgument(previousParent, "removeChildWindow:", _avaloniaWindow);
        }
        SendPointerIntegerArgument(
            _panel,
            "addChildWindow:ordered:",
            _avaloniaWindow,
            WindowAbove);
    }

    private void DetachSurfaceFromAnchor()
    {
        if (_panel != nint.Zero && _avaloniaWindow != nint.Zero &&
            SendPointer(_avaloniaWindow, "parentWindow") == _panel)
        {
            SendPointerArgument(_panel, "removeChildWindow:", _avaloniaWindow);
        }
    }

    private void EnsureAnchorOnScreen(NativeScreen target, NativeRect surfaceFrame)
    {
        if (_panel == nint.Zero || _avaloniaWindow == nint.Zero)
        {
            return;
        }

        var actualScreenNumber = ReadWindowScreenNumber(_panel);
        var actualAnchorFrame = GetRectResult(_panel, "frame");
        if (_anchorPlaced &&
            _anchorScreenNumber == target.Number &&
            actualScreenNumber == target.Number &&
            target.Frame.Contains(actualAnchorFrame) &&
            SendPointer(_avaloniaWindow, "parentWindow") == _panel)
        {
            return;
        }

        var wasVisible = SendBoolResult(_panel, "isVisible");
        DetachSurfaceFromAnchor();

        var anchorX = Math.Clamp(
            surfaceFrame.CenterX,
            target.Frame.Left,
            Math.Max(target.Frame.Left, target.Frame.Right - 1d));
        var anchorY = Math.Clamp(
            surfaceFrame.CenterY,
            target.Frame.Bottom,
            Math.Max(target.Frame.Bottom, target.Frame.Top - 1d));
        var anchorFrame = new NativeRect(
            new NativePoint(anchorX, anchorY),
            new NativeSize(1d, 1d));
        SendRectBool(_panel, "setFrame:display:", anchorFrame, false);
        _anchorPlaced = true;
        _anchorScreenNumber = target.Number;

        SetAllSpacesCollectionBehavior();
        SetAndVerifyIgnoresMouseEvents(
            _panel,
            ignoresMouseEvents: true,
            "the macOS anchor panel");
        SetClickThrough(_clickThrough);
        ApplySurfaceActivationPolicy();
        AttachSurfaceToAnchor();
        if (wasVisible)
        {
            SendVoid(_panel, "orderFrontRegardless");
        }
    }

    private void ReleaseAnchor()
    {
        var panel = _panel;
        _panel = nint.Zero;
        _anchorPlaced = false;
        _anchorScreenNumber = 0;
        if (panel == nint.Zero)
        {
            return;
        }

        if (_avaloniaWindow != nint.Zero &&
            SendPointer(_avaloniaWindow, "parentWindow") == panel)
        {
            SendPointerArgument(panel, "removeChildWindow:", _avaloniaWindow);
        }
        SendPointerArgument(panel, "orderOut:", nint.Zero);
        SendVoid(panel, "close");
        Release(panel);
    }

    private void ReleaseNativeWindows()
    {
        var avaloniaWindow = _avaloniaWindow;
        _clickThrough = false;
        ReleaseAnchor();
        _avaloniaWindow = nint.Zero;
        if (avaloniaWindow != nint.Zero)
        {
            RestoreOriginalWindowClass(avaloniaWindow);
        }
        Release(avaloniaWindow);
    }

    private void InstallNonactivatingWindowSubclass()
    {
        if (_interactiveDiagnosticMode || _avaloniaWindow == nint.Zero)
        {
            return;
        }

        try
        {
            var originalClass = object_getClass(_avaloniaWindow);
            if (originalClass == nint.Zero)
            {
                throw new InvalidOperationException(
                    "Objective-C did not report Avalonia's window class.");
            }

            var subclass = objc_getClass(NonactivatingWindowClassName);
            if (subclass == nint.Zero)
            {
                subclass = objc_allocateClassPair(
                    originalClass,
                    NonactivatingWindowClassName,
                    0u);
                if (subclass == nint.Zero)
                {
                    throw new InvalidOperationException(
                        "Objective-C could not allocate the non-activating window subclass.");
                }

                var implementation = Marshal.GetFunctionPointerForDelegate(
                    RejectWindowEligibility);
                var boolEncoding = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                    ? "B@:"
                    : "c@:";
                if (class_addMethod(
                        subclass,
                        sel_registerName("canBecomeKeyWindow"),
                        implementation,
                        boolEncoding) == 0 ||
                    class_addMethod(
                        subclass,
                        sel_registerName("canBecomeMainWindow"),
                        implementation,
                        boolEncoding) == 0)
                {
                    objc_disposeClassPair(subclass);
                    throw new InvalidOperationException(
                        "Objective-C could not install window eligibility overrides.");
                }
                objc_registerClassPair(subclass);
            }
            else if (class_getSuperclass(subclass) != originalClass)
            {
                throw new InvalidOperationException(
                    "The non-activating window subclass has an unexpected superclass.");
            }

            var previousClass = object_setClass(_avaloniaWindow, subclass);
            if (previousClass != originalClass)
            {
                _ = object_setClass(_avaloniaWindow, previousClass);
                throw new InvalidOperationException(
                    "Avalonia's native window class changed while attaching the overlay.");
            }

            _originalAvaloniaWindowClass = originalClass;
            _installedAvaloniaWindowClass = subclass;
        }
        catch (Exception exception)
        {
            _originalAvaloniaWindowClass = nint.Zero;
            _installedAvaloniaWindowClass = nint.Zero;
            EnterActivationPolicyFailClosed(
                "Could not install the non-activating AvnWindow subclass.",
                exception);
        }
    }

    private void RestoreOriginalWindowClass(nint window)
    {
        var originalClass = _originalAvaloniaWindowClass;
        var installedClass = _installedAvaloniaWindowClass;
        _originalAvaloniaWindowClass = nint.Zero;
        _installedAvaloniaWindowClass = nint.Zero;
        if (window == nint.Zero || originalClass == nint.Zero || installedClass == nint.Zero)
        {
            return;
        }

        if (object_getClass(window) == installedClass)
        {
            _ = object_setClass(window, originalClass);
        }
        else if (!_activationPolicyFailureReported)
        {
            Trace.WriteLine(
                "[InfiniteLizards] native window class changed externally; " +
                "skipping unsafe Objective-C class restoration.");
        }
    }

    private void ApplySurfaceActivationPolicy()
    {
        var surface = _avaloniaWindow;
        if (surface == nint.Zero)
        {
            return;
        }

        if (_interactiveDiagnosticMode)
        {
            if (RespondsToSelector(surface, "setCanBecomeKeyWindow:"))
            {
                SendBool(surface, "setCanBecomeKeyWindow:", true);
            }
            return;
        }

        if (_activationPolicyFailedClosed)
        {
            EnsureFailClosedClickThrough(
                "The native activation policy has already failed closed.");
            return;
        }

        try
        {
            if (_installedAvaloniaWindowClass == nint.Zero ||
                object_getClass(surface) != _installedAvaloniaWindowClass)
            {
                EnterActivationPolicyFailClosed(
                    "The protected AvnWindow subclass is missing or was replaced.");
                return;
            }

            // Avalonia's AvnWindow exposes this explicit setter even though
            // NSWindow's public canBecomeKeyWindow API is read-only. The
            // dynamic subclass independently rejects both key and main status.
            if (RespondsToSelector(surface, "setCanBecomeKeyWindow:"))
            {
                SendBool(surface, "setCanBecomeKeyWindow:", false);
            }

            if (!RespondsToSelector(surface, "canBecomeKeyWindow") ||
                !RespondsToSelector(surface, "canBecomeMainWindow") ||
                SendBoolResult(surface, "canBecomeKeyWindow") ||
                SendBoolResult(surface, "canBecomeMainWindow"))
            {
                EnterActivationPolicyFailClosed(
                    "The native window did not verify as non-key and non-main.");
            }
        }
        catch (Exception exception)
        {
            EnterActivationPolicyFailClosed(
                "The native non-activation policy could not be verified.",
                exception);
        }
    }

    private void EnterActivationPolicyFailClosed(
        string reason,
        Exception? exception = null)
    {
        _activationPolicyFailedClosed = true;
        var clickThroughException = ForceFailClosedClickThrough();
        var failure = clickThroughException is null
            ? exception
            : exception is null
                ? clickThroughException
                : new AggregateException(exception, clickThroughException);
        if (_activationPolicyFailureReported)
        {
            if (clickThroughException is not null)
            {
                throw new NativeOverlaySafetyException(
                    "The macOS overlay could not re-establish verified click-through safety.",
                    failure);
            }
            return;
        }

        _activationPolicyFailureReported = true;
        var exceptionText = failure is null
            ? string.Empty
            : $" {failure.GetType().Name}: {failure.Message}";
        var message =
            "[InfiniteLizards] native activation safety failed; " +
            "input is permanently click-through for this window. " +
            reason + exceptionText;
        Trace.WriteLine(message);
        if (_writeNativeDiagnostics)
        {
            Console.Error.WriteLine(message);
        }

        if (clickThroughException is not null)
        {
            throw new NativeOverlaySafetyException(
                "The macOS overlay activation policy failed and click-through " +
                "could not be verified.",
                failure);
        }
    }

    private Exception? ForceFailClosedClickThrough()
    {
        var surface = _avaloniaWindow;
        if (surface == nint.Zero)
        {
            return new NativeOverlaySafetyException(
                "There is no NSWindow on which to verify fail-closed click-through.");
        }

        try
        {
            SetAndVerifyIgnoresMouseEvents(
                surface,
                ignoresMouseEvents: true,
                "the fail-closed macOS surface");
            _clickThrough = true;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private void EnsureFailClosedClickThrough(string reason)
    {
        var exception = ForceFailClosedClickThrough();
        if (exception is not null)
        {
            throw new NativeOverlaySafetyException(
                $"{reason} The NSWindow did not verify as click-through.",
                exception);
        }
    }

    private static void SetAndVerifyIgnoresMouseEvents(
        nint window,
        bool ignoresMouseEvents,
        string owner)
    {
        if (window == nint.Zero)
        {
            throw new NativeOverlaySafetyException(
                $"Cannot change click-through state because {owner} is unavailable.");
        }

        SendBool(window, "setIgnoresMouseEvents:", ignoresMouseEvents);
        MacOsClickThroughPolicy.RequireObservedState(
            ignoresMouseEvents,
            SendBoolResult(window, "ignoresMouseEvents"),
            owner);
    }

    private static bool RespondsToSelector(nint receiver, string selectorName) =>
        receiver != nint.Zero &&
        objc_msgSend_bool_pointer(
            receiver,
            sel_registerName("respondsToSelector:"),
            sel_registerName(selectorName)) != 0;

    private static string ReadClassName(nint value)
    {
        var objectClass = value == nint.Zero ? nint.Zero : object_getClass(value);
        return objectClass == nint.Zero
            ? "?"
            : Marshal.PtrToStringAnsi(class_getName(objectClass)) ?? "?";
    }

    private static nint ReadApplicationActivationPolicy()
    {
        var application = SendPointer(objc_getClass("NSApplication"), "sharedApplication");
        return application == nint.Zero
            ? new nint(-1)
            : SendIntegerResult(application, "activationPolicy");
    }

    private static bool ReadApplicationActive()
    {
        var application = SendPointer(objc_getClass("NSApplication"), "sharedApplication");
        return application != nint.Zero && SendBoolResult(application, "isActive");
    }

    private static nint ReadFrontmostApplicationProcessIdentifier()
    {
        var workspace = SendPointer(objc_getClass("NSWorkspace"), "sharedWorkspace");
        var application = SendPointer(workspace, "frontmostApplication");
        return application == nint.Zero
            ? nint.Zero
            : SendIntegerResult(application, "processIdentifier");
    }

    private void StartNativeAcceptance()
    {
        if (!_nativeAcceptanceMode || _acceptanceTimer is not null)
        {
            return;
        }

        _acceptanceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15d)
        };
        _acceptanceTimer.Tick += OnNativeAcceptanceTick;
        AdvanceNativeAcceptance();
        _acceptanceTimer.Start();
    }

    private void StopNativeAcceptance()
    {
        var timer = _acceptanceTimer;
        _acceptanceTimer = null;
        if (timer is not null)
        {
            timer.Stop();
            timer.Tick -= OnNativeAcceptanceTick;
        }
        _acceptanceProbeTimer?.Dispose();
        _acceptanceProbeTimer = null;
        if (_acceptanceProbeInFlight &&
            _avaloniaWindow != nint.Zero &&
            !_clickThrough)
        {
            // A cancellation between the two WindowServer observations must
            // never leave the production overlay intercepting input.
            SetClickThrough(true);
        }
        _acceptanceProbeInFlight = false;
        _acceptancePlacement = null;
        _acceptanceDisplayScale = 1d;
        _acceptanceScreenIndex = -1;
    }

    private void OnNativeAcceptanceTick(object? sender, EventArgs e) =>
        AdvanceNativeAcceptance();

    private void AdvanceNativeAcceptance()
    {
        if (_acceptanceProbeInFlight ||
            _avaloniaWindow == nint.Zero ||
            _windowOwner is not { } owner)
        {
            return;
        }

        var screens = ReadScreens()
            .OrderBy(screen => screen.Number)
            .ToArray();
        if (screens.Length == 0)
        {
            Console.Error.WriteLine(
                "[InfiniteLizards] native-acceptance: no NSScreen available");
            return;
        }

        _acceptanceScreenIndex = (_acceptanceScreenIndex + 1) % screens.Length;
        var target = screens[_acceptanceScreenIndex];
        var currentFrame = GetRectResult(_avaloniaWindow, "frame");
        var referenceFrame = GetCoordinateReferenceScreenFrame();
        var placement = CreateAcceptancePlacement(
            target,
            currentFrame,
            referenceFrame);
        var targetFrame = ToAppKitFrame(placement, referenceFrame);
        _acceptancePlacement = placement;
        // Exercise the exact production path: managed Avalonia placement,
        // top-left/Y-down SurfacePlacement conversion, screen-aware anchor
        // selection, and the final atomic AppKit frame update.
        PlaceSurface(owner, placement, _acceptanceDisplayScale);
        SetAndVerifyIgnoresMouseEvents(
            _panel,
            ignoresMouseEvents: true,
            "the native-acceptance anchor panel");
        SetAllSpacesCollectionBehavior();
        // Exercise both production click-through transitions on every screen.
        // WindowServer applies hit routing asynchronously, so each public
        // windowNumberAtPoint observation happens on a later run-loop turn;
        // the second phase leaves the non-activating surface click-through.
        var actualSurfaceFrame = GetRectResult(_avaloniaWindow, "frame");
        var hitTestPoint = new NativePoint(
            actualSurfaceFrame.CenterX,
            actualSurfaceFrame.CenterY);
        var surfaceWindowNumber = SendIntegerResult(_avaloniaWindow, "windowNumber");
        var anchorWindowNumber = SendIntegerResult(_panel, "windowNumber");
        SetClickThrough(false);
        _acceptanceProbeInFlight = true;
        ScheduleNativeAcceptanceProbe(() => ObserveInteractiveHitRouting(
            owner,
            target,
            placement,
            targetFrame,
            actualSurfaceFrame,
            hitTestPoint,
            surfaceWindowNumber,
            anchorWindowNumber,
            attemptNumber: 1));
    }

    private void ObserveInteractiveHitRouting(
        Window owner,
        NativeScreen target,
        SurfacePlacement placement,
        NativeRect targetFrame,
        NativeRect actualSurfaceFrame,
        NativePoint hitTestPoint,
        nint surfaceWindowNumber,
        nint anchorWindowNumber,
        int attemptNumber)
    {
        _acceptanceProbeTimer = null;
        if (!_acceptanceProbeInFlight ||
            _avaloniaWindow == nint.Zero ||
            _panel == nint.Zero ||
            !ReferenceEquals(owner, _windowOwner))
        {
            FinishNativeAcceptanceProbe();
            return;
        }

        var interactiveObserved = !SendBoolResult(
            _avaloniaWindow,
            "ignoresMouseEvents");
        var interactiveHitWindowNumber = ReadWindowNumberAtPoint(hitTestPoint);
        if (MacOsNativeAcceptanceRetryPolicy.ShouldRetryInteractiveHit(
                interactiveObserved,
                surfaceWindowNumber,
                interactiveHitWindowNumber,
                attemptNumber))
        {
            ScheduleNativeAcceptanceProbe(() => ObserveInteractiveHitRouting(
                owner,
                target,
                placement,
                targetFrame,
                actualSurfaceFrame,
                hitTestPoint,
                surfaceWindowNumber,
                anchorWindowNumber,
                checked(attemptNumber + 1)));
            return;
        }
        var canKeyWhileInteractive = SendBoolResult(
            _avaloniaWindow,
            "canBecomeKeyWindow");
        var canMainWhileInteractive = SendBoolResult(
            _avaloniaWindow,
            "canBecomeMainWindow");
        try
        {
            SetClickThrough(true);
        }
        catch
        {
            FinishNativeAcceptanceProbe();
            throw;
        }

        ScheduleNativeAcceptanceProbe(() => CompleteNativeAcceptance(
            owner,
            target,
            placement,
            targetFrame,
            actualSurfaceFrame,
            hitTestPoint,
            surfaceWindowNumber,
            anchorWindowNumber,
            interactiveObserved,
            interactiveHitWindowNumber,
            attemptNumber,
            canKeyWhileInteractive,
            canMainWhileInteractive));
    }

    private void CompleteNativeAcceptance(
        Window owner,
        NativeScreen target,
        SurfacePlacement placement,
        NativeRect targetFrame,
        NativeRect actualSurfaceFrame,
        NativePoint hitTestPoint,
        nint surfaceWindowNumber,
        nint anchorWindowNumber,
        bool interactiveObserved,
        nint interactiveHitWindowNumber,
        int interactiveHitAttempts,
        bool canKeyWhileInteractive,
        bool canMainWhileInteractive)
    {
        _acceptanceProbeTimer = null;
        if (!_acceptanceProbeInFlight ||
            _avaloniaWindow == nint.Zero ||
            _panel == nint.Zero ||
            !ReferenceEquals(owner, _windowOwner))
        {
            FinishNativeAcceptanceProbe();
            return;
        }

        try
        {
            var clickThroughObserved = SendBoolResult(
                _avaloniaWindow,
                "ignoresMouseEvents");
            var clickThroughHitWindowNumber = ReadWindowNumberAtPoint(hitTestPoint);
            var hitRoutingMatches =
                MacOsWindowHitRoutingPolicy.MatchesClickThroughTransition(
                    surfaceWindowNumber,
                    anchorWindowNumber,
                    interactiveHitWindowNumber,
                    clickThroughHitWindowNumber);
            var contentView = SendPointer(_avaloniaWindow, "contentView");
            var contentFrame = GetRectResult(contentView, "frame");
            var contentBacking = GetRectArgumentResult(
                contentView,
                "convertRectToBacking:",
                contentFrame);
            var actualBackingScale = ReadWindowBackingScale(_avaloniaWindow);
            var surfaceFrameMatches = RectsNearlyEqual(
                actualSurfaceFrame,
                targetFrame,
                tolerance: 0.01d);
            var managedPlacementMatches =
                owner.Position == new PixelPoint(placement.X, placement.Y) &&
                Math.Abs(owner.Width - placement.Width / _acceptanceDisplayScale) < 0.01d &&
                Math.Abs(owner.Height - placement.Height / _acceptanceDisplayScale) < 0.01d;
            var desiredLevel = OverlayWindowLevel();
            var desiredCollection = DesiredCollectionBehavior();
            var levelsMatch =
                SendIntegerResult(_avaloniaWindow, "level") == desiredLevel &&
                SendIntegerResult(_panel, "level") == desiredLevel;
            var collectionsMatch =
                SendUnsignedResult(_avaloniaWindow, "collectionBehavior") == desiredCollection &&
                SendUnsignedResult(_panel, "collectionBehavior") == desiredCollection;
            var subclassInstalled =
                _installedAvaloniaWindowClass != nint.Zero &&
                object_getClass(_avaloniaWindow) == _installedAvaloniaWindowClass;
            var activeSpaceWindowNumbers = ReadActiveSpaceWindowNumbers();
            var activeSpaceMembershipMatches =
                MacOsActiveSpaceMembershipPolicy.ContainsRequiredWindows(
                    surfaceWindowNumber,
                    anchorWindowNumber,
                    activeSpaceWindowNumbers);
            var backingMatches =
                Math.Abs(contentBacking.Size.Width - contentFrame.Size.Width * actualBackingScale) < 0.5d &&
                Math.Abs(contentBacking.Size.Height - contentFrame.Size.Height * actualBackingScale) < 0.5d;
            var currentProcessIdentifier = (nint)Environment.ProcessId;
            var frontmostProcessIdentifier =
                ReadFrontmostApplicationProcessIdentifier();
            var frontmostApplicationIsOther =
                MacOsFrontmostApplicationPolicy.IsOwnedByAnotherProcess(
                    currentProcessIdentifier,
                    frontmostProcessIdentifier);
            var applicationActiveDiagnostic = ReadApplicationActive();
            var acceptancePassed =
                ReadWindowScreenNumber(_avaloniaWindow) == target.Number &&
                ReadWindowScreenNumber(_panel) == target.Number &&
                Math.Abs(actualBackingScale - target.BackingScale) < 0.001d &&
                SendPointer(_avaloniaWindow, "parentWindow") == _panel &&
                SendPointer(contentView, "window") == _avaloniaWindow &&
                SendBoolResult(_avaloniaWindow, "isVisible") &&
                SendBoolResult(_panel, "isVisible") &&
                activeSpaceMembershipMatches &&
                !SendBoolResult(_avaloniaWindow, "isKeyWindow") &&
                !SendBoolResult(_avaloniaWindow, "isMainWindow") &&
                !canKeyWhileInteractive &&
                !canMainWhileInteractive &&
                !SendBoolResult(_avaloniaWindow, "canBecomeKeyWindow") &&
                !SendBoolResult(_avaloniaWindow, "canBecomeMainWindow") &&
                frontmostApplicationIsOther &&
                SendBoolResult(_panel, "ignoresMouseEvents") &&
                interactiveObserved &&
                clickThroughObserved &&
                hitRoutingMatches &&
                subclassInstalled &&
                !_activationPolicyFailedClosed &&
                surfaceFrameMatches &&
                managedPlacementMatches &&
                levelsMatch &&
                collectionsMatch &&
                backingMatches;
            Console.Error.WriteLine(
                $"[InfiniteLizards] native-acceptance " +
                $"status={(acceptancePassed ? "PASS" : "FAIL")}, " +
                $"targetScreen={target.Number}, " +
                $"targetBacking={target.BackingScale:F3}, " +
                $"surfacePlacement={placement}, targetFrame={targetFrame}, " +
                $"interactiveObserved={interactiveObserved}, " +
                $"interactiveHitWindow={interactiveHitWindowNumber}, " +
                $"interactiveHitAttempts={interactiveHitAttempts}, " +
                $"canKeyWhileInteractive={canKeyWhileInteractive}, " +
                $"canMainWhileInteractive={canMainWhileInteractive}, " +
                $"clickThroughObserved={clickThroughObserved}, " +
                $"clickThroughHitWindow={clickThroughHitWindowNumber}, " +
                $"hitRoutingMatches={hitRoutingMatches}, " +
                $"subclassInstalled={subclassInstalled}, " +
                $"activationFailClosed={_activationPolicyFailedClosed}, " +
                $"actualFrame={actualSurfaceFrame}, frameMatches={surfaceFrameMatches}, " +
                $"managedPlacementMatches={managedPlacementMatches}, " +
                $"desiredLevel={desiredLevel}, levelsMatch={levelsMatch}, " +
                $"desiredCollection=0x{desiredCollection:X}, " +
                $"collectionsMatch={collectionsMatch}, " +
                $"surfaceWindow={surfaceWindowNumber}, " +
                $"anchorWindow={anchorWindowNumber}, " +
                $"currentProcess={currentProcessIdentifier}, " +
                $"frontmostProcess={frontmostProcessIdentifier}, " +
                $"frontmostApplicationIsOther={frontmostApplicationIsOther}, " +
                $"applicationActiveDiagnostic={applicationActiveDiagnostic}, " +
                $"activeSpaceWindowListMatches={activeSpaceMembershipMatches}, " +
                $"surfaceActiveSpaceGetter={SendBoolResult(_avaloniaWindow, "isOnActiveSpace")}, " +
                $"anchorActiveSpaceGetter={SendBoolResult(_panel, "isOnActiveSpace")}, " +
                $"contentBacking={contentBacking}, backingMatches={backingMatches}");
            WriteWindowDiagnostic($"acceptance-screen-{target.Number}");
        }
        finally
        {
            FinishNativeAcceptanceProbe();
        }
    }

    private void ScheduleNativeAcceptanceProbe(Action action)
    {
        _acceptanceProbeTimer?.Dispose();
        _acceptanceProbeTimer = DispatcherTimer.RunOnce(
            action,
            TimeSpan.FromMilliseconds(100d));
    }

    private void FinishNativeAcceptanceProbe()
    {
        _acceptanceProbeTimer?.Dispose();
        _acceptanceProbeTimer = null;
        _acceptanceProbeInFlight = false;
    }

    private static nint ReadWindowNumberAtPoint(NativePoint point) =>
        objc_msgSend_integer_point_integer(
            objc_getClass("NSWindow"),
            sel_registerName("windowNumberAtPoint:belowWindowWithWindowNumber:"),
            point,
            nint.Zero);

    private static HashSet<nint> ReadActiveSpaceWindowNumbers()
    {
        // NSWindow's public list API is the authoritative current-Space
        // membership query. A child NSWindow's isOnActiveSpace getter can be
        // false on a secondary display even while WindowServer composites it;
        // keeping that getter in diagnostics is useful, but it must not turn a
        // visibly correct multi-display result into a false acceptance failure.
        var numbers = SendPointerUnsigned(
            objc_getClass("NSWindow"),
            "windowNumbersWithOptions:",
            WindowNumberListAllApplications);
        var count = numbers == nint.Zero
            ? 0u
            : SendUnsignedResult(numbers, "count");
        var result = new HashSet<nint>();
        for (nuint index = 0; index < count; index++)
        {
            var number = SendPointerUnsigned(numbers, "objectAtIndex:", index);
            if (number != nint.Zero)
            {
                result.Add(SendIntegerResult(number, "integerValue"));
            }
        }
        return result;
    }

    private static bool ReadEnvironmentSwitch(string name) =>
        string.Equals(
            Environment.GetEnvironmentVariable(name),
            "1",
            StringComparison.Ordinal);

    private static bool RectsNearlyEqual(
        NativeRect left,
        NativeRect right,
        double tolerance) =>
        Math.Abs(left.Origin.X - right.Origin.X) <= tolerance &&
        Math.Abs(left.Origin.Y - right.Origin.Y) <= tolerance &&
        Math.Abs(left.Size.Width - right.Size.Width) <= tolerance &&
        Math.Abs(left.Size.Height - right.Size.Height) <= tolerance;

    private static NativeRect ToAppKitFrame(
        SurfacePlacement placement,
        NativeRect coordinateReferenceFrame)
    {
        if (placement.Width <= 0 || placement.Height <= 0 ||
            !double.IsFinite(coordinateReferenceFrame.Top))
        {
            throw new ArgumentOutOfRangeException(
                nameof(placement),
                "A macOS surface placement and its coordinate reference must be valid.");
        }

        return new NativeRect(
            new NativePoint(
                placement.X,
                coordinateReferenceFrame.Top - placement.Y - placement.Height),
            new NativeSize(placement.Width, placement.Height));
    }

    private static SurfacePlacement CreateAcceptancePlacement(
        NativeScreen target,
        NativeRect currentFrame,
        NativeRect coordinateReferenceFrame)
    {
        if (!double.IsFinite(target.Frame.Size.Width) ||
            !double.IsFinite(target.Frame.Size.Height) ||
            target.Frame.Size.Width < 1d ||
            target.Frame.Size.Height < 1d)
        {
            throw new NativeOverlaySafetyException(
                "Native acceptance cannot target an invalid NSScreen frame.");
        }

        var width = CheckedPositiveDimension(Math.Min(
            Math.Max(1d, Math.Floor(currentFrame.Size.Width)),
            Math.Floor(target.Frame.Size.Width)));
        var height = CheckedPositiveDimension(Math.Min(
            Math.Max(1d, Math.Floor(currentFrame.Size.Height)),
            Math.Floor(target.Frame.Size.Height)));

        var minimumX = Math.Ceiling(target.Frame.Left);
        var maximumX = Math.Floor(target.Frame.Right - width);
        var desiredX = Math.Round(
            target.Frame.CenterX - width * 0.5d,
            MidpointRounding.AwayFromZero);

        var deviceTop = coordinateReferenceFrame.Top - target.Frame.Top;
        var deviceBottom = coordinateReferenceFrame.Top - target.Frame.Bottom;
        var minimumY = Math.Ceiling(deviceTop);
        var maximumY = Math.Floor(deviceBottom - height);
        var desiredY = Math.Round(
            (deviceTop + deviceBottom - height) * 0.5d,
            MidpointRounding.AwayFromZero);
        if (maximumX < minimumX || maximumY < minimumY)
        {
            throw new NativeOverlaySafetyException(
                "Native acceptance could not construct an integral placement inside NSScreen.");
        }

        var placement = new SurfacePlacement(
            CheckedCoordinate(Math.Clamp(desiredX, minimumX, maximumX)),
            CheckedCoordinate(Math.Clamp(desiredY, minimumY, maximumY)),
            width,
            height);
        var nativeFrame = ToAppKitFrame(placement, coordinateReferenceFrame);
        if (!target.Frame.Contains(nativeFrame))
        {
            throw new NativeOverlaySafetyException(
                "The normal SurfacePlacement conversion escaped its target NSScreen.");
        }
        return placement;
    }

    private static int CheckedPositiveDimension(double value)
    {
        var result = CheckedCoordinate(value);
        return result > 0
            ? result
            : throw new NativeOverlaySafetyException(
                "Native acceptance produced a non-positive surface dimension.");
    }

    private static int CheckedCoordinate(double value)
    {
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
        {
            throw new NativeOverlaySafetyException(
                "A native-acceptance placement exceeds Avalonia PixelPoint limits.");
        }
        return checked((int)value);
    }

    private static NativeScreen FindScreenForFrame(NativeRect frame)
    {
        var screens = ReadScreens();
        if (screens.Count == 0)
        {
            throw new InvalidOperationException("AppKit did not report a display.");
        }

        var center = new NativePoint(frame.CenterX, frame.CenterY);
        NativeScreen best = default;
        var bestDistanceSquared = double.PositiveInfinity;
        foreach (var candidate in screens)
        {
            if (candidate.Frame.Contains(center))
            {
                return candidate;
            }

            var nearestX = Math.Clamp(center.X, candidate.Frame.Left, candidate.Frame.Right);
            var nearestY = Math.Clamp(center.Y, candidate.Frame.Bottom, candidate.Frame.Top);
            var dx = center.X - nearestX;
            var dy = center.Y - nearestY;
            var distanceSquared = dx * dx + dy * dy;
            if (distanceSquared < bestDistanceSquared)
            {
                best = candidate;
                bestDistanceSquared = distanceSquared;
            }
        }

        return best;
    }

    private static IReadOnlyList<NativeScreen> ReadScreens()
    {
        var screens = SendPointer(objc_getClass("NSScreen"), "screens");
        var count = screens == nint.Zero
            ? 0u
            : SendUnsignedResult(screens, "count");
        var result = new List<NativeScreen>(checked((int)count));
        for (nuint index = 0; index < count; index++)
        {
            var screen = SendPointerUnsigned(screens, "objectAtIndex:", index);
            result.Add(new NativeScreen(
                screen,
                ReadScreenNumber(screen),
                GetRectResult(screen, "frame"),
                SendDoubleResult(screen, "backingScaleFactor")));
        }
        return result;
    }

    private static nuint ReadWindowScreenNumber(nint window) =>
        window == nint.Zero
            ? 0u
            : ReadScreenNumber(SendPointer(window, "screen"));

    private static double ReadWindowBackingScale(nint window) =>
        window == nint.Zero ? 0d : SendDoubleResult(window, "backingScaleFactor");

    private static nuint ReadScreenNumber(nint screen)
    {
        if (screen == nint.Zero)
        {
            return 0u;
        }

        var description = SendPointer(screen, "deviceDescription");
        var key = SendPointerUtf8(
            objc_getClass("NSString"),
            "stringWithUTF8String:",
            "NSScreenNumber");
        var number = SendPointerArgumentResult(description, "objectForKey:", key);
        return number == nint.Zero
            ? unchecked((nuint)screen)
            : SendUnsignedResult(number, "unsignedIntegerValue");
    }

    private static NativeRect GetRectResult(nint receiver, string selectorName)
    {
        var selector = sel_registerName(selectorName);
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            objc_msgSend_rect_stret(out var result, receiver, selector);
            return result;
        }
        return objc_msgSend_rect_result(receiver, selector);
    }

    private static NativeRect GetRectArgumentResult(
        nint receiver,
        string selectorName,
        NativeRect value)
    {
        var selector = sel_registerName(selectorName);
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            objc_msgSend_rect_rect_stret(out var result, receiver, selector, value);
            return result;
        }
        return objc_msgSend_rect_rect_result(receiver, selector, value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CgPoint(double X, double Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(double X, double Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeSize(double Width, double Height);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRect(NativePoint Origin, NativeSize Size)
    {
        public double Left => Origin.X;

        public double Bottom => Origin.Y;

        public double Right => Origin.X + Size.Width;

        public double Top => Origin.Y + Size.Height;

        public double CenterX => Origin.X + Size.Width * 0.5d;

        public double CenterY => Origin.Y + Size.Height * 0.5d;

        public bool Contains(NativePoint point) =>
            point.X >= Left && point.X < Right &&
            point.Y >= Bottom && point.Y < Top;

        public bool Contains(NativeRect rect) =>
            rect.Left >= Left && rect.Right <= Right &&
            rect.Bottom >= Bottom && rect.Top <= Top;
    }

    private readonly record struct NativeScreen(
        nint Handle,
        nuint Number,
        NativeRect Frame,
        double BackingScale);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte WindowEligibilityCallback(nint self, nint selector);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint sel_registerName(string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint objc_getClass(string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint object_getClass(nint value);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint class_getName(nint cls);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint class_getSuperclass(nint cls);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint objc_allocateClassPair(
        nint superclass,
        string name,
        nuint extraBytes);

    [DllImport(ObjectiveCLibrary)]
    private static extern byte class_addMethod(
        nint cls,
        nint selector,
        nint implementation,
        string types);

    [DllImport(ObjectiveCLibrary)]
    private static extern void objc_registerClassPair(nint cls);

    [DllImport(ObjectiveCLibrary)]
    private static extern void objc_disposeClassPair(nint cls);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint object_setClass(nint value, nint cls);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_bool(nint receiver, nint selector, byte value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern byte objc_msgSend_bool_result(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern byte objc_msgSend_bool_pointer(
        nint receiver,
        nint selector,
        nint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_integer(nint receiver, nint selector, nint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_integer_result(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_integer_point_integer(
        nint receiver,
        nint selector,
        NativePoint point,
        nint windowNumber);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern double objc_msgSend_double_result(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_unsigned(nint receiver, nint selector, nuint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nuint objc_msgSend_unsigned_result(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_pointer(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_pointer_argument(
        nint receiver,
        nint selector,
        nint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_pointer_argument_result(
        nint receiver,
        nint selector,
        nint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_pointer_utf8(
        nint receiver,
        nint selector,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_pointer_integer_argument(
        nint receiver,
        nint selector,
        nint pointer,
        nint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_pointer_unsigned(
        nint receiver,
        nint selector,
        nuint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_init_rect(
        nint receiver,
        nint selector,
        NativeRect contentRect,
        nuint styleMask,
        nuint backingStoreType,
        byte defer);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_rect_bool(
        nint receiver,
        nint selector,
        NativeRect value,
        byte flag);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern NativeRect objc_msgSend_rect_result(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern NativeRect objc_msgSend_rect_rect_result(
        nint receiver,
        nint selector,
        NativeRect value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend_stret")]
    private static extern void objc_msgSend_rect_stret(
        out NativeRect result,
        nint receiver,
        nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend_stret")]
    private static extern void objc_msgSend_rect_rect_stret(
        out NativeRect result,
        nint receiver,
        nint selector,
        NativeRect value);

    [DllImport(CoreGraphicsFramework)]
    private static extern nint CGEventCreate(nint source);

    [DllImport(CoreGraphicsFramework)]
    private static extern CgPoint CGEventGetLocation(nint mouseEvent);

    [DllImport(CoreGraphicsFramework)]
    private static extern int CGWindowLevelForKey(int key);

    [DllImport(CoreGraphicsFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGEventSourceButtonState(int stateId, uint button);

    [DllImport(CoreFoundationFramework)]
    private static extern void CFRelease(nint value);
}
