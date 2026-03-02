using System;
using System.Diagnostics;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.TestRunner.Framework
{
    /// <summary>
    /// Test runner for kernel-side test execution.
    /// Sends test results via UART using the binary protocol.
    /// </summary>
    public static class TestRunner
    {
        private static string? _currentSuite;
        private static ushort _testCount;
        private static ushort _expectedTestCount;
        private static ushort _passedCount;
        private static ushort _failedCount;
        private static ushort _currentTestNumber;
        private static long _testStartTicks;

        /// <summary>
        /// Start a test suite
        /// </summary>
        /// <param name="suiteName">Name of the test suite</param>
        /// <param name="expectedTests">Total number of tests that will be registered (0 = unknown)</param>
        public static void Start(string suiteName, ushort expectedTests = 0)
        {
            _currentSuite = suiteName;
            _testCount = 0;
            _expectedTestCount = expectedTests;
            _passedCount = 0;
            _failedCount = 0;
            _currentTestNumber = 0;

            // Send TestSuiteStart message with expected test count
            SendTestSuiteStart(suiteName, expectedTests);
        }

        /// <summary>
        /// Run a test with automatic failure detection
        /// </summary>
        public static void Run(string testName, Action testAction)
        {
            _currentTestNumber++;
            _testCount++;

            // Send TestStart message
            SendTestStart(_currentTestNumber, testName);

            // Reset assertion state
            Assert.Reset();

            // Record start time
            _testStartTicks = Stopwatch.GetTimestamp();

            // Execute test
            testAction();

            // Calculate duration
            var endTicks = Stopwatch.GetTimestamp();
            var elapsedTicks = endTicks - _testStartTicks;
            var durationMs = (uint)((elapsedTicks * 1000) / Stopwatch.Frequency);

            // Check if test failed via Assert
            if (Assert.Failed)
            {
                _failedCount++;
                SendTestFail(_currentTestNumber, Assert.FailureMessage ?? "Test failed");
            }
            else
            {
                _passedCount++;
                SendTestPass(_currentTestNumber, durationMs);
            }
        }

        /// <summary>
        /// Skip a test
        /// </summary>
        public static void Skip(string testName, string reason)
        {
            _currentTestNumber++;
            _testCount++;

            SendTestStart(_currentTestNumber, testName);
            SendTestSkip(_currentTestNumber, reason);
        }

        /// <summary>
        /// Finish the test suite and send summary
        /// </summary>
        public static void Finish()
        {
            // Use expected count if provided, otherwise actual count
            ushort totalToReport = _expectedTestCount > 0 ? _expectedTestCount : _testCount;
            SendTestSuiteEnd(totalToReport, _passedCount, _failedCount);

            // Also send a text message for fallback/debugging
            Serial.WriteString("\nTest Suite: ");
            Serial.WriteString(_currentSuite ?? "Unknown");
            Serial.WriteString("\nTotal: ");
            Serial.WriteNumber(_testCount);
            if (_expectedTestCount > 0 && _expectedTestCount != _testCount)
            {
                Serial.WriteString(" / ");
                Serial.WriteNumber(_expectedTestCount);
                Serial.WriteString(" expected");
            }
            Serial.WriteString("  Passed: ");
            Serial.WriteNumber(_passedCount);
            Serial.WriteString("  Failed: ");
            Serial.WriteNumber(_failedCount);
            Serial.WriteString("\n");
        }

        #region Protocol Message Sending

        // Protocol constants (must match Cosmos.TestRunner.Protocol/Consts.cs)
        private const byte TestSuiteStart = 100;
        private const byte TestStart = 101;
        private const byte TestPass = 102;
        private const byte TestFail = 103;
        private const byte TestSkip = 104;
        private const byte TestSuiteEnd = 105;
        private const byte TestRegister = 106; // New: register a test before execution

        /// <summary>
        /// Send a protocol message with format: [MAGIC:4][Command:1][Length:2][Payload:N]
        /// Magic signature = 0x19740807 (SerialSignature from Consts.cs)
        /// </summary>
        private static void SendMessage(byte command, byte[] payload)
        {
            // Send magic signature (0x19740807 little-endian)
            Serial.ComWrite(0x07);
            Serial.ComWrite(0x08);
            Serial.ComWrite(0x74);
            Serial.ComWrite(0x19);

            // Send command byte
            Serial.ComWrite(command);

            // Send length (little-endian ushort)
            ushort length = (ushort)payload.Length;
            Serial.ComWrite((byte)(length & 0xFF));
            Serial.ComWrite((byte)((length >> 8) & 0xFF));

            // Send payload
            foreach (var b in payload)
            {
                Serial.ComWrite(b);
            }
        }

        /// <summary>
        /// Encode string to UTF-8 bytes (simplified, assumes ASCII for kernel)
        /// </summary>
        private static byte[] EncodeString(string str)
        {
            var bytes = new byte[str.Length];
            for (int i = 0; i < str.Length; i++)
            {
                bytes[i] = (byte)str[i]; // ASCII only for simplicity
            }
            return bytes;
        }

        private static void SendTestSuiteStart(string suiteName, ushort expectedTests)
        {
            var nameBytes = EncodeString(suiteName);
            var payload = new byte[2 + nameBytes.Length];
            // First 2 bytes: expected test count
            payload[0] = (byte)(expectedTests & 0xFF);
            payload[1] = (byte)((expectedTests >> 8) & 0xFF);
            // Rest: suite name
            Array.Copy(nameBytes, 0, payload, 2, nameBytes.Length);
            SendMessage(TestSuiteStart, payload);
        }

        private static void SendTestStart(ushort testNumber, string testName)
        {
            var nameBytes = EncodeString(testName);
            var payload = new byte[2 + nameBytes.Length];
            payload[0] = (byte)(testNumber & 0xFF);
            payload[1] = (byte)((testNumber >> 8) & 0xFF);
            Array.Copy(nameBytes, 0, payload, 2, nameBytes.Length);
            SendMessage(TestStart, payload);
        }

        private static void SendTestPass(ushort testNumber, uint durationMs)
        {
            var payload = new byte[6];
            payload[0] = (byte)(testNumber & 0xFF);
            payload[1] = (byte)((testNumber >> 8) & 0xFF);
            payload[2] = (byte)(durationMs & 0xFF);
            payload[3] = (byte)((durationMs >> 8) & 0xFF);
            payload[4] = (byte)((durationMs >> 16) & 0xFF);
            payload[5] = (byte)((durationMs >> 24) & 0xFF);
            SendMessage(TestPass, payload);
        }

        private static void SendTestFail(ushort testNumber, string errorMessage)
        {
            var errorBytes = EncodeString(errorMessage);
            var payload = new byte[2 + errorBytes.Length];
            payload[0] = (byte)(testNumber & 0xFF);
            payload[1] = (byte)((testNumber >> 8) & 0xFF);
            Array.Copy(errorBytes, 0, payload, 2, errorBytes.Length);
            SendMessage(TestFail, payload);
        }

        private static void SendTestSkip(ushort testNumber, string skipReason)
        {
            var reasonBytes = EncodeString(skipReason);
            var payload = new byte[2 + reasonBytes.Length];
            payload[0] = (byte)(testNumber & 0xFF);
            payload[1] = (byte)((testNumber >> 8) & 0xFF);
            Array.Copy(reasonBytes, 0, payload, 2, reasonBytes.Length);
            SendMessage(TestSkip, payload);
        }

        private static void SendTestSuiteEnd(ushort total, ushort passed, ushort failed)
        {
            var payload = new byte[6];
            payload[0] = (byte)(total & 0xFF);
            payload[1] = (byte)((total >> 8) & 0xFF);
            payload[2] = (byte)(passed & 0xFF);
            payload[3] = (byte)((passed >> 8) & 0xFF);
            payload[4] = (byte)(failed & 0xFF);
            payload[5] = (byte)((failed >> 8) & 0xFF);
            SendMessage(TestSuiteEnd, payload);

            // Send unique end marker: 0xDE 0xAD 0xBE 0xEF 0xCA 0xFE 0xBA 0xBE
            // This sequence is unlikely to appear in normal text output
            Serial.ComWrite(0xDE);
            Serial.ComWrite(0xAD);
            Serial.ComWrite(0xBE);
            Serial.ComWrite(0xEF);
            Serial.ComWrite(0xCA);
            Serial.ComWrite(0xFE);
            Serial.ComWrite(0xBA);
            Serial.ComWrite(0xBE);
        }

        #endregion
    }
}
