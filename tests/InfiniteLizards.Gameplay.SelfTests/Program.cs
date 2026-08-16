using System.Diagnostics;
using DesktopLizard.Diagnostics;

var suites = new (string Name, Func<SuiteOutcome> Run)[]
{
    (nameof(ConfigurationSelfTest), () => FromResult(ConfigurationSelfTest.Run(), result => result.Passed)),
    (nameof(GaitSelfTest), () => FromResult(GaitSelfTest.Run(), result => result.Passed)),
    (nameof(GrabReleaseSelfTest), () => FromResult(GrabReleaseSelfTest.Run(), result => result.Passed)),
    (nameof(LivenessSelfTest), () => FromResult(LivenessSelfTest.Run(), result => result.Passed)),
    (nameof(LostGripFallSelfTest), () => FromResult(LostGripFallSelfTest.Run(), result => result.Passed)),
    (nameof(LostGripAccelerationSelfTest), () =>
        FromResult(LostGripAccelerationSelfTest.Run(), result => result.Passed)),
    (nameof(MouseChaseSelfTest), () => FromResult(MouseChaseSelfTest.Run(), result => result.Passed)),
    (nameof(RoamingSelfTest), () => FromResult(RoamingSelfTest.Run(), result => result.Passed)),
    (nameof(SimulationSessionSelfTest), () => FromResult(SimulationSessionSelfTest.Run(), result => result.Passed)),
    (nameof(AnimationProfileSmokeTest), () => FromResult(AnimationProfileSmokeTest.Run(), result => result.Passed)),
    (nameof(DanglingJitterAnalyzer), RunDanglingJitterAnalyzer),
    (nameof(DebugStateControlSelfTest), () =>
    {
        var report = DebugStateControlSelfTest.RunReport();
        return new SuiteOutcome(report.Passed, report.ToString());
    }),
    (nameof(DebugPanelPlacementSelfTest), () =>
    {
        var report = DebugPanelPlacementSelfTest.RunReport();
        return new SuiteOutcome(report.Passed, report.ToString());
    }),
    (nameof(ArchitectureBoundarySelfTest), () =>
        FromResult(ArchitectureBoundarySelfTest.Run(), result => result.Passed)),
    (nameof(DpiScaleInvarianceSelfTest), () =>
        FromResult(DpiScaleInvarianceSelfTest.Run(), result => result.Passed)),
    (nameof(RebaseAndResetSelfTest), () =>
        FromResult(RebaseAndResetSelfTest.Run(), result => result.Passed)),
    (nameof(PortableDebugBridgeSelfTest), () =>
        FromResult(PortableDebugBridgeSelfTest.Run(), result => result.Passed))
};

var failures = 0;
var totalStopwatch = Stopwatch.StartNew();
foreach (var suite in suites)
{
    var stopwatch = Stopwatch.StartNew();
    try
    {
        var outcome = suite.Run();
        stopwatch.Stop();
        if (outcome.Passed)
        {
            Console.WriteLine($"PASS {suite.Name} ({stopwatch.Elapsed.TotalSeconds:F2}s)");
            continue;
        }

        failures++;
        Console.Error.WriteLine($"FAIL {suite.Name} ({stopwatch.Elapsed.TotalSeconds:F2}s)");
        Console.Error.WriteLine($"  {outcome.Detail}");
    }
    catch (Exception exception)
    {
        stopwatch.Stop();
        failures++;
        Console.Error.WriteLine($"FAIL {suite.Name} ({stopwatch.Elapsed.TotalSeconds:F2}s)");
        Console.Error.WriteLine($"  {exception}");
    }
}

totalStopwatch.Stop();
Console.WriteLine(
    failures == 0
        ? $"PASS all {suites.Length} suites ({totalStopwatch.Elapsed.TotalSeconds:F2}s)"
        : $"FAIL {failures} of {suites.Length} suites ({totalStopwatch.Elapsed.TotalSeconds:F2}s)");

return failures == 0 ? 0 : 1;

static SuiteOutcome FromResult<T>(T result, Func<T, bool> passed) =>
    new(passed(result), result?.ToString() ?? "The suite returned no result.");

static SuiteOutcome RunDanglingJitterAnalyzer()
{
    var output = DanglingJitterAnalyzer.Run();
    var passed =
        !string.IsNullOrWhiteSpace(output) &&
        !output.Contains("NaN", StringComparison.Ordinal) &&
        !output.Contains("Infinity", StringComparison.Ordinal);
    return new SuiteOutcome(
        passed,
        passed ? "Analyzer completed with finite output." : output);
}

internal readonly record struct SuiteOutcome(bool Passed, string Detail);
