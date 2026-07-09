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
            // Explicit FAT32 (33 MiB, SPC auto-picks 1 → ~66k clusters):
            // entries are 4 bytes — 128 per 512-byte sector, half the
            // FAT16 density. Devices stay ≤ 33 MiB so the kernel heap can
            // recycle them between sub-cases.
            AssertFormattedFatCovers(
                new MemoryBlockDevice("FMT32", 512, (33UL * 1024 * 1024) / 512),
                new FatFormatOptions { Type = FatType.Fat32 });
            // Auto-detected FAT16 (32 MiB, SPC auto-picks 8 → ~8k
            // clusters): the FAT must be sized for the resolved type, not
            // the FAT12 first guess.
            AssertFormattedFatCovers(
                new MemoryBlockDevice("FMTAUTO16", 512, (32UL * 1024 * 1024) / 512),
                new FatFormatOptions());
            // Auto-detected FAT12 baseline.
            AssertFormattedFatCovers(
                new MemoryBlockDevice("FMTAUTO12", 512, (4UL * 1024 * 1024) / 512),
                new FatFormatOptions());
        });

        // Writers reject what the parser would drop, and cleanly: a
        // sub-512-byte sector crashed the fixed-offset BPB writer
        // mid-format, an oversized one truncated the 16-bit BPB field
        // into an unmountable volume, and an out-of-band FAT32 root
        // cluster underflowed ZeroRootArea into a wild write after the
        // BPB and FATs were already on disk.
        TR.Run("Test_Format_RejectsBogusGeometry", () =>
        {
            Assert.True(FormatRefusedCleanly(
                new MemoryBlockDevice("FMTSS256", 256, 16384), new FatFormatOptions()),
                "a 256-byte-sector device must be refused, not crash the BPB writer");
            Assert.True(FormatRefusedCleanly(
                new MemoryBlockDevice("FMTSS64K", 65536, 128), new FatFormatOptions()),
                "a 64 KiB-sector device must be refused, not truncated into an unmountable BPB");
            // One 33 MiB FAT32-band device shared by both root-cluster
            // cases to stay easy on the kernel heap.
            MemoryBlockDevice fat32Band = new("FMTRC", 512, (33UL * 1024 * 1024) / 512);
            Assert.True(FormatRefusedCleanly(
                fat32Band,
                new FatFormatOptions { Type = FatType.Fat32, RootCluster = 1 }),
                "a FAT32 root cluster below 2 must be refused");
            Assert.True(FormatRefusedCleanly(
                fat32Band,
                new FatFormatOptions { Type = FatType.Fat32, RootCluster = 1_000_000 }),
                "a FAT32 root cluster beyond the cluster count must be refused");
        });

        // The RAM test double must honor the throw-on-failure IBlockDevice
        // contract: (int)(blockNo * BlockSize) truncates, and a product
        // that is a multiple of 2^32 aliases sector 0 — a driver bug
        // computing a wild sector would read plausible data and pass.
        TR.Run("Test_MemoryBlockDevice_ThrowsOutOfRange", () =>
        {
            MemoryBlockDevice disk = new("MBDRANGE", 512, 8192);
            byte[] sector = new byte[512];
            // Stamp sector 0 so a silent alias would be detectable.
            sector[0] = 0xA5;
            disk.WriteBlock(0, 1, sector);

            Assert.True(ReadThrowsOutOfRange(disk, disk.BlockCount, 1),
                "a read past the device end must throw");
            // 8388608 * 512 == 2^32: the truncated cast yields offset 0.
            Assert.True(ReadThrowsOutOfRange(disk, 8_388_608, 1),
                "a read whose byte offset wraps 2^32 must throw, not alias sector 0");
            Assert.True(ReadThrowsOutOfRange(disk, disk.BlockCount - 1, 2),
                "a read straddling the device end must throw");
        });

        // The BPB is on-disk metadata: parse/mount-time validation must
        // reject what would otherwise wrap the uint geometry math into a
        // self-consistent-looking volume or wild cluster LBAs.
        TR.Run("Test_Mount_RejectsCorruptBpb", () =>
        {
            MemoryBlockDevice disk = new("BPBHARD", 512, (8UL * 1024 * 1024) / 512);

            Assert.True(MountRefusedCleanly(CorruptBpb(disk, PatchBpbFatRegionWraps)),
                "a FAT region that wraps uint must be rejected");
            Assert.True(MountRefusedCleanly(CorruptBpb(disk, PatchBpbTotalPastDevice)),
                "a volume extending past the device must be rejected");
            Assert.True(MountRefusedCleanly(CorruptBpb(disk, PatchBpbZeroFat)),
                "a zero-length FAT must be rejected");
            Assert.True(MountRefusedCleanly(CorruptBpb(disk, PatchBpbBadSpc)),
                "a non-power-of-two SectorsPerCluster must be rejected");

            // FAT32 root cluster 0 underflows ClusterToLba's cluster - 2.
            MemoryBlockDevice disk32 = FatTestVolume.CreateFat32("BPBHARD32");
            byte[] sector = new byte[512];
            disk32.ReadBlock(0, 1, sector);
            BitConverter.TryWriteBytes(sector.AsSpan(44, 4), 0u);
            disk32.WriteBlock(0, 1, sector);
            Assert.True(MountRefusedCleanly(disk32),
                "a FAT32 root cluster below 2 must be rejected at mount");

            // ClusterToLba bounds on a healthy volume.
            MemoryBlockDevice valid = new("BPBRANGE", 512, (8UL * 1024 * 1024) / 512);
            FatFilesystemType fmt = new(valid);
            Assert.True(fmt.TryFormat(default, new FatFormatOptions()));
            valid.ReadBlock(0, 1, sector);
            Assert.True(FatBootSector.TryParse(sector, out FatBootSector? bs) && bs != null);
            Assert.True(ClusterToLbaThrows(bs!, 0),
                "ClusterToLba(0) must throw, not underflow into an exabyte LBA");
            Assert.True(ClusterToLbaThrows(bs!, bs!.ClusterCount + 5),
                "ClusterToLba beyond the cluster count must throw");
        });

        // A corrupt FAT entry or dirent FirstCluster with a huge cluster
        // number passes the EOC/bad checks: GetChain follows it, Get reads
        // sectors past the first FAT copy as entries, and Free()'s
        // read-modify-write lands outside the FAT region (mirrored
        // NumberOfFats times) — silent corruption of other FAT copies or
        // file data.
        TR.Run("Test_FatTable_BoundsClusterNumbers", () =>
        {
            MemoryBlockDevice disk = new("FATBOUND", 512, (8UL * 1024 * 1024) / 512);
            FatFilesystemType driver = new(disk);
            Assert.True(driver.TryFormat(default, new FatFormatOptions()));
            byte[] sector = new byte[512];
            disk.ReadBlock(0, 1, sector);
            Assert.True(FatBootSector.TryParse(sector, out FatBootSector? bs) && bs != null);
            FatTable table = new(disk, bs!);

            uint first = table.AllocateChain(3);
            Assert.True(first != 0);
            Assert.Equal(3, table.GetChain(first).Count);

            // Corrupt the middle link to a cluster far past the volume's
            // count (still decodable — it lands inside the second FAT copy).
            List<uint> chain = table.GetChain(first);
            uint wild = bs!.ClusterCount + 500;
            table.Set(chain[1], wild);

            Assert.Equal(2, table.GetChain(first).Count,
                "the chain walk must stop at an out-of-range link");
            Assert.True(table.IsEndOfChain(table.Get(wild)),
                "Get outside the volume's clusters must return EOC, not decode other sectors");

            // Set on an out-of-range cluster must not touch the device:
            // snapshot the two sectors the unbounded math would RMW (one
            // in the second FAT copy, its mirror in the region beyond).
            uint wildOffset = wild + wild / 2;
            ulong wildLba = bs!.FatStartLba + wildOffset / 512;
            ulong mirrorLba = wildLba + bs!.FatSectorCount;
            byte[] beforeA = new byte[512];
            byte[] beforeB = new byte[512];
            disk.ReadBlock(wildLba, 1, beforeA);
            disk.ReadBlock(mirrorLba, 1, beforeB);
            table.Set(wild, FatTable.FreeCluster);
            byte[] afterA = new byte[512];
            byte[] afterB = new byte[512];
            disk.ReadBlock(wildLba, 1, afterA);
            disk.ReadBlock(mirrorLba, 1, afterB);
            AssertBytesEqual(beforeA, afterA);
            AssertBytesEqual(beforeB, afterB);

            // FAT32's gap below the EOC band is huge; a wild link must
            // resolve to EOC without out-of-range device I/O.
            MemoryBlockDevice disk32 = FatTestVolume.CreateFat32("FATBOUND32");
            disk32.ReadBlock(0, 1, sector);
            Assert.True(FatBootSector.TryParse(sector, out FatBootSector? bs32) && bs32 != null);
            FatTable table32 = new(disk32, bs32!);
            Assert.True(GetReturnsEocSafely(table32, 0x00FF0000u),
                "a wild FAT32 cluster must resolve to EOC, not out-of-range I/O");
        });

        // Unlink invalidates nothing on a still-referenced inode, so a
        // later Fsync through a stale handle patches whatever directory
        // entry reused the slot — cross-linking the new file onto the old
        // file's freed clusters.
        TR.Run("Test_Fat16_StaleHandleFsync_DoesNotClobberReusedSlot", () =>
        {
            IVfsInode root = ResolveRoot(Fat16Mount);
            Assert.True(root.InodeOperations.Create(root, "STALEA.TXT", ModeEnum.RegularFile, out IVfsInode? fileA));
            byte[] payloadA = MakePayload(1024, 0x51);
            WriteAll(fileA!, payloadA);

            Assert.True(root.InodeOperations.Unlink(root, "STALEA.TXT"));

            // Same slot count (plain 8.3 name): reuses A's freed slot.
            Assert.True(root.InodeOperations.Create(root, "STALEB.TXT", ModeEnum.RegularFile, out IVfsInode? fileB));
            byte[] payloadB = MakePayload(700, 0x52);
            WriteAll(fileB!, payloadB);

            // Fsync through the stale handle must not patch B's entry.
            IFileOperations opsA = fileA!.FileOperations!;
            opsA.Fsync(new TestOpenFile(fileA!, opsA));

            Assert.True(root.InodeOperations.Lookup(root, "STALEB.TXT", out IVfsInode? lookupB));
            Assert.True(lookupB!.InodeOperations.GetAttr(lookupB, out VfsStat statB));
            Assert.Equal<ulong>((ulong)payloadB.Length, statB.Size);
            byte[] readBack = ReadAll(lookupB!, payloadB.Length);
            AssertBytesEqual(payloadB, readBack);
        });

        // SetAttr(Mode) updates the in-memory attributes and relies on
        // UpdateInodeEntry to persist them; the attribute byte must reach
        // disk or the change evaporates on the next Lookup.
        TR.Run("Test_Fat16_SetAttrReadOnly_Persists", () =>
        {
            IVfsInode root = ResolveRoot(Fat16Mount);
            // Create writable (mode with a write bit maps to no ReadOnly
            // attribute), then chmod it read-only via SetAttr.
            Assert.True(root.InodeOperations.Create(
                root, "ROFLAG.TXT", ModeEnum.RegularFile | ModeEnum.OwnerRead | ModeEnum.OwnerWrite, out IVfsInode? created));
            Assert.True(root.InodeOperations.Lookup(root, "ROFLAG.TXT", out IVfsInode? sanity));
            Assert.True(sanity!.InodeOperations.GetAttr(sanity, out VfsStat sanityStat));
            Assert.True((sanityStat.Mode & ModeEnum.OwnerWrite) != 0, "baseline file must be writable on disk");

            VfsStat wanted = default;
            wanted.Mode = ModeEnum.RegularFile | ModeEnum.OwnerRead | ModeEnum.GroupRead | ModeEnum.OtherRead;
            Assert.True(created!.InodeOperations.SetAttr(created, SetAttrFlags.Mode, wanted));

            // A fresh Lookup re-parses the on-disk entry.
            Assert.True(root.InodeOperations.Lookup(root, "ROFLAG.TXT", out IVfsInode? lookup));
            Assert.True(lookup!.InodeOperations.GetAttr(lookup, out VfsStat stat));
            Assert.True((stat.Mode & ModeEnum.OwnerWrite) == 0,
                "the ReadOnly attribute must survive a re-parse of the on-disk entry");
        });

        // A maximal LFN needs 21 slots (672 bytes) but a 512-byte cluster
        // holds 16: growing by exactly one cluster can never satisfy the
        // run, so creates in a packed directory fail despite free space.
        TR.Run("Test_GrowDirectory_FitsMaxLfnRun", () =>
        {
            MemoryBlockDevice disk = new("GROWDIR", 512, (16UL * 1024 * 1024) / 512);
            FatFilesystemType driver = new(disk);
            Assert.True(driver.TryFormat(default, new FatFormatOptions { Type = FatType.Fat16, SectorsPerCluster = 1 }));
            Assert.True(driver.TryMount(default, MountFlags.None, out IVfsSuperblock? sb));
            IVfsInode root = sb!.Root;

            Assert.True(root.InodeOperations.Mkdir(root, "PACK", ModeEnum.Directory, out IVfsInode? dir));
            // '.' and '..' occupy 2 of the 16 slots; fill the other 14.
            for (int i = 0; i < 14; i++)
            {
                string name = "F" + (i / 10) + (i % 10) + ".TXT";
                Assert.True(dir!.InodeOperations.Create(dir, name, ModeEnum.RegularFile, out _));
            }

            string maxLfn = new string('x', 251) + ".txt";
            Assert.True(dir!.InodeOperations.Create(dir, maxLfn, ModeEnum.RegularFile, out _),
                "a packed directory must grow enough clusters to fit a maximal LFN run");
            Assert.True(dir.InodeOperations.Lookup(dir, maxLfn, out _));
        });

        // VFS sync and unmount are durability points: they must flush the
        // device's volatile write cache.
        TR.Run("Test_Superblock_SyncAndDrop_Flush", () =>
        {
            MemoryBlockDevice disk = new("SYNCFLUSH", 512, (16UL * 1024 * 1024) / 512);
            FatFilesystemType driver = new(disk);
            Assert.True(driver.TryFormat(default, new FatFormatOptions { Type = FatType.Fat16, SectorsPerCluster = 4 }));
            Assert.True(driver.TryMount(default, MountFlags.None, out IVfsSuperblock? sb));

            int before = disk.FlushCount;
            Assert.True(sb!.SuperOperations.Sync(sb));
            Assert.True(disk.FlushCount > before, "Sync must flush the device write cache");

            before = disk.FlushCount;
            sb.SuperOperations.Drop(sb);
            Assert.True(disk.FlushCount > before, "Drop (unmount) must flush the device write cache");
        });

        // Short-name generation must never silently lose the requested
        // spelling: mangled names take the LFN path, and generated 8.3
        // tails must be unique within the directory.
        TR.Run("Test_FatDirectory_ShortNameAndLfnHardening", () =>
        {
            IVfsInode root = ResolveRoot(Fat16Mount);

            Assert.True(root.InodeOperations.Create(root, "A+B.TXT", ModeEnum.RegularFile, out _));
            Assert.True(root.InodeOperations.Lookup(root, "A+B.TXT", out _),
                "a name the 8.3 encoder mangles must take the LFN path");

            Assert.True(root.InodeOperations.Create(root, "TailCollisionA.txt", ModeEnum.RegularFile, out IVfsInode? lfA));
            Assert.True(root.InodeOperations.Create(root, "TailCollisionB.txt", ModeEnum.RegularFile, out IVfsInode? lfB));
            WriteAll(lfA!, MakePayload(64, 0x0A));
            WriteAll(lfB!, MakePayload(64, 0x0B));
            Assert.True(root.InodeOperations.Lookup(root, "TAILCO~2.TXT", out IVfsInode? byTail),
                "the second colliding long name must get a ~2 tail");
            if (byTail != null)
            {
                AssertBytesEqual(MakePayload(64, 0x0B), ReadAll(byTail, 64));
            }

            Assert.False(root.InodeOperations.Create(root, new string('y', 300), ModeEnum.RegularFile, out _),
                "names longer than the 255-char LFN cap must be refused");
        });

        // LFN chains and reserved dirent fields are on-disk metadata:
        // orphaned LFN entries must not attach to the next short entry,
        // FstClusHI is reserved on FAT12/16 (EA handle may be nonzero),
        // and consuming the 0x00 terminator must re-terminate or stale
        // bytes past it get parsed back to life.
        TR.Run("Test_FatDirectory_DistrustsOnDiskMetadata", () =>
        {
            MemoryBlockDevice disk = new("DIRHARD", 512, (16UL * 1024 * 1024) / 512);
            FatFilesystemType driver = new(disk);
            Assert.True(driver.TryFormat(default, new FatFormatOptions { Type = FatType.Fat16, SectorsPerCluster = 4 }));
            Assert.True(driver.TryMount(default, MountFlags.None, out IVfsSuperblock? sb));
            IVfsInode root = sb!.Root;
            byte[] bpbSector = new byte[512];
            disk.ReadBlock(0, 1, bpbSector);
            Assert.True(FatBootSector.TryParse(bpbSector, out FatBootSector? bs) && bs != null);

            // FstClusHI carries an EA handle on real FAT16 volumes.
            Assert.True(root.InodeOperations.Create(root, "EAFILE.TXT", ModeEnum.RegularFile, out IVfsInode? ea));
            byte[] payload = MakePayload(1024, 0x33);
            WriteAll(ea!, payload);
            PatchRootEntry(disk, bs!, "EAFILE  TXT", static (region, off) =>
            {
                BitConverter.TryWriteBytes(region.AsSpan(off + 20, 2), (ushort)0x1234);
            });
            Assert.True(root.InodeOperations.Lookup(root, "EAFILE.TXT", out IVfsInode? eaBack));
            if (eaBack != null)
            {
                AssertBytesEqual(payload, ReadAll(eaBack, payload.Length));
            }

            // A non-LFN-aware tool renaming the 8.3 entry in place leaves
            // the preceding LFN chain orphaned; its checksum no longer
            // matches and it must not lend its name to the renamed entry.
            Assert.True(root.InodeOperations.Create(root, "orphantest.txt", ModeEnum.RegularFile, out _));
            Assert.True(root.InodeOperations.Create(root, "PLAIN.TXT", ModeEnum.RegularFile, out _));
            PatchRootEntry(disk, bs!, "ORPHAN~1TXT", static (region, off) =>
            {
                ReadOnlySpan<char> renamed = "RENAMED TXT";
                for (int i = 0; i < 11; i++)
                {
                    region[off + i] = (byte)renamed[i];
                }
            });
            Assert.False(root.InodeOperations.Lookup(root, "orphantest.txt", out _),
                "an orphaned LFN chain must not attach to a rewritten short entry");
            Assert.True(root.InodeOperations.Lookup(root, "RENAMED.TXT", out _));
            Assert.True(root.InodeOperations.Lookup(root, "PLAIN.TXT", out _));

            // Plant a plausible stale entry right after the terminator,
            // then create a file over the terminator slot.
            PlantAfterTerminator(disk, bs!);
            Assert.True(root.InodeOperations.Create(root, "REALFILE.TXT", ModeEnum.RegularFile, out _));
            Assert.False(root.InodeOperations.Lookup(root, "GHOST.TXT", out _),
                "stale bytes past the consumed terminator must not be parsed back to life");
        });

        // Durability and spec conformance of the written volume.
        TR.Run("Test_Format_DurabilityAndSpecFields", () =>
        {
            // TotSec16: a FAT12/16 volume under 65536 sectors must store
            // its count in the 16-bit field (strict drivers and fsck.fat
            // read only TotSec16 there), with TotSec32 zero.
            MemoryBlockDevice small16 = new("FMT16SMALL", 512, (16UL * 1024 * 1024) / 512);
            FatFilesystemType driver16 = new(small16);
            Assert.True(driver16.TryFormat(default, new FatFormatOptions { Type = FatType.Fat16, SectorsPerCluster = 4 }));
            Assert.True(small16.FlushCount > 0, "Format must flush the device before reporting success");
            byte[] bpb = new byte[512];
            small16.ReadBlock(0, 1, bpb);
            Assert.Equal<uint>((16u * 1024 * 1024) / 512, BitConverter.ToUInt16(bpb.AsSpan(19, 2)));
            Assert.Equal<uint>(0, BitConverter.ToUInt32(bpb.AsSpan(32, 4)));

            // Destroy must clear the FAT32 FSInfo and backup boot sector
            // too, or BPB_BkBootSec-honoring tools can still reconstruct
            // and mount the volume.
            MemoryBlockDevice disk32 = FatTestVolume.CreateFat32("FMTDESTROY");
            FatFilesystemType driver32 = new(disk32);
            Assert.True(driver32.TryDestroy(default));
            byte[] sector = new byte[512];
            disk32.ReadBlock(1, 1, sector);
            AssertAllZero(sector, "FSInfo sector must be wiped by Destroy");
            disk32.ReadBlock(6, 1, sector);
            AssertAllZero(sector, "backup boot sector must be wiped by Destroy");
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

    // Formats the disk fresh, applies the patch to sector 0, writes it
    // back. Keeps the corruption cells to one line per scenario.
    private static MemoryBlockDevice CorruptBpb(MemoryBlockDevice disk, Action<byte[]> patch)
    {
        FatFilesystemType driver = new(disk);
        Assert.True(driver.TryFormat(default, new FatFormatOptions()), "baseline format for a corruption cell failed");
        byte[] sector = new byte[512];
        disk.ReadBlock(0, 1, sector);
        patch(sector);
        disk.WriteBlock(0, 1, sector);
        return disk;
    }

    // FATSz16 = 0, FATSz32 = 0x80000000: with 2 FATs the uint fatRegion
    // product wraps to 0 and the geometry collapses onto the reserved area.
    private static void PatchBpbFatRegionWraps(byte[] bpb)
    {
        BitConverter.TryWriteBytes(bpb.AsSpan(22, 2), (ushort)0);
        BitConverter.TryWriteBytes(bpb.AsSpan(36, 4), 0x80000000u);
        BitConverter.TryWriteBytes(bpb.AsSpan(44, 4), 2u);
    }

    // Claims 4x the device's sectors.
    private static void PatchBpbTotalPastDevice(byte[] bpb)
    {
        BitConverter.TryWriteBytes(bpb.AsSpan(19, 2), (ushort)0);
        BitConverter.TryWriteBytes(bpb.AsSpan(32, 4), 4u * (8 * 1024 * 1024 / 512));
    }

    private static void PatchBpbZeroFat(byte[] bpb)
    {
        BitConverter.TryWriteBytes(bpb.AsSpan(22, 2), (ushort)0);
        BitConverter.TryWriteBytes(bpb.AsSpan(36, 4), 0u);
    }

    private static void PatchBpbBadSpc(byte[] bpb)
    {
        bpb[13] = 3;
    }

    // One try/catch per method on purpose: true = TryMount returned false
    // without throwing.
    private static bool MountRefusedCleanly(MemoryBlockDevice disk)
    {
        try
        {
            FatFilesystemType driver = new(disk);
            return !driver.TryMount(default, MountFlags.None, out _);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Locates the 8.3 entry with the given raw 11-char name in the FAT16
    // fixed root region and lets the caller patch it in place — the way
    // a foreign, non-LFN-aware tool would.
    private static void PatchRootEntry(MemoryBlockDevice disk, FatBootSector bs, string raw11, Action<byte[], int> patch)
    {
        byte[] region = new byte[bs.RootSectorCount * bs.BytesPerSector];
        disk.ReadBlock(bs.RootStartLba, bs.RootSectorCount, region);
        for (int off = 0; off + 32 <= region.Length; off += 32)
        {
            bool match = true;
            for (int i = 0; i < 11 && match; i++)
            {
                match = region[off + i] == (byte)raw11[i];
            }
            if (match)
            {
                patch(region, off);
                disk.WriteBlock(bs.RootStartLba, bs.RootSectorCount, region);
                return;
            }
        }
        Assert.True(false, "raw root entry not found: " + raw11);
    }

    // Writes a plausible stale 8.3 entry ("GHOST.TXT") into the slot right
    // after the root directory's 0x00 terminator — the on-disk state the
    // spec allows after a directory was truncated by a single terminator.
    private static void PlantAfterTerminator(MemoryBlockDevice disk, FatBootSector bs)
    {
        byte[] region = new byte[bs.RootSectorCount * bs.BytesPerSector];
        disk.ReadBlock(bs.RootStartLba, bs.RootSectorCount, region);
        for (int off = 0; off + 64 <= region.Length; off += 32)
        {
            if (region[off] != 0x00)
            {
                continue;
            }
            int ghost = off + 32;
            ReadOnlySpan<char> name = "GHOST   TXT";
            for (int i = 0; i < 11; i++)
            {
                region[ghost + i] = (byte)name[i];
            }
            region[ghost + 11] = 0x20; // Archive
            BitConverter.TryWriteBytes(region.AsSpan(ghost + 26, 2), (ushort)3);
            BitConverter.TryWriteBytes(region.AsSpan(ghost + 28, 4), 512u);
            disk.WriteBlock(bs.RootStartLba, bs.RootSectorCount, region);
            return;
        }
        Assert.True(false, "no root terminator found to plant behind");
    }

    // One try/catch per method on purpose: true = Get returned an EOC
    // marker without any out-of-range device I/O throwing.
    private static bool GetReturnsEocSafely(FatTable table, uint cluster)
    {
        try
        {
            return table.IsEndOfChain(table.Get(cluster));
        }
        catch (Exception)
        {
            return false;
        }
    }

    // One try/catch per method on purpose.
    private static bool ClusterToLbaThrows(FatBootSector bs, uint cluster)
    {
        try
        {
            bs.ClusterToLba(cluster);
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }

    // One try/catch per method on purpose: true = the read threw the
    // contract's ArgumentOutOfRangeException.
    private static bool ReadThrowsOutOfRange(MemoryBlockDevice disk, ulong blockNo, ulong blockCount)
    {
        try
        {
            byte[] buffer = new byte[(int)(disk.BlockSize * blockCount)];
            disk.ReadBlock(blockNo, blockCount, buffer);
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }

    // One try/catch per method on purpose: true = TryFormat returned
    // false without throwing and without claiming success.
    private static bool FormatRefusedCleanly(MemoryBlockDevice disk, FatFormatOptions opts)
    {
        try
        {
            FatFilesystemType driver = new(disk);
            return !driver.TryFormat(default, opts);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void AssertAllZero(byte[] sector, string message)
    {
        for (int i = 0; i < sector.Length; i++)
        {
            if (sector[i] != 0)
            {
                Assert.True(false, message);
                return;
            }
        }
        Assert.True(true, message);
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
