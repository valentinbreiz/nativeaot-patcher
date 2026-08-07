/*
 * PROJECT:          Aura Operating System Development
 * CONTENT:          IP Address
 * PROGRAMMERS:      Valentin Charbonnier <valentinbreiz@gmail.com>
 *                   Port of Cosmos Code.
 */

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.System.Network.IPv4;

namespace Cosmos.Kernel.System.Network;

public enum AddressType
{
    IPv4,
    IPv6
}

public enum AddressParsingStyle
{
    Dec,
    Hex
}

/// <summary>
/// Represents a IPv4 address.
/// </summary>
public abstract class Address : IComparable<Address>
{
    public bool IsIpv4 => this is Address4;
    public bool IsIpv6 => !IsIpv4;
    public abstract bool IsZero { get; }
    public abstract MaskedAddress Parts { get; }
    public AddressType AddressType => IsIpv6 ? AddressType.IPv6 : AddressType.IPv4;
    public abstract bool IsBroadcastAddress { get; }

    /// <summary>
    /// Parses an IP address in its string representation.
    /// </summary>
    /// <param name="addr">The IP address as string.</param>
    /// <returns>The parsed address value or null when parsing fails.</returns>
    public static Address? Parse(ReadOnlySpan<char> addr)
    {
       return Address4.Parse(addr);
    }

    /// <summary>
    /// Check if this address is a loopback address.
    /// </summary>
    public abstract bool IsLoopbackAddress();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint ToUint32(ReadOnlySpan<byte> buffer)
    {
        return ToUint32(buffer[0], buffer[1], buffer[2], buffer[3]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint ToUint32(byte aFirst, byte aSecond, byte aThird, byte aFourth)
    {
        return (uint)((aFirst << 24) | (aSecond << 16) | (aThird << 8) | aFourth);
    }

    internal static void SegmentToSpan(uint segment, Span<byte> destination)
    {
        destination[0] = (byte)(segment >> 24);
        destination[1] = (byte)((segment >> 16) & 0xFF);
        destination[2] = (byte)((segment >> 8) & 0xFF);
        destination[3] = (byte)(segment & 0xFF);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract ImmutableArray<byte> ToBytes();

    public int CompareTo(Address? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (this is Address4 address4 && other is Address4 otherAddress4)
        {
            return address4.CompareTo(otherAddress4);
        }

        throw new ArgumentException("Only addresses of same type can be compared", nameof(other));
    }

    public static MaskedAddress operator &(Address a, Address b)
    {
        return a.OperatorBitwiseAnd(b);
    }

    protected abstract MaskedAddress OperatorBitwiseAnd(Address other);

    public static bool operator ==(Address a, Address b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is Address4 a4 && b is Address4 b4)
        {
            return a4.Equals(b4);
        }
        return false;
    }

    public static bool operator !=(Address a, Address b) => !(a == b);

}

public readonly ref struct MaskedAddress: IEquatable<MaskedAddress>
{
    public uint Segment1 { get; }
    public uint Segment2 { get; }
    public uint Segment3 { get; }
    public uint Segment4 { get; }
    public AddressType AddressType { get; }

    public MaskedAddress(uint segment1)
    {
        Segment1 = segment1;
        AddressType = AddressType.IPv4;
    }
    public MaskedAddress(uint segment1, uint  segment2, uint segment3, uint segment4)
    {
        Segment1 = segment1;
        Segment2 = segment2;
        Segment3 = segment3;
        Segment4 = segment4;
        AddressType = AddressType.IPv6;
    }

    public bool Equals(MaskedAddress other) => Segment1 == other.Segment1 && Segment2 == other.Segment2 &&
                                               Segment3 == other.Segment3 && Segment4 == other.Segment4 &&
                                               AddressType == other.AddressType;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(MaskedAddress a, MaskedAddress b)
    {
        return a.Equals(b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(MaskedAddress a, MaskedAddress b) => !(a == b);

    public byte this[int index]
    {
        get
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException($"{nameof(index)} can not be lower than zero");
            }
            int maxIndex = AddressType == AddressType.IPv4 ? 4 : 16;
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, maxIndex, nameof(index));
            uint segment = (index / 4) switch
            {
                0 => Segment1,
                1 => Segment2,
                2 => Segment3,
                3 => Segment4,
                _ => throw new ArgumentOutOfRangeException($"Invalid {nameof(index)} of {index}"),
            };
            int part = index % 4;
            return (byte)(segment >> ((3 - part) * 8) & 0xFF);
        }
    }
}
