// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.System.Filesystems.Fat;

/// <summary>FAT directory-entry attribute bits (FAT spec).</summary>
[Flags]
public enum FatAttr : byte
{
    None = 0,
    ReadOnly = 0x01,
    Hidden = 0x02,
    System = 0x04,
    VolumeId = 0x08,
    Directory = 0x10,
    Archive = 0x20,
    Lfn = ReadOnly | Hidden | System | VolumeId,
}

/// <summary>Parsed FAT directory entry. <see cref="ByteOffset"/> is the byte position of the 8.3 record within the buffer that produced it; LFN entries that contributed to <see cref="Name"/> precede it.</summary>
public sealed class FatDirEntry
{
    public string Name { get; }
    public string ShortName { get; }
    public FatAttr Attributes { get; }
    public uint FirstCluster { get; }
    public uint Size { get; }
    public int ByteOffset { get; }
    public int LfnEntryCount { get; }

    public FatDirEntry(
        string name,
        string shortName,
        FatAttr attributes,
        uint firstCluster,
        uint size,
        int byteOffset,
        int lfnEntryCount)
    {
        Name = name;
        ShortName = shortName;
        Attributes = attributes;
        FirstCluster = firstCluster;
        Size = size;
        ByteOffset = byteOffset;
        LfnEntryCount = lfnEntryCount;
    }

    public bool IsDirectory => (Attributes & FatAttr.Directory) != 0;
    public bool IsVolumeId => (Attributes & FatAttr.VolumeId) != 0;
}

/// <summary>
/// Parser and encoder for FAT directory clusters. Operates over raw byte
/// spans; no I/O is performed here.
/// </summary>
public static class FatDirectory
{
    public const int EntrySize = 32;
    private const byte DeletedMarker = 0xE5;
    private const byte LfnLastBit = 0x40;
    private const int LfnCharsPerEntry = 13;

    public static List<FatDirEntry> Parse(ReadOnlySpan<byte> buffer)
    {
        List<FatDirEntry> result = new();
        Span<char> lfnAccum = stackalloc char[LfnCharsPerEntry * 20];
        int lfnLength = 0;
        int lfnEntryCount = 0;

        for (int offset = 0; offset + EntrySize <= buffer.Length; offset += EntrySize)
        {
            byte first = buffer[offset];
            if (first == 0x00)
            {
                break;
            }

            if (first == DeletedMarker)
            {
                lfnLength = 0;
                lfnEntryCount = 0;
                continue;
            }

            FatAttr attr = (FatAttr)buffer[offset + 11];

            if (attr == FatAttr.Lfn)
            {
                byte sequence = buffer[offset];
                int seqIndex = (sequence & 0x1F) - 1;
                if (seqIndex < 0 || seqIndex >= 20)
                {
                    lfnLength = 0;
                    lfnEntryCount = 0;
                    continue;
                }

                int destBase = seqIndex * LfnCharsPerEntry;
                ReadLfnChars(buffer.Slice(offset, EntrySize), lfnAccum.Slice(destBase, LfnCharsPerEntry));

                int candidateLength = destBase + LfnCharsPerEntry;
                if (candidateLength > lfnLength)
                {
                    lfnLength = candidateLength;
                }
                lfnEntryCount++;
                continue;
            }

            string shortName = DecodeShortName(buffer.Slice(offset, 11), first);
            string longName = lfnLength > 0
                ? TrimLfn(lfnAccum.Slice(0, lfnLength))
                : shortName;

            uint firstClusterHigh = BitConverter.ToUInt16(buffer.Slice(offset + 20, 2));
            uint firstClusterLow = BitConverter.ToUInt16(buffer.Slice(offset + 26, 2));
            uint firstCluster = (firstClusterHigh << 16) | firstClusterLow;
            uint size = BitConverter.ToUInt32(buffer.Slice(offset + 28, 4));

            result.Add(new FatDirEntry(
                longName,
                shortName,
                attr,
                firstCluster,
                size,
                offset,
                lfnEntryCount));

            lfnLength = 0;
            lfnEntryCount = 0;
        }

        return result;
    }

