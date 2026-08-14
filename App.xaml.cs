using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DesktopLizard.Core;
using DesktopLizard.Diagnostics;
using DesktopLizard.Windows;

namespace DesktopLizard;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        if (DiagnosticCommandRunner.TryRun(e.Args, out var diagnosticExitCode))
        {
            Environment.ExitCode = diagnosticExitCode;
            Shutdown();
            return;
        }

        var diagnosticMode =
            e.Args.Any(arg => string.Equals(arg, "--diagnostic", StringComparison.OrdinalIgnoreCase)) ||
            (Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? string.Empty)
                .Contains("Diagnostic", StringComparison.OrdinalIgnoreCase);
        var debugOverlayEnabled = diagnosticMode || e.Args.Any(
            arg => string.Equals(arg, "--debug-overlay", StringComparison.OrdinalIgnoreCase));
        var multiInstance = e.Args.Any(
            arg => string.Equals(arg, "--multi-instance", StringComparison.OrdinalIgnoreCase));
        if (!multiInstance)
        {
            var mutexName = diagnosticMode
                ? @"Local\DesktopLizard.DiagnosticInstance"
                : @"Local\DesktopLizard.SingleInstance";
            _singleInstanceMutex = new Mutex(true, mutexName, out var isFirstInstance);
            if (!isFirstInstance)
            {
                Shutdown();
                return;
            }
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var configurationPath = GetConfigurationPath(e.Args);
        ConfigurationLoadResult loadResult;
        try
        {
            loadResult = LizardConfigurationStore.LoadOrCreate(configurationPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            WriteConfigurationWarnings(
                configurationPath ?? "<default>",
                [$"配置路径无效，将使用默认路径：{exception.Message}"]);
            loadResult = LizardConfigurationStore.LoadOrCreate();
        }

        WriteConfigurationWarnings(loadResult.Path, loadResult.Warnings);
        var profile = LizardConfigurationStore.ResolveProfile(loadResult);
        Debug.WriteLine(
            $"[DesktopLizard] profile={profile.Traits.Id} ({profile.Traits.Name}), config={loadResult.Path}");

        var window = new PetWindow(
            profile,
            loadResult.Path,
            diagnosticMode,
            debugOverlayEnabled);
        MainWindow = window;
        window.Show();
    }

    private static string? GetConfigurationPath(IEnumerable<string> arguments)
    {
        const string prefix = "--config=";
        string? path = null;
        foreach (var argument in arguments)
        {
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var candidate = argument[prefix.Length..].Trim();
                path = string.IsNullOrWhiteSpace(candidate) ? null : candidate;
            }
        }
        return path;
    }

    private static void WriteConfigurationWarnings(
        string configurationPath,
        IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return;
        }

        var message = string.Join(
            Environment.NewLine,
            warnings.Select(warning =>
                $"[{DateTimeOffset.Now:O}] 配置 {configurationPath}: {warning}"));
        Debug.WriteLine(message);
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopLizard");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "error.log"),
                message + Environment.NewLine + Environment.NewLine);
        }
        catch
        {
            // Configuration fallback already succeeded; logging must not make
            // the desktop pet unavailable.
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopLizard");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "error.log"),
                $"[{DateTimeOffset.Now:O}] {e.Exception}\n\n");
        }
        catch
        {
            // A desktop pet should fail quietly if even logging is unavailable.
        }

        e.Handled = true;
        Current.Shutdown(1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
