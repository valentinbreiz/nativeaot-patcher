using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Network.Config;

/// <summary>
/// Represents a single network configuration entry, linking a network device
/// to an IP address.
/// </summary>
public class NetworkConfigEntry
{
    /// <summary>
    /// The network device associated with this <see cref="NetworkConfigEntry"/> instance.
    /// </summary>
    public INetworkDevice Device;

    /// <summary>
    /// The IPv4 configuration.
    /// </summary>
    public IPConfig IPConfig;

    internal NetworkConfigEntry(INetworkDevice device, IPConfig config)
    {
        Device = device;
        IPConfig = config;
    }
}
