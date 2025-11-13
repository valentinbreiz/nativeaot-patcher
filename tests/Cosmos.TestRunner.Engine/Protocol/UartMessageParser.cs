using System;
using System.Collections.Generic;
using System.Text;
using Cosmos.TestRunner.Protocol;

namespace Cosmos.TestRunner.Engine.Protocol;

/// <summary>
/// Parses binary protocol messages from UART log output
/// </summary>
public class UartMessageParser
{
    /// <summary>
    /// Parse UART log and extract test results
    /// </summary>
    public static TestResults ParseUartLog(string uartLog, string architecture)
    {
        var results = new TestResults { Architecture = architecture };

        // Extract binary data from UART log (filter out ANSI codes and text)
        var binaryData = ExtractBinaryData(uartLog);

        Console.WriteLine($"[UartParser] UART log length: {uartLog.Length} bytes");
        Console.WriteLine($"[UartParser] Binary data length: {binaryData.Length} bytes");

        // Parse protocol messages
        int offset = 0;
        int messagesFound = 0;
        while (offset < binaryData.Length)
        {
            int oldOffset = offset;
            if (!TryParseMessage(binaryData, ref offset, results))
            {
                // Skip byte if we can't parse a valid message
                offset++;
            }
            else if (offset > oldOffset)
            {
                messagesFound++;
            }
        }

        Console.WriteLine($"[UartParser] Found {messagesFound} protocol messages");
        Console.WriteLine($"[UartParser] Suite name: {results.SuiteName}");
        Console.WriteLine($"[UartParser] Tests found: {results.Tests.Count}");

        return results;
    }

    private static byte[] ExtractBinaryData(string uartLog)
    {
        // Convert entire UART log to bytes
        // Protocol messages are embedded in the byte stream alongside text output
        return Encoding.Latin1.GetBytes(uartLog);
    }

    private static bool TryParseMessage(byte[] data, ref int offset, TestResults results)
    {
        if (offset + 3 > data.Length)
            return false;

        byte command = data[offset];
        ushort length = (ushort)(data[offset + 1] | (data[offset + 2] << 8));

        // Validate we have enough data for payload
        if (offset + 3 + length > data.Length)
            return false;

        byte[] payload = new byte[length];
        Array.Copy(data, offset + 3, payload, 0, length);

        offset += 3 + length;

        // Parse based on command
        switch (command)
        {
            case Ds2Vs.TestSuiteStart:
                ParseTestSuiteStart(payload, results);
                return true;

            case Ds2Vs.TestStart:
                ParseTestStart(payload, results);
                return true;

            case Ds2Vs.TestPass:
                ParseTestPass(payload, results);
                return true;

            case Ds2Vs.TestFail:
                ParseTestFail(payload, results);
                return true;

            case Ds2Vs.TestSkip:
                ParseTestSkip(payload, results);
                return true;

            case Ds2Vs.TestSuiteEnd:
                ParseTestSuiteEnd(payload, results);
                return true;

            default:
                return false;
        }
    }

    private static void ParseTestSuiteStart(byte[] payload, TestResults results)
    {
        results.SuiteName = Encoding.UTF8.GetString(payload);
    }

    private static void ParseTestStart(byte[] payload, TestResults results)
    {
        // Payload: [TestNumber:4][TestName:string]
        if (payload.Length < 4) return;

        int testNumber = BitConverter.ToInt32(payload, 0);
        string testName = Encoding.UTF8.GetString(payload, 4, payload.Length - 4);

        // Add test with pending status
        results.Tests.Add(new TestResult
        {
            TestNumber = testNumber,
            TestName = testName,
            Status = TestStatus.Passed // Will be updated by Pass/Fail/Skip
        });
    }

    private static void ParseTestPass(byte[] payload, TestResults results)
    {
        // Payload: [TestNumber:4][DurationMs:4]
        if (payload.Length < 8) return;

        int testNumber = BitConverter.ToInt32(payload, 0);
        uint durationMs = BitConverter.ToUInt32(payload, 4);

        var test = results.Tests.Find(t => t.TestNumber == testNumber);
        if (test != null)
        {
            test.Status = TestStatus.Passed;
            test.DurationMs = durationMs;
        }
    }

    private static void ParseTestFail(byte[] payload, TestResults results)
    {
        // Payload: [TestNumber:4][ErrorMessage:string]
        if (payload.Length < 4) return;

        int testNumber = BitConverter.ToInt32(payload, 0);
        string errorMessage = Encoding.UTF8.GetString(payload, 4, payload.Length - 4);

        var test = results.Tests.Find(t => t.TestNumber == testNumber);
        if (test != null)
        {
            test.Status = TestStatus.Failed;
            test.ErrorMessage = errorMessage;
        }
    }

    private static void ParseTestSkip(byte[] payload, TestResults results)
    {
        // Payload: [TestNumber:4][Reason:string]
        if (payload.Length < 4) return;

        int testNumber = BitConverter.ToInt32(payload, 0);
        string reason = Encoding.UTF8.GetString(payload, 4, payload.Length - 4);

        var test = results.Tests.Find(t => t.TestNumber == testNumber);
        if (test != null)
        {
            test.Status = TestStatus.Skipped;
            test.ErrorMessage = reason;
        }
    }

    private static void ParseTestSuiteEnd(byte[] payload, TestResults results)
    {
        // Payload: [TotalTests:4][PassedTests:4][FailedTests:4][SkippedTests:4]
        // This is a summary - we already have individual test results
    }
}
