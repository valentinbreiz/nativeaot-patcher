using System;
using System.Collections.Generic;

namespace Cosmos.TestRunner.Engine.OutputHandlers;

/// <summary>
/// Output handler that multiplexes output to multiple handlers simultaneously
/// </summary>
public class MultiplexingOutputHandler : OutputHandlerBase
{
    private readonly List<OutputHandlerBase> _handlers;

    public MultiplexingOutputHandler(params OutputHandlerBase[] handlers)
    {
        _handlers = new List<OutputHandlerBase>(handlers ?? Array.Empty<OutputHandlerBase>());
    }

    public void AddHandler(OutputHandlerBase handler)
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        _handlers.Add(handler);
    }

    public override void OnTestSuiteStart(string suiteName, string architecture)
    {
        foreach (var handler in _handlers)
        {
            try
            {
                handler.OnTestSuiteStart(suiteName, architecture);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MultiplexingOutputHandler] ERROR in {handler.GetType().Name}.OnTestSuiteStart: {ex.Message}");
            }
        }
    }

    public override void OnTestStart(int testNumber, string testName)
    {
        foreach (var handler in _handlers)
        {
            try
            {
                handler.OnTestStart(testNumber, testName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MultiplexingOutputHandler] ERROR in {handler.GetType().Name}.OnTestStart: {ex.Message}");
            }
        }
    }

    public override void OnTestPass(int testNumber, string testName, uint durationMs)
    {
        foreach (var handler in _handlers)
        {
            try
            {
                handler.OnTestPass(testNumber, testName, durationMs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MultiplexingOutputHandler] ERROR in {handler.GetType().Name}.OnTestPass: {ex.Message}");
            }
        }
    }

    public override void OnTestFail(int testNumber, string testName, string errorMessage, uint durationMs)
    {
        foreach (var handler in _handlers)
        {
            try
            {
                handler.OnTestFail(testNumber, testName, errorMessage, durationMs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MultiplexingOutputHandler] ERROR in {handler.GetType().Name}.OnTestFail: {ex.Message}");
            }
        }
    }

    public override void OnTestSkip(int testNumber, string testName, string reason)
    {
        foreach (var handler in _handlers)
        {
            try
            {
                handler.OnTestSkip(testNumber, testName, reason);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MultiplexingOutputHandler] ERROR in {handler.GetType().Name}.OnTestSkip: {ex.Message}");
            }
        }
    }

    public override void OnTestSuiteEnd(TestResults results)
    {
        foreach (var handler in _handlers)
        {
            try
            {
                handler.OnTestSuiteEnd(results);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MultiplexingOutputHandler] ERROR in {handler.GetType().Name}.OnTestSuiteEnd: {ex.Message}");
            }
        }
    }

    public override void OnError(string errorMessage)
    {
        foreach (var handler in _handlers)
        {
            try
            {
                handler.OnError(errorMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MultiplexingOutputHandler] ERROR in {handler.GetType().Name}.OnError: {ex.Message}");
            }
        }
    }

    public override void Complete()
    {
        foreach (var handler in _handlers)
        {
            try
            {
                handler.Complete();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MultiplexingOutputHandler] ERROR in {handler.GetType().Name}.Complete: {ex.Message}");
            }
        }
    }
}