    /// <summary>Locate the first free entry slot (deleted or unused) where
    /// <paramref name="entriesNeeded"/> consecutive 32-byte slots fit.</summary>
    public static int FindFreeRun(ReadOnlySpan<byte> buffer, int entriesNeeded)
    {
        int run = 0;
        int runStart = -1;

        for (int offset = 0; offset + EntrySize <= buffer.Length; offset += EntrySize)
        {
            byte first = buffer[offset];
            if (first == 0x00 || first == DeletedMarker)
            {
                if (run == 0)
                {
                    runStart = offset;
                }
                run++;
                if (run >= entriesNeeded)
                {
                    return runStart;
                }
            }
            else
            {
                run = 0;
                runStart = -1;
            }
        }

        return -1;
    }

    public static int LfnEntryCountFor(string name)
    {
        if (FitsInShortName(name))
        {
            return 0;
        }
        return (name.Length + LfnCharsPerEntry - 1) / LfnCharsPerEntry;
    }

    /// <summary>Write an 8.3 entry to <paramref name="dest"/> at <paramref name="offset"/>; <paramref name="dest"/> must be writable.</summary>
    public static void WriteShortEntry(
        Span<byte> dest,
        int offset,
        ReadOnlySpan<char> shortName11,
        FatAttr attributes,
        uint firstCluster,
        uint size)
    {
        Span<byte> entry = dest.Slice(offset, EntrySize);
        entry.Clear();

        for (int i = 0; i < 11 && i < shortName11.Length; i++)
        {
            entry[i] = (byte)shortName11[i];
        }
        for (int i = shortName11.Length; i < 11; i++)
        {
            entry[i] = 0x20;
        }

        entry[11] = (byte)attributes;
        BitConverter.TryWriteBytes(entry.Slice(20, 2), (ushort)((firstCluster >> 16) & 0xFFFFu));
        BitConverter.TryWriteBytes(entry.Slice(26, 2), (ushort)(firstCluster & 0xFFFFu));
        BitConverter.TryWriteBytes(entry.Slice(28, 4), size);
    }

    public static void WriteLfnEntries(
        Span<byte> dest,
        int offset,
        ReadOnlySpan<char> longName,
        ReadOnlySpan<char> shortName11)
    {
        int entries = LfnEntryCountFor(longName.ToString());
        if (entries == 0)
        {
            return;
        }

        byte checksum = ComputeShortChecksum(shortName11);
        Span<char> chunk = stackalloc char[LfnCharsPerEntry];
        for (int i = 0; i < entries; i++)
        {
            int seq = entries - i;
            int sourceStart = (seq - 1) * LfnCharsPerEntry;
            for (int c = 0; c < LfnCharsPerEntry; c++)
            {
                int srcIdx = sourceStart + c;
                if (srcIdx < longName.Length)
                {
                    chunk[c] = longName[srcIdx];
                }
                else if (srcIdx == longName.Length)
                {
                    chunk[c] = '\0';
                }
                else
                {
                    chunk[c] = (char)0xFFFF;
                }
            }

            byte sequence = (byte)seq;
            if (i == 0)
            {
                sequence |= LfnLastBit;
            }

            Span<byte> entry = dest.Slice(offset + i * EntrySize, EntrySize);
            entry.Clear();
            entry[0] = sequence;
            entry[11] = (byte)FatAttr.Lfn;
            entry[12] = 0;
            entry[13] = checksum;

            WriteUcs2(chunk.Slice(0, 5), entry.Slice(1, 10));
            WriteUcs2(chunk.Slice(5, 6), entry.Slice(14, 12));
            WriteUcs2(chunk.Slice(11, 2), entry.Slice(28, 4));
        }
    }

    public static byte ComputeShortChecksum(ReadOnlySpan<char> shortName11)
    {
        byte sum = 0;
        for (int i = 0; i < 11; i++)
        {
            byte b = i < shortName11.Length ? (byte)shortName11[i] : (byte)0x20;
            sum = (byte)(((sum & 1) != 0 ? 0x80 : 0) + (sum >> 1) + b);
        }
        return sum;
    }

