using System.Diagnostics.CodeAnalysis;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Devices.Network;
using Cosmos.Kernel.System.Network.IPv4.UDP.DHCP;

namespace Cosmos.Kernel.System.Network.IPv4.UDP;

/// <summary>
/// Delegate for UDP data received events.
/// </summary>
internal delegate void UdpDataReceivedHandler(UdpPacket packet);

/// <summary>
/// Represents a UDP datagram carried in an <see cref="IPPacket"/>. Header properties are
/// snapshots parsed from <see cref="EthernetPacket.RawData"/> when the packet is constructed;
/// they are not re-read afterwards. The UDP checksum field is written as zero and never
/// computed, which is legal for UDP over IPv4.
/// </summary>
[Experimental(Experimentals.PacketSeamDiagId)]
public class UdpPacket : IPPacket
{
    /// <summary>
    /// Callback for receiving UDP data.
    /// </summary>
    internal static UdpDataReceivedHandler? OnUDPDataReceived { get; set; }

    /// <summary>
    /// Handles UDP packets.
    /// </summary>
    /// <param name="packetData">The raw packet data.</param>
    internal static void UDPHandler(byte[] packetData)
    {
        var udpPacket = new UdpPacket(packetData);

        Serial.WriteString("[UDP] Received from ");
        Serial.WriteString(udpPacket.SourceIP.ToString());
        Serial.WriteString(":");
        Serial.WriteNumber((ulong)udpPacket.SourcePort);
        Serial.WriteString(" -> ");
        Serial.WriteNumber((ulong)udpPacket.DestinationPort);
        Serial.WriteString(" len=");
        Serial.WriteNumber((ulong)udpPacket.UDPDataLength);
        Serial.WriteString("\n");

        // Route to specific protocol handlers based on port
        if (udpPacket.DestinationPort == 68) // DHCP client
        {
            DhcpPacket.DHCPHandler(packetData);
        }
        else
        {
            // Route to UdpClient if available. DNS used to be routed here a
            // second time as well, which enqueued every reply twice into the
            // one client bound to port 53: the client dequeued one and the
            // stale copy then satisfied the next query's wait, so every second
            // lookup on a DnsClient failed its own query-name check.
            UdpClient? client = UdpClient.GetClient(udpPacket.DestinationPort);
            client?.ReceiveData(udpPacket);
        }

        // Call the registered callback if any
        OnUDPDataReceived?.Invoke(udpPacket);
    }

    /// <summary>
    /// Parses a UDP packet from a raw Ethernet frame. The instance aliases
    /// <paramref name="rawData"/> without copying, so later changes to the array are visible
    /// through this packet. Header properties are parsed once during construction.
    /// </summary>
    /// <param name="rawData">The raw frame bytes, starting at the Ethernet header.</param>
    public UdpPacket(byte[] rawData)
        : base(rawData)
    {
    }

    /// <summary>
    /// Creates a UDP packet with an uninitialized payload area of
    /// <paramref name="datalength"/> bytes. The Ethernet, IP, and UDP headers, including all
    /// length fields and the IP header checksum, are written during construction and never
    /// recomputed, so the payload must be filled in before the packet is sent. The UDP
    /// checksum is written as zero and never computed, which is legal for UDP over IPv4.
    /// </summary>
    /// <param name="source">The source IPv4 address.</param>
    /// <param name="dest">The destination IPv4 address.</param>
    /// <param name="srcport">The source port.</param>
    /// <param name="destport">The destination port.</param>
    /// <param name="datalength">The payload length in bytes.</param>
    public UdpPacket(Address source, Address dest, ushort srcport, ushort destport, ushort datalength)
        : base((ushort)(datalength + 8), 17, source, dest, 0x00)
    {
        MakePacket(srcport, destport, datalength);
        InitializeFields();
    }

    /// <summary>
    /// Creates a UDP packet with an uninitialized payload area of
    /// <paramref name="datalength"/> bytes and a preset destination MAC address, bypassing ARP
    /// resolution. Headers, length fields, and the IP header checksum are written during
    /// construction and never recomputed. The UDP checksum is written as zero and never
    /// computed, which is legal for UDP over IPv4.
    /// </summary>
    /// <param name="source">The source IPv4 address.</param>
    /// <param name="dest">The destination IPv4 address.</param>
    /// <param name="srcport">The source port.</param>
    /// <param name="destport">The destination port.</param>
    /// <param name="datalength">The payload length in bytes.</param>
    /// <param name="destmac">The destination MAC address to write into the Ethernet header.</param>
    public UdpPacket(Address source, Address dest, ushort srcport, ushort destport, ushort datalength, MACAddress destmac)
        : base((ushort)(datalength + 8), 17, source, dest, 0x00, destmac)
    {
        MakePacket(srcport, destport, datalength);
        InitializeFields();
    }

