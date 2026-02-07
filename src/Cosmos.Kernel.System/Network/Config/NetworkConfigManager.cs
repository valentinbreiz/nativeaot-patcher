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
    public static NetworkConfigEntry? CurrentNetworkConfig { get; set; }

    /// <summary>
    /// The current network configuration list used by the network stack.
    /// </summary>
    public static readonly List<NetworkConfigEntry> NetworkConfigs = new();

    /// <summary>
    /// Gets the amount of available network configurations.
    /// </summary>
    public static int Count => NetworkConfigs.Count;

    /// <summary>
    /// Gets the current IPv4 address.
    /// </summary>
    public static Address CurrentAddress => CurrentNetworkConfig?.IPConfig?.IPAddress;

    /// <summary>
    /// Sets the configuration of the current network.
    /// </summary>
    /// <param name="device">The network device to use.</param>
    /// <param name="config">The IPv4 configuration associated with the device to use.</param>
    public static void SetCurrentConfig(INetworkDevice device, IPConfig config)
    {
        CurrentNetworkConfig = new NetworkConfigEntry(device, config);
    }

    /// <summary>
    /// Adds a new network configuration.
    /// </summary>
    /// <param name="device">The network device to use.</param>
    /// <param name="config">The IPv4 configuration associated with the device to use.</param>
    public static void AddConfig(INetworkDevice device, IPConfig config)
    {
        NetworkConfigs.Add(new NetworkConfigEntry(device, config));
    }

    /// <summary>
    /// Returns whether the network stack contains the given network device.
    /// </summary>
    public static bool ConfigsContainsDevice(INetworkDevice targetDevice)
    {
        if (NetworkConfigs == null)
        {
            return false;
        }

        foreach (var config in NetworkConfigs)
        {
            if (targetDevice == config.Device)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Clears network configurations, removing each configuration.
    /// </summary>
    public static void ClearConfigs()
    {
        NetworkConfigs.Clear();
    }

    /// <summary>
    /// Get the IPv4 configuration for the given network device.
    /// </summary>
    /// <param name="device">Network device.</param>
    public static IPConfig? Get(INetworkDevice device)
    {
        foreach (var networkConfig in NetworkConfigs)
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
    public static void Remove(INetworkDevice key)
    {
        NetworkConfigEntry? toRemove = null;
        foreach (var networkConfig in NetworkConfigs)
        {
            if (key == networkConfig.Device)
            {
                toRemove = networkConfig;
                break;
            }
        }

        if (toRemove != null)
        {
            NetworkConfigs.Remove(toRemove);
        }
    }
}
