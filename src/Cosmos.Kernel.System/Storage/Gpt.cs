// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Storage;

/// <summary>
/// GPT (GUID Partition Table) parser / writer. Reads / writes the primary
/// header at LBA 1 plus the partition entry array starting at LBA 2.
/// 512-byte sectors assumed (matches every block device this kernel
/// currently exposes).
/// </summary>
public static class Gpt
{
    /// <summary>"EFI PART" little-endian.</summary>
    private const ulong EfiPartSignature = 0x5452415020494645UL;

    /// <summary>
    /// Upper bound on the on-disk NumberOfPartitionEntries field this
    /// parser will honor (8x the standard 128). This format writes CRC32s
    /// as 0, so header corruption is undetectable — an unclamped count
    /// (up to 2^32) would drive the boot-time partition scan through
    /// millions of sector reads.
    /// </summary>
    private const uint MaxEntryCount = 1024;

    /// <summary>GPT header field offset of the "EFI PART" signature (UEFI spec, byte 0).</summary>
    private const int HeaderSignatureOffset = 0;

    /// <summary>GPT header field offset of the revision (UEFI spec, byte 8).</summary>
    private const int HeaderRevisionOffset = 8;

    /// <summary>GPT header field offset of the header size (UEFI spec, byte 12).</summary>
    private const int HeaderSizeOffset = 12;

    /// <summary>GPT header field offset of the header CRC32 (UEFI spec, byte 16).</summary>
    private const int HeaderCrcOffset = 16;

    /// <summary>GPT header field offset of the current (my) LBA (UEFI spec, byte 24).</summary>
    private const int HeaderCurrentLbaOffset = 24;

    /// <summary>GPT header field offset of the backup header LBA (UEFI spec, byte 32).</summary>
    private const int HeaderBackupLbaOffset = 32;

    /// <summary>GPT header field offset of the first usable LBA (UEFI spec, byte 40).</summary>
    private const int HeaderFirstUsableOffset = 40;

    /// <summary>GPT header field offset of the last usable LBA (UEFI spec, byte 48).</summary>
    private const int HeaderLastUsableOffset = 48;

    /// <summary>GPT header field offset of the disk GUID (UEFI spec, byte 56).</summary>
    private const int HeaderDiskGuidOffset = 56;

    /// <summary>GPT header field offset of the partition entry array starting LBA (UEFI spec, byte 72).</summary>
    private const int HeaderEntryArrayLbaOffset = 72;

    /// <summary>GPT header field offset of the number of partition entries (UEFI spec, byte 80).</summary>
    private const int HeaderEntryCountOffset = 80;

    /// <summary>GPT header field offset of the size of one partition entry (UEFI spec, byte 84).</summary>
    private const int HeaderEntrySizeOffset = 84;

    /// <summary>GPT header field offset of the partition entry array CRC32 (UEFI spec, byte 88).</summary>
    private const int HeaderEntryArrayCrcOffset = 88;

    /// <summary>GPT revision 1.0 as encoded in the header (0x00010000).</summary>
    private const uint GptRevision = 0x00010000u;

    /// <summary>Size in bytes of the GPT header structure (UEFI spec: 92).</summary>
    private const uint GptHeaderSizeBytes = 92u;

    /// <summary>Standard GPT partition entry size in bytes (UEFI spec); also the minimum SizeOfPartitionEntry accepted when parsing.</summary>
    private const uint PartitionEntrySizeBytes = 128u;

    /// <summary>Standard number of partition entries written to a fresh GPT (UEFI spec minimum array of 128 entries).</summary>
    private const uint DefaultPartitionEntryCount = 128u;

    /// <summary>Partition entry field offset of the unique partition GUID (UEFI spec, byte 16).</summary>
    private const int EntryUniqueGuidOffset = 16;

    /// <summary>Partition entry field offset of the first (starting) LBA (UEFI spec, byte 32).</summary>
    private const int EntryFirstLbaOffset = 32;

    /// <summary>Partition entry field offset of the last (ending, inclusive) LBA (UEFI spec, byte 40).</summary>
    private const int EntryLastLbaOffset = 40;

