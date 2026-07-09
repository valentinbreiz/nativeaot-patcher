using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System.Filesystems.Fat;
using Cosmos.Kernel.System.Vfs;
using Cosmos.TestRunner.Framework;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;

namespace Cosmos.Kernel.Tests.Fat;

public class Kernel : Sys.Kernel
{
    private const string Fat16Mount = "/fat";
    private const string Fat32Mount = "/fat32";

    protected override void BeforeRun()
    {
        Serial.WriteString("[FatTests] BeforeRun() reached!\n");

        TR.Start("FAT Driver Tests", expectedTests: 0);

        // Two independent disks + drivers, mounted at distinct points so the
        // FAT16 and FAT32 suites can't perturb one another.
        MemoryBlockDevice fat16Disk = FatTestVolume.CreateFat16("MEMFAT16");
        FatFilesystemType fat16Driver = new(fat16Disk);

        MemoryBlockDevice fat32Disk = FatTestVolume.CreateFat32("MEMFAT32");
        FatFilesystemType fat32Driver = new(fat32Disk);

        TR.Run("Test_RegisterFilesystem_Fat16", () =>
        {
            Assert.True(VfsManager.RegisterFilesystem("fat16-test", fat16Driver));
        });

        TR.Run("Test_RegisterFilesystem_Fat32", () =>
        {
            Assert.True(VfsManager.RegisterFilesystem("fat32-test", fat32Driver));
        });

        TR.Run("Test_Mount_FAT16", () =>
        {
            Assert.True(VfsManager.TryMount("fat16-test", "", MountFlags.None, Fat16Mount, out VfsManager.VfsMount? mount));
            Assert.NotNull(mount);
            Assert.Equal<long>(512, mount!.Superblock.BlockSize);
        });

        TR.Run("Test_Mount_FAT32", () =>
        {
            Assert.True(VfsManager.TryMount("fat32-test", "", MountFlags.None, Fat32Mount, out VfsManager.VfsMount? mount));
            Assert.NotNull(mount);
            Assert.Equal<long>(512, mount!.Superblock.BlockSize);
            Assert.True(mount.Superblock.MaxNameLength >= 255);

            // Cluster count >= 65525 is what makes the volume FAT32 (lower
            // counts trigger the FAT16/FAT12 paths); the StatFs report below
            // pulls that number directly from the parsed BPB.
            Assert.True(mount.Superblock.SuperOperations.StatFs(mount.Superblock, out VfsStatFs stat));
            Assert.True(stat.Blocks >= 65525);
        });

        TR.Run("Test_StatFs_FAT16", () =>
        {
            Assert.True(VfsManager.TryGetMount(Fat16Mount, out VfsManager.VfsMount? mount));
            Assert.True(mount!.Superblock.SuperOperations.StatFs(mount.Superblock, out VfsStatFs stat));
            Assert.True(stat.Blocks > 0);
            Assert.True(stat.Bfree > 0);
            Assert.True(stat.NameMax >= 255);
        });

        TR.Run("Test_StatFs_FAT32", () =>
        {
            Assert.True(VfsManager.TryGetMount(Fat32Mount, out VfsManager.VfsMount? mount));
            Assert.True(mount!.Superblock.SuperOperations.StatFs(mount.Superblock, out VfsStatFs stat));
            Assert.True(stat.Blocks >= 65525);
            Assert.True(stat.Bfree > 0);
            Assert.True(stat.NameMax >= 255);
        });

        // ---------- FAT16 inode-level coverage (kept from prior pass) ----------

        TR.Run("Test_Fat16_Create_Write_Read_RoundTrip", () =>
        {
            IVfsInode root = ResolveRoot(Fat16Mount);
            Assert.True(root.InodeOperations.Create(root, "HELLO.TXT", ModeEnum.RegularFile, out IVfsInode? created));
            Assert.NotNull(created);

            byte[] payload = MakePayload(1024, 0xA5);
            WriteAll(created!, payload);

            Assert.True(root.InodeOperations.Lookup(root, "HELLO.TXT", out IVfsInode? lookup));
            byte[] readBack = ReadAll(lookup!, payload.Length);
            AssertBytesEqual(payload, readBack);
        });

        TR.Run("Test_Fat16_LongFileName_LookupBothNames", () =>
        {
            IVfsInode root = ResolveRoot(Fat16Mount);
            const string longName = "longfilename.txt";
            Assert.True(root.InodeOperations.Create(root, longName, ModeEnum.RegularFile, out _));
            Assert.True(root.InodeOperations.Lookup(root, longName, out IVfsInode? byLong));
            Assert.NotNull(byLong);
            Assert.True(root.InodeOperations.Lookup(root, "LONGFI~1.TXT", out IVfsInode? byShort));
            Assert.NotNull(byShort);
        });

        TR.Run("Test_Fat16_Mkdir_Nested_Lookup", () =>
        {
            IVfsInode root = ResolveRoot(Fat16Mount);
            Assert.True(root.InodeOperations.Mkdir(root, "DIR", ModeEnum.Directory, out IVfsInode? dir));
            Assert.True(dir!.InodeOperations.Create(dir, "INNER.TXT", ModeEnum.RegularFile, out IVfsInode? inner));

            byte[] data = MakePayload(64, 0x10);
            WriteAll(inner!, data);

            Assert.True(root.InodeOperations.Lookup(root, "DIR", out IVfsInode? lookupDir));
            Assert.True(lookupDir!.InodeOperations.Lookup(lookupDir, "INNER.TXT", out IVfsInode? lookupInner));
            byte[] readBack = ReadAll(lookupInner!, data.Length);
            AssertBytesEqual(data, readBack);
        });

        TR.Run("Test_Fat16_Unlink_RemovesFile", () =>
        {
            IVfsInode root = ResolveRoot(Fat16Mount);
            Assert.True(root.InodeOperations.Create(root, "GONE.TXT", ModeEnum.RegularFile, out _));
            Assert.True(root.InodeOperations.Unlink(root, "GONE.TXT"));
            Assert.False(root.InodeOperations.Lookup(root, "GONE.TXT", out _));
        });

        TR.Run("Test_Fat16_Rmdir_RemovesEmptyDir", () =>
        {
            IVfsInode root = ResolveRoot(Fat16Mount);
            Assert.True(root.InodeOperations.Mkdir(root, "EMPTY", ModeEnum.Directory, out _));
            Assert.True(root.InodeOperations.Rmdir(root, "EMPTY"));
            Assert.False(root.InodeOperations.Lookup(root, "EMPTY", out _));
        });

        TR.Run("Test_Fat16_LargeWrite_CrossesClusters", () =>
        {
            IVfsInode root = ResolveRoot(Fat16Mount);
            Assert.True(root.InodeOperations.Create(root, "BIG.BIN", ModeEnum.RegularFile, out IVfsInode? created));
            byte[] payload = MakePayload(32 * 1024, 0x07);
            WriteAll(created!, payload);
            byte[] readBack = ReadAll(created!, payload.Length);
            AssertBytesEqual(payload, readBack);
        });

        // ---------- FAT32 + VFS-handle coverage ----------

        TR.Run("Test_Fat32_OpenDirectory_Root", () =>
        {
            Assert.True(VfsManager.TryOpenDirectory(Fat32Mount, out IVfsDirectoryHandle? root));
            Assert.NotNull(root);
            Assert.True(root!.TryStat(out VfsStat stat));
            Assert.True((stat.Mode & ModeEnum.FileTypeMask) == ModeEnum.Directory);
        });

        TR.Run("Test_Fat32_Vfs_CreateFile_OpenByPath_RoundTrip", () =>
        {
            Assert.True(VfsManager.TryOpenDirectory(Fat32Mount, out IVfsDirectoryHandle? root));
            Assert.True(root!.TryCreateFile("HELLO32.TXT", ModeEnum.RegularFile, out _));

            using IVfsFileHandle? file = OpenFile(Fat32Mount + "/HELLO32.TXT");
            Assert.NotNull(file);

            byte[] payload = MakePayload(2048, 0x33);
            long written = file!.Write(payload);
            Assert.Equal<long>(payload.Length, written);
            Assert.Equal<long>(payload.Length, file.Position);
            Assert.True(file.Flush());

            using IVfsFileHandle? reader = OpenFile(Fat32Mount + "/HELLO32.TXT");
            Assert.NotNull(reader);
            byte[] readBack = new byte[payload.Length];
            long readBytes = reader!.Read(readBack);
            Assert.Equal<long>(payload.Length, readBytes);
            AssertBytesEqual(payload, readBack);
        });

        TR.Run("Test_Fat32_Vfs_Seek_Set_Cur_End", () =>
        {
            Assert.True(VfsManager.TryOpenDirectory(Fat32Mount, out IVfsDirectoryHandle? root));
            Assert.True(root!.TryCreateFile("SEEK.BIN", ModeEnum.RegularFile, out _));

            byte[] payload = MakePayload(4096, 0x42);
            using (IVfsFileHandle? w = OpenFile(Fat32Mount + "/SEEK.BIN"))
            {
                Assert.NotNull(w);
                w!.Write(payload);
            }

            using IVfsFileHandle? r = OpenFile(Fat32Mount + "/SEEK.BIN");
            Assert.NotNull(r);

            // Seek End-128 and read 128 bytes; must match payload tail.
            Assert.True(r!.TrySeek(-128, SeekWhence.End));
            byte[] tail = new byte[128];
            long got = r.Read(tail);
            Assert.Equal<long>(128, got);
            for (int i = 0; i < 128; i++)
            {
                Assert.Equal<byte>(payload[payload.Length - 128 + i], tail[i]);
            }

            // Seek Set + Cur navigates correctly.
            Assert.True(r.TrySeek(0, SeekWhence.Set));
            Assert.Equal<long>(0, r.Position);
            Assert.True(r.TrySeek(1024, SeekWhence.Cur));
            Assert.Equal<long>(1024, r.Position);
            byte[] middle = new byte[16];
            r.Read(middle);
            for (int i = 0; i < 16; i++)
            {
                Assert.Equal<byte>(payload[1024 + i], middle[i]);
            }
        });

        TR.Run("Test_Fat32_Vfs_Read_AtEof_ReturnsZero", () =>
        {
            Assert.True(VfsManager.TryOpenDirectory(Fat32Mount, out IVfsDirectoryHandle? root));
            Assert.True(root!.TryCreateFile("EOF.BIN", ModeEnum.RegularFile, out _));

            byte[] payload = MakePayload(64, 0x77);
            using (IVfsFileHandle? w = OpenFile(Fat32Mount + "/EOF.BIN"))
            {
                w!.Write(payload);
            }

            using IVfsFileHandle? r = OpenFile(Fat32Mount + "/EOF.BIN");
            Assert.True(r!.TrySeek(0, SeekWhence.End));
            byte[] tail = new byte[8];
            long n = r.Read(tail);
            Assert.Equal<long>(0, n);
        });

        TR.Run("Test_Fat32_Vfs_LargeFile_AcrossManyClusters", () =>
        {
            Assert.True(VfsManager.TryOpenDirectory(Fat32Mount, out IVfsDirectoryHandle? root));
            Assert.True(root!.TryCreateFile("BIG32.BIN", ModeEnum.RegularFile, out _));

            // 256 KB file at 512 B/cluster (FAT32 SPC=1) = 512 clusters.
            const int totalBytes = 256 * 1024;
            byte[] payload = MakePayload(totalBytes, 0xC9);
            using (IVfsFileHandle? w = OpenFile(Fat32Mount + "/BIG32.BIN"))
            {
                long written = w!.Write(payload);
                Assert.Equal<long>(totalBytes, written);
            }

            using IVfsFileHandle? r = OpenFile(Fat32Mount + "/BIG32.BIN");
            byte[] readBack = new byte[totalBytes];
            long readBytes = r!.Read(readBack);
            Assert.Equal<long>(totalBytes, readBytes);
            AssertBytesEqual(payload, readBack);
        });

        TR.Run("Test_Fat32_Vfs_LongFileName", () =>
        {
            Assert.True(VfsManager.TryOpenDirectory(Fat32Mount, out IVfsDirectoryHandle? root));
            const string lfn = "very_long_file_name_test.bin";
            Assert.True(root!.TryCreateFile(lfn, ModeEnum.RegularFile, out _));

            using IVfsFileHandle? f = OpenFile(Fat32Mount + "/" + lfn);
            Assert.NotNull(f);
            byte[] payload = MakePayload(256, 0x5A);
            f!.Write(payload);
            f.Flush();

            using IVfsFileHandle? rByLong = OpenFile(Fat32Mount + "/" + lfn);
            Assert.NotNull(rByLong);
            byte[] tmp = new byte[payload.Length];
            Assert.Equal<long>(payload.Length, rByLong!.Read(tmp));
            AssertBytesEqual(payload, tmp);

            using IVfsFileHandle? rByShort = OpenFile(Fat32Mount + "/VERY_L~1.BIN");
            Assert.NotNull(rByShort);
        });

        TR.Run("Test_Fat32_Vfs_ManyFiles_RootGrowsAcrossClusters", () =>
        {
            // FAT32 root is cluster-chained; at SPC=1 (512 B/cluster) every
            // 16 entries pushes root past a cluster boundary. Create 64
            // distinct files and confirm each one is reachable afterwards.
            Assert.True(VfsManager.TryOpenDirectory(Fat32Mount, out IVfsDirectoryHandle? root));

            const int count = 64;
            for (int i = 0; i < count; i++)
            {
                string name = "F" + IntToHex(i, 4) + ".TXT";
                Assert.True(root!.TryCreateFile(name, ModeEnum.RegularFile, out _));
            }

            for (int i = 0; i < count; i++)
            {
                string name = "F" + IntToHex(i, 4) + ".TXT";
                Assert.True(root!.TryLookup(name, out IVfsNodeHandle? child));
                Assert.NotNull(child);
            }
        });

        TR.Run("Test_Fat32_Vfs_NestedDirectories", () =>
        {
            Assert.True(VfsManager.TryOpenDirectory(Fat32Mount, out IVfsDirectoryHandle? root));
            Assert.True(root!.TryCreateDirectory("A", ModeEnum.Directory, out IVfsDirectoryHandle? a));
            Assert.True(a!.TryCreateDirectory("B", ModeEnum.Directory, out IVfsDirectoryHandle? b));
            Assert.True(b!.TryCreateDirectory("C", ModeEnum.Directory, out IVfsDirectoryHandle? c));
            Assert.True(c!.TryCreateFile("LEAF.TXT", ModeEnum.RegularFile, out _));

            byte[] payload = MakePayload(128, 0xEE);
            using (IVfsFileHandle? leaf = OpenFile(Fat32Mount + "/A/B/C/LEAF.TXT"))
            {
                Assert.NotNull(leaf);
                leaf!.Write(payload);
            }

            using IVfsFileHandle? readback = OpenFile(Fat32Mount + "/A/B/C/LEAF.TXT");
            Assert.NotNull(readback);
            byte[] got = new byte[payload.Length];
            Assert.Equal<long>(payload.Length, readback!.Read(got));
            AssertBytesEqual(payload, got);
        });

        TR.Run("Test_Fat32_Vfs_Truncate_GrowsViaSetAttr", () =>
        {
            Assert.True(VfsManager.TryOpenDirectory(Fat32Mount, out IVfsDirectoryHandle? root));
            Assert.True(root!.TryCreateFile("GROW.BIN", ModeEnum.RegularFile, out IVfsNodeHandle? created));
            Assert.NotNull(created);

            // SetAttr to enlarge the file from 0 to 4096 bytes; the FS must
            // allocate clusters and zero-fill them.
            VfsStat want = default;
            want.Size = 4096;
            Assert.True(created!.Inode.InodeOperations.SetAttr(created.Inode, SetAttrFlags.Size, want));

            using IVfsFileHandle? r = OpenFile(Fat32Mount + "/GROW.BIN");
            Assert.NotNull(r);
            byte[] readBack = new byte[4096];
            long readBytes = r!.Read(readBack);
            Assert.Equal<long>(4096, readBytes);
            for (int i = 0; i < readBack.Length; i++)
            {
                Assert.Equal<byte>(0, readBack[i]);
            }
        });

        TR.Run("Test_Fat32_Vfs_Unlink_FreesClusters", () =>
        {
            Assert.True(VfsManager.TryGetMount(Fat32Mount, out VfsManager.VfsMount? mount));
            Assert.True(mount!.Superblock.SuperOperations.StatFs(mount.Superblock, out VfsStatFs before));

            Assert.True(VfsManager.TryOpenDirectory(Fat32Mount, out IVfsDirectoryHandle? root));
            Assert.True(root!.TryCreateFile("HOG.BIN", ModeEnum.RegularFile, out _));
            using (IVfsFileHandle? w = OpenFile(Fat32Mount + "/HOG.BIN"))
            {
                w!.Write(MakePayload(64 * 1024, 0xDE));
            }

            Assert.True(root.TryUnlink("HOG.BIN"));
            Assert.False(root.TryLookup("HOG.BIN", out _));

            Assert.True(mount.Superblock.SuperOperations.StatFs(mount.Superblock, out VfsStatFs after));
            // Removing a 64 KB file must free at least 64 clusters at SPC=1.
            Assert.True(after.Bfree >= before.Bfree - 8 && after.Bfree + 8 >= before.Bfree);
        });

        TR.Run("Test_Fat32_Persistence_AcrossUnmount", () =>
        {
            Assert.True(VfsManager.TryOpenDirectory(Fat32Mount, out IVfsDirectoryHandle? root));
            Assert.True(root!.TryCreateFile("SURVIVE.TXT", ModeEnum.RegularFile, out _));
            byte[] payload = MakePayload(800, 0x9F);
            using (IVfsFileHandle? w = OpenFile(Fat32Mount + "/SURVIVE.TXT"))
            {
                w!.Write(payload);
                w.Flush();
            }

            // Drop the superblock and remount on the same backing buffer.
            Assert.True(VfsManager.TryGetMount(Fat32Mount, out VfsManager.VfsMount? oldMount));
            oldMount!.Superblock.SuperOperations.Drop(oldMount.Superblock);

            FatFilesystemType freshDriver = new(fat32Disk);
            Assert.True(VfsManager.RegisterFilesystem("fat32-remount", freshDriver));
            Assert.True(VfsManager.TryMount("fat32-remount", "", MountFlags.None, "/fat32b", out _));

            using IVfsFileHandle? r = OpenFile("/fat32b/SURVIVE.TXT");
            Assert.NotNull(r);
            byte[] readBack = new byte[payload.Length];
            Assert.Equal<long>(payload.Length, r!.Read(readBack));
            AssertBytesEqual(payload, readBack);
        });

        // ---------- VfsManager.TryFormat / TryDestroy round-trip ----------

        TR.Run("Test_Vfs_Format_Fat32_RoundTrip", () =>
        {
            // Fresh empty disk -> format via VfsManager -> mount -> create +
            // write across cluster boundaries -> read back. Proves the formatter
            // produced a real FAT32, not just one that statfs's correctly.
            MemoryBlockDevice freshDisk = new("MEMFAT32B", 512, (33UL * 1024 * 1024) / 512);
            FatFilesystemType driver = new(freshDisk);
            Assert.True(VfsManager.RegisterFilesystem("fat32-fresh", driver));

            FatFormatOptions opts = new()
            {
                Type = FatType.Fat32,
                SectorsPerCluster = 1,
                ReservedSectorCount = 32,
                NumberOfFats = 2,
                FatSectorCount = 512,
                RootCluster = 2,
                VolumeLabel = "FRESHFAT32 ",
            };
            Assert.True(VfsManager.TryFormat("fat32-fresh", "", opts));

            Assert.True(VfsManager.TryMount("fat32-fresh", "", MountFlags.None, "/freshfat32", out VfsManager.VfsMount? mount));
            Assert.True(mount!.Superblock.SuperOperations.StatFs(mount.Superblock, out VfsStatFs stat));
            Assert.True(stat.Blocks >= 65525);

            // Write spans many clusters at SPC=1 (8 KiB = 16 clusters).
            byte[] payload = MakePayload(8 * 1024, 0x6C);
            Assert.True(VfsManager.TryOpenDirectory("/freshfat32", out IVfsDirectoryHandle? rootDir));
            Assert.True(rootDir!.TryCreateFile("FORMAT.BIN", ModeEnum.RegularFile, out _));
            using (IVfsFileHandle? w = OpenFile("/freshfat32/FORMAT.BIN"))
            {
                Assert.NotNull(w);
                Assert.Equal<long>(payload.Length, w!.Write(payload));
                w.Flush();
            }

            using IVfsFileHandle? r = OpenFile("/freshfat32/FORMAT.BIN");
            Assert.NotNull(r);
            byte[] readBack = new byte[payload.Length];
            Assert.Equal<long>(payload.Length, r!.Read(readBack));
            AssertBytesEqual(payload, readBack);

            // Persist across remount on the same backing buffer.
            Assert.True(VfsManager.TryGetMount("/freshfat32", out VfsManager.VfsMount? m2));
            m2!.Superblock.SuperOperations.Drop(m2.Superblock);

            FatFilesystemType remountDriver = new(freshDisk);
            Assert.True(VfsManager.RegisterFilesystem("fat32-fresh-remount", remountDriver));
            Assert.True(VfsManager.TryMount("fat32-fresh-remount", "", MountFlags.None, "/freshfat32b", out _));

            using IVfsFileHandle? r2 = OpenFile("/freshfat32b/FORMAT.BIN");
            Assert.NotNull(r2);
            byte[] readBack2 = new byte[payload.Length];
            Assert.Equal<long>(payload.Length, r2!.Read(readBack2));
            AssertBytesEqual(payload, readBack2);
        });

        TR.Run("Test_Vfs_Destroy_PreventsRemount", () =>
        {
            MemoryBlockDevice disk = FatTestVolume.CreateFat16("MEMFAT16D");
            FatFilesystemType driver = new(disk);
            Assert.True(VfsManager.RegisterFilesystem("fat16-destroy", driver));

            // Pre-destroy: the BPB is valid, mount works.
            Assert.True(VfsManager.TryMount("fat16-destroy", "", MountFlags.None, "/destroy-pre", out _));

            // Destroy wipes sector 0; a fresh driver against the same buffer must refuse to mount.
            Assert.True(VfsManager.TryDestroy("fat16-destroy", ""));

            FatFilesystemType post = new(disk);
            Assert.True(VfsManager.RegisterFilesystem("fat16-destroyed", post));
            Assert.False(VfsManager.TryMount("fat16-destroyed", "", MountFlags.None, "/destroy-post", out _));
        });

        TR.Run("Test_Vfs_Format_Fat12_Mountable", () =>
        {
            // ~3 MiB / SPC=1 / 1-sector reserved -> ~6000 sectors of data, well inside FAT12 band.
            MemoryBlockDevice disk = new("MEMFAT12", 512, (3UL * 1024 * 1024) / 512);
            FatFilesystemType driver = new(disk);
            Assert.True(VfsManager.RegisterFilesystem("fat12-fresh", driver));

            FatFormatOptions opts = new()
            {
                Type = FatType.Fat12,
                SectorsPerCluster = 2,
                ReservedSectorCount = 1,
                NumberOfFats = 2,
                RootEntryCount = 512,
                FatSectorCount = 12,
                VolumeLabel = "FAT12VOL   ",
            };
            Assert.True(VfsManager.TryFormat("fat12-fresh", "", opts));

            Assert.True(VfsManager.TryMount("fat12-fresh", "", MountFlags.None, "/fat12", out VfsManager.VfsMount? mount));
            Assert.True(mount!.Superblock.SuperOperations.StatFs(mount.Superblock, out VfsStatFs stat));
            Assert.True(stat.Blocks > 0);
            Assert.True(stat.Blocks < 4085);

            // Real write through the freshly formatted FAT12.
            byte[] payload = MakePayload(2048, 0x12);
            Assert.True(VfsManager.TryOpenDirectory("/fat12", out IVfsDirectoryHandle? rootDir));
            Assert.True(rootDir!.TryCreateFile("HELLO.TXT", ModeEnum.RegularFile, out _));
            using (IVfsFileHandle? w = OpenFile("/fat12/HELLO.TXT"))
            {
                Assert.NotNull(w);
                Assert.Equal<long>(payload.Length, w!.Write(payload));
                w.Flush();
            }
            using IVfsFileHandle? r = OpenFile("/fat12/HELLO.TXT");
            Assert.NotNull(r);
            byte[] back = new byte[payload.Length];
            Assert.Equal<long>(payload.Length, r!.Read(back));
            AssertBytesEqual(payload, back);
        });

        // fatgen103 §6.2: one FAT copy must hold an entry for every data
        // cluster plus the two reserved entries. An undersized FAT makes
        // FatTable index past the FAT region into the second copy or the
        // data area — silent corruption on any moderately full volume.
        TR.Run("Test_Format_FatSize_CoversClusterCount", () =>
        {
            // Explicit FAT32: entries are 4 bytes — 128 per 512-byte
            // sector, half the FAT16 density.
            AssertFormattedFatCovers(
                new MemoryBlockDevice("FMT32", 512, (64UL * 1024 * 1024) / 512),
                new FatFormatOptions { Type = FatType.Fat32 });
            // Auto-detected FAT16 (64 MiB, SPC auto-picks 8 → ~16k
            // clusters): the FAT must be sized for the resolved type, not
            // the FAT12 first guess.
            AssertFormattedFatCovers(
                new MemoryBlockDevice("FMTAUTO16", 512, (64UL * 1024 * 1024) / 512),
                new FatFormatOptions());
            // Auto-detected FAT12 baseline.
            AssertFormattedFatCovers(
                new MemoryBlockDevice("FMTAUTO12", 512, (4UL * 1024 * 1024) / 512),
                new FatFormatOptions());
        });

        TR.Finish();

        Serial.WriteString("\n[Tests Complete - System Halting]\n");
    }

