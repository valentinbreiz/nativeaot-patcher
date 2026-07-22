/*
 * PROJECT:          Aura Operating System Development
 * CONTENT:          IP Address
 * PROGRAMMERS:      Valentin Charbonnier <valentinbreiz@gmail.com>
 *                   Port of Cosmos Code.
 */

using System.Collections.Immutable;

namespace Cosmos.Kernel.System.Network.IPv4;

/// <summary>
/// Represents a IPv4 address.
/// </summary>
public sealed class Address : IComparable
{
    private uint _hash;

    /// <summary>
    /// The parts of the address.
    /// </summary>
    public ImmutableArray<byte> Parts { get; }

    public bool IsIpv4 => Parts.Length == 4;
    public bool IsIpv6 => !IsIpv4;

    /// <summary>
    /// The <c>0.0.0.0</c> IP address.
    /// </summary>
    public static readonly Address Zero = new(0, 0, 0, 0);

    /// <summary>
    /// The broadcast address <c>(255.255.255.255)</c>.
    /// </summary>
    public static readonly Address Broadcast = new(255, 255, 255, 255);

    /// <summary>
    /// Create new instance of the <see cref="Address"/> class, with specified IP address.
    /// </summary>
    /// <param name="address">Address</param>
    public Address(uint address)
    {
        Parts =
        [
            (byte)((address >> 24) & 0xFF),
            (byte)((address >> 16) & 0xFF),
            (byte)((address >> 8) & 0xFF),
            (byte)(address & 0xFF)
        ];
    }

    /// <summary>
    /// Create new instance of the <see cref="Address"/> class, with specified IP address.
    /// </summary>
    /// <param name="aFirst">First block of the address.</param>
    /// <param name="aSecond">Second block of the address.</param>
    /// <param name="aThird">Third block of the address.</param>
    /// <param name="aFourth">Fourth block of the address.</param>
    public Address(byte aFirst, byte aSecond, byte aThird, byte aFourth)
    {
        Parts = [aFirst, aSecond, aThird, aFourth];
    }

    /// <summary>
    /// Create new instance of the <see cref="Address"/> class, with specified buffer and offset.
    /// </summary>
    /// <param name="buffer">Buffer.</param>
    /// <param name="offset">Offset.</param>
    public Address(byte[] buffer, int offset) : this(new ReadOnlySpan<byte>(buffer, offset, 4))
    {
    }

    /// <summary>
    /// Creates a new <see cref="Address"/> instance, with the specified byte span.
    /// </summary>
    /// <param name="buffer"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public Address(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length != 4)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer), $"Buffer has to be 4 bytes long");
        }

        Parts = [..buffer[0..4]];

    }

    /// <summary>
    /// Parses an IP address in its string representation.
    /// </summary>
    /// <param name="addr">The IP address as string.</param>
    /// <returns>The parsed address value or null when parsing fails.</returns>
    public static Address? Parse(ReadOnlySpan<char> addr)
    {
        var fragments = addr.Split('.');
        Span<byte> addressBytes = stackalloc byte[4];

        int index = 0;
        foreach (var fragment in fragments)
        {
            if (!byte.TryParse(addr[fragment], out byte value))
            {
                return null;
            }
            addressBytes[index++] = value;
            if (index == 4)
            {
                return new Address(addressBytes);
            }
        }

        return null;
    }

    /// <summary>
    /// Convert a CIDR number to an IPv4 address.
    /// </summary>
    /// <param name="cidr">The CIDR number.</param>
    public static Address? CIDRToAddress(int cidr)
    {
        try
        {
            uint mask = 0xffffffff << (32 - cidr);
            return new Address((byte)(mask >> 24), (byte)(mask >> 16 & 0xff), (byte)(mask >> 8 & 0xff), (byte)(mask & 0xff));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Check if this address is a loopback address.
    /// </summary>
    public bool IsLoopbackAddress() => Parts[0] == 127;

    /// <summary>
    /// Check if this address is a broadcast address.
    /// </summary>
    public bool IsBroadcastAddress() =>
        Parts[0] == 0xFF
        && Parts[1] == 0xFF
        && Parts[2] == 0xFF
        && Parts[3] == 0xFF;

    /// <summary>
    /// Check if this address is an APIPA address.
    /// </summary>
    public bool IsAPIPA() => Parts[0] == 169 && Parts[1] == 254;

    public override string ToString()
    {
        return $"{Parts[0]}.{Parts[1]}.{Parts[2]}.{Parts[3]}";
    }

    public ReadOnlySpan<byte> ToSpan() => Parts.AsSpan();

    /// <summary>
    /// Convert this address to a 32-bit number.
    /// </summary>
    public uint ToUInt32()
    {
        return (uint)((Parts[0] << 24) | (Parts[1] << 16) | (Parts[2] << 8) | (Parts[3] << 0));
    }

    /// <summary>
    /// The hash value for this IP. Used to uniquely identify each IP.
    /// </summary>
    public uint Hash
    {
        get
        {
            if (_hash == 0)
            {
                _hash = ToUInt32();
            }

            return _hash;
        }
    }

    // TODO remove, equals is enough
    public int CompareTo(object? obj)
    {
        if (obj is Address other)
        {
            // Compare through the property: the backing field is lazily
            // computed and stays 0 until Hash is first read.
            if (other.Hash != Hash)
            {
                return -1;
            }

            return 0;
        }
        else
        {
            throw new ArgumentException("obj is not a IPv4Address", nameof(obj));
        }
    }

    public override bool Equals(object? obj)
    {
        if (obj is Address other)
        {
            return Parts.SequenceEqual(other.Parts);
        }

        return false; // obj is not an Address

    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Hash);
    }
}