    /// <summary>Partition entry field offset of the attribute flags (UEFI spec, byte 48).</summary>
    private const int EntryAttributesOffset = 48;

    /// <summary>Width in bytes of a 64-bit on-disk field (signature, LBA values).</summary>
    private const int UInt64FieldSize = 8;

    /// <summary>Width in bytes of a 32-bit on-disk field (revision, sizes, counts, CRCs).</summary>
    private const int UInt32FieldSize = 4;

    /// <summary>Width in bytes of a 16-bit on-disk field (MBR boot signature).</summary>
    private const int UInt16FieldSize = 2;

    /// <summary>Width in bytes of an on-disk GUID field.</summary>
    private const int GuidFieldSize = 16;

    /// <summary>LBA of the protective MBR (sector 0).</summary>
    private const ulong ProtectiveMbrLba = 0;

    /// <summary>LBA of the primary GPT header (sector 1).</summary>
    private const ulong PrimaryHeaderLba = 1;

    /// <summary>LBA where the partition entry array starts in the standard layout (first sector after the protective MBR and primary header); also the minimum PartitionEntryLBA accepted when parsing.</summary>
    private const ulong EntryArrayLba = 2;

    /// <summary>
    /// First usable LBA in the standard layout: protective MBR + header +
    /// 32 entry-array sectors occupy LBAs 0..33. Public so shell tooling
    /// validates against the same value the writer stamps.
    /// </summary>
    public const ulong FirstUsableLba = 34;

    /// <summary>Minimum device size in sectors for a GPT to exist at all (LBA 0 plus the header at LBA 1).</summary>
    private const ulong MinGptBlockCount = 2;

    /// <summary>Minimum device size in sectors accepted by Create: LBAs 0..33 plus a usable area and a backup slot at BlockCount-1.</summary>
    private const ulong MinCreateBlockCount = 64;

    /// <summary>Byte offset of the first MBR partition table entry within LBA 0.</summary>
    private const int MbrPartitionTableOffset = 446;

    /// <summary>MBR partition entry field offset of the boot indicator (byte 0).</summary>
    private const int MbrBootIndicatorOffset = 0;

    /// <summary>MBR partition entry field offset of the starting CHS head (byte 1).</summary>
    private const int MbrStartChsHeadOffset = 1;

    /// <summary>MBR partition entry field offset of the starting CHS sector (byte 2).</summary>
    private const int MbrStartChsSectorOffset = 2;

    /// <summary>MBR partition entry field offset of the starting CHS cylinder (byte 3).</summary>
    private const int MbrStartChsCylinderOffset = 3;

    /// <summary>MBR partition entry field offset of the partition type id (byte 4).</summary>
    private const int MbrPartitionTypeOffset = 4;

    /// <summary>MBR partition entry field offset of the ending CHS head (byte 5).</summary>
    private const int MbrEndChsHeadOffset = 5;

    /// <summary>MBR partition entry field offset of the ending CHS sector (byte 6).</summary>
    private const int MbrEndChsSectorOffset = 6;

    /// <summary>MBR partition entry field offset of the ending CHS cylinder (byte 7).</summary>
    private const int MbrEndChsCylinderOffset = 7;

    /// <summary>MBR partition entry field offset of the first absolute LBA (byte 8).</summary>
    private const int MbrFirstLbaOffset = 8;

    /// <summary>MBR partition entry field offset of the size in LBAs (byte 12).</summary>
    private const int MbrSizeInLbaOffset = 12;

    /// <summary>Byte offset of the MBR boot signature within LBA 0.</summary>
    private const int MbrBootSignatureOffset = 510;

    /// <summary>MBR boot signature value (0x55, 0xAA little-endian).</summary>
    private const ushort MbrBootSignature = 0xAA55;

    /// <summary>MBR partition type id of the GPT protective partition (UEFI spec: 0xEE).</summary>
    private const byte GptProtectivePartitionType = 0xEE;

    /// <summary>CHS placeholder value used when the geometry exceeds CHS addressing (all bits set).</summary>
    private const byte MbrChsPlaceholder = 0xFF;

