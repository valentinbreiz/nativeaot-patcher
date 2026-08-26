namespace Cosmos.Kernel.HAL.Devices.Network;

/// <summary>
/// A 48-bit Ethernet MAC address.
/// </summary>
public class MACAddress : IComparable
{
    private static MACAddress? s_broadcast;
    private static MACAddress? s_none;

    /// <summary>
    /// The broadcast address (FF:FF:FF:FF:FF:FF).
    /// </summary>
    public static MACAddress Broadcast
    {
        get
        {
            if (s_broadcast == null)
            {
                s_broadcast = new MACAddress([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
            }
            return s_broadcast;
        }
    }

    /// <summary>
    /// The all-zero address (00:00:00:00:00:00), used when no address is assigned.
    /// </summary>
    public static MACAddress None
    {
        get
        {
            if (s_none == null)
            {
                s_none = new MACAddress([0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
            }
            return s_none;
        }
    }

    /// <summary>
    /// The six address bytes, most significant first.
    /// </summary>
    public readonly byte[] bytes = new byte[6];

    /// <summary>
    /// Create a MAC address from a 6-byte array.
    /// </summary>
    /// <param name="address">The six address bytes, most significant first.</param>
    public MACAddress(byte[] address)
    {
        if (address == null || address.Length != 6)
        {
            throw new ArgumentException("MACAddress is null or has wrong length", nameof(address));
        }

        bytes[0] = address[0];
        bytes[1] = address[1];
        bytes[2] = address[2];
        bytes[3] = address[3];
        bytes[4] = address[4];
        bytes[5] = address[5];

    }

    /// <summary>
    /// Create a MAC address from a byte buffer starting at the specified offset
    /// </summary>
    /// <param name="buffer">byte buffer</param>
    /// <param name="offset">offset in buffer to start from</param>
    public MACAddress(byte[] buffer, int offset)
    {
        if (buffer == null || buffer.Length < offset + 6)
        {
            throw new ArgumentException("buffer does not contain enough data starting at offset", nameof(buffer));
        }

        bytes[0] = buffer[offset];
        bytes[1] = buffer[offset + 1];
        bytes[2] = buffer[offset + 2];
        bytes[3] = buffer[offset + 3];
        bytes[4] = buffer[offset + 4];
        bytes[5] = buffer[offset + 5];
    }

    /// <summary>
    /// Create a copy of an existing MAC address.
    /// </summary>
    /// <param name="m">MAC address to copy.</param>
    public MACAddress(MACAddress m)
        : this(m.bytes)
    {
    }


    /// <summary>
    /// Check that the address holds six bytes.
    /// </summary>
    /// <returns>True if the address is 6 bytes long.</returns>
    public bool IsValid()
    {
        return bytes.Length == 6;
    }

    /// <summary>
    /// Compare this address to another MAC address, byte by byte from the
    /// most significant byte.
    /// </summary>
    /// <param name="obj">MAC address to compare against.</param>
    /// <returns>Negative, zero, or positive following the ordering of the first differing byte.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="obj"/> is not a <see cref="MACAddress"/>.</exception>
    public int CompareTo(object? obj)
    {
        if (obj is MACAddress)
        {
            MACAddress other = (MACAddress)obj;
            int i = 0;
            i = bytes[0].CompareTo(other.bytes[0]);
            if (i != 0)
            {
                return i;
            }

            i = bytes[1].CompareTo(other.bytes[1]);
            if (i != 0)
            {
                return i;
            }

            i = bytes[2].CompareTo(other.bytes[2]);
            if (i != 0)
            {
                return i;
            }

            i = bytes[3].CompareTo(other.bytes[3]);
            if (i != 0)
            {
                return i;
            }

            i = bytes[4].CompareTo(other.bytes[4]);
            if (i != 0)
            {
                return i;
            }

            i = bytes[5].CompareTo(other.bytes[5]);
            if (i != 0)
            {
                return i;
            }

            return 0;
        }
        else
        {
            throw new ArgumentException("obj is not a MACAddress", "obj");
        }
    }

    /// <summary>
    /// Check whether another MAC address has the same six bytes.
    /// </summary>
    /// <param name="obj">MAC address to compare against.</param>
    /// <returns>True if all six bytes are equal.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="obj"/> is not a <see cref="MACAddress"/>.</exception>
    public override bool Equals(object? obj)
    {
        if (obj is MACAddress)
        {
            MACAddress other = (MACAddress)obj;

            return bytes[0] == other.bytes[0] &&
                bytes[1] == other.bytes[1] &&
                bytes[2] == other.bytes[2] &&
                bytes[3] == other.bytes[3] &&
                bytes[4] == other.bytes[4] &&
                bytes[5] == other.bytes[5];
        }
        else
        {
            throw new ArgumentException("obj is not a MACAddress", "obj");
        }
    }

    /// <summary>
    /// Get a hash code derived from the type name and the string form of the address.
    /// </summary>
    /// <returns>Hash code for this address.</returns>
    public override int GetHashCode()
    {
        return (GetType().AssemblyQualifiedName + "|" + ToString()).GetHashCode();
    }

    /// <summary>
    /// Combine the address bytes into a single unsigned number,
    /// most significant byte first.
    /// </summary>
    /// <returns>The address as a number.</returns>
    public ulong ToNumber()
    {
        return ((ulong)bytes[0] << 40) | ((ulong)bytes[1] << 32) | ((ulong)bytes[2] << 24) |
            ((ulong)bytes[3] << 16) | ((ulong)bytes[4] << 8) | bytes[5];
    }

    private static void PutByte(char[] aChars, int aIndex, byte aByte)
    {
        string xChars = "0123456789ABCDEF";
        aChars[aIndex + 0] = xChars[(aByte >> 4) & 0xF];
        aChars[aIndex + 1] = xChars[aByte & 0xF];
    }

    /// <summary>
    /// Fold all six address bytes into a 32-bit unsigned number. Used as the
    /// <see cref="Hash"/> value; it is a hash, not a truncation, so it does
    /// not round-trip back to an address.
    /// </summary>
    /// <returns>The folded address.</returns>
    public uint To32BitNumber()
    {
        ulong value = ToNumber();
        return (uint)value ^ (uint)(value >> 32);
    }

    private uint _hash;
    /// <summary>
    /// Hash value for this mac. Used to uniquely identify each mac
    /// </summary>
    public uint Hash
    {
        get
        {
            if (_hash == 0)
            {
                _hash = To32BitNumber();
            }

            return _hash;
        }
    }

    /// <summary>
    /// Format the address as six colon-separated hex byte pairs
    /// (e.g. "52:54:00:12:34:56").
    /// </summary>
    /// <returns>The address in colon-separated hex notation.</returns>
    public override string ToString()
    {
        // mac address consists of 6 2chars pairs, delimited by :
        char[] xChars = new char[17];
        PutByte(xChars, 0, bytes[0]);
        xChars[2] = ':';
        PutByte(xChars, 3, bytes[1]);
        xChars[5] = ':';
        PutByte(xChars, 6, bytes[2]);
        xChars[8] = ':';
        PutByte(xChars, 9, bytes[3]);
        xChars[11] = ':';
        PutByte(xChars, 12, bytes[4]);
        xChars[14] = ':';
        PutByte(xChars, 15, bytes[5]);
        return new string(xChars);
    }
}
