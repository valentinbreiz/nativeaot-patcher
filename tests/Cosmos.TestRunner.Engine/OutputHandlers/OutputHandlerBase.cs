using System;

namespace Cosmos.TestRunner.Engine.OutputHandlers;

/// <summary>
/// Base class for all output handlers - provides abstract interface for structured test result output
/// </summary>
public abstract class OutputHandlerBase
{
    /// <summary>
    /// Called when test execution begins
    /// </summary>
    /// <param name="suiteName">Name of the test suite</param>
    /// <param name="architecture">Target architecture (x64, arm64)</param>
    public abstract void OnTestSuiteStart(string suiteName, string architecture);

    /// <summary>
    /// Called when an individual test starts
    /// </summary>
    /// <param name="testNumber">Sequential test number</param>
    /// <param name="testName">Name of the test</param>
    public abstract void OnTestStart(int testNumber, string testName);

    /// <summary>
    /// Called when a test passes
    /// </summary>
    /// <param name="testNumber">Sequential test number</param>
    /// <param name="testName">Name of the test</param>
    /// <param name="durationMs">Test duration in milliseconds</param>
    public abstract void OnTestPass(int testNumber, string testName, uint durationMs);

    /// <summary>
    /// Called when a test fails
    /// </summary>
    /// <param name="testNumber">Sequential test number</param>
    /// <param name="testName">Name of the test</param>
    /// <param name="errorMessage">Failure message</param>
    /// <param name="durationMs">Test duration in milliseconds</param>
    public abstract void OnTestFail(int testNumber, string testName, string errorMessage, uint durationMs);

    /// <summary>
    /// Called when a test is skipped
    /// </summary>
    /// <param name="testNumber">Sequential test number</param>
    /// <param name="testName">Name of the test</param>
    /// <param name="reason">Reason for skipping</param>
    public abstract void OnTestSkip(int testNumber, string testName, string reason);

    /// <summary>
    /// Called when test suite execution completes
    /// </summary>
    /// <param name="results">Complete test results</param>
    public abstract void OnTestSuiteEnd(TestResults results);

    /// <summary>
    /// Called when an error occurs outside of test execution
    /// </summary>
    /// <param name="errorMessage">Error message</param>
    public abstract void OnError(string errorMessage);

    /// <summary>
    /// Called to flush/finalize output (e.g., write XML file, close streams)
    /// </summary>
    public abstract void Complete();
}
