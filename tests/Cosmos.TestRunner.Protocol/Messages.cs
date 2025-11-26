using System;
using System.Text;

namespace Cosmos.TestRunner.Protocol
{
    /// <summary>
    /// Base class for all protocol messages
    /// Message format: [Command:1][Length:2][Payload:N]
    /// </summary>
    public abstract class ProtocolMessage
    {
        public abstract byte Command { get; }
        public abstract byte[] GetPayload();

        /// <summary>
        /// Serialize message to byte array
        /// </summary>
        public byte[] Serialize()
        {
            var payload = GetPayload();
            var length = (ushort)payload.Length;

            var result = new byte[3 + payload.Length];
            result[0] = Command;
            result[1] = (byte)(length & 0xFF);        // Little-endian
            result[2] = (byte)((length >> 8) & 0xFF);
            Array.Copy(payload, 0, result, 3, payload.Length);

            return result;
        }
    }

    /// <summary>
    /// Test suite started
    /// </summary>
    public class TestSuiteStartMessage : ProtocolMessage
    {
        public override byte Command => Ds2Vs.TestSuiteStart;
        public string SuiteName { get; set; } = "";

        public TestSuiteStartMessage() { }
        public TestSuiteStartMessage(string suiteName)
        {
            SuiteName = suiteName;
        }

        public override byte[] GetPayload()
        {
            return Encoding.UTF8.GetBytes(SuiteName);
        }

        public static TestSuiteStartMessage Deserialize(byte[] payload)
        {
            return new TestSuiteStartMessage(Encoding.UTF8.GetString(payload));
        }
    }

    /// <summary>
    /// Individual test started
    /// </summary>
    public class TestStartMessage : ProtocolMessage
    {
        public override byte Command => Ds2Vs.TestStart;
        public ushort TestNumber { get; set; }
        public string TestName { get; set; } = "";

        public TestStartMessage() { }
        public TestStartMessage(ushort testNumber, string testName)
        {
            TestNumber = testNumber;
            TestName = testName;
        }

        public override byte[] GetPayload()
        {
            var nameBytes = Encoding.UTF8.GetBytes(TestName);
            var result = new byte[2 + nameBytes.Length];
            result[0] = (byte)(TestNumber & 0xFF);
            result[1] = (byte)((TestNumber >> 8) & 0xFF);
            Array.Copy(nameBytes, 0, result, 2, nameBytes.Length);
            return result;
        }

        public static TestStartMessage Deserialize(byte[] payload)
        {
            if (payload.Length < 2)
                throw new ArgumentException("Invalid TestStart payload length");

            var testNumber = (ushort)(payload[0] | (payload[1] << 8));
            var testName = Encoding.UTF8.GetString(payload, 2, payload.Length - 2);
            return new TestStartMessage(testNumber, testName);
        }
    }

    /// <summary>
    /// Test passed
    /// </summary>
    public class TestPassMessage : ProtocolMessage
    {
        public override byte Command => Ds2Vs.TestPass;
        public ushort TestNumber { get; set; }
        public uint DurationMs { get; set; }

        public TestPassMessage() { }
        public TestPassMessage(ushort testNumber, uint durationMs)
        {
            TestNumber = testNumber;
            DurationMs = durationMs;
        }

        public override byte[] GetPayload()
        {
            var result = new byte[6];
            result[0] = (byte)(TestNumber & 0xFF);
            result[1] = (byte)((TestNumber >> 8) & 0xFF);
            result[2] = (byte)(DurationMs & 0xFF);
            result[3] = (byte)((DurationMs >> 8) & 0xFF);
            result[4] = (byte)((DurationMs >> 16) & 0xFF);
            result[5] = (byte)((DurationMs >> 24) & 0xFF);
            return result;
        }

        public static TestPassMessage Deserialize(byte[] payload)
        {
            if (payload.Length != 6)
                throw new ArgumentException("Invalid TestPass payload length");

            var testNumber = (ushort)(payload[0] | (payload[1] << 8));
            var duration = (uint)(payload[2] | (payload[3] << 8) | (payload[4] << 16) | (payload[5] << 24));
            return new TestPassMessage(testNumber, duration);
        }
    }

    /// <summary>
    /// Test failed
    /// </summary>
    public class TestFailMessage : ProtocolMessage
    {
        public override byte Command => Ds2Vs.TestFail;
        public ushort TestNumber { get; set; }
        public string ErrorMessage { get; set; } = "";

        public TestFailMessage() { }
        public TestFailMessage(ushort testNumber, string errorMessage)
        {
            TestNumber = testNumber;
            ErrorMessage = errorMessage;
        }

        public override byte[] GetPayload()
        {
            var errorBytes = Encoding.UTF8.GetBytes(ErrorMessage);
            var result = new byte[2 + errorBytes.Length];
            result[0] = (byte)(TestNumber & 0xFF);
            result[1] = (byte)((TestNumber >> 8) & 0xFF);
            Array.Copy(errorBytes, 0, result, 2, errorBytes.Length);
            return result;
        }

