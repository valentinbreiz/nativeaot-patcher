using System;
using System.Diagnostics;
using Cosmos.Kernel.Boot.Limine;
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
        /// Run a destructive test whose action is expected to never return
        /// (e.g. a successful Power.Reboot / Power.Shutdown). The test is
        /// pre-emptively reported as passed before invoking the action; if
        /// the action returns the pre-emptive pass is overridden by a fail
        /// message and the call returns normally so the suite can finalise.
        /// </summary>
        public static void RunDestructive(string testName, Action testAction, string failureMessage)
        {
            _currentTestNumber++;
            _testCount++;

            // Pre-send TestStart + TestPass so a successful destructive op
            // (which never returns) still leaves a passing record in the log.
            SendTestStart(_currentTestNumber, testName);
            SendTestPass(_currentTestNumber, 0);
            _passedCount++;

            // Distinct sentinel for the engine's re-launch heuristic. A regular
            // TestPass alone is ambiguous (every passing test emits one), so
            // without this the engine would misread a mid-suite crash as a
            // destructive op and burn boot attempts on skip=N+1 re-launches.
            SendTestDestructiveReached(_currentTestNumber);

            testAction();

            // Action returned — destructive op didn't fire. Demote to fail
            // (last write wins in the parser).
            _passedCount--;
            _failedCount++;
            SendTestFail(_currentTestNumber, failureMessage);
        }

        /// <summary>
        /// Reads the <c>skip=N</c> integer from the Limine kernel cmdline.
        /// The test runner sets this on each re-launch when a previous boot
        /// fired a test that exited QEMU (Reboot, Shutdown). Returns 0 if
        /// the cmdline is missing or has no <c>skip=</c> token (default
        /// first-boot behaviour).
        /// </summary>
        public static unsafe int GetSkipCount()
        {
            byte* cmdline = Limine.Cmdline;
            if (cmdline == null)
            {
                return 0;
            }

            // Walk the null-terminated cmdline looking for "skip=" then digits.
            byte* p = cmdline;
            while (*p != 0)
            {
                if (p[0] == (byte)'s' && p[1] == (byte)'k' && p[2] == (byte)'i' &&
                    p[3] == (byte)'p' && p[4] == (byte)'=')
                {
                    p += 5;
                    int value = 0;
                    while (*p >= (byte)'0' && *p <= (byte)'9')
                    {
                        value = value * 10 + (*p - (byte)'0');
                        p++;
                    }
                    return value;
                }
                p++;
            }
            return 0;
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
        /// Finish the test suite and send summary.
        /// Does NOT flush coverage or send the QEMU kill marker.
        /// Call Complete() after AfterRun() for that.
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

        /// <summary>
        /// Final step: flush coverage data and send the QEMU termination marker.
        /// Call this in AfterRun() so that Run() and AfterRun() are covered.
        /// After this call, the test engine will kill QEMU.
        /// </summary>
        public static void Complete()
        {
            // Flush coverage data (no-op if not instrumented)
            CoverageTracker.Flush();

            // Send unique end marker: 0xDE 0xAD 0xBE 0xEF 0xCA 0xFE 0xBA 0xBE
            // This sequence tells the QEMU host to kill the VM
            Serial.ComWrite(0xDE);
            Serial.ComWrite(0xAD);
            Serial.ComWrite(0xBE);
            Serial.ComWrite(0xEF);
            Serial.ComWrite(0xCA);
            Serial.ComWrite(0xFE);
            Serial.ComWrite(0xBA);
            Serial.ComWrite(0xBE);
        }

        #region Protocol Message Sending

        // Protocol constants (must match Cosmos.TestRunner.Protocol/Consts.cs)
        private const byte TestSuiteStart = 100;
        private const byte TestStart = 101;
        private const byte TestPass = 102;
        private const byte TestFail = 103;
        private const byte TestSkip = 104;
        private const byte TestSuiteEnd = 105;
        private const byte TestDestructiveReached = 108;

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

        private static void SendTestDestructiveReached(ushort testNumber)
        {
            var payload = new byte[2];
            payload[0] = (byte)(testNumber & 0xFF);
            payload[1] = (byte)((testNumber >> 8) & 0xFF);
            SendMessage(TestDestructiveReached, payload);
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
}