    /// <summary>Starting CHS sector of the protective partition (sector numbering is 1-based; 2 maps to LBA 1).</summary>
    private const byte ProtectiveMbrStartChsSector = 0x02;

    /// <summary>First absolute LBA of the protective partition (LBA 1, right after the MBR).</summary>
    private const uint ProtectiveMbrStartLba = 1u;

    /// <summary>Maximum size-in-LBA value the 32-bit MBR field can express; larger disks are clamped to it (UEFI spec).</summary>
    private const uint MbrMaxSizeInLba = 0xFFFFFFFFu;

    /// <summary>Byte offset of the second 32-bit word inside a 16-byte GUID.</summary>
    private const int GuidDword1Offset = 4;

    /// <summary>Byte offset of the third 32-bit word inside a 16-byte GUID.</summary>
    private const int GuidDword2Offset = 8;

    /// <summary>Byte offset of the fourth 32-bit word inside a 16-byte GUID.</summary>
    private const int GuidDword3Offset = 12;

    /// <summary>XOR salt mixed into the second GUID dword so deterministic GUIDs differ per word.</summary>
    private const ulong GuidMixSalt1 = 0x12345678UL;

    /// <summary>XOR salt mixed into the third GUID dword so deterministic GUIDs differ per word.</summary>
    private const ulong GuidMixSalt2 = 0x87654321UL;

    /// <summary>XOR salt mixed into the fourth GUID dword so deterministic GUIDs differ per word.</summary>
    private const ulong GuidMixSalt3 = 0xDEADBEEFUL;

    /// <summary>Microsoft Basic Data Partition GUID — used by FAT/NTFS/exFAT volumes.</summary>
    public static readonly Guid BasicDataPartitionType = new(
        0xEBD0A0A2, 0xB9E5, 0x4433, 0x87, 0xC0, 0x68, 0xB6, 0xB7, 0x26, 0x99, 0xC7);

    /// <summary>Single parsed partition. Sector positions are absolute on the host disk.</summary>
    public sealed class PartitionEntry
    {
        /// <summary>Partition type GUID (see <see cref="Gpt.BasicDataPartitionType"/>).</summary>
        public Guid PartitionType { get; }

        /// <summary>Unique partition GUID.</summary>
        public Guid PartitionGuid { get; }

        /// <summary>First absolute LBA of the partition on the host disk.</summary>
        public ulong StartSector { get; }

        /// <summary>Length of the partition in sectors.</summary>
        public ulong SectorCount { get; }

        /// <summary>Creates a GPT partition entry.</summary>
        public PartitionEntry(Guid partitionType, Guid partitionGuid, ulong startSector, ulong sectorCount)
        {
            PartitionType = partitionType;
            PartitionGuid = partitionGuid;
            StartSector = startSector;
            SectorCount = sectorCount;
        }
    }

    /// <summary>True if the GPT header at LBA 1 starts with the EFI PART signature.</summary>
    public static bool IsGpt(IBlockDevice device)
    {
        // A GPT needs at least LBA 0 + LBA 1; per the IBlockDevice contract
        // reading past the end throws, and that throw would otherwise leak
        // out of a "is this a GPT?" probe (and kill a boot-time scan).
        if (device.BlockCount < MinGptBlockCount)
        {
            return false;
        }

        Span<byte> header = new byte[device.BlockSize];
        device.ReadBlock(PrimaryHeaderLba, 1, header);
        return BitConverter.ToUInt64(header.Slice(HeaderSignatureOffset, UInt64FieldSize)) == EfiPartSignature;
    }