    protected override void Run()
    {
        Stop();
    }

    protected override void AfterRun()
    {
        TR.Complete();
    }

    private static IVfsInode ResolveRoot(string mountPoint)
    {
        Assert.True(VfsManager.TryGetMount(mountPoint, out VfsManager.VfsMount? mount));
        Assert.NotNull(mount);
        return mount!.Superblock.Root;
    }

    private static IVfsFileHandle? OpenFile(string path)
    {
        VfsManager.TryOpenFile(path, out IVfsFileHandle? handle);
        return handle;
    }

    // Formats the disk, decodes the fresh BPB, and checks one FAT copy's
    // capacity against the volume's cluster count (fatgen103 §6.2: data
    // clusters + 2 reserved entries must fit).
    private static void AssertFormattedFatCovers(MemoryBlockDevice disk, FatFormatOptions opts)
    {
        FatFilesystemType driver = new(disk);
        Assert.True(driver.TryFormat(default, opts));

        byte[] bpb = new byte[512];
        disk.ReadBlock(0, 1, bpb);
        Span<byte> b = bpb;
        uint bytesPerSector = BitConverter.ToUInt16(b.Slice(11, 2));
        uint spc = b[13];
        uint reserved = BitConverter.ToUInt16(b.Slice(14, 2));
        uint numFats = b[16];
        uint rootEntries = BitConverter.ToUInt16(b.Slice(17, 2));
        uint fatSize = BitConverter.ToUInt16(b.Slice(22, 2));
        if (fatSize == 0)
        {
            fatSize = BitConverter.ToUInt32(b.Slice(36, 4));
        }
        uint totalSectors = BitConverter.ToUInt16(b.Slice(19, 2));
        if (totalSectors == 0)
        {
            totalSectors = BitConverter.ToUInt32(b.Slice(32, 4));
        }

        uint rootDirSectors = (rootEntries * 32u + bytesPerSector - 1) / bytesPerSector;
        uint dataStart = reserved + numFats * fatSize + rootDirSectors;
        uint clusterCount = (totalSectors - dataStart) / spc;

        // Entry density per family, derived from the cluster count the
        // way fatgen103 §3.5 resolves the type.
        uint entriesPerSector = clusterCount < 4085
            ? bytesPerSector * 2 / 3
            : (clusterCount < 65525 ? bytesPerSector / 2 : bytesPerSector / 4);
        Assert.True(fatSize * entriesPerSector >= clusterCount + 2,
            "one FAT copy must cover every data cluster plus the two reserved entries");
    }

