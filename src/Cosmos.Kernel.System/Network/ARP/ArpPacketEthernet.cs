using Cosmos.Kernel.HAL.Devices.Network;
using Cosmos.Kernel.System.Network.IPv4;

namespace Cosmos.Kernel.System.Network.ARP;

/// <summary>
/// Represents an ARP Ethernet packet.
/// </summary>
internal abstract class ArpPacketEthernet : ArpPacket
{
    /// <summary>
    /// The sender MAC address.
    /// </summary>
    protected MACAddress senderMAC = null!;

    /// <summary>
    /// The target MAC address.
    /// </summary>
    protected MACAddress targetMAC = null!;

    /// <summary>
    /// The sender IP address.
    /// </summary>
    protected Address senderIP = null!;

    /// <summary>
    /// The target IP address.
    /// </summary>
    protected Address targetIP = null!;

    // /// <summary>
    // /// Initializes a new instance of the <see cref="ArpRequestEthernet"/> class.
    // /// </summary>
    // internal ArpPacketEthernet()
    //     : base()
    // { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArpRequestEthernet"/> class.
    /// </summary>
    /// <param name="rawData">Raw data.</param>
    internal ArpPacketEthernet(byte[] rawData)
        : base(rawData)
    { }

    protected override void InitializeFields()
    {
        base.InitializeFields();
        senderMAC = new MACAddress(RawData, 22);
        senderIP = new Address(RawData, 28);
        targetMAC = new MACAddress(RawData, 32);
        targetIP = new Address(RawData, 38);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArpRequestEthernet"/> class.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="senderMAC">The source MAC address.</param>
    /// <param name="senderIP">The source IP address.</param>
    /// <param name="targetMAC">The destination MAC address.</param>
    /// <param name="targetIP">The destination IP address.</param>
    /// <param name="packetSize">The packet size.</param>
    /// <param name="arpTargetMAC">The ARP destination MAC address.</param>
    /// <exception cref="ArgumentException">Thrown if RawData is invalid or null.</exception>
    protected ArpPacketEthernet(ushort operation, MACAddress senderMAC, Address senderIP,
        MACAddress targetMAC, Address targetIP, int packetSize, MACAddress arpTargetMAC)
        : base(targetMAC, senderMAC, 1, 0x0800, 6, 4, operation, packetSize)
    {
        for (int i = 0; i < 6; i++)
        {
            RawData[22 + i] = senderMAC.bytes[i];
            RawData[32 + i] = arpTargetMAC.bytes[i];
        }
        for (int i = 0; i < 4; i++)
        {
            RawData[28 + i] = senderIP.Parts[i];
            RawData[38 + i] = targetIP.Parts[i];
        }

        InitializeFields();
    }

    /// <summary>
    /// Gets the senders MAC address.
    /// </summary>
    internal MACAddress SenderMAC => senderMAC;

    /// <summary>
    /// Gets the targets MAC address.
    /// </summary>
    internal MACAddress TargetMAC => targetMAC;

    /// <summary>
    /// Gets the senders IP address.
    /// </summary>
    internal Address SenderIP => senderIP;

    /// <summary>
    /// Gets the targets IP address.
    /// </summary>
    internal Address TargetIP => targetIP;

    public override string ToString()
    {
        return "IPv4 Ethernet ARP Packet SenderMAC=" + senderMAC + ", TargetMAC=" + targetMAC + ", SenderIP=" + senderIP +
            ", TargetIP=" + targetIP + ", Operation=" + opCode;
    }
}

/// <summary>
/// Represents an ARP reply Ethernet packet.
/// </summary>
/// <remarks>
/// See also: <seealso cref="ArpPacketEthernet"/>.
/// </remarks>
internal class ArpReplyEthernet : ArpPacketEthernet
{
    // /// <summary>
    // /// Initializes a new instance of the <see cref="ArpReplyEthernet"/> class.
    // /// </summary>
    // internal ArpReplyEthernet()
    //     : base()
    // { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArpReplyEthernet"/> class.
    /// </summary>
    /// <param name="rawData">Raw data.</param>
    internal ArpReplyEthernet(byte[] rawData)
        : base(rawData)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArpReplyEthernet"/> class.
    /// </summary>
    /// <param name="ourMAC">The source MAC address.</param>
    /// <param name="ourIP">The source IP address.</param>
    /// <param name="targetMAC">The destination MAC address.</param>
    /// <param name="targetIP">The destination IP address.</param>
    /// <exception cref="ArgumentException">Thrown if RawData is invalid or null.</exception>
    internal ArpReplyEthernet(MACAddress ourMAC, Address ourIP, MACAddress targetMAC, Address targetIP)
        : base(2, ourMAC, ourIP, targetMAC, targetIP, 42, MACAddress.None)
    { }

    public override string ToString()
    {
        return "ARP Reply Src=" + srcMAC + ", Dest=" + destMAC + ", Sender=" + senderIP + ", Target=" + targetIP;
    }
}

/// <summary>
/// Represents an ARP request Ethernet packet.
/// </summary>
/// <remarks>
/// See also: <seealso cref="ArpPacketEthernet"/>.
/// </remarks>
internal class ArpRequestEthernet : ArpPacketEthernet
{
    // /// <summary>
    // /// Initializes a new instance of the <see cref="ArpRequestEthernet"/> class.
    // /// </summary>
    // internal ArpRequestEthernet()
    //     : base()
    // { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArpRequestEthernet"/> class.
    /// </summary>
    /// <param name="rawData">Raw data.</param>
    internal ArpRequestEthernet(byte[] rawData)
        : base(rawData)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArpRequestEthernet"/> class.
    /// </summary>
    /// <param name="ourMAC">The source MAC address.</param>
    /// <param name="ourIP">The source IP address.</param>
    /// <param name="targetMAC">The destination MAC address.</param>
    /// <param name="targetIP">The destination IP address.</param>
    /// <param name="arpTargetMAC">The ARP destination MAC address.</param>
    /// <exception cref="ArgumentException">Thrown if RawData is invalid or null.</exception>
    internal ArpRequestEthernet(MACAddress ourMAC, Address ourIP, MACAddress targetMAC, Address targetIP, MACAddress arpTargetMAC)
        : base(1, ourMAC, ourIP, targetMAC, targetIP, 42, arpTargetMAC)
    { }

    public override string ToString()
    {
        return "ARP Request Src=" + srcMAC + ", Dest=" + destMAC + ", Sender=" + senderIP + ", Target=" + targetIP;
    }
}
