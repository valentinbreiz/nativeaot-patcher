using System;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.HAL.Pci;
using Cosmos.Kernel.HAL.Pci.Enums;
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

        // 3 manager + 1 boot-scan + 2 profile + 13 device + 9 partition
        // + 2 mmio/pci + 1 boot-reboot = 31 tests per profile.
        TR.Start("Storage Block Device Tests", expectedTests: 31);

        bool hasDevice = StorageManager.DeviceCount > 0;
        s_dev = hasDevice ? StorageManager.GetDevice(0) : null;

        // ==================== Manager ====================
        TR.Run("Manager_StorageInitialized", TestManager_StorageInitialized);
        // A cell that attached a disk must SEE a disk: on x64 PCI enumerates
        // with or without ACPI, so zero devices is always a bind regression
        // and must fail, not skip; on arm64, acpi-off removes PCIe discovery,
        // so only those cells may legitimately come up empty.
#if ARCH_X64
        bool deviceExpected = true;
#else
        bool deviceExpected = !TR.ProfileContains("acpi-off");
#endif
        TR.RunIf(deviceExpected, "Manager_ExactlyOneDevice", TestManager_ExactlyOneDevice, SkipNoDevice);
        TR.RunIf(hasDevice, "Manager_DuplicateRegistrationIgnored", TestManager_DuplicateRegistrationIgnored, SkipNoDevice);

        // ==================== Boot persistence (works with Boot_RebootAfterGptWrite) ====================
        // Runs before anything mutates partition state: on boot 0 the engine's
        // fresh image must scan clean; on boot 1 the boot-time (phase-3) scan
        // must have found the GPT written before the reboot. Pins the init-window
        // partition scan, which regressed once without CI noticing (interpolated
        // partition names triple-faulted phase 3 on any populated disk).
        TR.RunIf(hasDevice, "Boot_PartitionScanMatchesBootState", TestBoot_PartitionScanMatchesBootState, SkipNoDevice);

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
        TR.RunIf(dev && TR.ProfileHasPrefix("nvme"), "Nvme_ShortSpanWritesDeterministicTail", TestNvme_ShortSpanTail, "NVMe controller API is nvme-profile only");

        // ==================== Partition (MBR/GPT, partition translation) ====================
        // These run last because they overwrite LBA 0..33, which the device
        // round-trip tests above also touch — ordering them last keeps the
        // earlier results from being affected by the partition-table writes.
        TR.RunIf(dev, "Partition_MBR_RoundTrip",          TestPartition_MBRRoundTrip,          SkipNoHost);
        TR.RunIf(dev, "Partition_GPT_RoundTrip",          TestPartition_GPTRoundTrip,          SkipNoHost);
        TR.RunIf(dev, "Partition_RescanPartitions",       TestPartition_RescanPartitions,      SkipNoHost);
        TR.RunIf(dev, "Partition_ReadWrite_TranslatesLba", TestPartition_ReadWriteTranslatesLba, SkipNoHost);
        TR.RunIf(dev, "Partition_OutOfBounds_Throws",      TestPartition_OutOfBoundsThrows,     SkipNoHost);
        TR.RunIf(dev, "Partition_MbrParseRejectsBogus",    TestPartition_MbrParseRejectsBogus,  SkipNoHost);
        TR.RunIf(dev, "Partition_GptAddRejectsBogus",      TestPartition_GptAddRejectsBogus,    SkipNoHost);

        // Overflow-safety of the bounds check is hardware-independent (in-memory
        // probe host), so it runs unconditionally, even on cells where no disk bound.
        TR.Run("Partition_BoundsOverflow_Throws", TestPartition_BoundsOverflowThrows);
        TR.Run("Partition_GptTinyDeviceSafe", TestPartition_GptTinyDeviceSafe);

        // ==================== MMIO mapping (64-bit BAR above 4 GiB) ====================
        // Runs after every cell that does block I/O: it relocates the live
        // NVMe controller's BAR while no transfer is in flight and restores
        // it before returning, so ordering it here keeps a mapper regression
        // (crash mid-cell) from also taking down the I/O results above.
