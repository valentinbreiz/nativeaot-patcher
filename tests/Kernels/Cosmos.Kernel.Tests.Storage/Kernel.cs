using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Storage;
using Cosmos.TestRunner.Framework;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;

namespace Cosmos.Kernel.Tests.Storage;

public class Kernel : Sys.Kernel
{
    // The single block device the active QEMU profile attached, captured
    // once at BeforeRun. The profile name (set by the engine — ahci,
    // ahci+gicv2, nvme, nvme+gicv3, nvme+acpi-off, ...) is what the report's
    // [profile] prefix shows; this field just holds whatever device bound.
    private static IBlockDevice? s_dev;

    // Reason surfaced through TR.RunIf when a test depends on a device
    // having actually bound. A profile whose driver did not enumerate
    // (e.g. nvme+acpi-off on arm64, where no-ACPI removes PCIe discovery)
    // lands here and the device tests skip.
    private const string SkipNoDevice = "no block device bound for this profile";

    // Same gating reason for the partition-table tests, which also need
    // a backing host device.
    private const string SkipNoHost = "no block device bound for partition-table tests";

    protected override void BeforeRun()
    {
        Serial.WriteString("[Storage] BeforeRun() reached!\n");

        // 2 manager + 2 profile + 12 device + 6 partition = 22 tests per profile.
        TR.Start("Storage Block Device Tests", expectedTests: 22);

        bool hasDevice = StorageManager.DeviceCount > 0;
        s_dev = hasDevice ? StorageManager.GetDevice(0) : null;

        // ==================== Manager ====================
        TR.Run("Manager_StorageInitialized", TestManager_StorageInitialized);
        TR.RunIf(hasDevice, "Manager_ExactlyOneDevice", TestManager_ExactlyOneDevice, SkipNoDevice);

        // ==================== Profile (assert the cell's hardware path) ====================
        // Prove the cell exercised the hardware it names, not just that block
        // I/O happened to work — the gap that let a silent MSI-X->polled
        // regression pass before.
        TR.RunIf(hasDevice, "Profile_DeviceKindMatches", TestProfile_DeviceKindMatches, SkipNoDevice);

        if (!TR.ProfileHasPrefix("nvme"))
        {
            TR.Skip("Profile_NvmeInterruptModeMatches", "not an NVMe profile");
        }
        else if (Nvme.Controllers.Count == 0)
        {
            TR.Skip("Profile_NvmeInterruptModeMatches", SkipNoDevice);
        }
        else if (TR.ProfileContains("gicv2") || TR.ProfileContains("gicv3"))
        {
            // Only the GIC-version cells pin a determinate NVMe interrupt path:
            // gicv3 brings up the ITS so MSI-X can route; gicv2 has no ITS so
            // the driver must fall back to polled. Adaptive run (Action<bool>):
            // the condition is "expect MSI-X" == this is the gicv3 cell.
            TR.RunIf(TR.ProfileContains("gicv3"), "Profile_NvmeInterruptModeMatches", TestProfile_NvmeInterruptMode);
        }
        else
        {
            // Bare nvme / acpi-off: the interrupt mode depends on the
            // platform default (x64 APIC vs arm64 default GIC) or is moot
            // (acpi-off has no MSI), so it is not pinned here — the gicv2/gicv3
            // cells assert both paths explicitly.
            TR.Skip("Profile_NvmeInterruptModeMatches", "interrupt mode not pinned by this cell");
        }

        // ==================== Device (single-disk round-trip) ====================
        bool dev = s_dev != null;
        TR.RunIf(dev, "Device_BlockGeometry_Sane",         TestDevice_BlockGeometrySane,        SkipNoDevice);
        TR.RunIf(dev, "Device_WriteRead_SingleBlock",      TestDevice_WriteReadSingleBlock,     SkipNoDevice);
        TR.RunIf(dev, "Device_WriteRead_MultiBlock",       TestDevice_WriteReadMultiBlock,      SkipNoDevice);
        TR.RunIf(dev, "Device_WriteRead_Idempotent",       TestDevice_WriteReadIdempotent,      SkipNoDevice);
        TR.RunIf(dev, "Device_ReReadStable",               TestDevice_ReReadStable,             SkipNoDevice);
        TR.RunIf(dev, "Device_LargeTransfer",              TestDevice_LargeTransfer,            SkipNoDevice);
        TR.RunIf(dev, "Device_BoundaryLBA",                TestDevice_BoundaryLBA,              SkipNoDevice);
        TR.RunIf(dev, "Device_LBA_Zero_RoundTrip",         TestDevice_LBAZeroRoundTrip,         SkipNoDevice);
        TR.RunIf(dev, "Device_CrossBlock_Isolation",       TestDevice_CrossBlockIsolation,      SkipNoDevice);
        TR.RunIf(dev, "Device_LBA_Stride_Sweep",           TestDevice_LBAStrideSweep,           SkipNoDevice);
        TR.RunIf(dev, "Device_RandomOrder_ReadAfterWrite", TestDevice_RandomOrderReadAfterWrite, SkipNoDevice);
        TR.RunIf(dev, "Device_Multiblock_TailBoundary",    TestDevice_MultiblockTailBoundary,   SkipNoDevice);

        // ==================== Partition (MBR/GPT, partition translation) ====================
        // These run last because they overwrite LBA 0..33, which the device
        // round-trip tests above also touch — ordering them last keeps the
        // earlier results from being affected by the partition-table writes.
        TR.RunIf(dev, "Partition_MBR_RoundTrip",          TestPartition_MBRRoundTrip,          SkipNoHost);
        TR.RunIf(dev, "Partition_GPT_RoundTrip",          TestPartition_GPTRoundTrip,          SkipNoHost);
        TR.RunIf(dev, "Partition_RescanPartitions",       TestPartition_RescanPartitions,      SkipNoHost);
        TR.RunIf(dev, "Partition_ReadWrite_TranslatesLba", TestPartition_ReadWriteTranslatesLba, SkipNoHost);
        TR.RunIf(dev, "Partition_OutOfBounds_Throws",      TestPartition_OutOfBoundsThrows,     SkipNoHost);

        // Overflow-safety of the bounds check is hardware-independent (in-memory
        // probe host), so it runs unconditionally, even on cells where no disk bound.
        TR.Run("Partition_BoundsOverflow_Throws", TestPartition_BoundsOverflowThrows);

        TR.Finish();

        Serial.WriteString("\n[Tests Complete - System Halting]\n");
    }

