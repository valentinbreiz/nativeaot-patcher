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
        {
            return false;
        }

        // Check for magic signature (0x19740807 little-endian)
        if (data[offset] != 0x07 || data[offset + 1] != 0x08 ||
            data[offset + 2] != 0x74 || data[offset + 3] != 0x19)
        {
            return false;
        }

        byte command = data[offset + 4];

        // Only proceed if this looks like a valid protocol command
        if (command < Ds2Vs.TestSuiteStart || command > Ds2Vs.TestDestructiveReached)
        {
            return false;
        }

        ushort length = (ushort)(data[offset + 5] | (data[offset + 6] << 8));

        // Sanity check: coverage data can be large, other messages should be small
        int maxLength = (command == Ds2Vs.CoverageData) ? 65535 : 1024;
        if (length > maxLength)
        {
            return false;
        }

        // Validate we have enough data for payload
        if (offset + 7 + length > data.Length)
        {
            return false;
        }

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

            case Ds2Vs.CoverageData:
                ParseCoverageData(payload, results);
                return true;

            case Ds2Vs.ArchitectureInfo:
                // Architecture bootstrap message (arch + cpu count).
                // Not used for test assertions, but must be consumed as a valid frame.
                return true;

            case Ds2Vs.TestDestructiveReached:
                // Sentinel for the engine's re-launch heuristic. The frame must be
                // consumed as a valid message; no parsing into TestResults needed.
                return true;

            default:
                return false;
        }
    }

    private static void ParseTestSuiteStart(byte[] payload, TestResults results)
    {
        if (payload.Length < 2)
        {
            string shortName = Encoding.UTF8.GetString(payload);
            if (HasControlChars(shortName))
            {
                return;
            }

            results.SuiteName = shortName;
            results.ExpectedTestCount = 0;
            return;
        }

        // Payload: [ExpectedTests:2][SuiteName:string]
        string suiteName = Encoding.UTF8.GetString(payload, 2, payload.Length - 2);
        if (HasControlChars(suiteName))
        {
            return;
        }

        results.ExpectedTestCount = BitConverter.ToUInt16(payload, 0);
        results.SuiteName = suiteName;
    }

    private static void ParseTestStart(byte[] payload, TestResults results)
    {
        // Payload: [TestNumber:2][TestName:string]
        if (payload.Length < 2)
        {
            return;
        }

        int testNumber = BitConverter.ToUInt16(payload, 0);
        string testName = Encoding.UTF8.GetString(payload, 2, payload.Length - 2);

        // Defensive: a non-protocol byte sequence in UART (e.g. an IRQ handler
        // calling Serial.WriteString mid-frame) can interleave with a real
        // TestStart in a way that produces a false-positive magic match here,
        // and the resulting "name" carries control characters (0x00..0x1F)
        // that downstream XML emission rejects. Drop messages whose name
        // contains anything below 0x20 — real test names are ASCII
        // identifiers. Same defense on every string-bearing frame (suite
        // start, fail, skip).
        if (HasControlChars(testName))
        {
            return;
        }

        TestResult? existingTest = results.Tests.Find(t => t.TestNumber == testNumber);
        if (existingTest != null)
        {
            existingTest.TestName = testName;
            return;
        }

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
        if (payload.Length < 6)
        {
            return;
        }

        int testNumber = BitConverter.ToUInt16(payload, 0);
        uint durationMs = BitConverter.ToUInt32(payload, 2);

        TestResult? test = FindTestResult(results, testNumber);
        if (test == null)
        {
            return;
        }

        // Mirror the kernel-side clamp (TestRunner.Run): if a non-protocol
        // byte sequence in UART interleaves with a real TestPass frame, the
        // 4 raw duration bytes can be anything — that's where the
        // "1,129,536-second test" rows came from. Anything beyond 5 minutes
        // is a misaligned-frame artifact, not a measurement; pin it to the
        // cap so the report stays interpretable. Real tests finish in
        // seconds; this gives ~300x headroom.
        const uint MaxSaneDurationMs = 5u * 60u * 1000u;
        if (durationMs > MaxSaneDurationMs)
        {
            durationMs = MaxSaneDurationMs;
        }

        test.Status = TestStatus.Passed;
        test.DurationMs = durationMs;
    }

    private static void ParseTestFail(byte[] payload, TestResults results)
    {
        // Payload: [TestNumber:2][ErrorMessage:string]
        if (payload.Length < 2)
        {
            return;
        }

        int testNumber = BitConverter.ToUInt16(payload, 0);
        string errorMessage = Encoding.UTF8.GetString(payload, 2, payload.Length - 2);
        if (HasControlChars(errorMessage))
        {
            return;
        }

        // The kernel always emits TestStart before Pass/Fail/Skip, so a
        // number with no prior TestStart is a misaligned-frame artifact —
        // fabricating a result for it turns corruption into a phantom
        // "Test 51234" failure. A genuinely lost test still fails the run
        // via the expected-count gap ("Test did not execute").
        TestResult? test = FindTestResult(results, testNumber);
        if (test == null)
        {
            return;
        }

        test.Status = TestStatus.Failed;
        test.ErrorMessage = errorMessage;
    }

    private static void ParseTestSkip(byte[] payload, TestResults results)
    {
        // Payload: [TestNumber:2][Reason:string]
        if (payload.Length < 2)
        {
            return;
        }

        int testNumber = BitConverter.ToUInt16(payload, 0);
        string reason = Encoding.UTF8.GetString(payload, 2, payload.Length - 2);
        if (HasControlChars(reason))
        {
            return;
        }

        TestResult? test = FindTestResult(results, testNumber);
        if (test == null)
        {
            return;
        }

        test.Status = TestStatus.Skipped;
        test.ErrorMessage = reason;
    }

    private static void ParseTestSuiteEnd(byte[] payload, TestResults results)
    {
        // Payload: [Total:2][Passed:2][Failed:2][Skipped:2]
        if (payload.Length < 8)
        {
            return;
        }

        ushort total = BitConverter.ToUInt16(payload, 0);
        ushort passed = BitConverter.ToUInt16(payload, 2);
        ushort failed = BitConverter.ToUInt16(payload, 4);
        ushort skipped = BitConverter.ToUInt16(payload, 6);

        // Validate: total must equal passed + failed + skipped (catches corruption
        // from timer interrupt interleaving). Skips count: TR.Finish reports the
        // expected total, and a suite with skip cells is still a completed suite.
        if (total == (ushort)(passed + failed + skipped))
        {
            // Use the validated total from the end message as the authoritative expected count.
            // This overrides the potentially corrupted value from TestSuiteStart.
            results.ExpectedTestCount = total;
            results.SuiteCompleted = true;
        }
    }

    private static void ParseCoverageData(byte[] payload, TestResults results)
    {
        // Payload: [HitCount:2][HitId1:2][HitId2:2]...
        if (payload.Length < 2)
        {
            return;
        }

        ushort hitCount = BitConverter.ToUInt16(payload, 0);

        Console.WriteLine($"[UartParser] Coverage data: {hitCount} methods hit");

        for (int i = 0; i < hitCount && (2 + i * 2 + 1) < payload.Length; i++)
        {
            ushort methodId = BitConverter.ToUInt16(payload, 2 + i * 2);
            results.CoverageHitMethodIds.Add(methodId);
        }
    }

    private static TestResult? FindTestResult(TestResults results, int testNumber)
        => results.Tests.Find(t => t.TestNumber == testNumber);

    // Real protocol strings are ASCII identifiers / prose; anything below
    // 0x20 means the frame was assembled from interleaved UART bytes.
    private static bool HasControlChars(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] < 0x20)
            {
                return true;
            }
        }

        return false;
    }
}
