using Microsoft.Win32;

namespace DesktopLizard.Services;

internal static class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopLizard";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (enabled)
            {
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --autostart", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