    protected override void Run() => Stop();

    protected override void AfterRun()
    {
        TR.Complete();
        Cosmos.Kernel.System.Power.Halt();
    }

    // ==================== Manager ====================

    private static void TestManager_StorageInitialized()
    {
        Assert.True(StorageManager.IsEnabled);
        Assert.True(StorageManager.IsInitialized);
    }

    // The engine attaches exactly one disk per profile, so anything other
    // than 1 means either the profile is misconfigured or the driver
    // double-registered. Bound devices stay enumerated for the run.
    private static void TestManager_ExactlyOneDevice()
    {
        Assert.Equal(1, StorageManager.DeviceCount);
    }

    // ==================== Profile ====================

    // The cell name encodes the controller it attached: ahci => SATA,
    // nvme-* => Nvme. Proves the driver that bound matches the cell's intent.
    private static void TestProfile_DeviceKindMatches()
    {
        string expected = TR.ProfileHasPrefix("ahci") ? "SATA" : "NVMe";
        Assert.Equal(expected, s_dev!.Name);
    }

    // A gicv3 cell must come up MSI-X (arm64 GICv3 ITS routes it); a gicv2 cell
    // must fall back to polled (no ITS). The bool is the cell's expected mode
    // (true = MSI-X), supplied by the adaptive RunIf overload.
    private static void TestProfile_NvmeInterruptMode(bool expectMsix)
    {
        bool actual = Nvme.Controllers[0].IsMsiXEnabled;
        if (expectMsix)
        {
            Assert.True(actual, "expected NVMe MSI-X interrupts but the controller is polled");
        }
        else
        {
            Assert.False(actual, "expected NVMe polled fallback but the controller enabled MSI-X");
        }
    }

