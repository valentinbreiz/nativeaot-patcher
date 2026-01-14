/*
* PROJECT:          Cosmos OS Development
* CONTENT:          DNS Packet
* PROGRAMMERS:      Valentin Charbonnier <valentinbreiz@gmail.com>
*                   Port of Cosmos Code.
*/

using System.Text;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.System.Network.IPv4.UDP.DNS;

/// <summary>
/// ReplyCode set in Flags
/// </summary>
public enum ReplyCode
{
    OK = 0000,
    FormatError = 0001,
    ServerFailure = 0010,
    NameError = 0011,
    NotSupported = 0100,
    Refused = 0101
}

/// <summary>
/// Represents a DNS query.
/// </summary>
public class DNSQuery
{
    public string? Name { get; set; }
    public ushort Type { get; set; }
    public ushort Class { get; set; }
}

/// <summary>
/// Represents a DNS answer (response).
/// </summary>
public class DNSAnswer
{
    public ushort Name { get; set; }
    public ushort Type { get; set; }
    public ushort Class { get; set; }
    public int TimeToLive { get; set; }
    public ushort DataLength { get; set; }
    public byte[]? Address { get; set; }
}

/// <summary>
/// Represents a DNS packet.
/// </summary>
public class DNSPacket : UDPPacket
{
    // Simple transaction ID generator
    private static byte transactionCounter = 1;

    /// <summary>
    /// Handles DNS packets.
    /// </summary>
    internal static void DNSHandler(byte[] packetData)
    {
        var dnsPacket = new DNSPacket(packetData);
        var receiver = (DnsClient)UdpClient.GetClient(dnsPacket.DestinationPort);
        receiver?.ReceiveData(dnsPacket);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DNSPacket"/> class.
    /// </summary>
    internal DNSPacket()
        : base()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="DNSPacket"/> class.
    /// </summary>
    public DNSPacket(byte[] rawData)
        : base(rawData)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="DNSPacket"/> class.
    /// </summary>
    public DNSPacket(Address source, Address dest, ushort urlnb, ushort len)
        : base(source, dest, 53, 53, (ushort)(len + 12))
    {
        byte transactionID = transactionCounter++;
        RawData[this.DataOffset + 8] = (byte)((transactionID >> 8) & 0xFF);
        RawData[this.DataOffset + 9] = (byte)((transactionID >> 0) & 0xFF);

        RawData[this.DataOffset + 10] = (byte)((0x0100 >> 8) & 0xFF);
        RawData[this.DataOffset + 11] = (byte)((0x0100 >> 0) & 0xFF);

        RawData[this.DataOffset + 12] = (byte)((urlnb >> 8) & 0xFF);
        RawData[this.DataOffset + 13] = (byte)((urlnb >> 0) & 0xFF);

        RawData[this.DataOffset + 14] = (byte)((0 >> 8) & 0xFF);
        RawData[this.DataOffset + 15] = (byte)((0 >> 0) & 0xFF);

        RawData[this.DataOffset + 16] = (byte)((0 >> 8) & 0xFF);
        RawData[this.DataOffset + 17] = (byte)((0 >> 0) & 0xFF);

        RawData[this.DataOffset + 18] = (byte)((0 >> 8) & 0xFF);
        RawData[this.DataOffset + 19] = (byte)((0 >> 0) & 0xFF);

        InitializeFields();
    }

    protected override void InitializeFields()
    {
        base.InitializeFields();
        TransactionID = (ushort)((RawData[this.DataOffset + 8] << 8) | RawData[this.DataOffset + 9]);
        DNSFlags = (ushort)((RawData[this.DataOffset + 10] << 8) | RawData[this.DataOffset + 11]);
        Questions = (ushort)((RawData[this.DataOffset + 12] << 8) | RawData[this.DataOffset + 13]);
        AnswerRRs = (ushort)((RawData[this.DataOffset + 14] << 8) | RawData[this.DataOffset + 15]);
        AuthorityRRs = (ushort)((RawData[this.DataOffset + 16] << 8) | RawData[this.DataOffset + 17]);
        AdditionalRRs = (ushort)((RawData[this.DataOffset + 18] << 8) | RawData[this.DataOffset + 19]);
    }

    /// <summary>
    /// Gets the domain name from the given data and offset.
    /// </summary>
    public string ParseName(byte[] rawData, ref int index)
    {
        var url = new StringBuilder();

        while (rawData[index] != 0x00 && index < rawData.Length)
        {
            byte wordlength = rawData[index];
            index++;
            for (int j = 0; j < wordlength; j++)
            {
                url.Append((char)rawData[index]);
                index++;
            }
            url.Append('.');
        }

        index++; //End 0x00
        if (url.Length > 0)
        {
            return url.ToString().Substring(0, url.Length - 1);
        }
        return url.ToString();
    }

    /// <summary>
    /// The amount of answer Resource Records.
    /// </summary>
    internal ushort AnswerRRs { get; private set; }

    /// <summary>
    /// The amount of authority Resource Records.
    /// </summary>
    internal ushort AuthorityRRs { get; private set; }

    /// <summary>
    /// The amount of additional Resource Records.
    /// </summary>
    internal ushort AdditionalRRs { get; private set; }

    /// <summary>
    /// The DNS transaction ID.
    /// </summary>
    internal ushort TransactionID { get; private set; }

    /// <summary>
    /// The flags of the packet.
    /// </summary>
    internal ushort DNSFlags { get; private set; }

    /// <summary>
    /// The number of DNS queries.
    /// </summary>
    internal ushort Questions { get; private set; }

    /// <summary>
    /// The DNS queries.
    /// </summary>
    internal List<DNSQuery> Queries { get; set; }

    /// <summary>
    /// The DNS answers (responses).
    /// </summary>
    internal List<DNSAnswer> Answers { get; set; }

    public override string ToString()
    {
        return "DNS Packet Src=" + SourceIP + ":" + SourcePort + ", Dest=" + DestinationIP + ":" + DestinationPort;
    }
}

/// <summary>
/// Represents a DNS translation request packet.
/// </summary>
public class DNSPacketAsk : DNSPacket
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DNSPacketAsk"/> class.
    /// </summary>
    internal DNSPacketAsk()
        : base()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="DNSPacketAsk"/> class.
    /// </summary>
    public DNSPacketAsk(byte[] rawData)
        : base(rawData)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="DNSPacketAsk"/> class.
    /// </summary>
    public DNSPacketAsk(Address source, Address dest, string url)
        : base(source, dest, 1, (ushort)(5 + url.Length + 1))
    {
        int b = 0;

        foreach (string item in url.Split('.'))
        {
            byte[] word = Encoding.ASCII.GetBytes(item);

            RawData[this.DataOffset + 20 + b] = (byte)word.Length; //set word length

            b++;

            foreach (byte letter in word)
            {
                RawData[this.DataOffset + 20 + b] = letter;
                b++;
            }
        }

        RawData[this.DataOffset + 20 + b] = 0x00;

        RawData[this.DataOffset + 20 + b + 1] = 0x00;
        RawData[this.DataOffset + 20 + b + 2] = 0x01;

        RawData[this.DataOffset + 20 + b + 3] = 0x00;
        RawData[this.DataOffset + 20 + b + 4] = 0x01;
    }
}