    /// <summary>
    /// Walk the GPT partition entry array. Empty slots (zero PartitionType
    /// GUID) are skipped.
    /// </summary>
    public static List<PartitionEntry> Parse(IBlockDevice device)
    {
        List<PartitionEntry> partitions = new();
        if (device.BlockCount < MinGptBlockCount)
        {
            return partitions;
        }

        ulong blockSize = device.BlockSize;

        Span<byte> header = new byte[blockSize];
        device.ReadBlock(PrimaryHeaderLba, 1, header);
        if (BitConverter.ToUInt64(header.Slice(HeaderSignatureOffset, UInt64FieldSize)) != EfiPartSignature)
        {
            return partitions;
        }

        // Trust nothing in the header beyond the signature: CRC32s are
        // written as 0 by this format, so corruption is undetectable and
        // every field must be range-checked before it drives I/O.
        ulong entryStartLba = BitConverter.ToUInt64(header.Slice(HeaderEntryArrayLbaOffset, UInt64FieldSize));
        uint entryCount = BitConverter.ToUInt32(header.Slice(HeaderEntryCountOffset, UInt32FieldSize));
        uint entrySize = BitConverter.ToUInt32(header.Slice(HeaderEntrySizeOffset, UInt32FieldSize));
        if (entrySize < PartitionEntrySizeBytes || entrySize > blockSize
            || entryCount > MaxEntryCount
            || entryStartLba < EntryArrayLba || entryStartLba >= device.BlockCount)
        {
            return partitions;
        }

        uint entriesPerSector = (uint)(blockSize / entrySize);
        if (entriesPerSector == 0)
        {
            return partitions;
        }

        // Bound the END of the entry array too: a start LBA near the disk
        // end with a large count would otherwise still drive out-of-range
        // sector reads.
        ulong arraySectors = ((ulong)entryCount + entriesPerSector - 1) / entriesPerSector;
        if (entryStartLba + arraySectors > device.BlockCount)
        {
            return partitions;
        }

        Span<byte> sector = new byte[blockSize];
        for (ulong s = 0; s < (ulong)entryCount; s += entriesPerSector)
        {
            device.ReadBlock(entryStartLba + s / entriesPerSector, 1, sector);
            uint thisSector = (uint)Math.Min((ulong)entriesPerSector, entryCount - s);
            for (uint j = 0; j < thisSector; j++)
            {
                int offset = (int)(j * entrySize);
                Guid partType = ReadGuid(sector.Slice(offset, GuidFieldSize));
                if (partType == Guid.Empty)
                {
                    continue;
                }

                Guid partGuid = ReadGuid(sector.Slice(offset + EntryUniqueGuidOffset, GuidFieldSize));
                ulong startLba = BitConverter.ToUInt64(sector.Slice(offset + EntryFirstLbaOffset, UInt64FieldSize));
                ulong endLba = BitConverter.ToUInt64(sector.Slice(offset + EntryLastLbaOffset, UInt64FieldSize));
                // endLba is inclusive. Reject corrupt entries outright:
                // endLba < startLba would underflow the count to ~2^64, a
                // range past the disk would authorize wild host I/O, and a
                // start inside the GPT structures (protective MBR, header,
                // entry array) would let a partition write corrupt the
                // table itself — CRCs are 0, so nothing else would notice.
                if (endLba < startLba || startLba < entryStartLba + arraySectors || endLba >= device.BlockCount)
                {
                    continue;
                }
                ulong count = endLba + 1 - startLba;

                partitions.Add(new PartitionEntry(partType, partGuid, startLba, count));
            }
        }

        return partitions;
    }

