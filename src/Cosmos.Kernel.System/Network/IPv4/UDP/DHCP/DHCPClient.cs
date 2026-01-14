/*
* PROJECT:          Cosmos OS Development
* CONTENT:          DHCP Client
* PROGRAMMERS:      Alexy DA CRUZ <dacruzalexy@gmail.com>
*                   Valentin CHARBONNIER <valentinbreiz@gmail.com>
*                   Port of Cosmos Code.
*/

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Network.Config;

namespace Cosmos.Kernel.System.Network.IPv4.UDP.DHCP;

/// <summary>
/// Used to manage the DHCP connection to a server.
/// </summary>
public class DHCPClient : UdpClient
{
    /// <summary>
    /// Is DHCP asked check variable
    /// </summary>
    private bool applied = false;

    /// <summary>
    /// Gets the IP address of the DHCP server.
    /// </summary>
    public static Address DHCPServerAddress(INetworkDevice networkDevice)
    {
        return NetworkConfigManager.Get(networkDevice)?.DefaultGateway;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DHCPClient"/> class.
    /// </summary>
    public DHCPClient() : base(68)
    {
    }

    /// <summary>
    /// Receive data
    /// </summary>
    /// <param name="timeout">timeout value, default 5000ms</param>
    /// <returns>time value (-1 = timeout)</returns>
    private int Receive(int timeout = 5000)
    {
        int iterations = 0;
        int maxIterations = timeout * 1000;

        while (rxBuffer.Count < 1)
        {
            iterations++;
            if (iterations > maxIterations)
            {
                return -1;
            }
        }

        var packet = new DHCPPacket(rxBuffer.Dequeue().RawData);

        if (packet.MessageType == 2) //Boot Reply
        {
            if (packet.RawData[284] == 0x02) //Offer packet received
            {
                Serial.WriteString("[DHCP] Offer received.\n");
                return SendRequestPacket(packet.Client);
            }
            else if (packet.RawData[284] == 0x05 || packet.RawData[284] == 0x06) //ACK or NAK DHCP packet received
            {
                if (applied == false)
                {
                    Apply(packet, true);

                    Close();
                }
            }
        }

        return iterations / 1000;
    }

    /// <summary>
    /// Sends a packet to the DHCP server in order to make the address available again.
    /// </summary>
    public void SendReleasePacket()
    {
        for (int i = 0; i < NetworkManager.DeviceCount; i++)
        {
            var networkDevice = NetworkManager.GetDevice(i);
            if (networkDevice == null) continue;

            Address source = IPConfig.FindNetwork(DHCPServerAddress(networkDevice));
            var dhcpRelease = new DHCPRelease(source, DHCPServerAddress(networkDevice), networkDevice.MacAddress);

            OutgoingBuffer.AddPacket(dhcpRelease);
            NetworkStack.Update();

            NetworkStack.RemoveAllConfigIP();

            IPConfig.Enable(networkDevice, new Address(0, 0, 0, 0), new Address(0, 0, 0, 0), new Address(0, 0, 0, 0));
        }

        Close();
    }

    /// <summary>
    /// Send a packet to find the DHCP server and inform the host that we
    /// are requesting a new IP address.
    /// </summary>
    /// <returns>The amount of time elapsed, or -1 if a timeout has been reached.</returns>
    public int SendDiscoverPacket()
    {
        NetworkStack.RemoveAllConfigIP();

        for (int i = 0; i < NetworkManager.DeviceCount; i++)
        {
            var networkDevice = NetworkManager.GetDevice(i);
            if (networkDevice == null) continue;

            IPConfig.Enable(networkDevice, new Address(0, 0, 0, 0), new Address(0, 0, 0, 0), new Address(0, 0, 0, 0));

            var dhcpDiscover = new DHCPDiscover(networkDevice.MacAddress);
            OutgoingBuffer.AddPacket(dhcpDiscover);
            NetworkStack.Update();

            applied = false;
        }

        return Receive();
    }

    /// <summary>
    /// Sends a request to apply the new IP configuration.
    /// </summary>
    /// <returns>The amount of time elapsed, or -1 if a timeout has been reached.</returns>
    private int SendRequestPacket(Address requestedAddress)
    {
        for (int i = 0; i < NetworkManager.DeviceCount; i++)
        {
            var networkDevice = NetworkManager.GetDevice(i);
            if (networkDevice == null) continue;

            var dhcpRequest = new DHCPRequest(networkDevice.MacAddress, requestedAddress);
            OutgoingBuffer.AddPacket(dhcpRequest);
            NetworkStack.Update();
        }
        return Receive();
    }

    /// <summary>
    /// Applies the newly received IP configuration.
    /// </summary>
    /// <param name="packet">The DHCP ACK packet.</param>
    /// <param name="message">Enable/Disable the displaying of messages about DHCP applying and conf.</param>
    private void Apply(DHCPPacket packet, bool message = false)
    {
        if (applied == false)
        {
            NetworkStack.RemoveAllConfigIP();

            for (int i = 0; i < NetworkManager.DeviceCount; i++)
            {
                var networkDevice = NetworkManager.GetDevice(i);
                if (networkDevice == null) continue;

                if (packet.Client == null || packet.Client.ToString() == null)
                {
                    throw new Exception("Parsing DHCP ACK Packet failed, can't apply network configuration.");
                }
                else
                {
                    Serial.WriteString("[DHCP ACK] Packet received, applying IP configuration...\n");
                    Serial.WriteString("   IP Address  : " + packet.Client.ToString() + "\n");
                    Serial.WriteString("   Subnet mask : " + (packet.Subnet?.ToString() ?? "null") + "\n");
                    Serial.WriteString("   Gateway     : " + (packet.Server?.ToString() ?? "null") + "\n");
                    Serial.WriteString("   DNS server  : " + (packet.DNS?.ToString() ?? "null") + "\n");

                    IPConfig.Enable(networkDevice, packet.Client, packet.Subnet ?? new Address(255, 255, 255, 0), packet.Server ?? Address.Zero);
                    if (packet.DNS != null)
                    {
                        DNSConfig.Add(packet.DNS);
                    }

                    Serial.WriteString("[DHCP CONFIG] IP configuration applied.\n");

                    applied = true;

                    return;
                }
            }

            Serial.WriteString("[DHCP CONFIG] No DHCP Config applied!\n");
        }
        else
        {
            Serial.WriteString("[DHCP CONFIG] DHCP already applied.\n");
        }
    }
}
