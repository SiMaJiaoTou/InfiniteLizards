using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace InfiniteLizards.Desktop;

/// <summary>
/// Applies the small native contract that Avalonia's managed window flags
/// cannot completely express for the diagnostic sidecar: it remains above
/// normal windows and accepts pointer input without taking application focus.
/// This behavior is intentionally diagnostic-only and never participates in
/// the desktop-pet render or input path.
/// </summary>
internal sealed class DiagnosticPanelNativeBehavior : IDisposable
{
    private const int GwlExStyle = -20;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly nint HwndTopmost = new(-1);

    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private const string CoreGraphicsFramework =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string NonactivatingWindowClassName =
        "InfiniteLizardsDiagnosticNonactivatingAvnWindow";
    private const nuint CanJoinAllSpaces = 1u << 0;
    private const nuint IgnoresCycle = 1u << 6;
    private const nuint FullScreenAuxiliary = 1u << 8;
    private const nuint CanJoinAllApplications = 1u << 18;
    private const int MainMenuWindowLevelKey = 8;
    private const int StatusWindowLevelKey = 9;

    // Objective-C keeps this IMP after class registration, so the managed
    // delegate must remain rooted for the lifetime of the process.
    private static readonly WindowEligibilityCallback RejectWindowEligibility =
        static (_, _) => 0;

