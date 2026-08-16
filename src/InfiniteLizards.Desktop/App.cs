using System.Diagnostics;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using DesktopPet.Engine;
using DesktopLizard.Core;
using InfiniteLizards.Desktop.Rendering;
using InfiniteLizards.Gameplay;

namespace InfiniteLizards.Desktop;

internal sealed class App : Application
{
    private Mutex? _singleInstanceMutex;

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var options = AppOptions.Parse(desktop.Args ?? []);
            if (!options.AllowMultipleInstances && !TryAcquireSingleInstance())
            {
                desktop.Shutdown();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            ConfigurationLoadResult loadResult;
            try
            {
                loadResult = LizardConfigurationStore.LoadOrCreate(options.ConfigurationPath);
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
            {
                Trace.WriteLine(
                    $"[InfiniteLizards] invalid configuration path; using the default: {exception.Message}");
                loadResult = LizardConfigurationStore.LoadOrCreate();
            }
            foreach (var warning in loadResult.Warnings)
            {
                Trace.WriteLine($"[InfiniteLizards] {warning}");
            }

            var profile = LizardConfigurationStore.ResolveProfile(loadResult);
            var behaviorSeed = unchecked(
                (loadResult.Configuration.IndividualSeed ?? profile.Traits.Seed) ^
                (int)0x4C495A41);
            var game = new LizardGameModule(profile, behaviorSeed);
            var runtime = new DesktopPetRuntime<LizardRenderFrame>(game);
            var presenter = new LizardView(profile, runtime.CurrentSnapshot);
            var debugSettings = new DesktopPetDebugSettings(
                profile.Runtime.DebugPanelWidthPixels,
                profile.Runtime.DebugPanelHeightPixels,
                profile.Runtime.DebugPanelGapPixels,
                profile.Runtime.DebugFpsResponse,
                profile.Runtime.DebugMaximumFps);
            var petWindow = new PetWindow<LizardRenderFrame>(
                runtime,
                presenter,
                loadResult.Path,
                options.DiagnosticMode,
                options.DiagnosticMode || options.DebugOverlayMode,
                debugSettings);
            var diagnosticPanel = new DiagnosticPanelWindow(petWindow);
            _ = new LizardDebugPanelController(
                game.DebugBridge,
                petWindow,
                diagnosticPanel,
                presenter);
            petWindow.Opened += (_, _) => diagnosticPanel.ShowForOwner(petWindow);
            petWindow.PrepareForShow();
            desktop.MainWindow = petWindow;
            desktop.Exit += (_, _) => ReleaseSingleInstance();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private bool TryAcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            "InfiniteLizards.Desktop.SingleInstance",
            out var isFirstInstance);
        return isFirstInstance;
    }

    private void ReleaseSingleInstance()
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
    }
}

internal sealed record AppOptions(
    string? ConfigurationPath,
    bool DiagnosticMode,
    bool DebugOverlayMode,
    bool AllowMultipleInstances)
{
    public static AppOptions Parse(IEnumerable<string> arguments)
    {
        string? configurationPath = null;
        var diagnostic = false;
        var debugOverlay = false;
        var multiInstance = false;
        foreach (var argument in arguments)
        {
            if (argument.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
            {
                configurationPath = argument["--config=".Length..].Trim();
            }
            else if (string.Equals(argument, "--diagnostic", StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = true;
            }
            else if (string.Equals(argument, "--debug-overlay", StringComparison.OrdinalIgnoreCase))
            {
                debugOverlay = true;
            }
            else if (string.Equals(argument, "--multi-instance", StringComparison.OrdinalIgnoreCase))
            {
                multiInstance = true;
            }
        }

        return new AppOptions(
            string.IsNullOrWhiteSpace(configurationPath) ? null : configurationPath,
            diagnostic,
            debugOverlay,
            multiInstance);
    }
}
