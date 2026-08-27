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
    /// The six address bytes, most significant first. Internal because the
    /// array is mutable: handing it out would let a caller rewrite an
    /// address in place and desynchronize the maps keyed on it.
    /// </summary>
    internal readonly byte[] _bytes = new byte[6];

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

        _bytes[0] = address[0];
        _bytes[1] = address[1];
        _bytes[2] = address[2];
        _bytes[3] = address[3];
        _bytes[4] = address[4];
        _bytes[5] = address[5];

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

        _bytes[0] = buffer[offset];
        _bytes[1] = buffer[offset + 1];
        _bytes[2] = buffer[offset + 2];
        _bytes[3] = buffer[offset + 3];
        _bytes[4] = buffer[offset + 4];
        _bytes[5] = buffer[offset + 5];
    }

    /// <summary>
    /// Create a copy of an existing MAC address.
    /// </summary>
    /// <param name="m">MAC address to copy.</param>
    public MACAddress(MACAddress m)
        : this(m._bytes)
    {
    }


    /// <summary>
    /// Check that the address holds six bytes.
    /// </summary>
    /// <returns>True if the address is 6 bytes long.</returns>
    public bool IsValid()
    {
        return _bytes.Length == 6;
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
            i = _bytes[0].CompareTo(other._bytes[0]);
            if (i != 0)
            {
                return i;
            }

            i = _bytes[1].CompareTo(other._bytes[1]);
            if (i != 0)
            {
                return i;
            }

            i = _bytes[2].CompareTo(other._bytes[2]);
            if (i != 0)
            {
                return i;
            }

            i = _bytes[3].CompareTo(other._bytes[3]);
            if (i != 0)
            {
                return i;
            }

            i = _bytes[4].CompareTo(other._bytes[4]);
            if (i != 0)
            {
                return i;
            }

            i = _bytes[5].CompareTo(other._bytes[5]);
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

            return _bytes[0] == other._bytes[0] &&
                _bytes[1] == other._bytes[1] &&
                _bytes[2] == other._bytes[2] &&
                _bytes[3] == other._bytes[3] &&
                _bytes[4] == other._bytes[4] &&
                _bytes[5] == other._bytes[5];
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
    /// <returns>The address as a 48-bit number in the low six bytes.</returns>
    public ulong ToNumber()
    {
        // Each byte is widened before shifting: a byte promotes to int, an
        // int shift masks the count to five bits, so <<40 and <<32 used to
        // fold the first two bytes onto the last two, and a byte above 0x7F
        // in the <<24 lane made the int negative and sign-extended the cast.
        return ((ulong)_bytes[0] << 40) | ((ulong)_bytes[1] << 32) | ((ulong)_bytes[2] << 24) |
            ((ulong)_bytes[3] << 16) | ((ulong)_bytes[4] << 8) | _bytes[5];
    }

    private static void PutByte(char[] aChars, int aIndex, byte aByte)
    {
        string xChars = "0123456789ABCDEF";
        aChars[aIndex + 0] = xChars[(aByte >> 4) & 0xF];
        aChars[aIndex + 1] = xChars[aByte & 0xF];
    }

    /// <summary>
    /// Fold the six address bytes into a 32-bit value, used as the
    /// <see cref="Hash"/>. Six bytes do not fit in four, so this is a hash
    /// rather than a lossless conversion; every byte contributes to it.
    /// </summary>
    /// <returns>A 32-bit value derived from all six address bytes.</returns>
    public uint To32BitNumber()
    {
        // FNV-1a. The previous expression dropped the first two bytes onto
        // the lanes of the last two, so two addresses differing only in
        // their OUI could collide in NetworkStack's device map.
        const uint FnvOffsetBasis = 2166136261;
        const uint FnvPrime = 16777619;

        uint hash = FnvOffsetBasis;
        for (int i = 0; i < 6; i++)
        {
            hash ^= _bytes[i];
            hash *= FnvPrime;
        }

        return hash;
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
        PutByte(xChars, 0, _bytes[0]);
        xChars[2] = ':';
        PutByte(xChars, 3, _bytes[1]);
        xChars[5] = ':';
        PutByte(xChars, 6, _bytes[2]);
        xChars[8] = ':';
        PutByte(xChars, 9, _bytes[3]);
        xChars[11] = ':';
        PutByte(xChars, 12, _bytes[4]);
        xChars[14] = ':';
        PutByte(xChars, 15, _bytes[5]);
        return new string(xChars);
    }
}
