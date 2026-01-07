using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Network.Config;

/// <summary>
/// Represents network configuration, linking a network device
/// to an IP address.
/// </summary>
public class NetworkConfig
{
    /// <summary>
    /// The network device associated with this <see cref="NetworkConfig"/> instance.
    /// </summary>
    public INetworkDevice Device;

    /// <summary>
    /// The IPv4 configuration.
    /// </summary>
    public IPConfig IPConfig;

    internal NetworkConfig(INetworkDevice device, IPConfig config)
    {
        Device = device;
        IPConfig = config;
    }
}