    private static byte[] MakePayload(int size, byte salt)
    {
        byte[] buffer = new byte[size];
        for (int i = 0; i < size; i++)
        {
            buffer[i] = (byte)((i ^ salt) & 0xFF);
        }
        return buffer;
    }

    private static void AssertBytesEqual(byte[] expected, byte[] actual)
    {
        Assert.Equal<int>(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal<byte>(expected[i], actual[i]);
        }
    }

    private static void WriteAll(IVfsInode inode, byte[] data)
    {
        IFileOperations ops = inode.FileOperations!;
        IVfsOpenFile h = new TestOpenFile(inode, ops);
        long written = ops.Write(h, data);
        Assert.Equal<long>(data.Length, written);
        ops.Release(h);
    }

    private static byte[] ReadAll(IVfsInode inode, int length)
    {
        IFileOperations ops = inode.FileOperations!;
        IVfsOpenFile h = new TestOpenFile(inode, ops);
        byte[] buf = new byte[length];
        long n = ops.Read(h, buf);
        Assert.Equal<long>(length, n);
        ops.Release(h);
        return buf;
    }

    private static string IntToHex(int value, int width)
    {
        Span<char> chars = stackalloc char[width];
        for (int i = width - 1; i >= 0; i--)
        {
            int nibble = value & 0xF;
            chars[i] = (char)(nibble < 10 ? '0' + nibble : 'A' + (nibble - 10));
            value >>= 4;
        }
        return new string(chars);
    }

    private sealed class TestOpenFile : IVfsOpenFile
    {
        public TestOpenFile(IVfsInode inode, IFileOperations ops)
        {
            Inode = inode;
            Operations = ops;
        }

        public IVfsInode Inode { get; }
        public IFileOperations Operations { get; }
        public long Position { get; set; }
    }
}
