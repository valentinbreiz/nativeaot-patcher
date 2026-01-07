/*
* PROJECT:          Cosmos OS Development
* CONTENT:          DNS Config
* PROGRAMMERS:      Valentin Charbonnier <valentinbreiz@gmail.com>
*                   Port of Cosmos Code.
*/

using System.Collections.Generic;
using Cosmos.Kernel.System.Network.IPv4;

namespace Cosmos.Kernel.System.Network.Config;

/// <summary>
/// Represents DNS configuration.
/// </summary>
public class DNSConfig
{
    /// <summary>
    /// The list of known DNS nameserver addresses.
    /// </summary>
    public static readonly List<Address> DNSNameservers = new();

    /// <summary>
    /// Registers a given DNS server.
    /// </summary>
    /// <param name="nameserver">The IP address of the target DNS server.</param>
    public static void Add(Address nameserver)
    {
        for (int i = 0; i < DNSNameservers.Count; i++)
        {
            if (DNSNameservers[i].Hash == nameserver.Hash)
            {
                return;
            }
        }
        DNSNameservers.Add(nameserver);
    }

    /// <summary>
    /// Removes the given DNS server from the list of registered nameservers.
    /// </summary>
    /// <param name="nameserver">The IP address of the target DNS server.</param>
    public static void Remove(Address nameserver)
    {
        Address toRemove = null;
        for (int i = 0; i < DNSNameservers.Count; i++)
        {
            if (DNSNameservers[i].Hash == nameserver.Hash)
            {
                toRemove = DNSNameservers[i];
                break;
            }
        }
        if (toRemove != null)
        {
            DNSNameservers.Remove(toRemove);
        }
    }
}