    public static void BuildShortName(string longName, Span<char> dest11)
    {
        for (int i = 0; i < 11; i++)
        {
            dest11[i] = ' ';
        }

        if (string.IsNullOrEmpty(longName))
        {
            return;
        }

        int dot = longName.LastIndexOf('.');
        ReadOnlySpan<char> baseName = dot >= 0 ? longName.AsSpan(0, dot) : longName.AsSpan();
        ReadOnlySpan<char> ext = dot >= 0 && dot + 1 < longName.Length
            ? longName.AsSpan(dot + 1)
            : ReadOnlySpan<char>.Empty;

        int e = 0;
        for (int i = 0; i < ext.Length && e < 3; i++)
        {
            char c = NormalizeShort(ext[i]);
            if (c != '\0')
            {
                dest11[8 + e++] = c;
            }
        }

        bool fits = FitsInShortName(longName);
        int baseBudget = fits ? 8 : 6;

        int b = 0;
        for (int i = 0; i < baseName.Length && b < baseBudget; i++)
        {
            char c = NormalizeShort(baseName[i]);
            if (c != '\0')
            {
                dest11[b++] = c;
            }
        }

        if (!fits)
        {
            dest11[b++] = '~';
            dest11[b++] = '1';
        }
    }

    public static bool FitsInShortName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 12)
        {
            return false;
        }

        int dot = name.LastIndexOf('.');
        int baseLen = dot >= 0 ? dot : name.Length;
        int extLen = dot >= 0 ? name.Length - dot - 1 : 0;
        if (baseLen == 0 || baseLen > 8 || extLen > 3)
        {
            return false;
        }

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '.')
            {
                continue;
            }
            if (NormalizeShort(c) == '\0' || char.IsLower(c))
            {
                return false;
            }
        }
        return true;
    }

    public static void MarkDeleted(Span<byte> dest, int offset, int entryCount)
    {
        for (int i = 0; i < entryCount; i++)
        {
            dest[offset + i * EntrySize] = DeletedMarker;
        }
    }

    private static void ReadLfnChars(ReadOnlySpan<byte> entry, Span<char> dest13)
    {
        ReadUcs2(entry.Slice(1, 10), dest13.Slice(0, 5));
        ReadUcs2(entry.Slice(14, 12), dest13.Slice(5, 6));
        ReadUcs2(entry.Slice(28, 4), dest13.Slice(11, 2));
    }

    private static void ReadUcs2(ReadOnlySpan<byte> src, Span<char> dest)
    {
        for (int i = 0; i < dest.Length; i++)
        {
            dest[i] = (char)(src[i * 2] | (src[i * 2 + 1] << 8));
        }
    }

    private static void WriteUcs2(ReadOnlySpan<char> src, Span<byte> dest)
    {
        for (int i = 0; i < src.Length; i++)
        {
            dest[i * 2] = (byte)(src[i] & 0xFF);
            dest[i * 2 + 1] = (byte)((src[i] >> 8) & 0xFF);
        }
    }

    private static string TrimLfn(Span<char> chars)
    {
        int end = chars.Length;
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '\0' || chars[i] == (char)0xFFFF)
            {
                end = i;
                break;
            }
        }
        return new string(chars.Slice(0, end));
    }

    private static string DecodeShortName(ReadOnlySpan<byte> raw11, byte firstByte)
    {
        Span<char> chars = stackalloc char[12];
        int len = 0;

        byte effectiveFirst = firstByte == 0x05 ? (byte)0xE5 : firstByte;
        chars[len++] = (char)effectiveFirst;

        for (int i = 1; i < 8; i++)
        {
            if (raw11[i] == 0x20)
            {
                break;
            }
            chars[len++] = (char)raw11[i];
        }

        bool hasExt = false;
        for (int i = 8; i < 11; i++)
        {
            if (raw11[i] != 0x20)
            {
                hasExt = true;
                break;
            }
        }

        if (hasExt)
        {
            chars[len++] = '.';
            for (int i = 8; i < 11; i++)
            {
                if (raw11[i] == 0x20)
                {
                    break;
                }
                chars[len++] = (char)raw11[i];
            }
        }

        return new string(chars.Slice(0, len));
    }

    private static char NormalizeShort(char c)
    {
        if (c == ' ')
        {
            return '\0';
        }
        if (c >= 'a' && c <= 'z')
        {
            return (char)(c - 32);
        }
        if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
        {
            return c;
        }
        if (c == '$' || c == '%' || c == '\'' || c == '-' || c == '_'
            || c == '@' || c == '~' || c == '`' || c == '!' || c == '('
            || c == ')' || c == '{' || c == '}' || c == '^' || c == '#' || c == '&')
        {
            return c;
        }
        return '_';
    }
}