    // ==================== Device ====================

    private static void TestDevice_BlockGeometrySane()
    {
        Assert.Equal<ulong>(512, s_dev!.BlockSize);
        Assert.True(s_dev.BlockCount > 0);
    }

    private static void TestDevice_WriteReadSingleBlock()
    {
        const ulong lba = 100;
        ulong size = s_dev!.BlockSize;

        Span<byte> writeBuf = new byte[size];
        for (int i = 0; i < (int)size; i++)
        {
            writeBuf[i] = (byte)(i ^ 0xA5);
        }
        s_dev.WriteBlock(lba, 1, writeBuf);

        Span<byte> readBuf = new byte[size];
        s_dev.ReadBlock(lba, 1, readBuf);

        for (int i = 0; i < (int)size; i++)
        {
            Assert.Equal(writeBuf[i], readBuf[i]);
        }
    }

    private static void TestDevice_WriteReadMultiBlock()
    {
        const ulong lba = 200;
        const ulong blocks = 4;
        ulong total = blocks * s_dev!.BlockSize;

        Span<byte> writeBuf = new byte[total];
        for (int i = 0; i < (int)total; i++)
        {
            writeBuf[i] = (byte)((i * 7) ^ 0x3C);
        }
        s_dev.WriteBlock(lba, blocks, writeBuf);

        Span<byte> readBuf = new byte[total];
        s_dev.ReadBlock(lba, blocks, readBuf);

        for (int i = 0; i < (int)total; i++)
        {
            Assert.Equal(writeBuf[i], readBuf[i]);
        }
    }

    private static void TestDevice_WriteReadIdempotent()
    {
        const ulong lba = 250;
        ulong size = s_dev!.BlockSize;

        Span<byte> first = new byte[size];
        first.Fill(0x11);
        s_dev.WriteBlock(lba, 1, first);

        Span<byte> second = new byte[size];
        second.Fill(0x22);
        s_dev.WriteBlock(lba, 1, second);

        Span<byte> readBuf = new byte[size];
        s_dev.ReadBlock(lba, 1, readBuf);
        for (int i = 0; i < (int)size; i++)
        {
            Assert.Equal((byte)0x22, readBuf[i]);
        }
    }

    private static void TestDevice_ReReadStable()
    {
        const ulong lba = 300;
        ulong size = s_dev!.BlockSize;

        Span<byte> first = new byte[size];
        s_dev.ReadBlock(lba, 1, first);

        Span<byte> second = new byte[size];
        s_dev.ReadBlock(lba, 1, second);

        for (int i = 0; i < (int)size; i++)
        {
            Assert.Equal(first[i], second[i]);
        }
    }

    private static void TestDevice_LargeTransfer()
    {
        const ulong lba = 1000;
        const ulong blocks = 32;
        ulong total = blocks * s_dev!.BlockSize;

        Span<byte> writeBuf = new byte[total];
        for (int i = 0; i < (int)total; i++)
        {
            writeBuf[i] = (byte)((i + (i >> 8)) ^ 0x5A);
        }
        s_dev.WriteBlock(lba, blocks, writeBuf);

        Span<byte> readBuf = new byte[total];
        s_dev.ReadBlock(lba, blocks, readBuf);

        for (int i = 0; i < (int)total; i++)
        {
            Assert.Equal(writeBuf[i], readBuf[i]);
        }
    }

