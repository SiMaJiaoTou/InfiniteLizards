using System.Diagnostics;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using DesktopPet.Engine;
using DesktopLizard.Core;
using InfiniteLizards.Desktop.Management;
using InfiniteLizards.Desktop.Persistence;
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

            var individualSeed = loadResult.Configuration.IndividualSeed ?? 0;
            var behaviorSeed = unchecked(
                individualSeed ^ (int)0x4C495A41);
            var breedingSeed = unchecked(
                ((ulong)(uint)individualSeed << 32) ^
                (uint)behaviorSeed ^
                0x4252454544494E47UL);
            var ephemeralBreedingWorld =
                options.DiagnosticMode || options.AllowMultipleInstances;
            var managementSource = CreateManagementSource(
                loadResult.Path,
                ephemeralBreedingWorld,
                breedingSeed);
            var desktopProfile = ResolveDesktopProfile(
                loadResult,
                managementSource,
                behaviorSeed);
            if (!desktopProfile.UsesBreedingGenome)
            {
                managementSource.MarkRunningDesktopFallback();
            }
            var activeLizardId = desktopProfile.UsesBreedingGenome
                ? managementSource.RunningDesktopLizardId
                : null;
            var profile = desktopProfile.Profile;
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

            var managerWindow = new GameManagerWindow(managementSource);
            var shutdownStarted = false;

            void ShutdownApplication()
            {
                if (shutdownStarted)
                {
                    return;
                }
                shutdownStarted = true;
                managerWindow.AllowPermanentClose();
                if (managerWindow.IsVisible)
                {
                    managerWindow.Close();
                }
                else
                {
                    managerWindow.Dispose();
                }
                desktop.Shutdown();
            }

            petWindow.PrimaryClicked += (_, _) =>
                managerWindow.ShowDesktopPetDetails(activeLizardId);
            petWindow.DetailsRequested += (_, _) =>
                managerWindow.ShowDesktopPetDetails(activeLizardId);
            petWindow.ManagementRequested += (_, _) => managerWindow.ShowHome();
            petWindow.ExitRequested += (_, _) => ShutdownApplication();
            petWindow.Closed += (_, _) => ShutdownApplication();
            petWindow.Opened += (_, _) => diagnosticPanel.ShowForOwner(petWindow);
            petWindow.PrepareForShow();
            desktop.MainWindow = petWindow;
            desktop.Exit += (_, _) =>
            {
                if (!shutdownStarted)
                {
                    managerWindow.AllowPermanentClose();
                    managerWindow.Dispose();
                }
                ReleaseSingleInstance();
            };
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

    internal static LizardBreedingManagementSource CreateManagementSource(
        string configurationPath,
        bool ephemeral,
        ulong seed)
    {
        BreedingWorldStore? store = null;
        try
        {
            store = BreedingWorldStore.Create(configurationPath, ephemeral);
            return new LizardBreedingManagementSource(store, seed, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            store?.Dispose();
            Trace.WriteLine(
                $"[InfiniteLizards] breeding world unavailable; using an ephemeral session: {exception}");
            var fallback = BreedingWorldStore.Create(configurationPath, ephemeral: true);
            return new LizardBreedingManagementSource(fallback, seed, DateTimeOffset.UtcNow);
        }
    }

    private static DesktopProfileResolution ResolveDesktopProfile(
        ConfigurationLoadResult loadResult,
        LizardBreedingManagementSource managementSource,
        int behaviorSeed)
    {
        var genome = managementSource.RunningDesktopLizardGenome;
        if (genome is null)
        {
            return new DesktopProfileResolution(
                LizardConfigurationStore.ResolveProfile(loadResult),
                UsesBreedingGenome: false);
        }

        try
        {
            return new DesktopProfileResolution(
                IndividualProfileFactory.CreateFromGenome(
                    loadResult.Configuration,
                    genome,
                    behaviorSeed: behaviorSeed),
                UsesBreedingGenome: true);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidDataException or
            InvalidOperationException or
            KeyNotFoundException)
        {
            Trace.WriteLine(
                $"[InfiniteLizards] cannot project the active breeding genome; using the legacy profile: {exception}");
            return new DesktopProfileResolution(
                LizardConfigurationStore.ResolveProfile(loadResult),
                UsesBreedingGenome: false);
        }
    }
}

internal readonly record struct DesktopProfileResolution(
    LizardProfile Profile,
    bool UsesBreedingGenome);

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
