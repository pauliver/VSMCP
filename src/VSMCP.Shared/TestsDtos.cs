using System.Collections.Generic;

namespace VSMCP.Shared
{
    public sealed class TestDiscoveryResult
    {
        public List<TestCase> Tests { get; set; } = new();
        public int Total { get; set; }
    }
public sealed class TestCase
{
    public string FullyQualifiedName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Source { get; set; }
    public string? CodeFilePath { get; set; }
    public int LineNumber { get; set; }
}
public enum TestOutcome
{
    None = 0,
    Passed = 1,
    Failed = 2,
    Skipped = 3,
    NotFound = 4,
}
public sealed class TestRunResult
{
    public string RunId { get; set; } = "";
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public List<TestResultItem> Results { get; set; } = new();
    public string? Output { get; set; }
}
public sealed class TestResultItem
{
    public string FullyQualifiedName { get; set; } = "";
    public TestOutcome Outcome { get; set; }
    public double DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
}
}