    private static void TestDevice_BoundaryLBA()
    {
        ulong lba = s_dev!.BlockCount - 1;
        ulong size = s_dev.BlockSize;

        Span<byte> writeBuf = new byte[size];
        for (int i = 0; i < (int)size; i++)
        {
            writeBuf[i] = (byte)(i ^ 0xF0);
        }
        s_dev.WriteBlock(lba, 1, writeBuf);

        Span<byte> readBuf = new byte[size];
        s_dev.ReadBlock(lba, 1, readBuf);

        for (int i = 0; i < (int)size; i++)
        {
            Assert.Equal(writeBuf[i], readBuf[i]);
        }
    }

    // LBA 0 is often special (boot sector / protective MBR). Round-trip
    // must work there too.
    private static void TestDevice_LBAZeroRoundTrip()
    {
        ulong size = s_dev!.BlockSize;

        Span<byte> writeBuf = new byte[size];
        for (int i = 0; i < (int)size; i++)
        {
            writeBuf[i] = (byte)(i ^ 0x96);
        }
        s_dev.WriteBlock(0, 1, writeBuf);

        Span<byte> readBuf = new byte[size];
        s_dev.ReadBlock(0, 1, readBuf);

        for (int i = 0; i < (int)size; i++)
        {
            Assert.Equal(writeBuf[i], readBuf[i]);
        }
    }

    // Catches bounce-buffer reuse / wrong-LBA-cached bugs: write 8 contiguous
    // blocks each filled with its own marker byte, then read each block back
    // individually and confirm the markers haven't bled across blocks.
    private static void TestDevice_CrossBlockIsolation()
    {
        const ulong baseLba = 4000;
        const int blocks = 8;
        ulong size = s_dev!.BlockSize;

        for (int b = 0; b < blocks; b++)
        {
            Span<byte> wbuf = new byte[size];
            wbuf.Fill((byte)(0xC0 | b));
            s_dev.WriteBlock(baseLba + (ulong)b, 1, wbuf);
        }

        for (int b = 0; b < blocks; b++)
        {
            Span<byte> rbuf = new byte[size];
            s_dev.ReadBlock(baseLba + (ulong)b, 1, rbuf);
            byte expected = (byte)(0xC0 | b);
            for (int i = 0; i < (int)size; i++)
            {
                Assert.Equal(expected, rbuf[i]);
            }
        }
    }

    // Catches LBA off-by-one / stride bugs: write to LBAs that are far apart
    // (n * 1024) so any wrap or shift error lands on a different block; the
    // pattern depends on n so we can detect a mis-routed write.
    private static void TestDevice_LBAStrideSweep()
    {
        const int slots = 8;
        const ulong stride = 1024;
        ulong size = s_dev!.BlockSize;

        for (int n = 0; n < slots; n++)
        {
            Span<byte> wbuf = new byte[size];
            byte tag = (byte)((n * 17) ^ 0x5A);
            wbuf.Fill(tag);
            s_dev.WriteBlock((ulong)n * stride, 1, wbuf);
        }

        for (int n = 0; n < slots; n++)
        {
            Span<byte> rbuf = new byte[size];
            s_dev.ReadBlock((ulong)n * stride, 1, rbuf);
            byte tag = (byte)((n * 17) ^ 0x5A);
            for (int i = 0; i < (int)size; i++)
            {
                Assert.Equal(tag, rbuf[i]);
            }
        }
    }

