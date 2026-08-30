using System.Diagnostics.CodeAnalysis;
using Cosmos.Kernel.HAL.Devices.Network;
using Cosmos.Kernel.System.Network.IPv4;

namespace Cosmos.Kernel.System.Network.ARP;

/// <summary>
/// Manages the ARP (Address Resolution Protocol) cache.
/// </summary>
internal static class ARPCache
{
    /// <summary>
    /// The cache map.
    /// </summary>
    public static Dictionary<Address, MACAddress>? Cache;

    /// <summary>
    /// Ensures the cache map exists.
    /// </summary>
    [MemberNotNull(nameof(Cache))]
    private static void EnsureCacheExists()
    {
        Cache ??= new Dictionary<Address, MACAddress>();
    }

    /// <summary>
    /// Checks whether the ARP cache contains the given IP.
    /// </summary>
    /// <param name="ipAddress">The IP address to check.</param>
    internal static bool Contains(Address ipAddress)
    {
        EnsureCacheExists();
        return Cache.ContainsKey(ipAddress);
    }

    /// <summary>
    /// Updates the ARP cache.
    /// </summary>
    /// <param name="ipAddress">The IP address.</param>
    /// <param name="macAddress">The MAC address.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown on fatal error.</exception>
    /// <exception cref="global::System.IO.IOException">Thrown on IO error.</exception>
    /// <exception cref="ArgumentException">Thrown on fatal error.</exception>
    internal static void Update(Address ipAddress, MACAddress macAddress)
    {
        EnsureCacheExists();
        if (Equals(ipAddress, Address4.Zero))
        {
            return;
        }

        if (Cache.ContainsKey(ipAddress) == false)
        {
            Cache.Add(ipAddress, macAddress);
        }
        else
        {
            Cache[ipAddress] = macAddress;
        }
    }

    /// <summary>
    /// Resolve an IP address to a MAC address using the ARP cache.
    /// </summary>
    /// <param name="ipAddress">IP address.</param>
    /// <returns>The resolved MAC address, or <see langword="null"/> if no cache entry for the given IP address exists.</returns>
    internal static MACAddress? Resolve(Address ipAddress)
    {
        EnsureCacheExists();

        if (!Cache.TryGetValue(ipAddress, out MACAddress? resolve))
        {
            return null;
        }

        return resolve;
    }
}
