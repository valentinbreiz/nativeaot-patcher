using System;
using System.Collections.Generic;
using System.Linq;

namespace Cosmos.TestRunner.Engine;

/// <summary>
/// Results from running a test kernel
/// </summary>
public class TestResults
{
    public string SuiteName { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public List<TestResult> Tests { get; set; } = new();
    public TimeSpan TotalDuration { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public bool TimedOut { get; set; }
    public string UartLog { get; set; } = string.Empty;

    public int TotalTests => Tests.Count;
    public int PassedTests => Tests.Count(t => t.Status == TestStatus.Passed);
    public int FailedTests => Tests.Count(t => t.Status == TestStatus.Failed);
    public int SkippedTests => Tests.Count(t => t.Status == TestStatus.Skipped);

    /// <summary>
    /// Returns true if all tests passed (skipped tests are acceptable, only failures count as not passed)
    /// </summary>
    public bool AllTestsPassed => Tests.Count > 0 && FailedTests == 0;
}

/// <summary>
/// Individual test result
/// </summary>
public class TestResult
{
    public int TestNumber { get; set; }
    public string TestName { get; set; } = string.Empty;
    public TestStatus Status { get; set; }
    public uint DurationMs { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Test execution status
/// </summary>
public enum TestStatus
{
    Passed,
    Failed,
    Skipped
}
