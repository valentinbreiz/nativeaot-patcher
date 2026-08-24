namespace Cosmos.Kernel.System.Network.IPv4;

/// <summary>
/// Represents an IPv4 end-point.
/// </summary>
public class EndPoint : IComparable
{
    /// <summary>
    /// The address of the end-point.
    /// </summary>
    public Address Address { get; set; }

    /// <summary>
    /// The port of the end-point.
    /// </summary>
    public ushort Port { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="EndPoint"/> class.
    /// </summary>
    /// <param name="addr">The IPv4 address.</param>
    /// <param name="port">The port.</param>
    public EndPoint(Address addr, ushort port)
    {
        Address = addr;
        Port = port;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EndPoint"/> class.
    /// </summary>
    /// <param name="addr">The IPv4 address.</param>
    /// <param name="port">The port.</param>
    public EndPoint(uint addr, ushort port)
    {
        Address = new Address(addr);
        Port = port;
    }

    /// <summary>
    /// Formats the end point as <c>address:port</c>.
    /// </summary>
    public override string ToString()
    {
        return Address.ToString() + ":" + Port.ToString();
    }

    /// <summary>
    /// Compares this end point with another: 0 when address and port both
    /// match, -1 otherwise; non-<see cref="EndPoint"/> arguments sort after.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    public int CompareTo(object? obj)
    {
        if (obj is EndPoint other)
        {
            if (other.Address.CompareTo(Address) != 0 || other.Port != Port)
            {
                return -1;
            }

            return 0;
        }
        else
        {
            throw new ArgumentException("'obj' is not an EndPoint instance", nameof(obj));
        }
    }
}
