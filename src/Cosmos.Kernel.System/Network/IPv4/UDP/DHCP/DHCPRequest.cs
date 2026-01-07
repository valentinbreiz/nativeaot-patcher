/*
* PROJECT:          Cosmos OS Development
* CONTENT:          DHCP Request
* PROGRAMMERS:      Alexy DA CRUZ <dacruzalexy@gmail.com>
*                   Valentin CHARBONNIER <valentinbreiz@gmail.com>
*                   Port of Cosmos Code.
*/

using Cosmos.Kernel.HAL.Devices.Network;

namespace Cosmos.Kernel.System.Network.IPv4.UDP.DHCP;

/// <summary>
/// Represents a DHCP request packet.
/// </summary>
internal class DHCPRequest : DHCPPacket
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DHCPRequest"/> class.
    /// </summary>
    internal DHCPRequest() : base()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="DHCPRequest"/> class.
    /// </summary>
    internal DHCPRequest(byte[] rawData) : base(rawData)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="DHCPRequest"/> class.
    /// </summary>
    internal DHCPRequest(MACAddress sourceMAC, Address requestedAddress) : base(sourceMAC, 16)
    {
        // Request
        RawData[282] = 53;
        RawData[283] = 1;
        RawData[284] = 3;

        // Requested Address
        RawData[285] = 50;
        RawData[286] = 4;

        RawData[287] = requestedAddress.Parts[0];
        RawData[288] = requestedAddress.Parts[1];
        RawData[289] = requestedAddress.Parts[2];
        RawData[290] = requestedAddress.Parts[3];

        // Parameters start here
        RawData[291] = 0x37;
        RawData[292] = 4;

        // Parameters
        RawData[293] = 0x01;
        RawData[294] = 0x03;
        RawData[295] = 0x0f;
        RawData[296] = 0x06;

        RawData[297] = 0xff; // ENDMARK
    }
}
