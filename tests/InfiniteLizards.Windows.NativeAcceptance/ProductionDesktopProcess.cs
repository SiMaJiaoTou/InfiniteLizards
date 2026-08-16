using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace InfiniteLizards.Windows.NativeAcceptance;

internal sealed class ProductionDesktopProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _standardOutput;
    private readonly Task<string> _standardError;
    private readonly string _temporaryDirectory;
    private bool _preserveForInputSafety;

    private ProductionDesktopProcess(
        Process process,
        Task<string> standardOutput,
        Task<string> standardError,
        string temporaryDirectory,
        nint window)
    {
        _process = process;
        _standardOutput = standardOutput;
        _standardError = standardError;
        _temporaryDirectory = temporaryDirectory;
        Window = window;
    }

    internal Process Process => _process;
    internal nint Window { get; }

    internal void PreserveForInputSafety(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _preserveForInputSafety = true;
        Console.Error.WriteLine(string.Join(
            '|',
            "INPUT_SAFETY_KEEPALIVE",
            "kind=production-desktop",
            $"pid={_process.Id}",
            $"hwnd=0x{unchecked((ulong)Window.ToInt64()):X}",
            $"config={_temporaryDirectory.Replace('|', '/')}",
            $"reason={reason.Replace('|', '/')}"));
    }

    internal static async Task<ProductionDesktopProcess> StartAsync(
        string executablePath,
        ProductionAcceptanceProtocol protocol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(protocol);
        var fullPath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The published InfiniteLizards.Desktop executable was not found.",
                fullPath);
        }

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"InfiniteLizards.ProductionAcceptance.{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var configurationPath = Path.Combine(temporaryDirectory, "lizard-settings.json");
        var startInfo = new ProcessStartInfo
        {
            FileName = fullPath,
            WorkingDirectory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--multi-instance");
        startInfo.ArgumentList.Add($"--config={configurationPath}");
        startInfo.Environment[ProductionAcceptanceProtocol.NonceEnvironment] =
            protocol.NonceText;
        startInfo.Environment[ProductionAcceptanceProtocol.ParentProcessEnvironment] =
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        startInfo.Environment[ProductionAcceptanceProtocol.RouteTagEnvironment] =
            protocol.RouteTagText;

        Process? process = null;
        Task<string>? standardOutput = null;
        Task<string>? standardError = null;
        try
        {
            File.WriteAllText(
                configurationPath,
                CreateAcceptanceConfigurationJson(protocol.Marker),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            process = Process.Start(startInfo) ?? throw new InvalidOperationException(
                "Could not launch the published production desktop application.");
            standardOutput = process.StandardOutput.ReadToEndAsync();
            standardError = process.StandardError.ReadToEndAsync();
            var window = await WaitForReadyWindowAsync(process, protocol);
            return new ProductionDesktopProcess(
                process,
                standardOutput,
                standardError,
                temporaryDirectory,
                window);
        }
        catch
        {
            if (process is not null)
            {
                await TerminateAsync(process);
                process.Dispose();
            }
            TryDeleteTemporaryDirectory(temporaryDirectory);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_preserveForInputSafety)
        {
            // Preserve both the live HWND and its configuration. The emitted
            // PID is the explicit operator-controlled termination handle once
            // mouse-up has been independently confirmed.
            return;
        }

        Exception? failure = null;
        try
        {
            if (!_process.HasExited && Win32Native.IsWindow(Window))
            {
                if (!Win32Native.PostMessage(
                        Window,
                        Win32Native.WmClose,
                        nint.Zero,
                        nint.Zero))
                {
                    failure = Win32Native.Failure("PostMessageW(WM_CLOSE production)");
                }
                await WaitForExitWithoutThrowingAsync(_process, TimeSpan.FromSeconds(8));
            }

            if (!_process.HasExited)
            {
                await TerminateAsync(_process);
                failure ??= new InvalidOperationException(
                    "The production desktop did not exit through its real WM_CLOSE lifecycle.");
            }

            var standardOutput = await ReadWithoutThrowingAsync(_standardOutput);
            var standardError = await ReadWithoutThrowingAsync(_standardError);
            if (_process.ExitCode != 0)
            {
                failure ??= new InvalidOperationException(
                    $"The production desktop exited with code {_process.ExitCode}. " +
                    $"stdout={Sanitize(standardOutput)}, stderr={Sanitize(standardError)}");
            }
        }
        catch (Exception exception)
        {
            failure = failure is null
                ? exception
                : new AggregateException(failure, exception);
        }
        finally
        {
            _process.Dispose();
            TryDeleteTemporaryDirectory(_temporaryDirectory);
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    private static async Task<nint> WaitForReadyWindowAsync(
        Process process,
        ProductionAcceptanceProtocol protocol)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(25))
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The production desktop exited before native readiness with code {process.ExitCode}.");
            }

            var matches = EnumerateProcessWindows(process.Id)
                .Where(window => Win32Native.IsWindowVisible(window))
                .Where(window => TryIsReadyWindow(protocol, window))
                .Distinct()
                .ToArray();
            if (matches.Length == 1)
            {
                return matches[0];
            }
            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    "More than one production HWND answered the nonce-scoped readiness ping.");
            }

            await Task.Delay(25);
        }

        throw new TimeoutException(
            "Timed out waiting for the real production Win32 overlay to pass EnsureVisible.");
    }

    private static IReadOnlyList<nint> EnumerateProcessWindows(int processId)
    {
        var windows = new List<nint>();
        Win32Native.EnumWindowsProcedure callback = (window, parameter) =>
        {
            _ = Win32Native.GetWindowThreadProcessId(window, out var ownerProcessId);
            if (ownerProcessId == checked((uint)processId))
            {
                windows.Add(window);
            }
            GC.KeepAlive(parameter);
            return true;
        };
        if (!Win32Native.EnumWindows(callback, nint.Zero))
        {
            throw Win32Native.Failure("EnumWindows(production)");
        }

        GC.KeepAlive(callback);
        return windows;
    }

    private static bool TryIsReadyWindow(
        ProductionAcceptanceProtocol protocol,
        nint window)
    {
        try
        {
            return protocol.IsReadyWindow(window);
        }
        catch
        {
            return false;
        }
    }

    private static async Task TerminateAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await WaitForExitWithoutThrowingAsync(process, TimeSpan.FromSeconds(5));
        }
    }

    private static async Task WaitForExitWithoutThrowingAsync(
        Process process,
        TimeSpan timeout)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task<string> ReadWithoutThrowingAsync(Task<string> output)
    {
        try
        {
            return await output.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static void TryDeleteTemporaryDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string Sanitize(string value) =>
        value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();

    // A valid schema-6 document with omitted properties retaining their typed
    // defaults. The long spawn state freezes the pose for pixel/region/input
    // correlation; disabled variation fixes the documented default palette.
    internal static string CreateAcceptanceConfigurationJson(
        ProductionAcceptanceMarker marker) => $$"""
        {
          "SchemaVersion": 6,
          "IndividualSeed": 41821,
          "Behavior": {
            "Timing": {
              "InitialSpawnDuration": 60.0
            }
          },
          "IndividualVariation": {
            "Enabled": false
          },
          "Appearance": {
            "BodyColor": {
              "Red": {{marker.BodyRed}},
              "Green": {{marker.BodyGreen}},
              "Blue": {{marker.BodyBlue}}
            },
            "PupilColor": {
              "Red": {{marker.PupilRed}},
              "Green": {{marker.PupilGreen}},
              "Blue": {{marker.PupilBlue}}
            }
          },
          "SecondaryMotion": {
            "BodyBobBaseAmplitude": 0.0,
            "BodyBobSpeedAmplitude": 0.0,
            "LandingBobAmplitude": 0.0,
            "IdleTailAmplitude": 0.0,
            "ObserveTailAmplitude": 0.0,
            "BreathingAmplitude": 0.0,
            "LandingSquashAmplitude": 0.0,
            "EyeBobFactor": 0.0,
            "BlinkInitialDelay": 60.0
          }
        }
        """;
}
