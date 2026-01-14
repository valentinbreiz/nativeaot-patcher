using System.Collections.Generic;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Network.IPv4;

namespace Cosmos.Kernel.System.Network.Config;

/// <summary>
/// Represents IPv4 configuration.
/// </summary>
public class IPConfig
{
    private static readonly List<IPConfig> ipConfigs = new();

    /// <summary>
    /// Add the given IPv4 configuration.
    /// </summary>
    internal static void Add(IPConfig config)
    {
        ipConfigs.Add(config);
    }

    /// <summary>
    /// Removes the given IPv4 configuration.
    /// </summary>
    internal static void Remove(IPConfig config)
    {
        ipConfigs.Remove(config);
    }

    /// <summary>
    /// Remove all IPv4 configurations.
    /// </summary>
    internal static void RemoveAll()
    {
        ipConfigs.Clear();
    }

    /// <summary>
    /// Finds the network address for the specified destination IP address.
    /// </summary>
    /// <param name="destIP">The destination IP address.</param>
    public static Address FindNetwork(Address destIP)
    {
        Address? defaultGw = null;

        foreach (IPConfig ipConfig in ipConfigs)
        {
            if ((ipConfig.IPAddress.Hash & ipConfig.SubnetMask.Hash) ==
                (destIP.Hash & ipConfig.SubnetMask.Hash))
            {
                return ipConfig.IPAddress;
            }
            if (defaultGw == null && ipConfig.DefaultGateway.CompareTo(Address.Zero) != 0)
            {
                defaultGw = ipConfig.IPAddress;
            }

            if (!IsLocalAddress(destIP))
            {
                return ipConfig.IPAddress;
            }
        }

        return defaultGw;
    }

    /// <summary>
    /// Enables a network device with the specified IP configuration.
    /// </summary>
    /// <param name="device">The network device to enable.</param>
    /// <param name="ip">The IP address to assign to the device.</param>
    /// <param name="subnet">The subnet mask to use for the device.</param>
    /// <param name="gw">The default gateway address to use for the device.</param>
    /// <returns><see langword="true"/> if the device was successfully enabled, <see langword="false"/> otherwise.</returns>
    public static bool Enable(INetworkDevice device, Address ip, Address subnet, Address gw)
    {
        if (device != null)
        {
            var config = new IPConfig(ip, subnet, gw);
            NetworkStack.ConfigIP(device, config);
            Serial.WriteString("[IPConfig] Config OK.\n");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Check if the given address is a local address.
    /// </summary>
    /// <param name="destIP">The address to check.</param>
    internal static bool IsLocalAddress(Address destIP)
    {
        for (int c = 0; c < ipConfigs.Count; c++)
        {
            if ((ipConfigs[c].IPAddress.Hash & ipConfigs[c].SubnetMask.Hash) ==
                (destIP.Hash & ipConfigs[c].SubnetMask.Hash))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Find the interface by the given IP address.
    /// </summary>
    /// <param name="sourceIP">Source IP.</param>
    internal static INetworkDevice? FindInterface(Address sourceIP)
    {
        if (NetworkStack.AddressMap != null && NetworkStack.AddressMap.ContainsKey(sourceIP.Hash))
        {
            return NetworkStack.AddressMap[sourceIP.Hash];
        }
        return null;
    }

    /// <summary>
    /// Find route to address.
    /// </summary>
    /// <param name="destIP">Destination IP.</param>
    /// <returns>Address value.</returns>
    internal static Address? FindRoute(Address destIP)
    {
        for (int c = 0; c < ipConfigs.Count; c++)
        {
            return ipConfigs[c].DefaultGateway;
        }

        return null;
    }

    /// <summary>
    /// Creates a IPv4 Configuration with no default gateway.
    /// </summary>
    /// <param name="ip">IP Address</param>
    /// <param name="subnet">Subnet Mask</param>
    public IPConfig(Address ip, Address subnet)
        : this(ip, subnet, Address.Zero)
    {
    }

    /// <summary>
    /// Creates a IPv4 Configuration.
    /// </summary>
    /// <param name="ip">IP Address</param>
    /// <param name="subnet">Subnet Mask</param>
    /// <param name="gw">Default gateway</param>
    public IPConfig(Address ip, Address subnet, Address gw)
    {
        IPAddress = ip;
        SubnetMask = subnet;
        DefaultGateway = gw;
    }

    /// <summary>
    /// The IP address.
    /// </summary>
    public Address IPAddress { get; }

    /// <summary>
    /// The subnet mask.
    /// </summary>
    public Address SubnetMask { get; }

    /// <summary>
    /// The default gateway address.
    /// </summary>
    public Address DefaultGateway { get; }
}
