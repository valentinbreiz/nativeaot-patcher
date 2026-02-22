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
        // Need at least 7 bytes: [MAGIC:4][Command:1][Length:2]
        if (offset + 7 > data.Length)
            return false;

        // Check for magic signature (0x19740807 little-endian)
        if (data[offset] != 0x07 || data[offset + 1] != 0x08 ||
            data[offset + 2] != 0x74 || data[offset + 3] != 0x19)
            return false;

        byte command = data[offset + 4];

        // Only proceed if this looks like a valid protocol command
        if (command < Ds2Vs.TestSuiteStart || command > Ds2Vs.TestSuiteEnd)
            return false;

        ushort length = (ushort)(data[offset + 5] | (data[offset + 6] << 8));

        // Sanity check: length shouldn't be huge (max test name ~256 chars)
        if (length > 1024)
            return false;

        // Validate we have enough data for payload
        if (offset + 7 + length > data.Length)
            return false;

        byte[] payload = new byte[length];
        Array.Copy(data, offset + 7, payload, 0, length);

        // Only advance offset after we've validated this is a real message
        offset += 7 + length;

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
        if (payload.Length < 2)
        {
            results.SuiteName = Encoding.UTF8.GetString(payload);
            results.ExpectedTestCount = 0;
            return;
        }

        // Payload: [ExpectedTests:2][SuiteName:string]
        results.ExpectedTestCount = BitConverter.ToUInt16(payload, 0);
        results.SuiteName = Encoding.UTF8.GetString(payload, 2, payload.Length - 2);
    }

    private static void ParseTestStart(byte[] payload, TestResults results)
    {
        // Payload: [TestNumber:2][TestName:string]
        if (payload.Length < 2) return;

        int testNumber = BitConverter.ToUInt16(payload, 0);
        string testName = Encoding.UTF8.GetString(payload, 2, payload.Length - 2);

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
        // Payload: [TestNumber:2][DurationMs:4]
        if (payload.Length < 6) return;

        int testNumber = BitConverter.ToUInt16(payload, 0);
        uint durationMs = BitConverter.ToUInt32(payload, 2);

        var test = results.Tests.Find(t => t.TestNumber == testNumber);
        if (test != null)
        {
            test.Status = TestStatus.Passed;
            test.DurationMs = durationMs;
        }
    }

    private static void ParseTestFail(byte[] payload, TestResults results)
    {
        // Payload: [TestNumber:2][ErrorMessage:string]
        if (payload.Length < 2) return;

        int testNumber = BitConverter.ToUInt16(payload, 0);
        string errorMessage = Encoding.UTF8.GetString(payload, 2, payload.Length - 2);

        var test = results.Tests.Find(t => t.TestNumber == testNumber);
        if (test != null)
        {
            test.Status = TestStatus.Failed;
            test.ErrorMessage = errorMessage;
        }
    }

    private static void ParseTestSkip(byte[] payload, TestResults results)
    {
        // Payload: [TestNumber:2][Reason:string]
        if (payload.Length < 2) return;

        int testNumber = BitConverter.ToUInt16(payload, 0);
        string reason = Encoding.UTF8.GetString(payload, 2, payload.Length - 2);

        var test = results.Tests.Find(t => t.TestNumber == testNumber);
        if (test != null)
        {
            test.Status = TestStatus.Skipped;
            test.ErrorMessage = reason;
        }
    }

    private static void ParseTestSuiteEnd(byte[] payload, TestResults results)
    {
        // Payload: [Total:2][Passed:2][Failed:2]
        if (payload.Length < 6) return;

        ushort total = BitConverter.ToUInt16(payload, 0);
        ushort passed = BitConverter.ToUInt16(payload, 2);
        ushort failed = BitConverter.ToUInt16(payload, 4);

        // Validate: total must equal passed + failed (catches corruption from timer interrupt interleaving)
        if (total == (ushort)(passed + failed))
        {
            // Use the validated total from the end message as the authoritative expected count.
            // This overrides the potentially corrupted value from TestSuiteStart.
            results.ExpectedTestCount = total;
            results.SuiteCompleted = true;
        }
    }
}
