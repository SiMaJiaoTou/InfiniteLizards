namespace InfiniteLizards.Windows.NativeAcceptance;

internal static class Program
{
    private const int Passed = 0;
    private const int Failed = 1;
    private const int InvalidArguments = 2;
    private const int Skipped = 77;

    public static async Task<int> Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            try
            {
                ProductionAcceptancePureSelfTests.Run();
                return Passed;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return Failed;
            }
        }

        if (args is ["--window-child", var kind, var x, var y, var width, var height])
        {
            return NativeWindowChild.Run(kind, x, y, width, height, routeTagText: null);
        }
        if (args is ["--window-child", var taggedKind, var taggedX, var taggedY,
                var taggedWidth, var taggedHeight, var routeTag])
        {
            return NativeWindowChild.Run(
                taggedKind,
                taggedX,
                taggedY,
                taggedWidth,
                taggedHeight,
                routeTag);
        }

        AcceptanceOptions options;
        try
        {
            options = AcceptanceOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            WriteUsage();
            return InvalidArguments;
        }

        var skipReason = InteractiveWindowsSession.GetSkipReason();
        if (skipReason is not null)
        {
            Console.WriteLine(
                $"RESULT|status=SKIP|scope={options.Scope}|" +
                $"production=UNVERIFIED|reason={Sanitize(skipReason)}");
            Console.WriteLine($"SKIP: {skipReason}");
            Console.WriteLine(
                options.AllowSkip
                    ? "SKIP was explicitly allowed; returning exit code 0."
                    : "SKIP is a non-pass; returning conventional exit code 77. " +
                      "Pass --allow-skip only for informational runs.");
            return options.AllowSkip ? Passed : Skipped;
        }

        try
        {
            if (options.ProductionExecutable is { } productionExecutable)
            {
                await ProductionDesktopAcceptanceRunner.RunAsync(
                    productionExecutable,
                    options.RequireMixedDpi);
                Console.WriteLine(
                    "RESULT|status=PASS|scope=production-desktop-native|" +
                    "production=VERIFIED_ON_THIS_MACHINE");
                Console.WriteLine(
                    "PASS: The published production Desktop/Avalonia/Skia/Win32 overlay " +
                    "completed native routing, DWM composite, DPI and resource acceptance.");
            }
            else
            {
                await NativeAcceptanceRunner.RunAsync();
                Console.WriteLine(
                    "RESULT|status=PASS|scope=win32-primitives-smoke|production=UNVERIFIED");
                Console.WriteLine(
                    "PASS: Win32 primitives smoke completed. The production Desktop/Avalonia/DWM " +
                    "path was not exercised.");
            }
            return Passed;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"RESULT|status=FAIL|scope={options.Scope}|" +
                $"production={(options.ProductionExecutable is null ? "UNVERIFIED" : "FAILED")}|" +
                $"error={Sanitize(exception.GetType().Name)}");
            Console.Error.WriteLine(
                options.ProductionExecutable is null
                    ? "FAIL: Win32 primitives smoke failed."
                    : "FAIL: Production Desktop native acceptance failed.");
            Console.Error.WriteLine(exception);
            return Failed;
        }
    }

    private static string Sanitize(string value) =>
        value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');

    private static void WriteUsage() => Console.Error.WriteLine(
        "Usage:\n" +
        "  InfiniteLizards.Windows.NativeAcceptance --self-test\n" +
        "  InfiniteLizards.Windows.NativeAcceptance [--allow-skip]\n" +
        "  InfiniteLizards.Windows.NativeAcceptance --production <Desktop.exe> " +
        "[--require-mixed-dpi] [--allow-skip]");

    private sealed record AcceptanceOptions(
        bool AllowSkip,
        string? ProductionExecutable,
        bool RequireMixedDpi)
    {
        internal string Scope => ProductionExecutable is null
            ? "win32-primitives-smoke"
            : "production-desktop-native";

        internal static AcceptanceOptions Parse(string[] arguments)
        {
            var allowSkip = false;
            var requireMixedDpi = false;
            string? productionExecutable = null;
            for (var index = 0; index < arguments.Length; index++)
            {
                switch (arguments[index])
                {
                    case "--allow-skip":
                        if (allowSkip)
                        {
                            throw new ArgumentException("--allow-skip was specified more than once.");
                        }
                        allowSkip = true;
                        break;
                    case "--require-mixed-dpi":
                        if (requireMixedDpi)
                        {
                            throw new ArgumentException(
                                "--require-mixed-dpi was specified more than once.");
                        }
                        requireMixedDpi = true;
                        break;
                    case "--production":
                        if (productionExecutable is not null || index + 1 >= arguments.Length)
                        {
                            throw new ArgumentException(
                                "--production requires exactly one executable path.");
                        }
                        productionExecutable = arguments[++index];
                        if (string.IsNullOrWhiteSpace(productionExecutable) ||
                            productionExecutable.StartsWith("--", StringComparison.Ordinal))
                        {
                            throw new ArgumentException(
                                "--production requires a non-empty executable path.");
                        }
                        break;
                    default:
                        throw new ArgumentException(
                            $"Unknown native acceptance argument: {arguments[index]}");
                }
            }

            if (requireMixedDpi && productionExecutable is null)
            {
                throw new ArgumentException(
                    "--require-mixed-dpi is valid only with --production.");
            }
            return new AcceptanceOptions(
                allowSkip,
                productionExecutable,
                requireMixedDpi);
        }
    }
}
