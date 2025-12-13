using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Devices.Network;

namespace Cosmos.Kernel.Services.Network.ARP;

/// <summary>
/// Represents an ARP (Address Resolution Protocol) packet.
/// </summary>
public class ARPPacket : EthernetPacket
{
    protected ushort hardwareType;
    protected ushort protocolType;
    protected byte hardwareAddrLength;
    protected byte protocolAddrLength;
    protected ushort opCode;

    /// <summary>
    /// Handles ARP packets.
    /// </summary>
    /// <param name="packetData">Packet data.</param>
    internal static void ARPHandler(byte[] packetData)
    {
        var arpPacket = new ARPPacket(packetData);

        if (arpPacket.Operation == 0x01)
        {
            // ARP Request
            if (arpPacket.HardwareType == 1 && arpPacket.ProtocolType == 0x0800)
            {
                var arpRequest = new ARPRequestEthernet(packetData);
                if (arpRequest.SenderIP == null)
                {
                    Serial.WriteString("[ARP] SenderIP null in ARPHandler!\n");
                    return;
                }

                ARPCache.Update(arpRequest.SenderIP, arpRequest.SenderMAC);

                if (NetworkStack.AddressMap.ContainsKey(arpRequest.TargetIP.Hash))
                {
                    Serial.WriteString("[ARP] Request received from ");
                    Serial.WriteString(arpRequest.SenderIP.ToString());
                    Serial.WriteString("\n");

                    var nic = NetworkStack.AddressMap[arpRequest.TargetIP.Hash];
                    var nicMac = new MACAddress(nic.MacAddress);

                    var reply = new ARPReplyEthernet(
                        nicMac,
                        arpRequest.TargetIP,
                        arpRequest.SenderMAC,
                        arpRequest.SenderIP
                    );

                    nic.Send(reply.RawData, reply.RawData.Length);
                }
            }
        }
        else if (arpPacket.Operation == 0x02)
        {
            // ARP Reply
            if (arpPacket.HardwareType == 1 && arpPacket.ProtocolType == 0x0800)
            {
                var arpReply = new ARPReplyEthernet(packetData);
                Serial.WriteString("[ARP] Reply received from ");
                Serial.WriteString(arpReply.SenderIP.ToString());
                Serial.WriteString("\n");
                ARPCache.Update(arpReply.SenderIP, arpReply.SenderMAC);
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ARPPacket"/> class.
    /// </summary>
    internal ARPPacket()
        : base()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ARPPacket"/> class.
    /// </summary>
    /// <param name="rawData">Raw data.</param>
    public ARPPacket(byte[] rawData)
        : base(rawData)
    { }

    protected override void InitializeFields()
    {
        base.InitializeFields();
        hardwareType = (ushort)((RawData[14] << 8) | RawData[15]);
        protocolType = (ushort)((RawData[16] << 8) | RawData[17]);
        hardwareAddrLength = RawData[18];
        protocolAddrLength = RawData[19];
        opCode = (ushort)((RawData[20] << 8) | RawData[21]);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ARPPacket"/> class.
    /// </summary>
    /// <param name="dest">Destination MAC address.</param>
    /// <param name="src">Source MAC address.</param>
    /// <param name="hwType">Hardware type.</param>
    /// <param name="protoType">Protocol type.</param>
    /// <param name="hwLen">Hardware address length.</param>
    /// <param name="protoLen">Protocol length.</param>
    /// <param name="operation">Operation.</param>
    /// <param name="packet_size">Packet size.</param>
    protected ARPPacket(MACAddress dest, MACAddress src, ushort hwType, ushort protoType,
        byte hwLen, byte protoLen, ushort operation, int packet_size)
        : base(dest, src, 0x0806, packet_size)
    {
        RawData[14] = (byte)(hwType >> 8);
        RawData[15] = (byte)(hwType >> 0);
        RawData[16] = (byte)(protoType >> 8);
        RawData[17] = (byte)(protoType >> 0);
        RawData[18] = hwLen;
        RawData[19] = protoLen;
        RawData[20] = (byte)(operation >> 8);
        RawData[21] = (byte)(operation >> 0);

        InitializeFields();
    }

    /// <summary>
    /// Gets the operation code.
    /// </summary>
    internal ushort Operation => opCode;

    /// <summary>
    /// Get the hardware type.
    /// </summary>
    internal ushort HardwareType => hardwareType;

    /// <summary>
    /// Gets the protocol type.
    /// </summary>
    internal ushort ProtocolType => protocolType;

    public override string ToString()
    {
        return "ARP Packet Src=" + srcMAC + ", Dest=" + destMAC + ", HWType=" + hardwareType + ", Protocol=" + protocolType +
            ", Operation=" + Operation;
    }
}
