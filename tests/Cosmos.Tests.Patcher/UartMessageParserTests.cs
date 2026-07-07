using System.Collections.Generic;
using System.Text;
using Cosmos.TestRunner.Engine;
using Cosmos.TestRunner.Engine.Protocol;
using Cosmos.TestRunner.Protocol;

namespace Cosmos.Tests.Patcher;

[Collection("PatcherTests")]
public class UartMessageParserTests
{
    [Fact]
    public void ParseUartLog_DoesNotLoseFramesAfterArchitectureInfoMessage()
    {
        List<byte> stream = new();
        stream.AddRange(CreateFrame(Ds2Vs.TestSuiteStart, [1, 0, (byte)'S', (byte)'u', (byte)'i', (byte)'t', (byte)'e']));
        stream.AddRange(CreateFrame(Ds2Vs.ArchitectureInfo, [2, 1]));
        stream.AddRange(CreateFrame(Ds2Vs.TestStart, [1, 0, (byte)'A']));
        stream.AddRange(CreateFrame(Ds2Vs.TestPass, [1, 0, 5, 0, 0, 0]));
        stream.AddRange(CreateFrame(Ds2Vs.TestSuiteEnd, [1, 0, 1, 0, 0, 0, 0, 0]));

        string uartLog = Encoding.Latin1.GetString(stream.ToArray());

        TestResults results = UartMessageParser.ParseUartLog(uartLog, "x64");

        Assert.True(results.SuiteCompleted);
        Assert.Equal(1, results.TotalTests);
        Assert.Single(results.Tests);
        Assert.Equal(1, results.PassedTests);
        Assert.Equal(0, results.FailedTests);
    }

    [Fact]
    public void ParseUartLog_CreatesTestEntryWhenPassArrivesWithoutStart()
    {
        List<byte> stream = new();
        stream.AddRange(CreateFrame(Ds2Vs.TestSuiteStart, [1, 0, (byte)'S', (byte)'u', (byte)'i', (byte)'t', (byte)'e']));
        stream.AddRange(CreateFrame(Ds2Vs.TestPass, [1, 0, 5, 0, 0, 0]));
        stream.AddRange(CreateFrame(Ds2Vs.TestSuiteEnd, [1, 0, 1, 0, 0, 0, 0, 0]));

        string uartLog = Encoding.Latin1.GetString(stream.ToArray());
        TestResults results = UartMessageParser.ParseUartLog(uartLog, "x64");

        Assert.True(results.SuiteCompleted);
        Assert.Single(results.Tests);
        Assert.Equal(1, results.PassedTests);
        Assert.Equal("Test 1", results.Tests[0].TestName);
    }

    [Fact]
    public void ParseUartLog_UpdatesPlaceholderWhenStartArrivesAfterPass()
    {
        List<byte> stream = new();
        stream.AddRange(CreateFrame(Ds2Vs.TestSuiteStart, [1, 0, (byte)'S', (byte)'u', (byte)'i', (byte)'t', (byte)'e']));
        stream.AddRange(CreateFrame(Ds2Vs.TestPass, [1, 0, 5, 0, 0, 0]));
        stream.AddRange(CreateFrame(Ds2Vs.TestStart, [1, 0, (byte)'A']));
        stream.AddRange(CreateFrame(Ds2Vs.TestSuiteEnd, [1, 0, 1, 0, 0, 0, 0, 0]));

        string uartLog = Encoding.Latin1.GetString(stream.ToArray());
        TestResults results = UartMessageParser.ParseUartLog(uartLog, "x64");

        Assert.Single(results.Tests);
        Assert.Equal("A", results.Tests[0].TestName);
        Assert.Equal(1, results.PassedTests);
    }

    private static byte[] CreateFrame(byte command, byte[] payload)
    {
        List<byte> bytes = new();
        bytes.Add(0x07);
        bytes.Add(0x08);
        bytes.Add(0x74);
        bytes.Add(0x19);
        bytes.Add(command);

        ushort payloadLength = (ushort)payload.Length;
        bytes.Add((byte)(payloadLength & 0xFF));
        bytes.Add((byte)((payloadLength >> 8) & 0xFF));

        bytes.AddRange(payload);
        return bytes.ToArray();
    }
}
