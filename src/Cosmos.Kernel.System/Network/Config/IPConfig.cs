using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Network.IPv4;

namespace Cosmos.Kernel.System.Network.Config;

/// <summary>
/// Represents IPv4 configuration.
/// </summary>
public class IPConfig
{
    /// <summary>
    /// One device and the configuration in force on it.
    /// </summary>
    private sealed class Entry
    {
        internal Entry(INetworkDevice device, IPConfig config)
        {
            Device = device;
            Config = config;
        }

        internal INetworkDevice Device { get; }

        internal IPConfig Config { get; }
    }

    /// <summary>
    /// Every configured interface. This is the only store: routing lookups and
    /// per-device lookups read the same list, so neither can drift from the
    /// other.
    /// </summary>
    private static readonly List<Entry> s_configs = new();

    /// <summary>
    /// Record the configuration now in force on a device, replacing any
    /// earlier one so a reconfigured device leaves no stale route behind.
    /// </summary>
    /// <param name="device">The configured device.</param>
    /// <param name="config">The configuration applied to it.</param>
    internal static void Set(INetworkDevice device, IPConfig config)
    {
        for (int i = 0; i < s_configs.Count; i++)
        {
            if (s_configs[i].Device == device)
            {
                s_configs[i] = new Entry(device, config);
                return;
            }
        }

        s_configs.Add(new Entry(device, config));
    }

    /// <summary>
    /// Forget every configured interface. Internal: this drops the routing
    /// list alone, leaving the stack's address and MAC maps behind.
    /// <see cref="NetworkStack.RemoveAllConfigIP"/> is the complete reset and
    /// the only caller.
    /// </summary>
    internal static void RemoveAll()
    {
        s_configs.Clear();
    }

    /// <summary>
    /// The configuration in force on a device, or null when it has none.
    /// </summary>
    /// <param name="device">The device to look up.</param>
    internal static IPConfig? Get(INetworkDevice device)
    {
        foreach (Entry entry in s_configs)
        {
            if (entry.Device == device)
            {
                return entry.Config;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the network address for the specified destination IP address.
    /// </summary>
    /// <param name="destIP">The destination IP address.</param>
    public static Address? FindNetwork(Address destIP)
    {
        Address? defaultGw = null;

        foreach (Entry entry in s_configs)
        {
            IPConfig ipConfig = entry.Config;

            if ((ipConfig.IPAddress.Id & ipConfig.SubnetMask.Id) ==
                (destIP.Id & ipConfig.SubnetMask.Id))
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
    internal static bool Enable(INetworkDevice device, Address ip, Address subnet, Address gw)
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
    /// Assign an IPv4 configuration to the primary network device.
    /// </summary>
    /// <param name="ip">The IP address to assign.</param>
    /// <param name="subnet">The subnet mask to use.</param>
    /// <param name="gw">The default gateway address to use.</param>
    /// <returns><see langword="true"/> if the configuration was applied, <see langword="false"/> when there is no device.</returns>
    public static bool Enable(Address ip, Address subnet, Address gw)
    {
        INetworkDevice? device = NetworkManager.PrimaryDevice;
        if (device == null)
        {
            return false;
        }

        return Enable(device, ip, subnet, gw);
    }

    /// <summary>
    /// Assign an IPv4 configuration to a named network device.
    /// </summary>
    /// <param name="adapter">Handle to the device, from <see cref="NetworkManager.GetAdapter(int)"/>.</param>
    /// <param name="ip">The IP address to assign.</param>
    /// <param name="subnet">The subnet mask to use.</param>
    /// <param name="gw">The default gateway address to use.</param>
    /// <returns><see langword="true"/> if the configuration was applied, <see langword="false"/> when the handle names no device.</returns>
    public static bool Enable(NetworkAdapter adapter, Address ip, Address subnet, Address gw)
    {
        INetworkDevice? device = adapter.Device;
        if (device == null)
        {
            return false;
        }

        return Enable(device, ip, subnet, gw);
    }

    /// <summary>
    /// Check if the given address is a local address.
    /// </summary>
    /// <param name="destIP">The address to check.</param>
    internal static bool IsLocalAddress(Address destIP)
    {
        for (int c = 0; c < s_configs.Count; c++)
        {
            IPConfig ipConfig = s_configs[c].Config;

            if ((ipConfig.IPAddress.Id & ipConfig.SubnetMask.Id) ==
                (destIP.Id & ipConfig.SubnetMask.Id))
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
        if (NetworkStack.AddressMap != null && NetworkStack.AddressMap.ContainsKey(sourceIP.Id))
        {
            return NetworkStack.AddressMap[sourceIP.Id];
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
        // TODO is this correct implementation?
        for (int c = 0; c < s_configs.Count; c++)
        {
            return s_configs[c].Config.DefaultGateway;
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
