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
        private static ushort _passedCount;
        private static ushort _failedCount;
        private static ushort _currentTestNumber;
        private static long _testStartTicks;

        /// <summary>
        /// Start a test suite
        /// </summary>
        public static void Start(string suiteName)
        {
            _currentSuite = suiteName;
            _testCount = 0;
            _passedCount = 0;
            _failedCount = 0;
            _currentTestNumber = 0;

            // Send TestSuiteStart message
            SendTestSuiteStart(suiteName);
        }

        /// <summary>
        /// Run a test with automatic exception handling
        /// </summary>
        public static void Run(string testName, Action testAction)
        {
            _currentTestNumber++;
            _testCount++;

            // Send TestStart message
            SendTestStart(_currentTestNumber, testName);

            // Record start time
            _testStartTicks = Stopwatch.GetTimestamp();

            try
            {
                // Execute test
                testAction();

                // Calculate duration
                var endTicks = Stopwatch.GetTimestamp();
                var elapsedTicks = endTicks - _testStartTicks;
                var durationMs = (uint)((elapsedTicks * 1000) / Stopwatch.Frequency);

                // Test passed
                _passedCount++;
                SendTestPass(_currentTestNumber, durationMs);
            }
            catch (AssertionException ex)
            {
                // Test failed with assertion
                _failedCount++;
                SendTestFail(_currentTestNumber, ex.Message);
            }
            catch (Exception ex)
            {
                // Test failed with unexpected exception
                _failedCount++;
                SendTestFail(_currentTestNumber, $"Unexpected exception: {ex.GetType().Name}: {ex.Message}");
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
            SendTestSuiteEnd(_testCount, _passedCount, _failedCount);

            // Also send a text message for fallback/debugging
            Serial.WriteString($"\nTest Suite: {_currentSuite ?? "Unknown"}\n");
            Serial.WriteString($"Total: {_testCount}  Passed: {_passedCount}  Failed: {_failedCount}\n");
        }

        #region Protocol Message Sending

        // Protocol constants (must match Cosmos.TestRunner.Protocol/Consts.cs)
        private const byte TestSuiteStart = 100;
        private const byte TestStart = 101;
        private const byte TestPass = 102;
        private const byte TestFail = 103;
        private const byte TestSkip = 104;
        private const byte TestSuiteEnd = 105;

        /// <summary>
        /// Send a protocol message with format: [Command:1][Length:2][Payload:N]
        /// </summary>
        private static void SendMessage(byte command, byte[] payload)
        {
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

        private static void SendTestSuiteStart(string suiteName)
        {
            var payload = EncodeString(suiteName);
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
        }

        #endregion
    }

    /// <summary>
    /// Exception thrown when an assertion fails
    /// </summary>
    public class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }
}