/// <summary>
/// Represents a DNS translation result packet.
/// </summary>
public class DNSPacketAnswer : DNSPacket
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DNSPacketAnswer"/> class.
    /// </summary>
    internal DNSPacketAnswer()
        : base()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="DNSPacketAnswer"/> class.
    /// </summary>
    public DNSPacketAnswer(byte[] rawData)
        : base(rawData)
    { }

    protected override void InitializeFields()
    {
        base.InitializeFields();

        if ((ushort)(DNSFlags & 0x0F) != (ushort)ReplyCode.OK)
        {
            Serial.WriteString("[DNS] Packet response not OK. Passing packet.\n");
            return;
        }

        int index = DataOffset + 20;
        if (Questions > 0)
        {
            Queries = new List<DNSQuery>();

            for (int i = 0; i < Questions; i++)
            {
                var query = new DNSQuery();
                query.Name = ParseName(RawData, ref index);
                query.Type = (ushort)((RawData[index + 0] << 8) | RawData[index + 1]);
                query.Class = (ushort)((RawData[index + 2] << 8) | RawData[index + 3]);
                Queries.Add(query);
                index += 4;
            }
        }
        if (AnswerRRs > 0)
        {
            Answers = new List<DNSAnswer>();

            for (int i = 0; i < AnswerRRs; i++)
            {
                var answer = new DNSAnswer();
                answer.Name = (ushort)((RawData[index + 0] << 8) | RawData[index + 1]);
                answer.Type = (ushort)((RawData[index + 2] << 8) | RawData[index + 3]);
                answer.Class = (ushort)((RawData[index + 4] << 8) | RawData[index + 5]);
                answer.TimeToLive = (RawData[index + 6] << 24) | (RawData[index + 7] << 16) | (RawData[index + 8] << 8) | RawData[index + 9];
                answer.DataLength = (ushort)((RawData[index + 10] << 8) | RawData[index + 11]);
                index += 12;
                answer.Address = new byte[answer.DataLength];
                for (int j = 0; j < answer.DataLength; j++, index++)
                {
                    answer.Address[j] = RawData[index];
                }

                Answers.Add(answer);
            }
        }
    }
}
