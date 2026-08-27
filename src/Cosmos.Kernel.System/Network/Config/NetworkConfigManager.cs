using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Network.IPv4;

namespace Cosmos.Kernel.System.Network.Config;

/// <summary>
/// Manages the global network stack configuration.
/// </summary>
public static class NetworkConfigManager
{
    /// <summary>
    /// The current network configuration used by the network stack.
    /// </summary>
    internal static NetworkConfigEntry? CurrentNetworkConfig { get; private set; }

    /// <summary>
    /// The current network configuration list used by the network stack.
    /// </summary>
    private static readonly List<NetworkConfigEntry> s_networkConfigs = new();

    /// <summary>
    /// Gets the amount of available network configurations.
    /// </summary>
    public static int Count => s_networkConfigs.Count;

    /// <summary>
    /// Gets the current IPv4 address.
    /// </summary>
    public static Address? CurrentAddress => CurrentNetworkConfig?.IPConfig?.IPAddress;

    /// <summary>
    /// The IPv4 configuration in force, or null when the stack is unconfigured.
    /// </summary>
    public static IPConfig? Current => CurrentNetworkConfig?.IPConfig;

    /// <summary>
    /// Sets the configuration of the current network.
    /// </summary>
    /// <param name="device">The network device to use.</param>
    /// <param name="config">The IPv4 configuration associated with the device to use.</param>
    internal static void SetCurrentConfig(INetworkDevice device, IPConfig config)
    {
        CurrentNetworkConfig = new NetworkConfigEntry(device, config);
    }

    /// <summary>
    /// Adds a new network configuration.
    /// </summary>
    /// <param name="device">The network device to use.</param>
    /// <param name="config">The IPv4 configuration associated with the device to use.</param>
    internal static void AddConfig(INetworkDevice device, IPConfig config)
    {
        s_networkConfigs.Add(new NetworkConfigEntry(device, config));
    }

    /// <summary>
    /// Returns whether the network stack contains the given network device.
    /// </summary>
    internal static bool ConfigsContainsDevice(INetworkDevice targetDevice)
    {
        if (s_networkConfigs == null)
        {
            return false;
        }

        foreach (var config in s_networkConfigs)
        {
            if (targetDevice == config.Device)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Clears network configurations, removing each configuration. Internal:
    /// this drops the config list alone, leaving the stack's address and MAC
    /// maps behind. <see cref="NetworkStack.RemoveAllConfigIP"/> is the
    /// complete reset and the only caller.
    /// </summary>
    internal static void ClearConfigs()
    {
        s_networkConfigs.Clear();
    }

    /// <summary>
    /// Get the IPv4 configuration for the given network device.
    /// </summary>
    /// <param name="device">Network device.</param>
    internal static IPConfig? Get(INetworkDevice device)
    {
        foreach (var networkConfig in s_networkConfigs)
        {
            if (device == networkConfig.Device)
            {
                return networkConfig.IPConfig;
            }
        }

        return null;
    }

    /// <summary>
    /// Remove the configuration for the given network device.
    /// </summary>
    /// <param name="key">The target network device.</param>
    internal static void Remove(INetworkDevice key)
    {
        NetworkConfigEntry? toRemove = null;
        foreach (var networkConfig in s_networkConfigs)
        {
            if (key == networkConfig.Device)
            {
                toRemove = networkConfig;
                break;
            }
        }

        if (toRemove != null)
        {
            s_networkConfigs.Remove(toRemove);
        }
    }
}
