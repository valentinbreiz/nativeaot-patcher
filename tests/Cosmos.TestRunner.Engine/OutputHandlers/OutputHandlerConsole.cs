using System;

namespace Cosmos.TestRunner.Engine.OutputHandlers;

/// <summary>
/// Console output handler with colored, real-time test progress display
/// </summary>
public class OutputHandlerConsole : OutputHandlerBase
{
    private readonly bool _useColors;
    private readonly bool _verbose;
    private string _currentSuite = string.Empty;
    private int _passCount = 0;
    private int _failCount = 0;
    private int _skipCount = 0;

    public OutputHandlerConsole(bool useColors = true, bool verbose = false)
    {
        _useColors = useColors && !Console.IsOutputRedirected;
        _verbose = verbose;
    }

    public override void OnTestSuiteStart(string suiteName, string architecture)
    {
        _currentSuite = suiteName;
        _passCount = 0;
        _failCount = 0;
        _skipCount = 0;

        WriteHeader();
        WriteLine($"Starting test suite: {suiteName}", ConsoleColor.Cyan);
        WriteLine($"Architecture: {architecture}", ConsoleColor.Gray);
        WriteLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", ConsoleColor.Gray);
        WriteHeader();
    }

    public override void OnTestStart(int testNumber, string testName)
    {
        if (_verbose)
        {
            Write($"[{testNumber}] {testName} ... ", ConsoleColor.Gray);
        }
    }

    public override void OnTestPass(int testNumber, string testName, uint durationMs)
    {
        _passCount++;

        if (_verbose)
        {
            WriteLine($"PASS ({durationMs}ms)", ConsoleColor.Green);
        }
        else
        {
            Write(".", ConsoleColor.Green);
        }
    }

    public override void OnTestFail(int testNumber, string testName, string errorMessage, uint durationMs)
    {
        _failCount++;

        if (_verbose)
        {
            WriteLine($"FAIL ({durationMs}ms)", ConsoleColor.Red);
            WriteLine($"  Error: {errorMessage}", ConsoleColor.Red);
        }
        else
        {
            Write("F", ConsoleColor.Red);
            Console.WriteLine();
            WriteLine($"[{testNumber}] {testName}: {errorMessage}", ConsoleColor.Red);
        }
    }

    public override void OnTestSkip(int testNumber, string testName, string reason)
    {
        _skipCount++;

        if (_verbose)
        {
            WriteLine($"SKIP", ConsoleColor.Yellow);
            WriteLine($"  Reason: {reason}", ConsoleColor.Yellow);
        }
        else
        {
            Write("S", ConsoleColor.Yellow);
        }
    }

    public override void OnTestSuiteEnd(TestResults results)
    {
        Console.WriteLine(); // Newline after progress indicators
        WriteHeader();

        if (results.TimedOut)
        {
            WriteLine("TEST SUITE TIMED OUT", ConsoleColor.Red);
            WriteHeader();
        }

        if (!string.IsNullOrEmpty(results.ErrorMessage))
        {
            WriteLine($"ERROR: {results.ErrorMessage}", ConsoleColor.Red);
            WriteHeader();
        }

        // Summary
        WriteLine($"Suite: {results.SuiteName}", ConsoleColor.Cyan);
        WriteLine($"Total tests: {results.TotalTests}", ConsoleColor.Gray);
        WriteLine($"Passed: {results.PassedTests}", results.PassedTests > 0 ? ConsoleColor.Green : ConsoleColor.Gray);
        WriteLine($"Failed: {results.FailedTests}", results.FailedTests > 0 ? ConsoleColor.Red : ConsoleColor.Gray);
        WriteLine($"Skipped: {results.SkippedTests}", results.SkippedTests > 0 ? ConsoleColor.Yellow : ConsoleColor.Gray);
        WriteLine($"Duration: {results.TotalDuration.TotalSeconds:F2}s", ConsoleColor.Gray);

        WriteHeader();

        // Final result
        if (results.AllTestsPassed)
        {
            WriteLine("ALL TESTS PASSED", ConsoleColor.Green);
        }
        else if (results.FailedTests > 0)
        {
            WriteLine($"TESTS FAILED ({results.FailedTests} failures)", ConsoleColor.Red);
        }
        else if (results.TotalTests == 0)
        {
            WriteLine("NO TESTS EXECUTED", ConsoleColor.Yellow);
        }

        WriteHeader();
    }

    public override void OnError(string errorMessage)
    {
        Console.WriteLine();
        WriteLine($"ERROR: {errorMessage}", ConsoleColor.Red);
    }

    public override void Complete()
    {
        // Console output is already flushed in real-time
        // Nothing to finalize
    }

    private void WriteHeader()
    {
        WriteLine(new string('=', 80), ConsoleColor.DarkGray);
    }

    private void Write(string message, ConsoleColor color)
    {
        if (_useColors)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(message);
            Console.ForegroundColor = originalColor;
        }
        else
        {
            Console.Write(message);
        }
    }

    private void WriteLine(string message, ConsoleColor color)
    {
        if (_useColors)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = originalColor;
        }
        else
        {
            Console.WriteLine(message);
        }
    }
}
