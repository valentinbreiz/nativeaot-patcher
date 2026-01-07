using System.Collections.Generic;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Devices.Network;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Network.ARP;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Network.IPv4;

namespace Cosmos.Kernel.System.Network;

/// <summary>
/// Manages the Cosmos networking stack.
/// </summary>
public static class NetworkStack
{
    /// <summary>
    /// Maps IP (Internet Protocol) addresses to network devices.
    /// </summary>
    internal static Dictionary<uint, INetworkDevice>? AddressMap { get; private set; }

    /// <summary>
    /// Maps MAC addresses to network devices.
    /// </summary>
    internal static Dictionary<uint, INetworkDevice>? MACMap { get; private set; }

    /// <summary>
    /// Initializes the network stack.
    /// </summary>
    public static void Initialize()
    {
        AddressMap = new Dictionary<uint, INetworkDevice>();
        MACMap = new Dictionary<uint, INetworkDevice>();
    }

    /// <summary>
    /// Configures an IP address on the given network device.
    /// </summary>
    /// <param name="device">The target network device.</param>
    /// <param name="ipAddress">The IP address to assign to the device.</param>
    public static void ConfigIP(INetworkDevice device, Address ipAddress)
    {
        if (AddressMap == null || MACMap == null)
        {
            Initialize();
        }

        var mac = device.MacAddress;

        // Remove old config if exists
        if (MACMap!.ContainsKey(mac.Hash))
        {
            // Find and remove old IP mapping
            foreach (var pair in AddressMap!)
            {
                if (pair.Value == device)
                {
                    AddressMap.Remove(pair.Key);
                    break;
                }
            }
            MACMap.Remove(mac.Hash);
        }

        // Add new config
        AddressMap!.Add(ipAddress.Hash, device);
        MACMap.Add(mac.Hash, device);

        // Register packet handler
        device.OnPacketReceived = HandlePacket;

        Serial.WriteString("[NetworkStack] Configured IP ");
        Serial.WriteString(ipAddress.ToString());
        Serial.WriteString(" on device ");
        Serial.WriteString(device.Name);
        Serial.WriteString("\n");
    }

    /// <summary>
    /// Configures an IP address on the given network device using IPConfig.
    /// </summary>
    /// <param name="device">The target network device.</param>
    /// <param name="config">The IP configuration to apply.</param>
    public static void ConfigIP(INetworkDevice device, IPConfig config)
    {
        if (AddressMap == null || MACMap == null)
        {
            Initialize();
        }

        ConfigIP(device, config.IPAddress);
        IPConfig.Add(config);
        NetworkConfigManager.AddConfig(device, config);
        NetworkConfigManager.SetCurrentConfig(device, config);
    }

    /// <summary>
    /// Removes all IP configurations.
    /// </summary>
    public static void RemoveAllConfigIP()
    {
        AddressMap?.Clear();
        MACMap?.Clear();
        NetworkConfigManager.ClearConfigs();
    }

    /// <summary>
    /// Flag to prevent recursive Update calls.
    /// </summary>
    private static bool _updating = false;

    /// <summary>
    /// Updates the network stack (sends pending packets).
    /// </summary>
    public static void Update()
    {
        // Prevent recursive calls
        if (_updating)
        {
            return;
        }

        _updating = true;
        OutgoingBuffer.Send();
        _updating = false;
    }

    /// <summary>
    /// Handle a network packet.
    /// </summary>
    /// <param name="packetData">Packet data array.</param>
    /// <param name="length">Packet length.</param>
    public static void HandlePacket(byte[] packetData, int length)
    {
        Serial.WriteString("[NetworkStack] HandlePacket called, len=");
        Serial.WriteNumber((ulong)length);
        Serial.WriteString("\n");

        if (packetData == null || length < 14)
        {
            Serial.WriteString("[NetworkStack] Error: Invalid packet data\n");
            return;
        }

        ushort etherType = (ushort)((packetData[12] << 8) | packetData[13]);
        Serial.WriteString("[NetworkStack] EtherType: 0x");
        Serial.WriteHex(etherType);
        Serial.WriteString("\n");

        switch (etherType)
        {
            case 0x0806: // ARP
                Serial.WriteString("[NetworkStack] -> ARP\n");
                ARPPacket.ARPHandler(packetData);
                break;
            case 0x0800: // IPv4
                Serial.WriteString("[NetworkStack] -> IPv4\n");
                IPPacket.IPv4Handler(packetData);
                break;
            default:
                Serial.WriteString("[NetworkStack] Unknown EtherType, ignoring\n");
                break;
        }
    }
}