    // Reads in non-sequential order should still return the right data —
    // catches code that assumes the last-touched LBA is "current".
    private static void TestDevice_RandomOrderReadAfterWrite()
    {
        const ulong baseLba = 6000;
        ulong size = s_dev!.BlockSize;

        Span<byte> a = new byte[size]; a.Fill(0xAA);
        Span<byte> b = new byte[size]; b.Fill(0xBB);
        Span<byte> c = new byte[size]; c.Fill(0xCC);
        s_dev.WriteBlock(baseLba + 0, 1, a);
        s_dev.WriteBlock(baseLba + 1, 1, b);
        s_dev.WriteBlock(baseLba + 2, 1, c);

        Span<byte> r = new byte[size];

        s_dev.ReadBlock(baseLba + 2, 1, r);
        for (int i = 0; i < (int)size; i++) { Assert.Equal((byte)0xCC, r[i]); }

        s_dev.ReadBlock(baseLba + 0, 1, r);
        for (int i = 0; i < (int)size; i++) { Assert.Equal((byte)0xAA, r[i]); }

        s_dev.ReadBlock(baseLba + 1, 1, r);
        for (int i = 0; i < (int)size; i++) { Assert.Equal((byte)0xBB, r[i]); }
    }

    // Multi-block transfer that ends exactly at the device tail. Catches
    // truncation / wrap bugs at the upper edge of LBA space.
    private static void TestDevice_MultiblockTailBoundary()
    {
        const ulong blocks = 4;
        ulong size = s_dev!.BlockSize;
        ulong lba = s_dev.BlockCount - blocks;
        ulong total = blocks * size;

        Span<byte> writeBuf = new byte[total];
        for (int i = 0; i < (int)total; i++)
        {
            writeBuf[i] = (byte)((i * 13) ^ 0x6E);
        }
        s_dev.WriteBlock(lba, blocks, writeBuf);

        Span<byte> readBuf = new byte[total];
        s_dev.ReadBlock(lba, blocks, readBuf);

        for (int i = 0; i < (int)total; i++)
        {
            Assert.Equal(writeBuf[i], readBuf[i]);
        }
    }

    // ==================== Partition ====================

    private static void TestPartition_MBRRoundTrip()
    {
        // Wipe LBA 0 first so a leftover GPT signature from a prior
        // sub-test doesn't taint the IsMbr check.
        Span<byte> wipe = new byte[s_dev!.BlockSize];
        s_dev.WriteBlock(0, 1, wipe);

        Mbr.Create(s_dev);
        Assert.True(Mbr.IsMbr(s_dev));

        // Two primary entries at distinct LBA windows.
        Mbr.WritePartition(s_dev, 0, systemId: 0x83, startSector: 100, sectorCount: 200);
        Mbr.WritePartition(s_dev, 1, systemId: 0x0B, startSector: 1000, sectorCount: 500);

        List<Mbr.PartitionEntry> parts = Mbr.Parse(s_dev);
        Assert.Equal(2, parts.Count);
        Assert.Equal<byte>(0x83, parts[0].SystemId);
        Assert.Equal<ulong>(100, parts[0].StartSector);
        Assert.Equal<ulong>(200, parts[0].SectorCount);
        Assert.Equal<byte>(0x0B, parts[1].SystemId);
        Assert.Equal<ulong>(1000, parts[1].StartSector);
        Assert.Equal<ulong>(500, parts[1].SectorCount);
    }

    private static void TestPartition_GPTRoundTrip()
    {
        Gpt.Create(s_dev!);
        Assert.True(Gpt.IsGpt(s_dev));

        const ulong startA = 2048;
        const ulong countA = 4096;
        const ulong startB = startA + countA;
        const ulong countB = 8192;

        Assert.True(Gpt.AddPartition(s_dev, startA, countA, Gpt.BasicDataPartitionType));
        Assert.True(Gpt.AddPartition(s_dev, startB, countB, Gpt.BasicDataPartitionType));

        List<Gpt.PartitionEntry> parts = Gpt.Parse(s_dev);
        Assert.Equal(2, parts.Count);
        Assert.Equal(Gpt.BasicDataPartitionType, parts[0].PartitionType);
        Assert.Equal<ulong>(startA, parts[0].StartSector);
        Assert.Equal<ulong>(countA, parts[0].SectorCount);
        Assert.Equal<ulong>(startB, parts[1].StartSector);
        Assert.Equal<ulong>(countB, parts[1].SectorCount);
    }