    /// <summary>
    /// Creates a UDP packet and copies <paramref name="data"/> into its payload area. Headers,
    /// length fields, and the IP header checksum are written during construction and never
    /// recomputed. The UDP checksum is written as zero and never computed, which is legal for
    /// UDP over IPv4.
    /// </summary>
    /// <param name="source">The source IPv4 address.</param>
    /// <param name="dest">The destination IPv4 address.</param>
    /// <param name="srcPort">The source port.</param>
    /// <param name="destPort">The destination port.</param>
    /// <param name="data">The payload bytes to copy into the packet.</param>
    public UdpPacket(Address source, Address dest, ushort srcPort, ushort destPort, byte[] data)
        : base((ushort)(data.Length + 8), 17, source, dest, 0x00)
    {
        MakePacket(srcPort, destPort, (ushort)data.Length);

        for (int b = 0; b < data.Length; b++)
        {
            RawData[this.DataOffset + 8 + b] = data[b];
        }

        InitializeFields();
    }

    /// <summary>
    /// Creates a UDP packet with a copied payload and a preset destination MAC address,
    /// bypassing ARP resolution. Headers, length fields, and the IP header checksum are
    /// written during construction and never recomputed. The UDP checksum is written as zero
    /// and never computed, which is legal for UDP over IPv4.
    /// </summary>
    /// <param name="source">The source IPv4 address.</param>
    /// <param name="dest">The destination IPv4 address.</param>
    /// <param name="srcPort">The source port.</param>
    /// <param name="destPort">The destination port.</param>
    /// <param name="data">The payload bytes to copy into the packet.</param>
    /// <param name="destmac">The destination MAC address to write into the Ethernet header.</param>
    public UdpPacket(Address source, Address dest, ushort srcPort, ushort destPort, byte[] data, MACAddress destmac)
        : base((ushort)(data.Length + 8), 17, source, dest, 0x00, destmac)
    {
        MakePacket(srcPort, destPort, (ushort)data.Length);

        for (int b = 0; b < data.Length; b++)
        {
            RawData[this.DataOffset + 8 + b] = data[b];
        }

        InitializeFields();
    }

    private void MakePacket(ushort srcport, ushort destport, ushort length)
    {
        RawData[this.DataOffset + 0] = (byte)((srcport >> 8) & 0xFF);
        RawData[this.DataOffset + 1] = (byte)((srcport >> 0) & 0xFF);
        RawData[this.DataOffset + 2] = (byte)((destport >> 8) & 0xFF);
        RawData[this.DataOffset + 3] = (byte)((destport >> 0) & 0xFF);
        UDPLength = (ushort)(length + 8);

        RawData[this.DataOffset + 4] = (byte)((UDPLength >> 8) & 0xFF);
        RawData[this.DataOffset + 5] = (byte)((UDPLength >> 0) & 0xFF);

        RawData[this.DataOffset + 6] = (byte)((0 >> 8) & 0xFF);
        RawData[this.DataOffset + 7] = (byte)((0 >> 0) & 0xFF);
    }

    /// <summary>
    /// Parses the UDP header fields (source port, destination port, UDP length) from
    /// <see cref="EthernetPacket.RawData"/> into the header properties. Runs once during
    /// construction; the properties are snapshots and are not refreshed afterwards.
    /// </summary>
    protected override void InitializeFields()
    {
        base.InitializeFields();
        SourcePort = (ushort)((RawData[DataOffset] << 8) | RawData[DataOffset + 1]);
        DestinationPort = (ushort)((RawData[DataOffset + 2] << 8) | RawData[DataOffset + 3]);
        UDPLength = (ushort)((RawData[DataOffset + 4] << 8) | RawData[DataOffset + 5]);
    }

    /// <summary>
    /// Gets the destination port, a snapshot parsed from the UDP header at construction.
    /// </summary>
    public ushort DestinationPort { get; private set; }

    /// <summary>
    /// Gets the source port, a snapshot parsed from the UDP header at construction.
    /// </summary>
    public ushort SourcePort { get; private set; }

    /// <summary>
    /// Gets the value of the UDP length field: the 8-byte UDP header plus the payload. It is
    /// a snapshot parsed from the header at construction and is never recomputed.
    /// </summary>
    public ushort UDPLength { get; private set; }

    /// <summary>
    /// Gets the payload length in bytes: <see cref="UDPLength"/> minus the 8-byte UDP header.
    /// </summary>
    public ushort UDPDataLength => (ushort)(UDPLength - 8);

    /// <summary>
    /// Gets the UDP payload. Each access allocates and returns a fresh copy of the payload
    /// bytes; the packet's underlying buffer is not exposed.
    /// </summary>
    public byte[] UDPData
    {
        get
        {
            byte[] data = new byte[UDPDataLength];

            for (int b = 0; b < data.Length; b++)
            {
                data[b] = RawData[DataOffset + 8 + b];
            }

            return data;
        }
    }

    /// <summary>
    /// Returns a string with the source and destination endpoints and the payload length.
    /// </summary>
    /// <returns>A human-readable summary of the packet.</returns>
    public override string ToString()
    {
        return "UDP Packet Src=" + SourceIP + ":" + SourcePort + "," +
                "Dest=" + DestinationIP + ":" + DestinationPort + ", DataLen=" + UDPDataLength;
    }
}
