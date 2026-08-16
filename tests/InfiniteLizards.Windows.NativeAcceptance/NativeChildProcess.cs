using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace InfiniteLizards.Windows.NativeAcceptance;

internal sealed class NativeChildProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _standardError;
    private bool _preserveForInputSafety;

    private NativeChildProcess(
        Process process,
        Task<string> standardError,
        string kind,
        nint window,
        uint windowThreadId,
        uint windowProcessId,
        uint dpi)
    {
        _process = process;
        _standardError = standardError;
        Kind = kind;
        Window = window;
        WindowThreadId = windowThreadId;
        WindowProcessId = windowProcessId;
        Dpi = dpi;
    }

    internal string Kind { get; }
    internal nint Window { get; }
    internal uint WindowThreadId { get; }
    internal uint WindowProcessId { get; }
    internal uint Dpi { get; }
    internal Process Process => _process;

    internal void PreserveForInputSafety(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _preserveForInputSafety = true;
        Console.Error.WriteLine(string.Join(
            '|',
            "INPUT_SAFETY_KEEPALIVE",
            $"kind={Kind}",
            $"pid={_process.Id}",
            $"hwnd=0x{unchecked((ulong)Window.ToInt64()):X}",
            $"reason={reason.Replace('|', '/')}"));
    }

    internal void ResetCounters()
    {
        if (Win32Native.SendAcceptanceMessage(
                Window,
                Win32Native.WmAcceptanceResetCounters) != new nint(1))
        {
            throw new InvalidOperationException(
                $"The {Kind} child could not reset its native counters.");
        }
    }

    internal int ReadCounter(int counter) => checked((int)
        Win32Native.SendAcceptanceMessage(
            Window,
            Win32Native.WmAcceptanceReadCounter,
            new nint(counter)).ToInt64());

    internal static async Task<NativeChildProcess> StartAsync(
        string kind,
        int x,
        int y,
        int width,
        int height,
        nuint routeTag = 0)
    {
        var startInfo = CreateStartInfo();
        startInfo.ArgumentList.Add("--window-child");
        startInfo.ArgumentList.Add(kind);
        startInfo.ArgumentList.Add(x.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(y.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(width.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(height.ToString(CultureInfo.InvariantCulture));
        if (routeTag != 0)
        {
            startInfo.ArgumentList.Add(routeTag.ToString(CultureInfo.InvariantCulture));
        }

        var process = Process.Start(startInfo) ??
            throw new InvalidOperationException($"Could not launch the {kind} child process.");
        var standardError = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var readyLine = await process.StandardOutput.ReadLineAsync(timeout.Token);
            if (readyLine is null)
            {
                await WaitForExitWithoutThrowingAsync(process, TimeSpan.FromSeconds(2));
                throw new InvalidOperationException(
                    $"The {kind} child exited before its HWND was ready: " +
                    await ReadErrorWithoutThrowingAsync(standardError));
            }

            var fields = readyLine.Split('|');
            if (fields is not ["READY", var actualKind, var windowText,
                    var threadText, var processText, var dpiText] ||
                !string.Equals(actualKind, kind, StringComparison.Ordinal) ||
                !long.TryParse(windowText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var windowValue) ||
                !uint.TryParse(threadText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var threadId) ||
                !uint.TryParse(processText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var processId) ||
                !uint.TryParse(dpiText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var dpi))
            {
                throw new InvalidOperationException(
                    $"The {kind} child returned an invalid READY record: {readyLine}");
            }

            if (processId != checked((uint)process.Id))
            {
                throw new InvalidOperationException(
                    $"The {kind} HWND PID {processId} does not match child PID {process.Id}.");
            }

            var window = new nint(windowValue);
            if (window == nint.Zero || !Win32Native.IsWindow(window))
            {
                throw new InvalidOperationException(
                    $"The {kind} child did not expose a live HWND.");
            }

            return new NativeChildProcess(
                process,
                standardError,
                kind,
                window,
                threadId,
                processId,
                dpi);
        }
        catch
        {
            await TerminateAsync(process);
            process.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_preserveForInputSafety)
        {
            // Deliberately do not close the process handle or HWND. A failed
            // LEFTUP proof is safer with the controller-owned target alive;
            // the structured PID/HWND record above gives an operator an exact
            // process to terminate only after verifying the button is up.
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _ = Win32Native.PostMessage(
                    Window,
                    Win32Native.WmClose,
                    nint.Zero,
                    nint.Zero);
                await WaitForExitWithoutThrowingAsync(_process, TimeSpan.FromSeconds(5));
            }

            if (!_process.HasExited)
            {
                await TerminateAsync(_process);
            }

            var standardError = await ReadErrorWithoutThrowingAsync(_standardError);
            if (_process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"The {Kind} child exited with code {_process.ExitCode}: " +
                    standardError);
            }
        }
        finally
        {
            _process.Dispose();
        }
    }

    private static ProcessStartInfo CreateStartInfo()
    {
        var processPath = Environment.ProcessPath ??
            throw new InvalidOperationException("Environment.ProcessPath is unavailable.");
        var assemblyPath = Assembly.GetEntryAssembly()?.Location ??
            throw new InvalidOperationException("The entry assembly path is unavailable.");
        var isDotnetHost = string.Equals(
            Path.GetFileNameWithoutExtension(processPath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            WorkingDirectory = Path.GetDirectoryName(assemblyPath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (isDotnetHost)
        {
            startInfo.ArgumentList.Add(assemblyPath);
        }

        return startInfo;
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

    private static async Task<string> ReadErrorWithoutThrowingAsync(Task<string> errorTask)
    {
        try
        {
            return await errorTask.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch
        {
            return "<stderr unavailable>";
        }
    }
}