        public static TestFailMessage Deserialize(byte[] payload)
        {
            if (payload.Length < 2)
                throw new ArgumentException("Invalid TestFail payload length");

            var testNumber = (ushort)(payload[0] | (payload[1] << 8));
            var errorMessage = Encoding.UTF8.GetString(payload, 2, payload.Length - 2);
            return new TestFailMessage(testNumber, errorMessage);
        }
    }

    /// <summary>
    /// Test skipped
    /// </summary>
    public class TestSkipMessage : ProtocolMessage
    {
        public override byte Command => Ds2Vs.TestSkip;
        public ushort TestNumber { get; set; }
        public string SkipReason { get; set; } = "";

        public TestSkipMessage() { }
        public TestSkipMessage(ushort testNumber, string skipReason)
        {
            TestNumber = testNumber;
            SkipReason = skipReason;
        }

        public override byte[] GetPayload()
        {
            var reasonBytes = Encoding.UTF8.GetBytes(SkipReason);
            var result = new byte[2 + reasonBytes.Length];
            result[0] = (byte)(TestNumber & 0xFF);
            result[1] = (byte)((TestNumber >> 8) & 0xFF);
            Array.Copy(reasonBytes, 0, result, 2, reasonBytes.Length);
            return result;
        }

        public static TestSkipMessage Deserialize(byte[] payload)
        {
            if (payload.Length < 2)
                throw new ArgumentException("Invalid TestSkip payload length");

            var testNumber = (ushort)(payload[0] | (payload[1] << 8));
            var skipReason = Encoding.UTF8.GetString(payload, 2, payload.Length - 2);
            return new TestSkipMessage(testNumber, skipReason);
        }
    }

    /// <summary>
    /// Test suite ended
    /// </summary>
    public class TestSuiteEndMessage : ProtocolMessage
    {
        public override byte Command => Ds2Vs.TestSuiteEnd;
        public ushort TotalTests { get; set; }
        public ushort PassedTests { get; set; }
        public ushort FailedTests { get; set; }

        public TestSuiteEndMessage() { }
        public TestSuiteEndMessage(ushort total, ushort passed, ushort failed)
        {
            TotalTests = total;
            PassedTests = passed;
            FailedTests = failed;
        }

        public override byte[] GetPayload()
        {
            var result = new byte[6];
            result[0] = (byte)(TotalTests & 0xFF);
            result[1] = (byte)((TotalTests >> 8) & 0xFF);
            result[2] = (byte)(PassedTests & 0xFF);
            result[3] = (byte)((PassedTests >> 8) & 0xFF);
            result[4] = (byte)(FailedTests & 0xFF);
            result[5] = (byte)((FailedTests >> 8) & 0xFF);
            return result;
        }

        public static TestSuiteEndMessage Deserialize(byte[] payload)
        {
            if (payload.Length != 6)
                throw new ArgumentException("Invalid TestSuiteEnd payload length");

            var total = (ushort)(payload[0] | (payload[1] << 8));
            var passed = (ushort)(payload[2] | (payload[3] << 8));
            var failed = (ushort)(payload[4] | (payload[5] << 8));
            return new TestSuiteEndMessage(total, passed, failed);
        }
    }

    /// <summary>
    /// Architecture information
    /// </summary>
    public class ArchitectureInfoMessage : ProtocolMessage
    {
        public override byte Command => Ds2Vs.ArchitectureInfo;
        public Architecture ArchitectureId { get; set; }
        public byte CpuCount { get; set; }

        public ArchitectureInfoMessage() { }
        public ArchitectureInfoMessage(Architecture arch, byte cpuCount)
        {
            ArchitectureId = arch;
            CpuCount = cpuCount;
        }

        public override byte[] GetPayload()
        {
            return new byte[] { (byte)ArchitectureId, CpuCount };
        }

        public static ArchitectureInfoMessage Deserialize(byte[] payload)
        {
            if (payload.Length != 2)
                throw new ArgumentException("Invalid ArchitectureInfo payload length");

            return new ArchitectureInfoMessage((Architecture)payload[0], payload[1]);
        }
    }

    /// <summary>
    /// Simple text message (from original CosmosOS protocol)
    /// </summary>
    public class TextMessage : ProtocolMessage
    {
        public override byte Command => Ds2Vs.Message;
        public string Text { get; set; } = "";

        public TextMessage() { }
        public TextMessage(string text)
        {
            Text = text;
        }

        public override byte[] GetPayload()
        {
            return Encoding.UTF8.GetBytes(Text);
        }

        public static TextMessage Deserialize(byte[] payload)
        {
            return new TextMessage(Encoding.UTF8.GetString(payload));
        }
    }
}
