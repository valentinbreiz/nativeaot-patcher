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
        /// <summary>Milliseconds per second; converts a Stopwatch tick delta divided by frequency into ms. Public so test kernels can share the same conversion factor.</summary>
        public const int MillisecondsPerSecond = 1000;
        /// <summary>Upper bound (5 minutes, in ms) for a plausible single-test duration; larger raw readings are clamped (QEMU TCG CNTPCT_EL0 anomaly).</summary>
        private const long MaxSaneDurationMs = 5L * 60L * 1000L;
        /// <summary>Length of the "skip=" token skipped over when parsing the Limine cmdline.</summary>
        private const int SkipTokenLength = 5;
        /// <summary>Length of the "profile=" token skipped over when parsing the Limine cmdline.</summary>
        private const int ProfileTokenLength = 8;
        /// <summary>Base of the decimal number system, used when accumulating parsed digits.</summary>
        private const int DecimalBase = 10;

        /// <summary>QEMU kill marker byte 0 of the 0xDE 0xAD 0xBE 0xEF 0xCA 0xFE 0xBA 0xBE end sequence.</summary>
        private const byte QemuKillMarkerByte0 = 0xDE;
        /// <summary>QEMU kill marker byte 1 of the end sequence.</summary>
        private const byte QemuKillMarkerByte1 = 0xAD;
        /// <summary>QEMU kill marker byte 2 of the end sequence.</summary>
        private const byte QemuKillMarkerByte2 = 0xBE;
        /// <summary>QEMU kill marker byte 3 of the end sequence.</summary>
        private const byte QemuKillMarkerByte3 = 0xEF;
        /// <summary>QEMU kill marker byte 4 of the end sequence.</summary>
        private const byte QemuKillMarkerByte4 = 0xCA;
        /// <summary>QEMU kill marker byte 5 of the end sequence.</summary>
        private const byte QemuKillMarkerByte5 = 0xFE;
        /// <summary>QEMU kill marker byte 6 of the end sequence.</summary>
        private const byte QemuKillMarkerByte6 = 0xBA;
        /// <summary>QEMU kill marker byte 7 of the end sequence.</summary>
        private const byte QemuKillMarkerByte7 = 0xBE;

        private static string? _currentSuite;
        private static ushort _testCount;
        private static ushort _expectedTestCount;
        private static ushort _passedCount;
        private static ushort _failedCount;
        private static ushort _skippedCount;
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
            _skippedCount = 0;
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

            // Calculate duration. The raw CNTPCT_EL0 delta has produced
            // multi-million-second readings on github-CI arm64 (QEMU TCG)
            // for a sub-second test, propagating into the JUnit XML as
            // bogus times. When that happens, clamp to a sane max and
            // emit a UART warning with the raw inputs so the cause can be
            // debugged from the log.
            var endTicks = Stopwatch.GetTimestamp();
            var elapsedTicks = endTicks - _testStartTicks;
            long freq = Stopwatch.Frequency;
            long rawMs = (freq > 0 && elapsedTicks > 0)
                ? (elapsedTicks * MillisecondsPerSecond) / freq
                : 0;

            uint durationMs;
            if (rawMs < 0 || rawMs > MaxSaneDurationMs)
            {
                Serial.WriteString("[TestRunner] WARN clamped durationMs=");
                Serial.WriteNumber(rawMs);
                Serial.WriteString(" startTicks=");
                Serial.WriteNumber(_testStartTicks);
                Serial.WriteString(" endTicks=");
                Serial.WriteNumber(endTicks);
                Serial.WriteString(" elapsedTicks=");
                Serial.WriteNumber(elapsedTicks);
                Serial.WriteString(" freq=");
                Serial.WriteNumber(freq);
                Serial.WriteString("\n");
                durationMs = (uint)MaxSaneDurationMs;
            }
            else
            {
                durationMs = (uint)rawMs;
            }

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
        /// Run <paramref name="testAction"/> only when <paramref name="condition"/> is
        /// true; otherwise emit a <see cref="Skip(string, string)"/> with
        /// <paramref name="skipReason"/>. Use to gate a test on a feature that may or
        /// may not be present in the current QEMU profile (specific device kind, MSI-X
        /// capability, GIC version) — keeps the test in the report as Skipped instead
        /// of either silently disappearing or failing for a reason that's not a code
        /// regression.
        /// </summary>
        public static void RunIf(bool condition, string testName, Action testAction, string skipReason)
        {
            if (condition)
            {
                Run(testName, testAction);
            }
            else
            {
                Skip(testName, skipReason);
            }
        }

        /// <summary>
        /// Run a test that adapts to a capability instead of skipping. The
        /// <paramref name="condition"/> is passed into <paramref name="test"/>
        /// so the body can assert the capable path when true and the fallback
        /// path when false — both branches stay in the report as a real run.
        /// Use this when the cell always has something to assert but the
        /// expected outcome differs by profile (e.g. NVMe MSI-X vs polled),
        /// as opposed to <see cref="RunIf(bool, string, Action, string)"/>
        /// which reports a Skip when the feature is simply absent. Named
        /// distinctly from RunIf on purpose: an overload on delegate arity
        /// would silently flip between gate-execution and pass-the-flag
        /// semantics when a test method's signature changes.
        /// </summary>
        public static void RunWithExpectation(bool expectation, string testName, Action<bool> test)
        {
            Run(testName, () => test(expectation));
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
                    p += SkipTokenLength;
                    int value = 0;
                    while (*p >= (byte)'0' && *p <= (byte)'9')
                    {
                        value = value * DecimalBase + (*p - (byte)'0');
                        p++;
                    }
                    return value;
                }
                p++;
            }
            return 0;
        }

        /// <summary>
        /// Reads the <c>profile=&lt;name&gt;</c> token from the Limine kernel
        /// cmdline. The test engine sets this to the active QEMU profile-matrix
        /// cell name (e.g. <c>nvme+gicv3</c> or <c>nvme+gicv2+acpi-off</c>) so a
        /// suite can assert the hardware path that cell was meant to exercise.
        /// Returns an empty string when no profile token is present (a suite
        /// that opts into no profiles, or a non-test boot).
        /// </summary>
        public static unsafe string GetProfileName()
        {
            byte* cmdline = Limine.Cmdline;
            if (cmdline == null)
            {
                return string.Empty;
            }

            // Walk the null-terminated cmdline looking for "profile=" then read
            // the value up to the next space or the terminating null. The value
            // may contain '+' (composed cell names), which is preserved.
            byte* p = cmdline;
            while (*p != 0)
            {
                if (p[0] == (byte)'p' && p[1] == (byte)'r' && p[2] == (byte)'o' &&
                    p[3] == (byte)'f' && p[4] == (byte)'i' && p[5] == (byte)'l' &&
                    p[6] == (byte)'e' && p[7] == (byte)'=')
                {
                    p += ProfileTokenLength;
                    int len = 0;
                    while (p[len] != 0 && p[len] != (byte)' ')
                    {
                        len++;
                    }
                    char[] chars = new char[len];
                    for (int i = 0; i < len; i++)
                    {
                        chars[i] = (char)p[i];
                    }
                    return new string(chars);
                }
                p++;
            }
            return string.Empty;
        }

        // Cached profile cell name backing the prefix/contains helpers; reading
        // the Limine cmdline is cheap but they are called repeatedly. Each
        // matrix cell is a fresh boot, so the static starts empty per cell.
        private static string? s_profileName;

        /// <summary>
        /// The active profile-matrix cell name (cached <see cref="GetProfileName"/>),
        /// e.g. <c>nvme+gicv3</c>. Empty for a suite that opts into no profiles.
        /// </summary>
        public static string ProfileName
        {
            get
            {
                if (s_profileName == null)
                {
                    s_profileName = GetProfileName();
                }
                return s_profileName;
            }
        }

        /// <summary>
        /// True when the active profile cell name starts with <paramref name="prefix"/>.
        /// A cell name always leads with its base profile (e.g. the "nvme" in
        /// "nvme+gicv2"), so a prefix check identifies the profile. Lives here
        /// because the kernel runtime does not plug <c>string.StartsWith</c>.
        /// </summary>
        public static bool ProfileHasPrefix(string prefix)
        {
            string profile = ProfileName;
            if (profile.Length < prefix.Length)
            {
                return false;
            }
            for (int i = 0; i < prefix.Length; i++)
            {
                if (profile[i] != prefix[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// True when <paramref name="needle"/> occurs anywhere in the active
        /// profile cell name. Detects a composed modifier such as the "gicv2" in
        /// "nvme+gicv2+acpi-off". Lives here because the kernel runtime does not
        /// plug <c>string.Contains</c>.
        /// </summary>
        public static bool ProfileContains(string needle)
        {
            string profile = ProfileName;
            int limit = profile.Length - needle.Length;
            for (int i = 0; i <= limit; i++)
            {
                int j = 0;
                while (j < needle.Length && profile[i + j] == needle[j])
                {
                    j++;
                }
                if (j == needle.Length)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Skip a test
        /// </summary>
        public static void Skip(string testName, string reason)
        {
            _currentTestNumber++;
            _testCount++;

            _skippedCount++;
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

            SendTestSuiteEnd(totalToReport, _passedCount, _failedCount, _skippedCount);

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
            Serial.ComWrite(QemuKillMarkerByte0);
            Serial.ComWrite(QemuKillMarkerByte1);
            Serial.ComWrite(QemuKillMarkerByte2);
            Serial.ComWrite(QemuKillMarkerByte3);
            Serial.ComWrite(QemuKillMarkerByte4);
            Serial.ComWrite(QemuKillMarkerByte5);
            Serial.ComWrite(QemuKillMarkerByte6);
            Serial.ComWrite(QemuKillMarkerByte7);
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

        /// <summary>Byte 0 (least significant) of the protocol magic signature 0x19740807 (SerialSignature from Consts.cs), sent little-endian.</summary>
        private const byte SerialSignatureByte0 = 0x07;
        /// <summary>Byte 1 of the protocol magic signature 0x19740807.</summary>
        private const byte SerialSignatureByte1 = 0x08;
        /// <summary>Byte 2 of the protocol magic signature 0x19740807.</summary>
        private const byte SerialSignatureByte2 = 0x74;
        /// <summary>Byte 3 (most significant) of the protocol magic signature 0x19740807.</summary>
        private const byte SerialSignatureByte3 = 0x19;

        /// <summary>Mask isolating the low 8 bits when serializing multi-byte values little-endian.</summary>
        private const int ByteMask = 0xFF;
        /// <summary>Shift extracting byte 1 of a little-endian multi-byte value.</summary>
        private const int Byte1Shift = 8;
        /// <summary>Shift extracting byte 2 of a little-endian multi-byte value.</summary>
        private const int Byte2Shift = 16;
        /// <summary>Shift extracting byte 3 of a little-endian multi-byte value.</summary>
        private const int Byte3Shift = 24;

        /// <summary>Size in bytes of a little-endian ushort payload field (test number, expected count).</summary>
        private const int UInt16FieldSizeBytes = 2;
        /// <summary>Payload size of a TestPass message: ushort test number + uint duration in ms.</summary>
        private const int TestPassPayloadSizeBytes = 6;
        /// <summary>Payload size of a TestSuiteEnd message: four little-endian ushort counters (total, passed, failed, skipped).</summary>
        private const int SuiteEndPayloadSizeBytes = 8;

        /// <summary>
        /// Send a protocol message with format: [MAGIC:4][Command:1][Length:2][Payload:N]
        /// Magic signature = 0x19740807 (SerialSignature from Consts.cs)
        /// </summary>
        private static void SendMessage(byte command, byte[] payload)
        {
            // Send magic signature (0x19740807 little-endian)
            Serial.ComWrite(SerialSignatureByte0);
            Serial.ComWrite(SerialSignatureByte1);
            Serial.ComWrite(SerialSignatureByte2);
            Serial.ComWrite(SerialSignatureByte3);

            // Send command byte
            Serial.ComWrite(command);

            // Send length (little-endian ushort)
            ushort length = (ushort)payload.Length;
            Serial.ComWrite((byte)(length & ByteMask));
            Serial.ComWrite((byte)((length >> Byte1Shift) & ByteMask));

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
            var payload = new byte[UInt16FieldSizeBytes + nameBytes.Length];
            // First 2 bytes: expected test count
            payload[0] = (byte)(expectedTests & ByteMask);
            payload[1] = (byte)((expectedTests >> Byte1Shift) & ByteMask);
            // Rest: suite name
            Array.Copy(nameBytes, 0, payload, UInt16FieldSizeBytes, nameBytes.Length);
            SendMessage(TestSuiteStart, payload);
        }

        private static void SendTestStart(ushort testNumber, string testName)
        {
            var nameBytes = EncodeString(testName);
            var payload = new byte[UInt16FieldSizeBytes + nameBytes.Length];
            payload[0] = (byte)(testNumber & ByteMask);
            payload[1] = (byte)((testNumber >> Byte1Shift) & ByteMask);
            Array.Copy(nameBytes, 0, payload, UInt16FieldSizeBytes, nameBytes.Length);
            SendMessage(TestStart, payload);
        }

        private static void SendTestPass(ushort testNumber, uint durationMs)
        {
            var payload = new byte[TestPassPayloadSizeBytes];
            payload[0] = (byte)(testNumber & ByteMask);
            payload[1] = (byte)((testNumber >> Byte1Shift) & ByteMask);
            payload[2] = (byte)(durationMs & ByteMask);
            payload[3] = (byte)((durationMs >> Byte1Shift) & ByteMask);
            payload[4] = (byte)((durationMs >> Byte2Shift) & ByteMask);
            payload[5] = (byte)((durationMs >> Byte3Shift) & ByteMask);
            SendMessage(TestPass, payload);
        }

        private static void SendTestFail(ushort testNumber, string errorMessage)
        {
            var errorBytes = EncodeString(errorMessage);
            var payload = new byte[UInt16FieldSizeBytes + errorBytes.Length];
            payload[0] = (byte)(testNumber & ByteMask);
            payload[1] = (byte)((testNumber >> Byte1Shift) & ByteMask);
            Array.Copy(errorBytes, 0, payload, UInt16FieldSizeBytes, errorBytes.Length);
            SendMessage(TestFail, payload);
        }

        private static void SendTestSkip(ushort testNumber, string skipReason)
        {
            var reasonBytes = EncodeString(skipReason);
            var payload = new byte[UInt16FieldSizeBytes + reasonBytes.Length];
            payload[0] = (byte)(testNumber & ByteMask);
            payload[1] = (byte)((testNumber >> Byte1Shift) & ByteMask);
            Array.Copy(reasonBytes, 0, payload, UInt16FieldSizeBytes, reasonBytes.Length);
            SendMessage(TestSkip, payload);
        }

        private static void SendTestDestructiveReached(ushort testNumber)
        {
            var payload = new byte[UInt16FieldSizeBytes];
            payload[0] = (byte)(testNumber & ByteMask);
            payload[1] = (byte)((testNumber >> Byte1Shift) & ByteMask);
            SendMessage(TestDestructiveReached, payload);
        }

        private static void SendTestSuiteEnd(ushort total, ushort passed, ushort failed, ushort skipped)
        {
            var payload = new byte[SuiteEndPayloadSizeBytes];
            payload[0] = (byte)(total & ByteMask);
            payload[1] = (byte)((total >> Byte1Shift) & ByteMask);
            payload[2] = (byte)(passed & ByteMask);
            payload[3] = (byte)((passed >> Byte1Shift) & ByteMask);
            payload[4] = (byte)(failed & ByteMask);
            payload[5] = (byte)((failed >> Byte1Shift) & ByteMask);
            payload[6] = (byte)(skipped & ByteMask);
            payload[7] = (byte)((skipped >> Byte1Shift) & ByteMask);
            SendMessage(TestSuiteEnd, payload);
        }

        #endregion
    }
}