    // Layout left by Partition_GPTRoundTrip: GPT with two partitions on s_dev.
    // Run order matters — depends on the previous test having succeeded.
    private static void TestPartition_RescanPartitions()
    {
        StorageManager.RescanPartitions(s_dev!);

        int matches = 0;
        for (int i = 0; i < StorageManager.Partitions.Count; i++)
        {
            Partition p = StorageManager.Partitions[i];
            if (ReferenceEquals(p.Host, s_dev))
            {
                matches++;
            }
        }
        Assert.Equal(2, matches);
    }

    // Attach a partition starting at an arbitrary LBA, write to its LBA 0,
    // and verify the bytes show up at the host's StartSector — proves
    // the translation isn't off by one.
    private static void TestPartition_ReadWriteTranslatesLba()
    {
        const ulong startSector = 3000;
        const ulong sectorCount = 4;
        Partition partition = new(s_dev!, startSector, sectorCount, "test-part");

        Span<byte> writeBuf = new byte[s_dev!.BlockSize];
        for (int i = 0; i < writeBuf.Length; i++)
        {
            writeBuf[i] = (byte)(i ^ 0x42);
        }
        partition.WriteBlock(0, 1, writeBuf);

        Span<byte> hostBuf = new byte[s_dev.BlockSize];
        s_dev.ReadBlock(startSector, 1, hostBuf);
        for (int i = 0; i < hostBuf.Length; i++)
        {
            Assert.Equal(writeBuf[i], hostBuf[i]);
        }
    }

    private static void TestPartition_OutOfBoundsThrows()
    {
        Partition partition = new(s_dev!, startSector: 5000, sectorCount: 8, name: "tiny-part");
        Span<byte> buf = new byte[s_dev!.BlockSize];
        try
        {
            partition.ReadBlock(blockNo: 8, blockCount: 1, buf);
            Assert.Fail("Expected ArgumentOutOfRangeException for partition over-read.");
        }
        catch (ArgumentOutOfRangeException)
        {
            // Expected.
        }
    }

    // Regression (proves a real bug; expected to FAIL until Partition.CheckBounds
    // is overflow-safe): CheckBounds uses `blockNo + blockCount > BlockCount`. For a
    // blockNo near ulong.MaxValue the sum wraps below BlockCount, the check passes,
    // and an out-of-bounds request is dispatched to the host instead of throwing.
    // The fix is `blockNo > BlockCount || blockCount > BlockCount - blockNo`.
    private static void TestPartition_BoundsOverflowThrows()
    {
        BoundsProbeDevice probe = new();
        Partition partition = new(probe, startSector: 0, sectorCount: 4, name: "overflow-probe");
        Span<byte> buf = new byte[probe.BlockSize];
        try
        {
            // ulong.MaxValue + 1 wraps to 0, which is <= BlockCount (4): the current
            // check passes and the read reaches the host. It must throw instead.
            partition.ReadBlock(blockNo: ulong.MaxValue, blockCount: 1, buf);
            Assert.Fail("CheckBounds let an overflowing out-of-bounds read through (expected ArgumentOutOfRangeException).");
        }
        catch (ArgumentOutOfRangeException)
        {
            // Correct behavior once the overflow-safe check lands.
        }
    }

    // In-memory host for the bounds-overflow test: performs no I/O. If Partition's
    // bounds check wrongly lets a request through, the call lands here as a no-op
    // rather than issuing a wild DMA against a real disk.
    private sealed class BoundsProbeDevice : BlockDevice
    {
        public BoundsProbeDevice()
        {
            BlockSize = 512;
            BlockCount = 1024;
        }

        public override string Name => "bounds-probe";

        public override void ReadBlock(ulong blockNo, ulong blockCount, Span<byte> data)
        {
        }

        public override void WriteBlock(ulong blockNo, ulong blockCount, Span<byte> data)
        {
        }
    }
}