    /// <summary>
    /// Write a fresh GPT layout: protective MBR at LBA 0, primary header at
    /// LBA 1, and 32 zeroed sectors of 128-byte partition entries (LBAs
    /// 2..33). Backup header / array intentionally not written — callers
    /// targeting test images don't need them, and writing them needs CRC
    /// support that the boot path doesn't yet ship.
    /// </summary>
    public static void Create(IBlockDevice device)
    {
        ulong blockSize = device.BlockSize;

        // The layout needs LBAs 0..33 plus a usable area and a backup slot
        // at BlockCount-1; smaller devices would underflow the
        // FirstUsable/LastUsable math below.
        if (device.BlockCount < MinCreateBlockCount)
        {
            throw new ArgumentException("Device too small for a GPT layout.", nameof(device));
        }

        // Protective MBR at LBA 0.
        Span<byte> protectiveMbr = new byte[blockSize];
        protectiveMbr[MbrPartitionTableOffset + MbrBootIndicatorOffset] = 0x00; // boot indicator
        protectiveMbr[MbrPartitionTableOffset + MbrStartChsHeadOffset] = 0x00; // start CHS head
        protectiveMbr[MbrPartitionTableOffset + MbrStartChsSectorOffset] = ProtectiveMbrStartChsSector; // start CHS sector
        protectiveMbr[MbrPartitionTableOffset + MbrStartChsCylinderOffset] = 0x00; // start CHS cylinder
        protectiveMbr[MbrPartitionTableOffset + MbrPartitionTypeOffset] = GptProtectivePartitionType; // GPT protective system id
        protectiveMbr[MbrPartitionTableOffset + MbrEndChsHeadOffset] = MbrChsPlaceholder; // end CHS head
        protectiveMbr[MbrPartitionTableOffset + MbrEndChsSectorOffset] = MbrChsPlaceholder; // end CHS sector
        protectiveMbr[MbrPartitionTableOffset + MbrEndChsCylinderOffset] = MbrChsPlaceholder; // end CHS cylinder
        BitConverter.TryWriteBytes(protectiveMbr.Slice(MbrPartitionTableOffset + MbrFirstLbaOffset, UInt32FieldSize), ProtectiveMbrStartLba);
        uint sizeInLba = device.BlockCount > MbrMaxSizeInLba
            ? MbrMaxSizeInLba
            : (uint)(device.BlockCount - 1);
        BitConverter.TryWriteBytes(protectiveMbr.Slice(MbrPartitionTableOffset + MbrSizeInLbaOffset, UInt32FieldSize), sizeInLba);
        BitConverter.TryWriteBytes(protectiveMbr.Slice(MbrBootSignatureOffset, UInt16FieldSize), MbrBootSignature);
        device.WriteBlock(ProtectiveMbrLba, 1, protectiveMbr);

        // GPT header at LBA 1.
        Span<byte> header = new byte[blockSize];
        BitConverter.TryWriteBytes(header.Slice(HeaderSignatureOffset, UInt64FieldSize), EfiPartSignature);
        BitConverter.TryWriteBytes(header.Slice(HeaderRevisionOffset, UInt32FieldSize), GptRevision);  // Revision 1.0
        BitConverter.TryWriteBytes(header.Slice(HeaderSizeOffset, UInt32FieldSize), GptHeaderSizeBytes);          // header size
        BitConverter.TryWriteBytes(header.Slice(HeaderCrcOffset, UInt32FieldSize), 0u);           // CRC32 (not computed; consumers that care will reject)
        BitConverter.TryWriteBytes(header.Slice(HeaderCurrentLbaOffset, UInt64FieldSize), PrimaryHeaderLba);          // current LBA
        BitConverter.TryWriteBytes(header.Slice(HeaderBackupLbaOffset, UInt64FieldSize), device.BlockCount - 1);  // backup LBA
        BitConverter.TryWriteBytes(header.Slice(HeaderFirstUsableOffset, UInt64FieldSize), FirstUsableLba);         // first usable LBA
        BitConverter.TryWriteBytes(header.Slice(HeaderLastUsableOffset, UInt64FieldSize), device.BlockCount - FirstUsableLba); // last usable LBA
        // Disk GUID — deterministic, derived from disk size so identical inputs yield identical layouts.
        ulong sizeMix = device.BlockCount;
        WriteDeterministicGuid(header.Slice(HeaderDiskGuidOffset, GuidFieldSize), sizeMix);
        BitConverter.TryWriteBytes(header.Slice(HeaderEntryArrayLbaOffset, UInt64FieldSize), EntryArrayLba);          // partition entry array LBA
        BitConverter.TryWriteBytes(header.Slice(HeaderEntryCountOffset, UInt32FieldSize), DefaultPartitionEntryCount);         // entry count
        BitConverter.TryWriteBytes(header.Slice(HeaderEntrySizeOffset, UInt32FieldSize), PartitionEntrySizeBytes);         // entry size
        BitConverter.TryWriteBytes(header.Slice(HeaderEntryArrayCrcOffset, UInt32FieldSize), 0u);           // entry array CRC32
        device.WriteBlock(PrimaryHeaderLba, 1, header);

        // Zero the partition entry array (LBAs 2..33 with 512B sectors and 128 entries × 128B).
        Span<byte> empty = new byte[blockSize];
        ulong entriesPerSector = blockSize / PartitionEntrySizeBytes;
        ulong arraySectors = entriesPerSector == 0 ? 0 : (DefaultPartitionEntryCount + entriesPerSector - 1) / entriesPerSector;
        for (ulong i = 0; i < arraySectors; i++)
        {
            device.WriteBlock(EntryArrayLba + i, 1, empty);
        }
    }