    private Window? _window;
    private Win32Properties.CustomWindowStylesCallback? _windowStylesCallback;
    private Win32Properties.CustomWndProcHookCallback? _wndProcHookCallback;
    private bool _stylesCallbackInstalled;
    private bool _wndProcHookInstalled;
    private nint _retainedMacWindow;
    private nint _originalMacWindowClass;
    private nint _installedMacWindowClass;
    private bool _disposed;

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_window is not null)
        {
            throw new InvalidOperationException(
                "The diagnostic-panel native behavior is already attached.");
        }

        _window = window;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                AttachWindows(window);
            }
            else if (OperatingSystem.IsMacOS())
            {
                AttachMacOS(window);
            }
        }
        catch (Exception exception)
        {
            // The panel is a developer aid. A native-policy failure must not
            // terminate or alter the production pet engine/runtime.
            Trace.WriteLine(
                $"[InfiniteLizards] diagnostic panel native behavior was not applied: {exception}");
            ReleaseNativeState();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseNativeState();
        _window = null;
    }

    internal static uint ComposeWindowsExtendedStyle(uint existing) =>
        existing | WsExTopmost | WsExToolWindow | WsExNoActivate;

    internal static bool WindowsExtendedStyleMatches(uint actual)
    {
        var required = WsExTopmost | WsExToolWindow | WsExNoActivate;
        return (actual & required) == required;
    }

    internal static nuint DesiredMacCollectionBehavior() =>
        CanJoinAllSpaces |
        IgnoresCycle |
        FullScreenAuxiliary |
        (OperatingSystem.IsMacOSVersionAtLeast(13) ? CanJoinAllApplications : 0u);

    private void AttachWindows(Window window)
    {
        _windowStylesCallback = ApplyWindowsStyles;
        _wndProcHookCallback = WindowsWndProcHook;

        Win32Properties.AddWindowStylesCallback(window, _windowStylesCallback);
        _stylesCallbackInstalled = true;
        Win32Properties.AddWndProcHookCallback(window, _wndProcHookCallback);
        _wndProcHookInstalled = true;

        var hwnd = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (hwnd == nint.Zero || !IsWindow(hwnd))
        {
            throw new InvalidOperationException(
                "Avalonia did not expose a valid diagnostic-panel HWND after Opened.");
        }

        var current = unchecked((uint)GetWindowLongPtrChecked(hwnd, GwlExStyle).ToInt64());
        var desired = ComposeWindowsExtendedStyle(current);
        if (desired != current)
        {
            _ = SetWindowLongPtrChecked(hwnd, GwlExStyle, new nint(desired));
        }

        if (!SetWindowPos(
                hwnd,
                HwndTopmost,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var observed = unchecked((uint)GetWindowLongPtrChecked(hwnd, GwlExStyle).ToInt64());
        if (!WindowsExtendedStyleMatches(observed))
        {
            throw new InvalidOperationException(
                $"The diagnostic-panel HWND rejected its non-activating tool-window styles: " +
                $"GWL_EXSTYLE=0x{observed:X8}.");
        }
    }

    private static (uint style, uint exStyle) ApplyWindowsStyles(
        uint style,
        uint exStyle) =>
        (style, ComposeWindowsExtendedStyle(exStyle));

    private static nint WindowsWndProcHook(
        nint window,
        uint message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == WmMouseActivate)
        {
            // MA_NOACTIVATE still dispatches the pointer event to Avalonia, so
            // the action buttons remain clickable without changing foreground.
            handled = true;
            return new nint(MaNoActivate);
        }

        return nint.Zero;
    }

    private void AttachMacOS(Window window)
    {
        var platformHandle = window.TryGetPlatformHandle();
        var nativeWindow = platformHandle is IMacOSTopLevelPlatformHandle macHandle
            ? macHandle.NSWindow
            : nint.Zero;
        if (nativeWindow == nint.Zero)
        {
            throw new InvalidOperationException(
                "Avalonia did not expose a diagnostic-panel NSWindow after Opened.");
        }

        _retainedMacWindow = SendPointer(nativeWindow, "retain");
        if (_retainedMacWindow == nint.Zero)
        {
            throw new InvalidOperationException(
                "AppKit could not retain the diagnostic-panel NSWindow.");
        }

        InstallMacNonactivatingSubclass(nativeWindow);

        // Keep Avalonia's AvnWindow/contentView ownership intact. Only native
        // eligibility, collection and ordering policy are changed here.
        if (RespondsToSelector(nativeWindow, "setCanBecomeKeyWindow:"))
        {
            SendBool(nativeWindow, "setCanBecomeKeyWindow:", false);
        }
        SendBool(nativeWindow, "setIgnoresMouseEvents:", false);
        if (RespondsToSelector(nativeWindow, "setCanHide:"))
        {
            SendBool(nativeWindow, "setCanHide:", false);
        }
        if (RespondsToSelector(nativeWindow, "setExcludedFromWindowsMenu:"))
        {
            SendBool(nativeWindow, "setExcludedFromWindowsMenu:", true);
        }

        var desiredCollection = DesiredMacCollectionBehavior();
        var desiredLevel = OverlayWindowLevel();
        SendUnsigned(nativeWindow, "setCollectionBehavior:", desiredCollection);
        SendInteger(nativeWindow, "setLevel:", desiredLevel);
        SendVoid(nativeWindow, "orderFrontRegardless");

        var observedCollection = SendUnsignedResult(nativeWindow, "collectionBehavior");
        var observedLevel = SendIntegerResult(nativeWindow, "level");
        var verified =
            object_getClass(nativeWindow) == _installedMacWindowClass &&
            !SendBoolResult(nativeWindow, "canBecomeKeyWindow") &&
            !SendBoolResult(nativeWindow, "canBecomeMainWindow") &&
            !SendBoolResult(nativeWindow, "ignoresMouseEvents") &&
            (observedCollection & desiredCollection) == desiredCollection &&
            observedLevel == desiredLevel;
        if (!verified)
        {
            throw new InvalidOperationException(
                "AppKit did not verify the diagnostic panel as interactive, " +
                "non-key, non-main, all-Spaces and topmost.");
        }
    }

    private void InstallMacNonactivatingSubclass(nint nativeWindow)
    {
        var originalClass = object_getClass(nativeWindow);
        if (originalClass == nint.Zero)
        {
            throw new InvalidOperationException(
                "Objective-C did not report the diagnostic AvnWindow class.");
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
                    "Objective-C could not allocate the diagnostic AvnWindow subclass.");
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
                    "Objective-C could not install diagnostic window eligibility overrides.");
            }
            objc_registerClassPair(subclass);
        }
        else if (class_getSuperclass(subclass) != originalClass)
        {
            throw new InvalidOperationException(
                "The registered diagnostic AvnWindow subclass has an unexpected superclass.");
        }

        var previousClass = object_setClass(nativeWindow, subclass);
        if (previousClass != originalClass)
        {
            _ = object_setClass(nativeWindow, previousClass);
            throw new InvalidOperationException(
                "The diagnostic NSWindow class changed while native behavior was attaching.");
        }

        _originalMacWindowClass = originalClass;
        _installedMacWindowClass = subclass;
    }

    private void ReleaseNativeState()
    {
        var window = _window;
        if (window is not null)
        {
            try
            {
                if (_wndProcHookInstalled && _wndProcHookCallback is not null)
                {
                    Win32Properties.RemoveWndProcHookCallback(window, _wndProcHookCallback);
                }
                if (_stylesCallbackInstalled && _windowStylesCallback is not null)
                {
                    Win32Properties.RemoveWindowStylesCallback(window, _windowStylesCallback);
                }
            }
            catch (Exception exception)
            {
                Trace.WriteLine(
                    $"[InfiniteLizards] diagnostic Win32 callback cleanup failed: {exception}");
            }
        }

        _wndProcHookInstalled = false;
        _stylesCallbackInstalled = false;
        _wndProcHookCallback = null;
        _windowStylesCallback = null;

        var nativeWindow = _retainedMacWindow;
        var originalClass = _originalMacWindowClass;
        var installedClass = _installedMacWindowClass;
        _retainedMacWindow = nint.Zero;
        _originalMacWindowClass = nint.Zero;
        _installedMacWindowClass = nint.Zero;
        if (nativeWindow == nint.Zero)
        {
            return;
        }

        try
        {
            if (originalClass != nint.Zero &&
                installedClass != nint.Zero &&
                object_getClass(nativeWindow) == installedClass)
            {
                _ = object_setClass(nativeWindow, originalClass);
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine(
                $"[InfiniteLizards] diagnostic AppKit class cleanup failed: {exception}");
        }
        finally
        {
            SendVoid(nativeWindow, "release");
        }
    }

    private static bool RespondsToSelector(nint receiver, string selector) =>
        objc_msgSend_bool_pointer(
            receiver,
            sel_registerName("respondsToSelector:"),
            sel_registerName(selector)) != 0;

    private static void SendBool(nint receiver, string selector, bool value) =>
        objc_msgSend_bool(receiver, sel_registerName(selector), value ? (byte)1 : (byte)0);

    private static bool SendBoolResult(nint receiver, string selector) =>
        objc_msgSend_bool_result(receiver, sel_registerName(selector)) != 0;

    private static void SendInteger(nint receiver, string selector, nint value) =>
        objc_msgSend_integer(receiver, sel_registerName(selector), value);

    private static nint SendIntegerResult(nint receiver, string selector) =>
        objc_msgSend_integer_result(receiver, sel_registerName(selector));

    private static void SendUnsigned(nint receiver, string selector, nuint value) =>
        objc_msgSend_unsigned(receiver, sel_registerName(selector), value);

    private static nuint SendUnsignedResult(nint receiver, string selector) =>
        objc_msgSend_unsigned_result(receiver, sel_registerName(selector));

    private static nint SendPointer(nint receiver, string selector) =>
        objc_msgSend_pointer(receiver, sel_registerName(selector));

    private static void SendVoid(nint receiver, string selector) =>
        objc_msgSend_void(receiver, sel_registerName(selector));

    private static nint OverlayWindowLevel() => new(Math.Max(
        CGWindowLevelForKey(MainMenuWindowLevelKey),
        CGWindowLevelForKey(StatusWindowLevelKey)));

    private static nint GetWindowLongPtrChecked(nint window, int index)
    {
        Marshal.SetLastPInvokeError(0);
        var result = GetWindowLongPtr(window, index);
        var error = Marshal.GetLastPInvokeError();
        if (result == nint.Zero && error != 0)
        {
            throw new Win32Exception(error);
        }
        return result;
    }

    private static nint SetWindowLongPtrChecked(nint window, int index, nint value)
    {
        Marshal.SetLastPInvokeError(0);
        var result = SetWindowLongPtr(window, index, value);
        var error = Marshal.GetLastPInvokeError();
        if (result == nint.Zero && error != 0)
        {
            throw new Win32Exception(error);
        }
        return result;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte WindowEligibilityCallback(nint self, nint selector);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint sel_registerName(string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint objc_getClass(string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint object_getClass(nint value);

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
    private static extern void objc_msgSend_unsigned(nint receiver, nint selector, nuint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nuint objc_msgSend_unsigned_result(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_pointer(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(nint receiver, nint selector);

    [DllImport(CoreGraphicsFramework)]
    private static extern int CGWindowLevelForKey(int key);
}