#if ARCH_X64
        TR.RunIf(dev && TR.ProfileHasPrefix("nvme"), "Mmio_HighBar_RemappedOnDemand", TestMmio_HighBarRemapped,
            "64-bit BAR relocation probe is nvme-profile only");
        TR.RunIf(dev && TR.ProfileHasPrefix("nvme"), "Pci_GetBar64_ReadsLiveConfig", TestPciGetBar64ReadsLiveConfig,
            "64-bit BAR relocation probe is nvme-profile only");
#else
        TR.Skip("Mmio_HighBar_RemappedOnDemand", "x64 mapper cell; arm64 installs Device mappings via DeviceMapper");
        TR.Skip("Pci_GetBar64_ReadsLiveConfig", "BAR relocation probe is x64-only (same harness as the mapper cell)");
#endif

        // ==================== Boot persistence (destructive: reboots QEMU) ====================
        // Boot 0 stamps a fresh GPT with one partition and reboots; boot 1's
        // Boot_PartitionScanMatchesBootState (above) then proves the phase-3
        // boot-time scan survives a populated disk and names the partition.
        // Last on purpose: everything before it must have reported already.
        int skip = TR.GetSkipCount();
        if (!dev)
        {
            TR.Skip("Boot_RebootAfterGptWrite", SkipNoDevice);
        }
        else if (skip == 0)
        {
            TR.RunDestructive(
                "Boot_RebootAfterGptWrite",
                () =>
                {
                    Gpt.Create(s_dev!);
                    Assert.True(Gpt.AddPartition(s_dev!, 2048, 4096, Gpt.BasicDataPartitionType));
                    s_dev!.Flush();
                    Sys.Power.Reboot();
                },
                "Power.Reboot() returned without rebooting");
        }
        else
        {
            // Already fired in boot 0; replay as passed like the Power suite.
            TR.Run("Boot_RebootAfterGptWrite", () => { });
        }

        TR.Finish();

        Serial.WriteString("\n[Tests Complete - System Halting]\n");
    }

    protected override void Run() => Stop();

    protected override void AfterRun()
    {
        TR.Complete();
        Cosmos.Kernel.System.Power.Halt();
    }

    // Re-registering an already-known device must be a no-op: RegisterDevice
    // is public and unguarded (unlike Initialize), so a second
    // RegisterHalDevices call would otherwise double-count the device and
    // duplicate every partition under identical names.
    private static void TestManager_DuplicateRegistrationIgnored()
    {
        int before = StorageManager.DeviceCount;
        int partitionsBefore = StorageManager.Partitions.Count;
        StorageManager.RegisterDevice(s_dev!);
        Assert.Equal(before, StorageManager.DeviceCount, "duplicate registration must not add a device");
        Assert.Equal(partitionsBefore, StorageManager.Partitions.Count, "duplicate registration must not duplicate partitions");
    }

    // ==================== Boot persistence ====================

    // Boot 0 boots the engine's fresh blank image; boot 1 boots the GPT
    // written by Boot_RebootAfterGptWrite. Asserting against the boot-time
    // scan result (before any test mutates partition state) pins the
    // phase-3 partition scan path end to end, including partition naming.
    private static void TestBoot_PartitionScanMatchesBootState()
    {
        int partitionsOnDevice = 0;
        Partition? first = null;
        for (int i = 0; i < StorageManager.Partitions.Count; i++)
        {
            Partition p = StorageManager.Partitions[i];
            if (HasOrdinalPrefix(p.Name, s_dev!.Name))
            {
                first ??= p;
                partitionsOnDevice++;
            }
        }

        if (TR.GetSkipCount() == 0)
        {
            Assert.Equal(0, partitionsOnDevice, "blank disk must yield no boot-time partitions");
        }
        else
        {
            Assert.Equal(1, partitionsOnDevice, "boot-time scan must find the GPT partition written before reboot");
            Assert.Equal(s_dev!.Name + "p0", first!.Name, "boot-time partition name");
        }
    }

    // Regression guard: Mbr.Parse must not turn corrupt on-disk entries into
    // live partitions — start 0 aliases the MBR sector itself (formatting
    // that "partition" destroys the table) and past-end ranges authorize
    // wild host I/O. The GPT parser got this hardening; MBR must match.
    private static void TestPartition_MbrParseRejectsBogus()
    {
        Mbr.Create(s_dev!);

        // The writer must reject bogus ranges up front (same rules as the
        // parser): start 0 aliases the MBR, past-end authorizes wild I/O.
        Assert.True(MbrWritePartitionRejects(0, startSector: 0, sectorCount: 200),
            "WritePartition must reject startSector 0");
        Assert.True(MbrWritePartitionRejects(1, startSector: (uint)(s_dev!.BlockCount - 10), sectorCount: 100),
            "WritePartition must reject past-end ranges");

        // The parser is the trust boundary for on-disk corruption, so craft
        // the same bogus entries raw (bypassing the writer's validation).
        int sector = (int)s_dev!.BlockSize;
        byte[] mbr = new byte[sector];
        s_dev!.ReadBlock(0, 1, mbr);
        Span<byte> m = mbr;
        m[446 + 4] = 0x83;
        BitConverter.TryWriteBytes(m.Slice(446 + 8, 4), 0u);
        BitConverter.TryWriteBytes(m.Slice(446 + 12, 4), 200u);
        m[462 + 4] = 0x83;
        BitConverter.TryWriteBytes(m.Slice(462 + 8, 4), (uint)(s_dev!.BlockCount - 10));
        BitConverter.TryWriteBytes(m.Slice(462 + 12, 4), 100u);
        s_dev!.WriteBlock(0, 1, mbr);

        List<Mbr.PartitionEntry> parts = Mbr.Parse(s_dev!);
        Assert.Equal(0, parts.Count, "corrupt MBR entries must be rejected by Parse");
    }

    // One try/catch per method on purpose: mirrors the shape of the other
    // expected-throw cells (e.g. Partition_OutOfBounds_Throws). The arm64 EH
    // dispatch failed to match the catch clause when this cell inlined two
    // try/catch blocks alongside span locals, taking the whole boot down.
    private static bool MbrWritePartitionRejects(int index, uint startSector, uint sectorCount)
    {
        try
        {
            Mbr.WritePartition(s_dev!, index, systemId: 0x83, startSector: startSector, sectorCount: sectorCount);
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }

    // Same distrust for the GPT writer: AddPartition must reject entries its
    // own Parse would silently drop (zero-length) or that point past the disk,
    // instead of returning true for a partition that never materializes.
    private static void TestPartition_GptAddRejectsBogus()
    {
        Gpt.Create(s_dev!);
        Assert.False(Gpt.AddPartition(s_dev!, 2048, 0, Gpt.BasicDataPartitionType), "zero-length entry must be rejected");
        Assert.False(Gpt.AddPartition(s_dev!, s_dev!.BlockCount, 16, Gpt.BasicDataPartitionType), "past-end entry must be rejected");
        Assert.Equal(0, Gpt.Parse(s_dev!).Count, "rejected entries must not appear on disk");

        // Raw-craft an entry starting INSIDE the GPT entry array (LBA 10):
        // a write through such a partition would corrupt the table itself,
        // so Parse must drop it (CRCs are 0 — corruption is undetectable).
        int sector = (int)s_dev!.BlockSize;
        byte[] entries = new byte[sector];
        Span<byte> e = entries;
        Gpt.BasicDataPartitionType.TryWriteBytes(e.Slice(0, 16));
        e[16] = 0x42; // non-zero unique GUID
        BitConverter.TryWriteBytes(e.Slice(32, 8), 10UL);  // startLba inside the array
        BitConverter.TryWriteBytes(e.Slice(40, 8), 100UL); // endLba
        s_dev!.WriteBlock(2, 1, entries);
        Assert.Equal(0, Gpt.Parse(s_dev!).Count, "entry overlapping the GPT structures must be rejected");
    }

    // The NvmeController.Read/Write public API accepts spans shorter than
    // the device transfer; the bounce tail must then be deterministic
    // (zeroed), not the previous command's residue leaking to disk.
    private static void TestNvme_ShortSpanTail()
    {
        NvmeController controller = Nvme.Controllers[0];
        uint nsid = 1;
        ulong lba = 4242;
        int sector = (int)s_dev!.BlockSize;

        byte[] full = new byte[sector];
        for (int i = 0; i < sector; i++)
        {
            full[i] = 0xA5;
        }
        controller.Write(nsid, lba, full, 0);

        byte[] shortSpan = new byte[100];
        for (int i = 0; i < shortSpan.Length; i++)
        {
            shortSpan[i] = 0x5B;
        }
        controller.Write(nsid, lba, shortSpan, 0);

        byte[] readBack = new byte[sector];
        controller.Read(nsid, lba, readBack, 0);
        for (int i = 0; i < shortSpan.Length; i++)
        {
            Assert.Equal((byte)0x5B, readBack[i], "short-span payload");
        }
        for (int i = shortSpan.Length; i < sector; i++)
        {
            Assert.Equal((byte)0, readBack[i], "tail must be zeroed, not stale bounce residue");
        }
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

    // The cell name encodes the controller it attached: ahci => sata*,
    // nvme-* => nvme*. Proves the driver that bound matches the cell's
    // intent. Device names are unique per instance ("sata0", "nvme0n1"),
    // so only the driver prefix is pinned here.
    private static void TestProfile_DeviceKindMatches()
    {
        string expected = TR.ProfileHasPrefix("ahci") ? "sata" : "nvme";
        Assert.True(HasOrdinalPrefix(s_dev!.Name, expected),
            "device name does not match the cell's controller kind");
    }

    // Hand-rolled ordinal prefix check, mirroring TR.ProfileHasPrefix: the
    // kernel runtime does not plug the culture-sensitive string.StartsWith.
    private static bool HasOrdinalPrefix(string value, string prefix)
    {
        if (value.Length < prefix.Length)
        {
            return false;
        }

        for (int i = 0; i < prefix.Length; i++)
        {
            if (value[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
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

    // Regression guard: a naive `blockNo + blockCount > BlockCount` check wraps for
    // a blockNo near ulong.MaxValue and lets an out-of-bounds request through to the
    // host. Partition.CheckBounds ships the overflow-safe form
    // (`blockNo > BlockCount || blockCount > BlockCount - blockNo`); this test pins it.
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
    // Gpt.IsGpt/Parse/AddPartition read LBA 1; per the IBlockDevice contract
    // an out-of-range read throws, so on a degenerate 1-block device these
    // public APIs must return false/empty instead of leaking the throw.
    private static void TestPartition_GptTinyDeviceSafe()
    {
        TinyDevice tiny = new();
        Assert.False(Gpt.IsGpt(tiny), "1-block device cannot carry a GPT");
        Assert.Equal(0, Gpt.Parse(tiny).Count, "1-block device must parse empty");
        Assert.False(Gpt.AddPartition(tiny, 34, 1, Gpt.BasicDataPartitionType), "AddPartition must reject a 1-block device");
    }

#if ARCH_X64
    // Physical address for the relocation probe: above 4 GiB, where Limine's
    // base-revision-0 blanket map (identity + HHDM of the low 4 GiB plus
    // memory-map regions) no longer covers anything, and clear of RAM and of
    // every fixed q35 window (ECAM, LAPIC, IO-APIC all sit below 4 GiB).
    private const ulong HighBarPhys = 0x1_1000_0000;

    private static unsafe ulong HhdmOffset()
        => Limine.HHDM.Response != null ? Limine.HHDM.Response->Offset : 0;

    // Proves 64-bit BAR MMIO stays reachable when the BAR sits above 4 GiB,
    // where firmware on real hardware may place it: the HHDM alias of such a
    // BAR is unmapped until EnsureMmioMapped installs a page-table entry for
    // it. The cell relocates the NVMe controller's own BAR0 up there, mirrors
    // the driver-init access pattern (EnsureMmioMapped + phys-plus-HHDM
    // arithmetic) against the new address, and asserts the VS register reads
    // back identical — then restores the original BAR before returning. With
    // a no-op x64 EnsureMmioMapped this cell dies on an unhandled page fault.
    private static void TestMmio_HighBarRemapped()
    {
        PciDevice? nvmePci = PciManager.GetDeviceClass(ClassId.MassStorageController, SubclassId.NvmController);
        Assert.True(nvmePci != null, "an NVMe PCI function must exist on an nvme profile");

        uint barLow = nvmePci!.ReadRegister32(0x10);
        uint barHigh = nvmePci.ReadRegister32(0x14);
        Assert.True((barLow & 0x7) == 0x4, "NVMe BAR0 must be a 64-bit memory BAR");

        ulong origPhys = ((ulong)barHigh << 32) | (barLow & 0xFFFF_FFF0);
        ulong hhdm = HhdmOffset();
        uint vsOrig = Native.MMIO.Read32(origPhys + hhdm + 0x08);
        Assert.True(vsOrig != 0 && vsOrig != 0xFFFF_FFFF, "NVMe VS must read sane at the original BAR");

        // Quiesce decode while the BAR moves, like firmware would. No block
        // I/O is in flight (every I/O cell ran earlier), so nothing touches
        // the controller through the stale driver mapping meanwhile.
        ushort command = nvmePci.ReadRegister16(0x04);
        nvmePci.WriteRegister16(0x04, (ushort)(command & ~(ushort)PciCommand.Memory));
        nvmePci.WriteRegister32(0x10, (uint)(HighBarPhys & 0xFFFF_FFF0) | (barLow & 0xF));
        nvmePci.WriteRegister32(0x14, (uint)(HighBarPhys >> 32));
        nvmePci.WriteRegister16(0x04, command);

        uint vsHigh;
        try
        {
            PlatformHAL.Initializer?.EnsureMmioMapped(HighBarPhys);
            vsHigh = Native.MMIO.Read32(HighBarPhys + hhdm + 0x08);
        }
        finally
        {
            // Put the BAR back exactly as found so the destructive reboot
            // cell (and boot 1's scan) still see a working controller.
            nvmePci.WriteRegister16(0x04, (ushort)(command & ~(ushort)PciCommand.Memory));
            nvmePci.WriteRegister32(0x10, barLow);
            nvmePci.WriteRegister32(0x14, barHigh);
            nvmePci.WriteRegister16(0x04, command);
        }

        Assert.True(vsHigh == vsOrig, "VS read through the remapped high BAR must match the original");
    }

    // GetBar64Address must read BOTH halves of a 64-bit BAR from live
    // config space: mixing the enumeration-time cached lower half with a
    // live upper half splices two different addresses together the moment
    // a BAR is reprogrammed (exactly what the remap cell above — or any
    // future PCI resource allocator — does). Decode stays disabled for the
    // whole probe window: only config space is touched.
    private static void TestPciGetBar64ReadsLiveConfig()
    {
        PciDevice? nvmePci = PciManager.GetDeviceClass(ClassId.MassStorageController, SubclassId.NvmController);
        Assert.True(nvmePci != null, "an NVMe PCI function must exist on an nvme profile");

        uint barLow = nvmePci!.ReadRegister32(0x10);
        uint barHigh = nvmePci.ReadRegister32(0x14);
        ulong origPhys = ((ulong)barHigh << 32) | (barLow & 0xFFFF_FFF0);
        Assert.True(nvmePci.GetBar64Address(0) == origPhys, "baseline: GetBar64Address must match raw config space");

        ushort command = nvmePci.ReadRegister16(0x04);
        nvmePci.WriteRegister16(0x04, (ushort)(command & ~(ushort)PciCommand.Memory));
        nvmePci.WriteRegister32(0x10, (uint)(HighBarPhys & 0xFFFF_FFF0) | (barLow & 0xF));
        nvmePci.WriteRegister32(0x14, (uint)(HighBarPhys >> 32));

        ulong reported = nvmePci.GetBar64Address(0);

        nvmePci.WriteRegister32(0x10, barLow);
        nvmePci.WriteRegister32(0x14, barHigh);
        nvmePci.WriteRegister16(0x04, command);

        Assert.True(reported == HighBarPhys,
            "GetBar64Address must read both halves live after a BAR reprogram, not splice cached low with live high");
    }
#endif

    // Contract-faithful degenerate device: one 512-byte block, throws on any
    // out-of-range access like real drivers do.
    private sealed class TinyDevice : BlockDevice
    {
        public TinyDevice()
        {
            BlockSize = 512;
            BlockCount = 1;
        }

        public override string Name => "tiny-probe";

        public override void ReadBlock(ulong blockNo, ulong blockCount, Span<byte> data)
        {
            if (blockNo > BlockCount || blockCount > BlockCount - blockNo)
            {
                throw new ArgumentOutOfRangeException(nameof(blockNo));
            }
            data.Clear();
        }

        public override void WriteBlock(ulong blockNo, ulong blockCount, ReadOnlySpan<byte> data)
        {
            if (blockNo > BlockCount || blockCount > BlockCount - blockNo)
            {
                throw new ArgumentOutOfRangeException(nameof(blockNo));
            }
        }
    }

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

        public override void WriteBlock(ulong blockNo, ulong blockCount, ReadOnlySpan<byte> data)
        {
        }
    }
}
