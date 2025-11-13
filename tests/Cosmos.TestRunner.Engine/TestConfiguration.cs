using System;
using Cosmos.TestRunner.Engine.OutputHandlers;

namespace Cosmos.TestRunner.Engine;

/// <summary>
/// Test runner execution mode
/// </summary>
public enum TestRunnerMode
{
    /// <summary>
    /// CI mode: headless, fast, automated (no display, strict timeouts)
    /// </summary>
    CI,

    /// <summary>
    /// Dev mode: visual debugging (display window, relaxed timeouts, interactive)
    /// </summary>
    Dev
}

/// <summary>
/// Configuration for a test kernel execution
/// </summary>
public class TestConfiguration
{
    /// <summary>
    /// Path to the test kernel project directory
    /// </summary>
    public string KernelProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Target architecture (x64, arm64)
    /// </summary>
    public string Architecture { get; set; } = "x64";

    /// <summary>
    /// Build configuration (Debug, Release)
    /// </summary>
    public string BuildConfiguration { get; set; } = "Debug";

    /// <summary>
    /// Timeout in seconds for test execution
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Output directory for build artifacts
    /// </summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Path to store UART logs
    /// </summary>
    public string UartLogPath { get; set; } = string.Empty;

    /// <summary>
    /// Whether to keep build artifacts after test
    /// </summary>
    public bool KeepBuildArtifacts { get; set; } = false;

    /// <summary>
    /// Output handler for test results (defaults to console)
    /// </summary>
    public OutputHandlerBase? OutputHandler { get; set; }

    /// <summary>
    /// Optional XML output path for JUnit format
    /// </summary>
    public string XmlOutputPath { get; set; } = string.Empty;

    /// <summary>
    /// Execution mode: Dev (visual, interactive) or CI (headless, automated)
    /// </summary>
    public TestRunnerMode Mode { get; set; } = TestRunnerMode.CI;

    /// <summary>
    /// Show QEMU display window (overrides Mode if explicitly set)
    /// </summary>
    public bool? ShowDisplay { get; set; } = null;

    /// <summary>
    /// Computed: Should display be shown (based on Mode and ShowDisplay override)
    /// </summary>
    public bool ShouldShowDisplay => ShowDisplay ?? (Mode == TestRunnerMode.Dev);
}