    /// <summary>
    /// Add a partition entry of <paramref name="partitionType"/> covering
    /// <paramref name="sectorCount"/> sectors starting at
    /// <paramref name="startSector"/>. Returns false if the GPT header is
    /// missing or no slot is free.
    /// </summary>
    public static bool AddPartition(IBlockDevice device, ulong startSector, ulong sectorCount, Guid partitionType)
    {
        if (device.BlockCount < MinGptBlockCount)
        {
            return false;
        }

        ulong blockSize = device.BlockSize;
        Span<byte> header = new byte[blockSize];
        device.ReadBlock(PrimaryHeaderLba, 1, header);
        if (BitConverter.ToUInt64(header.Slice(HeaderSignatureOffset, UInt64FieldSize)) != EfiPartSignature)
        {
            return false;
        }

        // Reject entries this file's own Parse would silently drop (zero
        // length, inside the GPT structures) or that point past the disk:
        // returning true for a partition that never materializes — or
        // stamping a wild range other tooling will trust — helps nobody.
        if (sectorCount == 0 || startSector < FirstUsableLba || startSector >= device.BlockCount
            || sectorCount > device.BlockCount - startSector)
        {
            return false;
        }

        // Same distrust of on-disk header fields as Parse: no CRCs, so a
        // zeroed/corrupt SizeOfPartitionEntry would otherwise divide by
        // zero, and a wild entryCount/entryStartLba would drive unbounded
        // or out-of-range I/O.
        ulong entryStartLba = BitConverter.ToUInt64(header.Slice(HeaderEntryArrayLbaOffset, UInt64FieldSize));
        uint entryCount = BitConverter.ToUInt32(header.Slice(HeaderEntryCountOffset, UInt32FieldSize));
        uint entrySize = BitConverter.ToUInt32(header.Slice(HeaderEntrySizeOffset, UInt32FieldSize));
        if (entrySize < PartitionEntrySizeBytes || entrySize > blockSize
            || entryCount > MaxEntryCount
            || entryStartLba < EntryArrayLba || entryStartLba >= device.BlockCount)
        {
            return false;
        }

        uint entriesPerSector = (uint)(blockSize / entrySize);
        if (entriesPerSector == 0)
        {
            return false;
        }

        // Same end-of-array bound as Parse: never read (or claim a slot
        // in) sectors past the end of the device.
        ulong arraySectors = ((ulong)entryCount + entriesPerSector - 1) / entriesPerSector;
        if (entryStartLba + arraySectors > device.BlockCount)
        {
            return false;
        }

        Span<byte> sector = new byte[blockSize];
        for (uint s = 0; s < entryCount; s += entriesPerSector)
        {
            ulong lba = entryStartLba + s / entriesPerSector;
            device.ReadBlock(lba, 1, sector);
            uint thisSector = (uint)Math.Min((ulong)entriesPerSector, entryCount - s);
            for (uint j = 0; j < thisSector; j++)
            {
                int offset = (int)(j * entrySize);
                if (!IsZero(sector.Slice(offset, GuidFieldSize)))
                {
                    continue;
                }

                WriteGuid(sector.Slice(offset, GuidFieldSize), partitionType);
                ulong slotIdx = s + j;
                ulong guidMix = startSector ^ sectorCount ^ slotIdx;
                WriteDeterministicGuid(sector.Slice(offset + EntryUniqueGuidOffset, GuidFieldSize), guidMix);
                BitConverter.TryWriteBytes(sector.Slice(offset + EntryFirstLbaOffset, UInt64FieldSize), startSector);
                BitConverter.TryWriteBytes(sector.Slice(offset + EntryLastLbaOffset, UInt64FieldSize), startSector + sectorCount - 1);
                BitConverter.TryWriteBytes(sector.Slice(offset + EntryAttributesOffset, UInt64FieldSize), 0UL);

                device.WriteBlock(lba, 1, sector);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Mark the <paramref name="partitionIndex"/>-th non-empty entry as
    /// deleted by zeroing the whole entry — UEFI expects unused entries
    /// fully zeroed, and a stale UTF-16 name would resurface when
    /// <see cref="AddPartition"/> reuses the slot without rewriting it.
    /// </summary>
    public static bool RemovePartition(IBlockDevice device, int partitionIndex)
    {
        return MutateEntry(device, partitionIndex, (Span<byte> entry) =>
        {
            entry.Clear();
            return true;
        });
    }

    /// <summary>
    /// Rewrite the end LBA of <paramref name="partitionIndex"/> so the
    /// partition spans <paramref name="newSectorCount"/> sectors. Start LBA /
    /// type / partition GUID preserved. Table-level only — does not adjust
    /// the filesystem inside.
    /// </summary>
    public static bool ResizePartition(IBlockDevice device, int partitionIndex, ulong newSectorCount)
    {
        if (newSectorCount == 0)
        {
            return false;
        }

        return MutateEntry(device, partitionIndex, (Span<byte> entry) =>
        {
            ulong startLba = BitConverter.ToUInt64(entry.Slice(EntryFirstLbaOffset, UInt64FieldSize));
            // Same write-time rejection as AddPartition: never stamp a
            // geometry this file's own Parse would drop.
            if (newSectorCount > device.BlockCount - startLba)
            {
                return false;
            }
            BitConverter.TryWriteBytes(entry.Slice(EntryLastLbaOffset, UInt64FieldSize), startLba + newSectorCount - 1);
            return true;
        });
    }

    /// <summary>
    /// Rewrite the start and end LBAs of <paramref name="partitionIndex"/> so
    /// the partition lives at <paramref name="newStartSector"/> with the same
    /// length. Table-level only — does not relocate data.
    /// </summary>
    public static bool MovePartition(IBlockDevice device, int partitionIndex, ulong newStartSector)
    {
        return MutateEntry(device, partitionIndex, (Span<byte> entry) =>
        {
            ulong startLba = BitConverter.ToUInt64(entry.Slice(EntryFirstLbaOffset, UInt64FieldSize));
            ulong endLba = BitConverter.ToUInt64(entry.Slice(EntryLastLbaOffset, UInt64FieldSize));
            ulong sectorCount = endLba + 1 - startLba;
            // Same write-time rejection as AddPartition: a start inside the
            // GPT structures or a range past the disk end must not be
            // stamped into the table.
            if (newStartSector < FirstUsableLba || newStartSector >= device.BlockCount
                || sectorCount > device.BlockCount - newStartSector)
            {
                return false;
            }
            BitConverter.TryWriteBytes(entry.Slice(EntryFirstLbaOffset, UInt64FieldSize), newStartSector);
            BitConverter.TryWriteBytes(entry.Slice(EntryLastLbaOffset, UInt64FieldSize), newStartSector + sectorCount - 1);
            return true;
        });
    }

    /// <summary>
    /// Applies <paramref name="mutator"/> to the entry's 0..55 byte region;
    /// returns false (nothing written) to abort the mutation.
    /// </summary>
    private delegate bool EntryMutator(Span<byte> entry);

    /// <summary>
    /// Locate the <paramref name="partitionIndex"/>-th non-empty entry in the
    /// partition entry array, apply <paramref name="mutator"/> to it, and
    /// write the containing sector back. Returns false when the header is
    /// missing/corrupt, the index does not resolve to a used slot, or the
    /// mutator aborts. Applies the same distrust of on-disk header fields
    /// as <see cref="Parse"/> — CRCs are 0, so every field is range-checked
    /// before it drives I/O.
    /// </summary>
    private static bool MutateEntry(IBlockDevice device, int partitionIndex, EntryMutator mutator)
    {
        if (partitionIndex < 0 || device.BlockCount < MinGptBlockCount)
        {
            return false;
        }

        ulong blockSize = device.BlockSize;
        Span<byte> header = new byte[blockSize];
        device.ReadBlock(PrimaryHeaderLba, 1, header);
        if (BitConverter.ToUInt64(header.Slice(HeaderSignatureOffset, UInt64FieldSize)) != EfiPartSignature)
        {
            return false;
        }

        ulong entryStartLba = BitConverter.ToUInt64(header.Slice(HeaderEntryArrayLbaOffset, UInt64FieldSize));
        uint entryCount = BitConverter.ToUInt32(header.Slice(HeaderEntryCountOffset, UInt32FieldSize));
        uint entrySize = BitConverter.ToUInt32(header.Slice(HeaderEntrySizeOffset, UInt32FieldSize));
        if (entrySize < PartitionEntrySizeBytes || entrySize > blockSize
            || entryCount > MaxEntryCount
            || entryStartLba < EntryArrayLba || entryStartLba >= device.BlockCount)
        {
            return false;
        }

        uint entriesPerSector = (uint)(blockSize / entrySize);
        if (entriesPerSector == 0)
        {
            return false;
        }

        // Same end-of-array bound as Parse: never read sectors past the end
        // of the device.
        ulong arraySectors = ((ulong)entryCount + entriesPerSector - 1) / entriesPerSector;
        if (entryStartLba + arraySectors > device.BlockCount)
        {
            return false;
        }

        int seen = 0;
        Span<byte> sector = new byte[blockSize];
        for (uint s = 0; s < entryCount; s += entriesPerSector)
        {
            ulong lba = entryStartLba + s / entriesPerSector;
            device.ReadBlock(lba, 1, sector);
            uint thisSector = (uint)Math.Min((ulong)entriesPerSector, entryCount - s);
            for (uint j = 0; j < thisSector; j++)
            {
                int offset = (int)(j * entrySize);
                if (IsZero(sector.Slice(offset, GuidFieldSize)))
                {
                    continue;
                }

                // Skip entries with exactly Parse's validity criteria so
                // partitionIndex stays aligned with Parse's output — one
                // corrupt entry ahead of the target would otherwise shift
                // every later index onto a different, healthy partition.
                // This also guarantees mutators only see validated LBAs
                // (startLba < BlockCount), so ResizePartition's
                // BlockCount - startLba arithmetic cannot underflow.
                ulong entryStart = BitConverter.ToUInt64(sector.Slice(offset + EntryFirstLbaOffset, UInt64FieldSize));
                ulong entryEnd = BitConverter.ToUInt64(sector.Slice(offset + EntryLastLbaOffset, UInt64FieldSize));
                if (entryEnd < entryStart
                    || entryStart < entryStartLba + arraySectors
                    || entryEnd >= device.BlockCount)
                {
                    continue;
                }

                if (seen == partitionIndex)
                {
                    if (!mutator(sector.Slice(offset, (int)entrySize)))
                    {
                        return false;
                    }
                    device.WriteBlock(lba, 1, sector);
                    return true;
                }
                seen++;
            }
        }

        return false;
    }

    private static Guid ReadGuid(Span<byte> source)
    {
        byte[] bytes = new byte[GuidFieldSize];
        source.Slice(0, GuidFieldSize).CopyTo(bytes);
        return new Guid(bytes);
    }

    private static void WriteGuid(Span<byte> dest, Guid value)
    {
        byte[] bytes = value.ToByteArray();
        bytes.AsSpan().CopyTo(dest);
    }

    private static void WriteDeterministicGuid(Span<byte> dest, ulong mix)
    {
        BitConverter.TryWriteBytes(dest.Slice(0, UInt32FieldSize), (uint)mix);
        BitConverter.TryWriteBytes(dest.Slice(GuidDword1Offset, UInt32FieldSize), (uint)(mix ^ GuidMixSalt1));
        BitConverter.TryWriteBytes(dest.Slice(GuidDword2Offset, UInt32FieldSize), (uint)(mix ^ GuidMixSalt2));
        BitConverter.TryWriteBytes(dest.Slice(GuidDword3Offset, UInt32FieldSize), (uint)(mix ^ GuidMixSalt3));
    }

    private static bool IsZero(Span<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != 0)
            {
                return false;
            }
        }
        return true;
    }
}
