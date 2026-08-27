using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Network.Config;

/// <summary>
/// Represents a single network configuration entry, linking a network device
/// to an IP address. Internal: the device half is internal, and the IP half
/// is what <see cref="NetworkConfigManager.Current"/> hands a kernel.
/// </summary>
internal class NetworkConfigEntry
{
    /// <summary>
    /// The network device associated with this <see cref="NetworkConfigEntry"/> instance.
    /// </summary>
    internal INetworkDevice Device { get; }

    /// <summary>
    /// The IPv4 configuration.
    /// </summary>
    internal IPConfig IPConfig { get; }

    internal NetworkConfigEntry(INetworkDevice device, IPConfig config)
    {
        Device = device;
        IPConfig = config;
    }
}
