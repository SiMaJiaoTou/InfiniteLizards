using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DesktopLizard.Services;

namespace DesktopLizard.Windows;

internal sealed partial class PetWindow
{
    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging || !_view.IsPointOnLizard(e.GetPosition(_view)))
        {
            return;
        }

        var cursor = NativeMethods.CursorPosition;
        _dragOffset = cursor - _simulation.Position;
        _isDragging = true;
        SetClickThrough(false);
        if (!Mouse.Capture(_view, CaptureMode.Element))
        {
            _isDragging = false;
            UpdateClickThrough();
            return;
        }

        ClearDebugPath();
        _simulation.BeginGrab(_view.ViewToModel(e.GetPosition(_view)));
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var cursor = NativeMethods.CursorPosition;
        _simulation.DragTo(cursor - _dragOffset);
        // Do not move the HWND from the raw MouseMove event. At high mouse
        // polling rates this used to move the rendered pose several times
        // before the 120 Hz dangling rig had consumed the same displacement;
        // the next render then snapped every free joint back into world space.
        // AdvanceFrame now solves the rig first and moves the window once at
        // the end of that same display frame, keeping both updates atomic.
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            FinishDrag(false);
            e.Handled = true;
        }
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            FinishDrag(true);
        }
    }

    private void FinishDrag(bool captureAlreadyLost)
    {
        _isDragging = false;
        ClearDebugPath();
        if (!captureAlreadyLost && Mouse.Captured == _view)
        {
            Mouse.Capture(null);
        }

        var cursor = NativeMethods.CursorPosition;
        _activeWorkArea = NativeMethods.GetWorkingArea(cursor);
        // The normal navigation radius follows the visible animal, but the
        // short release recovery still renders the full dangling pose inside
        // the larger transparent HWND. Pull that surface fully onto the active
        // monitor before release so neither the body nor its shadow is clipped
        // by the physical screen edge.
        var releaseArea = SafeInset(ToFloatRect(_activeWorkArea), _releaseRenderRadiusPixels);
        var settledPosition = releaseArea.Clamp(_simulation.Position);
        // MouseMove updates the behavior immediately, while the render loop
        // owns the previous-position sample. Synchronize the sample before
        // leaving Grabbed so the final drag delta is not consumed a second
        // time by the first ReleaseSettle simulation step.
        _simulation.EndGrab(settledPosition);
        SetPetWindowCenter(settledPosition);
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // A captured left-button drag owns the interaction until release.
        // Opening the menu here could issue pause/center commands halfway
        // through Grabbed and split the release sequence.
        if (_isDragging || !_view.IsPointOnLizard(e.GetPosition(_view)))
        {
            return;
        }

        _pauseItem.IsChecked = _simulation.IsPaused;
        _pauseItem.Header = _simulation.IsPaused ? "继续散步" : "暂停散步";
        _autostartItem.IsChecked = AutostartService.IsEnabled();
        _debugItem.IsChecked = _debugOverlayEnabled;
        _contextMenuOpen = true;
        SetClickThrough(false);
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;
        e.Handled = true;
    }

    private (ContextMenu Menu, MenuItem Pause, MenuItem Autostart, MenuItem Debug) CreateContextMenu()
    {
        var menu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint,
            StaysOpen = false
        };

        var pause = new MenuItem { Header = "暂停散步", IsCheckable = true };
        pause.Click += (_, _) =>
        {
            var isPaused = _simulation.TogglePaused();
            pause.IsChecked = isPaused;
            pause.Header = isPaused ? "继续散步" : "暂停散步";
        };

        var autostart = new MenuItem { Header = "随 Windows 启动", IsCheckable = true };
        autostart.Click += (_, _) =>
        {
            var requested = !AutostartService.IsEnabled();
            if (AutostartService.SetEnabled(requested))
            {
                autostart.IsChecked = requested;
            }
        };

        var debug = new MenuItem
        {
            Header = "显示调试信息",
            IsCheckable = true,
            IsChecked = _debugOverlayEnabled
        };
        debug.Click += (_, _) => SetDebugOverlay(!_debugOverlayEnabled);

        var center = new MenuItem { Header = "回到屏幕中央" };
        center.Click += (_, _) =>
        {
            var area = ToFloatRect(_activeWorkArea);
            _simulation.MoveToCenter(area.Center);
            ClearDebugPath();
        };

        var editConfiguration = new MenuItem
        {
            Header = "编辑个体配置（重启后生效）",
            IsEnabled = !string.IsNullOrWhiteSpace(_configurationPath)
        };
        editConfiguration.Click += (_, _) => OpenConfigurationFile();

        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Application.Current.Shutdown();

        menu.Items.Add(pause);
        menu.Items.Add(autostart);
        menu.Items.Add(debug);
        menu.Items.Add(new Separator());
        menu.Items.Add(center);
        menu.Items.Add(editConfiguration);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        menu.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            UpdateClickThrough();
        };

        return (menu, pause, autostart, debug);
    }

    private void OpenConfigurationFile()
    {
        if (string.IsNullOrWhiteSpace(_configurationPath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_configurationPath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[DesktopLizard] 无法打开配置文件：{exception.Message}");
        }
    }
}
