using System.Collections.Immutable;

namespace DesktopLizard.Diagnostics.Framework;

internal readonly record struct DiagnosticMetric(
    string Name,
    double Value,
    string Unit = "")
{
    public int AsInt32 => checked((int)Value);
    public float AsSingle => (float)Value;
}

internal readonly record struct DiagnosticCheck(
    string Name,
    bool Passed,
    string Expected,
    string Actual);

internal sealed record DiagnosticReport(
    string Name,
    ImmutableArray<DiagnosticMetric> Metrics,
    ImmutableArray<DiagnosticCheck> Checks,
    ImmutableArray<DiagnosticReport> Children)
{
    public bool Passed =>
        Checks.All(check => check.Passed) &&
        Children.All(child => child.Passed);

    public DiagnosticMetric GetMetric(string name)
    {
        foreach (var metric in Metrics)
        {
            if (string.Equals(metric.Name, name, StringComparison.Ordinal))
            {
                return metric;
            }
        }

        throw new KeyNotFoundException($"Diagnostic metric '{name}' was not found in '{Name}'.");
    }
}

internal sealed class DiagnosticReportBuilder
{
    private readonly string _name;
    private readonly List<DiagnosticMetric> _metrics = [];
    private readonly List<DiagnosticCheck> _checks = [];
    private readonly List<DiagnosticReport> _children = [];

    public DiagnosticReportBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    public DiagnosticReportBuilder AddMetric(string name, double value, string unit = "")
    {
        _metrics.Add(new DiagnosticMetric(name, value, unit));
        return this;
    }

    public DiagnosticReportBuilder AddCheck(
        string name,
        bool passed,
        string expected,
        string actual)
    {
        _checks.Add(new DiagnosticCheck(name, passed, expected, actual));
        return this;
    }

    public DiagnosticReportBuilder AddChild(DiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _children.Add(report);
        return this;
    }

    public DiagnosticReport Build() => new(
        _name,
        _metrics.ToImmutableArray(),
        _checks.ToImmutableArray(),
        _children.ToImmutableArray());
}
