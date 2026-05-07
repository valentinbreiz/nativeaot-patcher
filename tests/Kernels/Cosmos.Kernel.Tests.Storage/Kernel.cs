using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Storage;
using Cosmos.TestRunner.Framework;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;

namespace Cosmos.Kernel.Tests.Storage;

public class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
        Serial.WriteString("[Storage] BeforeRun() reached!\n");
        Serial.WriteString("[Storage] Starting tests...\n");

        // Test count is dynamic: 2 meta tests + 12 per registered block device.
        // The test runner attaches multiple AHCI ports + multiple NVMe
        // controllers, so the suite walks StorageManager and runs the full
        // round-trip suite against every device individually — catches
        // cross-device aliasing, bounce-buffer reuse, and per-controller
        // state that a single-device test would miss.
        TR.Start("Storage Block Device Tests", expectedTests: 0);

        TR.Run("Test_StorageManager_Initialized", () =>
        {
            Assert.True(StorageManager.IsEnabled);
            Assert.True(StorageManager.IsInitialized);
        });

        TR.Run("Test_AtLeastOneDevice_Present", () =>
        {
            Assert.True(StorageManager.DeviceCount > 0);
        });

        int sataIdx = 0;
        int nvmeIdx = 0;
        for (int i = 0; i < StorageManager.DeviceCount; i++)
        {
            IBlockDevice? dev = StorageManager.GetDevice(i);
            if (dev == null)
            {
                continue;
            }
            string label = dev.Name == "SATA"
                ? $"SATA{sataIdx++}"
                : dev.Name == "NVMe"
                    ? $"NVMe{nvmeIdx++}"
                    : $"{dev.Name}{i}";
            RunDeviceSuite(label, dev);
        }

        // Partition table round-trip tests run AFTER the per-device suites
        // because they overwrite LBA 0..33, which the per-device tests
        // also touch. Pick the last registered device so earlier devices'
        // results aren't affected.
        IBlockDevice? partitionTarget = null;
        for (int i = StorageManager.DeviceCount - 1; i >= 0; i--)
        {
            IBlockDevice? dev = StorageManager.GetDevice(i);
            if (dev != null)
            {
                partitionTarget = dev;
                break;
            }
        }
        if (partitionTarget != null)
        {
            RunPartitionTableSuite(partitionTarget);
        }

        TR.Finish();

        Serial.WriteString("\n[Tests Complete - System Halting]\n");
    }

    private static void RunDeviceSuite(string label, IBlockDevice? dev)
    {
        TR.Run($"Test_{label}_BlockGeometry_Sane", () =>
        {
            Assert.NotNull(dev);
            Assert.Equal<ulong>(512, dev!.BlockSize);
            Assert.True(dev.BlockCount > 0);
        });

        TR.Run($"Test_{label}_WriteRead_SingleBlock", () =>
        {
            Assert.NotNull(dev);
            const ulong lba = 100;
            ulong size = dev!.BlockSize;

            Span<byte> writeBuf = new byte[size];
            for (int i = 0; i < (int)size; i++)
            {
                writeBuf[i] = (byte)(i ^ 0xA5);
            }
            dev.WriteBlock(lba, 1, writeBuf);

            Span<byte> readBuf = new byte[size];
            dev.ReadBlock(lba, 1, readBuf);

            for (int i = 0; i < (int)size; i++)
            {
                Assert.Equal(writeBuf[i], readBuf[i]);
            }
        });

        TR.Run($"Test_{label}_WriteRead_MultiBlock", () =>
        {
            Assert.NotNull(dev);
            const ulong lba = 200;
            const ulong blocks = 4;
            ulong total = blocks * dev!.BlockSize;

            Span<byte> writeBuf = new byte[total];
            for (int i = 0; i < (int)total; i++)
            {
                writeBuf[i] = (byte)((i * 7) ^ 0x3C);
            }
            dev.WriteBlock(lba, blocks, writeBuf);

            Span<byte> readBuf = new byte[total];
            dev.ReadBlock(lba, blocks, readBuf);

            for (int i = 0; i < (int)total; i++)
            {
                Assert.Equal(writeBuf[i], readBuf[i]);
            }
        });

        TR.Run($"Test_{label}_WriteRead_Idempotent", () =>
        {
            Assert.NotNull(dev);
            const ulong lba = 250;
            ulong size = dev!.BlockSize;

            Span<byte> first = new byte[size];
            first.Fill(0x11);
            dev.WriteBlock(lba, 1, first);

            Span<byte> second = new byte[size];
            second.Fill(0x22);
            dev.WriteBlock(lba, 1, second);

            Span<byte> readBuf = new byte[size];
            dev.ReadBlock(lba, 1, readBuf);
            for (int i = 0; i < (int)size; i++)
            {
                Assert.Equal((byte)0x22, readBuf[i]);
            }
        });

        TR.Run($"Test_{label}_ReReadStable", () =>
        {
            Assert.NotNull(dev);
            const ulong lba = 300;
            ulong size = dev!.BlockSize;

            Span<byte> first = new byte[size];
            dev.ReadBlock(lba, 1, first);

            Span<byte> second = new byte[size];
            dev.ReadBlock(lba, 1, second);

            for (int i = 0; i < (int)size; i++)
            {
                Assert.Equal(first[i], second[i]);
            }
        });

        TR.Run($"Test_{label}_LargeTransfer", () =>
        {
            Assert.NotNull(dev);
            const ulong lba = 1000;
            const ulong blocks = 32;
            ulong total = blocks * dev!.BlockSize;

            Span<byte> writeBuf = new byte[total];
            for (int i = 0; i < (int)total; i++)
            {
                writeBuf[i] = (byte)((i + (i >> 8)) ^ 0x5A);
            }
            dev.WriteBlock(lba, blocks, writeBuf);

            Span<byte> readBuf = new byte[total];
            dev.ReadBlock(lba, blocks, readBuf);

            for (int i = 0; i < (int)total; i++)
            {
                Assert.Equal(writeBuf[i], readBuf[i]);
            }
        });

        TR.Run($"Test_{label}_BoundaryLBA", () =>
        {
            Assert.NotNull(dev);
            ulong lba = dev!.BlockCount - 1;
            ulong size = dev.BlockSize;

            Span<byte> writeBuf = new byte[size];
            for (int i = 0; i < (int)size; i++)
            {
                writeBuf[i] = (byte)(i ^ 0xF0);
            }
            dev.WriteBlock(lba, 1, writeBuf);

            Span<byte> readBuf = new byte[size];
            dev.ReadBlock(lba, 1, readBuf);

            for (int i = 0; i < (int)size; i++)
            {
                Assert.Equal(writeBuf[i], readBuf[i]);
            }
        });

        // LBA 0 is often special (boot sector). Make sure round-trip works there.
        TR.Run($"Test_{label}_LBA_Zero_RoundTrip", () =>
        {
            Assert.NotNull(dev);
            ulong size = dev!.BlockSize;

            Span<byte> writeBuf = new byte[size];
            for (int i = 0; i < (int)size; i++)
            {
                writeBuf[i] = (byte)(i ^ 0x96);
            }
            dev.WriteBlock(0, 1, writeBuf);

            Span<byte> readBuf = new byte[size];
            dev.ReadBlock(0, 1, readBuf);

            for (int i = 0; i < (int)size; i++)
            {
                Assert.Equal(writeBuf[i], readBuf[i]);
            }
        });

        // Catches bounce-buffer reuse / wrong-LBA-cached bugs: write 8 contiguous
        // blocks each filled with its own marker byte, then read each block back
        // individually and confirm the markers haven't bled across blocks.
        TR.Run($"Test_{label}_CrossBlock_Isolation", () =>
        {
            Assert.NotNull(dev);
            const ulong baseLba = 4000;
            const int blocks = 8;
            ulong size = dev!.BlockSize;

            for (int b = 0; b < blocks; b++)
            {
                Span<byte> wbuf = new byte[size];
                wbuf.Fill((byte)(0xC0 | b));
                dev.WriteBlock(baseLba + (ulong)b, 1, wbuf);
            }

            for (int b = 0; b < blocks; b++)
            {
                Span<byte> rbuf = new byte[size];
                dev.ReadBlock(baseLba + (ulong)b, 1, rbuf);
                byte expected = (byte)(0xC0 | b);
                for (int i = 0; i < (int)size; i++)
                {
                    Assert.Equal(expected, rbuf[i]);
                }
            }
        });

        // Catches LBA off-by-one / stride bugs: write to LBAs that are far apart
        // (n * 1024) so any wrap or shift error lands on a different block; the
        // pattern depends on n so we can detect a mis-routed write.
        TR.Run($"Test_{label}_LBA_Stride_Sweep", () =>
        {
            Assert.NotNull(dev);
            const int slots = 8;
            const ulong stride = 1024;
            ulong size = dev!.BlockSize;

            for (int n = 0; n < slots; n++)
            {
                Span<byte> wbuf = new byte[size];
                byte tag = (byte)((n * 17) ^ 0x5A);
                wbuf.Fill(tag);
                dev.WriteBlock((ulong)n * stride, 1, wbuf);
            }

            for (int n = 0; n < slots; n++)
            {
                Span<byte> rbuf = new byte[size];
                dev.ReadBlock((ulong)n * stride, 1, rbuf);
                byte tag = (byte)((n * 17) ^ 0x5A);
                for (int i = 0; i < (int)size; i++)
                {
                    Assert.Equal(tag, rbuf[i]);
                }
            }
        });

        // Reads in non-sequential order should still return the right data —
        // catches code that assumes the last-touched LBA is "current".
        TR.Run($"Test_{label}_RandomOrder_ReadAfterWrite", () =>
        {
            Assert.NotNull(dev);
            const ulong baseLba = 6000;
            ulong size = dev!.BlockSize;

            Span<byte> a = new byte[size]; a.Fill(0xAA);
            Span<byte> b = new byte[size]; b.Fill(0xBB);
            Span<byte> c = new byte[size]; c.Fill(0xCC);
            dev.WriteBlock(baseLba + 0, 1, a);
            dev.WriteBlock(baseLba + 1, 1, b);
            dev.WriteBlock(baseLba + 2, 1, c);

            Span<byte> r = new byte[size];

            dev.ReadBlock(baseLba + 2, 1, r);
            for (int i = 0; i < (int)size; i++) { Assert.Equal((byte)0xCC, r[i]); }

            dev.ReadBlock(baseLba + 0, 1, r);
            for (int i = 0; i < (int)size; i++) { Assert.Equal((byte)0xAA, r[i]); }

            dev.ReadBlock(baseLba + 1, 1, r);
            for (int i = 0; i < (int)size; i++) { Assert.Equal((byte)0xBB, r[i]); }
        });

        // Multi-block transfer that ends exactly at the device tail. Catches
        // truncation / wrap bugs at the upper edge of LBA space.
        TR.Run($"Test_{label}_Multiblock_TailBoundary", () =>
        {
            Assert.NotNull(dev);
            const ulong blocks = 4;
            ulong size = dev!.BlockSize;
            ulong lba = dev.BlockCount - blocks;
            ulong total = blocks * size;

            Span<byte> writeBuf = new byte[total];
            for (int i = 0; i < (int)total; i++)
            {
                writeBuf[i] = (byte)((i * 13) ^ 0x6E);
            }
            dev.WriteBlock(lba, blocks, writeBuf);

            Span<byte> readBuf = new byte[total];
            dev.ReadBlock(lba, blocks, readBuf);

            for (int i = 0; i < (int)total; i++)
            {
                Assert.Equal(writeBuf[i], readBuf[i]);
            }
        });
    }

    private static void RunPartitionTableSuite(IBlockDevice host)
    {
        TR.Run("Test_MBR_RoundTrip", () =>
        {
            // Wipe LBA 0 first so a leftover GPT signature from a prior
            // sub-test doesn't taint the IsMBR check.
            Span<byte> wipe = new byte[host.BlockSize];
            host.WriteBlock(0, 1, wipe);

            MBR.Create(host);
            Assert.True(MBR.IsMBR(host));

            // Two primary entries at distinct LBA windows.
            MBR.WritePartition(host, 0, systemId: 0x83, startSector: 100, sectorCount: 200);
            MBR.WritePartition(host, 1, systemId: 0x0B, startSector: 1000, sectorCount: 500);

            List<MBR.PartitionEntry> parts = MBR.Parse(host);
            Assert.Equal(2, parts.Count);
            Assert.Equal<byte>(0x83, parts[0].SystemId);
            Assert.Equal<ulong>(100, parts[0].StartSector);
            Assert.Equal<ulong>(200, parts[0].SectorCount);
            Assert.Equal<byte>(0x0B, parts[1].SystemId);
            Assert.Equal<ulong>(1000, parts[1].StartSector);
            Assert.Equal<ulong>(500, parts[1].SectorCount);
        });

        TR.Run("Test_MBR_ResizePartition", () =>
        {
            // Resize slot 0 in place (same systemId, same start, larger size)
            // and verify Parse reports the new geometry.
            MBR.WritePartition(host, 0, systemId: 0x83, startSector: 100, sectorCount: 350);

            List<MBR.PartitionEntry> parts = MBR.Parse(host);
            Assert.Equal(2, parts.Count);
            Assert.Equal<ulong>(100, parts[0].StartSector);
            Assert.Equal<ulong>(350, parts[0].SectorCount);
            Assert.Equal<ulong>(1000, parts[1].StartSector);
            Assert.Equal<ulong>(500, parts[1].SectorCount);
        });

        TR.Run("Test_MBR_DeletePartition", () =>
        {
            // Delete slot 1 by writing systemId=0; Parse skips empty entries.
            MBR.WritePartition(host, 1, systemId: 0x00, startSector: 0, sectorCount: 0);

            List<MBR.PartitionEntry> parts = MBR.Parse(host);
            Assert.Equal(1, parts.Count);
            Assert.Equal<byte>(0x83, parts[0].SystemId);
            Assert.Equal<ulong>(100, parts[0].StartSector);
            Assert.Equal<ulong>(350, parts[0].SectorCount);
        });

        TR.Run("Test_GPT_RoundTrip", () =>
        {
            GPT.Create(host);
            Assert.True(GPT.IsGPT(host));

            const ulong startA = 2048;
            const ulong countA = 4096;
            const ulong startB = startA + countA;
            const ulong countB = 8192;

            Assert.True(GPT.AddPartition(host, startA, countA, GPT.BasicDataPartitionType));
            Assert.True(GPT.AddPartition(host, startB, countB, GPT.BasicDataPartitionType));

            List<GPT.PartitionEntry> parts = GPT.Parse(host);
            Assert.Equal(2, parts.Count);
            Assert.Equal(GPT.BasicDataPartitionType, parts[0].PartitionType);
            Assert.Equal<ulong>(startA, parts[0].StartSector);
            Assert.Equal<ulong>(countA, parts[0].SectorCount);
            Assert.Equal<ulong>(startB, parts[1].StartSector);
            Assert.Equal<ulong>(countB, parts[1].SectorCount);
        });

        TR.Run("Test_StorageManager_RescanPartitions", () =>
        {
            // Layout from the previous test: GPT with two partitions on `host`.
            StorageManager.RescanPartitions(host);

            int matches = 0;
            for (int i = 0; i < StorageManager.Partitions.Count; i++)
            {
                Partition p = StorageManager.Partitions[i];
                if (ReferenceEquals(p.Host, host))
                {
                    matches++;
                }
            }
            Assert.Equal(2, matches);
        });

        TR.Run("Test_Partition_ReadWrite_TranslatesLba", () =>
        {
            // Attach a partition starting at an arbitrary LBA, write to its
            // LBA 0, and verify the bytes show up at the host's
            // StartingSector — proving the translation isn't off by one.
            const ulong startSector = 3000;
            const ulong sectorCount = 4;
            Partition partition = new(host, startSector, sectorCount, "test-part");

            Span<byte> writeBuf = new byte[host.BlockSize];
            for (int i = 0; i < writeBuf.Length; i++)
            {
                writeBuf[i] = (byte)(i ^ 0x42);
            }
            partition.WriteBlock(0, 1, writeBuf);

            Span<byte> hostBuf = new byte[host.BlockSize];
            host.ReadBlock(startSector, 1, hostBuf);
            for (int i = 0; i < hostBuf.Length; i++)
            {
                Assert.Equal(writeBuf[i], hostBuf[i]);
            }
        });

        TR.Run("Test_Partition_OutOfBounds_Throws", () =>
        {
            Partition partition = new(host, startingSector: 5000, sectorCount: 8, name: "tiny-part");
            Span<byte> buf = new byte[host.BlockSize];
            try
            {
                partition.ReadBlock(blockNo: 8, blockCount: 1, buf);
                Assert.Fail("Expected ArgumentOutOfRangeException for partition over-read.");
            }
            catch (ArgumentOutOfRangeException)
            {
                // Expected.
            }
        });

        // EBR-chain tests are last because they overwrite LBA 0 with a fresh
        // MBR layout and exercise StorageManager rescanning, which would
        // perturb earlier GPT-based partition counts.
        const ulong ebrExtendedStart = 4000;
        const uint ebrLogicalRelStart = 32;
        const uint ebrLogicalSectorCount = 1000;
        const uint ebrNextRelativeLba = 2000;

        TR.Run("Test_EBR_Chain_DiscoversLogicals", () =>
        {
            // Wipe LBAs 0..1 to clear both the GPT signature left behind by
            // Test_GPT_RoundTrip and the MBR slot, so the upcoming
            // StorageManager rescan takes the MBR code path.
            Span<byte> wipe = new byte[host.BlockSize];
            host.WriteBlock(0, 1, wipe);
            host.WriteBlock(1, 1, wipe);

            MBR.Create(host);
            // Primary at slot 0, extended at slot 1.
            MBR.WritePartition(host, 0, systemId: 0x83, startSector: 100, sectorCount: 200);
            MBR.WritePartition(host, 1, systemId: 0x05, startSector: (uint)ebrExtendedStart, sectorCount: 8000);

            // Two-link EBR chain: first EBR at extendedStart, second at
            // extendedStart + ebrNextRelativeLba.
            WriteEbrSector(host, ebrExtendedStart, ebrLogicalRelStart, ebrLogicalSectorCount, hasNext: true, nextRelativeLba: ebrNextRelativeLba);
            WriteEbrSector(host, ebrExtendedStart + ebrNextRelativeLba, ebrLogicalRelStart, ebrLogicalSectorCount, hasNext: false, nextRelativeLba: 0);

            List<MBR.PartitionEntry> logicals = EBR.Parse(host, ebrExtendedStart);
            Assert.Equal(2, logicals.Count);
            Assert.Equal<ulong>(ebrExtendedStart + ebrLogicalRelStart, logicals[0].StartSector);
            Assert.Equal<ulong>(ebrLogicalSectorCount, logicals[0].SectorCount);
            Assert.Equal<ulong>(ebrExtendedStart + ebrNextRelativeLba + ebrLogicalRelStart, logicals[1].StartSector);
            Assert.Equal<ulong>(ebrLogicalSectorCount, logicals[1].SectorCount);
        });

        TR.Run("Test_StorageManager_RescanPartitions_WithEBR", () =>
        {
            // Layout left by the previous test: MBR with one primary + one
            // extended pointing at a 2-link EBR chain. Rescan should pick
            // up 1 primary + 2 logicals = 3 partitions on host.
            StorageManager.RescanPartitions(host);

            int matches = 0;
            for (int i = 0; i < StorageManager.Partitions.Count; i++)
            {
                Partition p = StorageManager.Partitions[i];
                if (ReferenceEquals(p.Host, host))
                {
                    matches++;
                }
            }
            Assert.Equal(3, matches);
        });

        // PartitionManager lifecycle tests run on a clean MBR layout; each
        // test wipes and re-creates so order doesn't matter.

        TR.Run("Test_PartitionManager_Create_OnMBR", () =>
        {
            ResetHostMbr(host);
            Assert.True(PartitionManager.Create(host, startSector: 200, sectorCount: 100, mbrSystemId: 0x83, gptType: GPT.BasicDataPartitionType));

            List<MBR.PartitionEntry> parts = MBR.Parse(host);
            Assert.Equal(1, parts.Count);
            Assert.Equal<byte>(0x83, parts[0].SystemId);
            Assert.Equal<ulong>(200, parts[0].StartSector);
            Assert.Equal<ulong>(100, parts[0].SectorCount);
        });

        TR.Run("Test_PartitionManager_Resize_OnMBR", () =>
        {
            ResetHostMbr(host);
            PartitionManager.Create(host, 200, 100, 0x83, GPT.BasicDataPartitionType);

            Assert.True(PartitionManager.Resize(host, new PartitionManager.PartitionLocation(200, 100), newSectorCount: 250));

            List<MBR.PartitionEntry> parts = MBR.Parse(host);
            Assert.Equal(1, parts.Count);
            Assert.Equal<ulong>(200, parts[0].StartSector);
            Assert.Equal<ulong>(250, parts[0].SectorCount);
        });

        TR.Run("Test_PartitionManager_Delete_OnMBR", () =>
        {
            ResetHostMbr(host);
            PartitionManager.Create(host, 200, 100, 0x83, GPT.BasicDataPartitionType);
            PartitionManager.Create(host, 500, 200, 0x0B, GPT.BasicDataPartitionType);

            Assert.True(PartitionManager.Delete(host, new PartitionManager.PartitionLocation(200, 100)));

            List<MBR.PartitionEntry> parts = MBR.Parse(host);
            Assert.Equal(1, parts.Count);
            Assert.Equal<ulong>(500, parts[0].StartSector);
            Assert.Equal<ulong>(200, parts[0].SectorCount);
        });

        TR.Run("Test_PartitionManager_MoveWithData_NonOverlapping", () =>
        {
            ResetHostMbr(host);
            const uint oldStart = 1000;
            const uint count = 64;
            const uint newStart = 5000;
            PartitionManager.Create(host, oldStart, count, 0x83, GPT.BasicDataPartitionType);

            Span<byte> patternSector = new byte[host.BlockSize];
            for (ulong lba = 0; lba < count; lba++)
            {
                FillPattern(patternSector, (uint)(0xC0DE0000 + lba));
                host.WriteBlock(oldStart + lba, 1, patternSector);
            }

            Assert.True(PartitionManager.MoveWithData(host, new PartitionManager.PartitionLocation(oldStart, count), newStart));

            List<MBR.PartitionEntry> parts = MBR.Parse(host);
            Assert.Equal(1, parts.Count);
            Assert.Equal<ulong>(newStart, parts[0].StartSector);
            Assert.Equal<ulong>(count, parts[0].SectorCount);

            Span<byte> readBuf = new byte[host.BlockSize];
            Span<byte> expected = new byte[host.BlockSize];
            for (ulong lba = 0; lba < count; lba++)
            {
                host.ReadBlock(newStart + lba, 1, readBuf);
                FillPattern(expected, (uint)(0xC0DE0000 + lba));
                for (int i = 0; i < (int)host.BlockSize; i++)
                {
                    Assert.Equal(expected[i], readBuf[i]);
                }
            }
        });

        TR.Run("Test_PartitionManager_MoveWithData_OverlappingForward", () =>
        {
            // Destination starts inside the source range, so the copy must
            // walk backwards or it will overwrite source bytes before reading
            // them.
            ResetHostMbr(host);
            const uint oldStart = 2000;
            const uint count = 200;
            const uint newStart = 2050; // overlaps oldStart..oldStart+count
            PartitionManager.Create(host, oldStart, count, 0x83, GPT.BasicDataPartitionType);

            Span<byte> patternSector = new byte[host.BlockSize];
            for (ulong lba = 0; lba < count; lba++)
            {
                FillPattern(patternSector, (uint)(0xBABE0000 + lba));
                host.WriteBlock(oldStart + lba, 1, patternSector);
            }

            Assert.True(PartitionManager.MoveWithData(host, new PartitionManager.PartitionLocation(oldStart, count), newStart));

            Span<byte> readBuf = new byte[host.BlockSize];
            Span<byte> expected = new byte[host.BlockSize];
            for (ulong lba = 0; lba < count; lba++)
            {
                host.ReadBlock(newStart + lba, 1, readBuf);
                FillPattern(expected, (uint)(0xBABE0000 + lba));
                for (int i = 0; i < (int)host.BlockSize; i++)
                {
                    Assert.Equal(expected[i], readBuf[i]);
                }
            }
        });

        TR.Run("Test_PartitionManager_Resize_OnGPT", () =>
        {
            ResetHostGpt(host);
            const ulong start = 2048;
            const ulong count = 4096;
            Assert.True(GPT.AddPartition(host, start, count, GPT.BasicDataPartitionType));

            Assert.True(PartitionManager.Resize(host, new PartitionManager.PartitionLocation(start, count), newSectorCount: 8192));

            List<GPT.PartitionEntry> parts = GPT.Parse(host);
            Assert.Equal(1, parts.Count);
            Assert.Equal<ulong>(start, parts[0].StartSector);
            Assert.Equal<ulong>(8192, parts[0].SectorCount);
        });

        TR.Run("Test_PartitionManager_Delete_OnGPT", () =>
        {
            ResetHostGpt(host);
            const ulong start = 2048;
            const ulong count = 4096;
            Assert.True(GPT.AddPartition(host, start, count, GPT.BasicDataPartitionType));
            Assert.True(GPT.AddPartition(host, start + count, count, GPT.BasicDataPartitionType));

            Assert.True(PartitionManager.Delete(host, new PartitionManager.PartitionLocation(start, count)));

            List<GPT.PartitionEntry> parts = GPT.Parse(host);
            Assert.Equal(1, parts.Count);
            Assert.Equal<ulong>(start + count, parts[0].StartSector);
        });

        TR.Run("Test_PartitionManager_MoveWithData_OnGPT", () =>
        {
            ResetHostGpt(host);
            const ulong start = 2048;
            const ulong count = 64;
            const ulong newStart = 4096;
            Assert.True(GPT.AddPartition(host, start, count, GPT.BasicDataPartitionType));

            Span<byte> patternSector = new byte[host.BlockSize];
            for (ulong lba = 0; lba < count; lba++)
            {
                FillPattern(patternSector, (uint)(0xF00D0000 + lba));
                host.WriteBlock(start + lba, 1, patternSector);
            }

            Assert.True(PartitionManager.MoveWithData(host, new PartitionManager.PartitionLocation(start, count), newStart));

            List<GPT.PartitionEntry> parts = GPT.Parse(host);
            Assert.Equal(1, parts.Count);
            Assert.Equal<ulong>(newStart, parts[0].StartSector);
            Assert.Equal<ulong>(count, parts[0].SectorCount);

            Span<byte> readBuf = new byte[host.BlockSize];
            Span<byte> expected = new byte[host.BlockSize];
            for (ulong lba = 0; lba < count; lba++)
            {
                host.ReadBlock(newStart + lba, 1, readBuf);
                FillPattern(expected, (uint)(0xF00D0000 + lba));
                for (int i = 0; i < (int)host.BlockSize; i++)
                {
                    Assert.Equal(expected[i], readBuf[i]);
                }
            }
        });

        // --- EBR (logical partition) lifecycle -----------------------------
        // Each test starts from a fresh MBR + extended-partition layout.
        const uint extStart = 4000;
        const uint extCount = 12000;

        TR.Run("Test_EBR_AddLogical_FirstAndSecond", () =>
        {
            ResetHostExtendedMbr(host, extStart, extCount);

            ulong logical0Start = EBR.AddLogical(host, extStart, extCount, 0x83, 100);
            Assert.True(logical0Start != 0);

            ulong logical1Start = EBR.AddLogical(host, extStart, extCount, 0x83, 200);
            Assert.True(logical1Start != 0);

            List<MBR.PartitionEntry> logicals = EBR.Parse(host, extStart);
            Assert.Equal(2, logicals.Count);
            Assert.Equal<ulong>(logical0Start, logicals[0].StartSector);
            Assert.Equal<ulong>(100, logicals[0].SectorCount);
            Assert.Equal<ulong>(logical1Start, logicals[1].StartSector);
            Assert.Equal<ulong>(200, logicals[1].SectorCount);
        });

        TR.Run("Test_EBR_RemoveLogical_Last", () =>
        {
            ResetHostExtendedMbr(host, extStart, extCount);
            EBR.AddLogical(host, extStart, extCount, 0x83, 100);
            EBR.AddLogical(host, extStart, extCount, 0x83, 200);

            Assert.True(EBR.RemoveLogical(host, extStart, 1));
            List<MBR.PartitionEntry> logicals = EBR.Parse(host, extStart);
            Assert.Equal(1, logicals.Count);
            Assert.Equal<ulong>(100, logicals[0].SectorCount);
        });

        TR.Run("Test_EBR_RemoveLogical_First_PromotesSuccessor", () =>
        {
            ResetHostExtendedMbr(host, extStart, extCount);
            EBR.AddLogical(host, extStart, extCount, 0x83, 100);
            EBR.AddLogical(host, extStart, extCount, 0x83, 200);

            Assert.True(EBR.RemoveLogical(host, extStart, 0));
            List<MBR.PartitionEntry> logicals = EBR.Parse(host, extStart);
            Assert.Equal(1, logicals.Count);
            Assert.Equal<ulong>(200, logicals[0].SectorCount);
        });

        TR.Run("Test_EBR_RemoveLogical_Middle_CollapsesChain", () =>
        {
            ResetHostExtendedMbr(host, extStart, extCount);
            EBR.AddLogical(host, extStart, extCount, 0x83, 100);
            EBR.AddLogical(host, extStart, extCount, 0x83, 200);
            EBR.AddLogical(host, extStart, extCount, 0x83, 300);

            Assert.True(EBR.RemoveLogical(host, extStart, 1));
            List<MBR.PartitionEntry> logicals = EBR.Parse(host, extStart);
            Assert.Equal(2, logicals.Count);
            Assert.Equal<ulong>(100, logicals[0].SectorCount);
            Assert.Equal<ulong>(300, logicals[1].SectorCount);
        });

        TR.Run("Test_EBR_RemoveLogical_OnlyOne_ClearsChain", () =>
        {
            ResetHostExtendedMbr(host, extStart, extCount);
            EBR.AddLogical(host, extStart, extCount, 0x83, 100);

            Assert.True(EBR.RemoveLogical(host, extStart, 0));
            List<MBR.PartitionEntry> logicals = EBR.Parse(host, extStart);
            Assert.Equal(0, logicals.Count);
        });

        TR.Run("Test_EBR_ResizeLogical", () =>
        {
            ResetHostExtendedMbr(host, extStart, extCount);
            EBR.AddLogical(host, extStart, extCount, 0x83, 100);

            Assert.True(EBR.ResizeLogical(host, extStart, 0, 250));
            List<MBR.PartitionEntry> logicals = EBR.Parse(host, extStart);
            Assert.Equal(1, logicals.Count);
            Assert.Equal<ulong>(250, logicals[0].SectorCount);
        });

        TR.Run("Test_EBR_MoveLogical_TableLevel", () =>
        {
            ResetHostExtendedMbr(host, extStart, extCount);
            ulong logicalStart = EBR.AddLogical(host, extStart, extCount, 0x83, 50);
            // logicalStart should be extStart + 1 (right after the EBR sector).
            Assert.Equal<ulong>(extStart + 1, logicalStart);

            // Move the logical's data range forward inside its EBR's frame.
            ulong newStart = logicalStart + 200;
            Assert.True(EBR.MoveLogical(host, extStart, 0, newStart));

            List<MBR.PartitionEntry> logicals = EBR.Parse(host, extStart);
            Assert.Equal(1, logicals.Count);
            Assert.Equal<ulong>(newStart, logicals[0].StartSector);
            Assert.Equal<ulong>(50, logicals[0].SectorCount);
        });

        TR.Run("Test_PartitionManager_CreateLogical", () =>
        {
            ResetHostExtendedMbr(host, extStart, extCount);

            ulong logical0 = PartitionManager.CreateLogical(host, 0x83, 100);
            Assert.True(logical0 != 0);

            ulong logical1 = PartitionManager.CreateLogical(host, 0x0B, 200);
            Assert.True(logical1 != 0);

            List<MBR.PartitionEntry> logicals = EBR.Parse(host, extStart);
            Assert.Equal(2, logicals.Count);
            Assert.Equal<byte>(0x83, logicals[0].SystemId);
            Assert.Equal<byte>(0x0B, logicals[1].SystemId);
        });

        TR.Run("Test_PartitionManager_Resize_OnLogical", () =>
        {
            ResetHostExtendedMbr(host, extStart, extCount);
            ulong logicalStart = PartitionManager.CreateLogical(host, 0x83, 100);

            Assert.True(PartitionManager.Resize(
                host,
                new PartitionManager.PartitionLocation(logicalStart, 100),
                newSectorCount: 250));

            List<MBR.PartitionEntry> logicals = EBR.Parse(host, extStart);
            Assert.Equal(1, logicals.Count);
            Assert.Equal<ulong>(250, logicals[0].SectorCount);
        });

        TR.Run("Test_PartitionManager_Delete_OnLogical", () =>
        {
            ResetHostExtendedMbr(host, extStart, extCount);
            ulong l0 = PartitionManager.CreateLogical(host, 0x83, 100);
            ulong l1 = PartitionManager.CreateLogical(host, 0x83, 200);

            Assert.True(PartitionManager.Delete(
                host,
                new PartitionManager.PartitionLocation(l0, 100)));

            List<MBR.PartitionEntry> logicals = EBR.Parse(host, extStart);
            Assert.Equal(1, logicals.Count);
            Assert.Equal<ulong>(200, logicals[0].SectorCount);
        });

        TR.Run("Test_PartitionManager_MoveWithData_OnLogical", () =>
        {
            ResetHostExtendedMbr(host, extStart, extCount);
            const uint logicalCount = 32;
            ulong logicalStart = PartitionManager.CreateLogical(host, 0x83, logicalCount);
            Assert.True(logicalStart != 0);

            Span<byte> patternSector = new byte[host.BlockSize];
            for (ulong lba = 0; lba < logicalCount; lba++)
            {
                FillPattern(patternSector, (uint)(0xCAFE0000 + lba));
                host.WriteBlock(logicalStart + lba, 1, patternSector);
            }

            ulong newStart = logicalStart + 500;
            Assert.True(PartitionManager.MoveWithData(
                host,
                new PartitionManager.PartitionLocation(logicalStart, logicalCount),
                newStart));

            List<MBR.PartitionEntry> logicals = EBR.Parse(host, extStart);
            Assert.Equal(1, logicals.Count);
            Assert.Equal<ulong>(newStart, logicals[0].StartSector);
            Assert.Equal<ulong>(logicalCount, logicals[0].SectorCount);

            Span<byte> readBuf = new byte[host.BlockSize];
            Span<byte> expected = new byte[host.BlockSize];
            for (ulong lba = 0; lba < logicalCount; lba++)
            {
                host.ReadBlock(newStart + lba, 1, readBuf);
                FillPattern(expected, (uint)(0xCAFE0000 + lba));
                for (int i = 0; i < (int)host.BlockSize; i++)
                {
                    Assert.Equal(expected[i], readBuf[i]);
                }
            }
        });
    }

    private static void ResetHostExtendedMbr(IBlockDevice host, uint extStart, uint extCount)
    {
        Span<byte> wipe = new byte[host.BlockSize];
        host.WriteBlock(0, 1, wipe);
        host.WriteBlock(1, 1, wipe);
        host.WriteBlock(extStart, 1, wipe);
        MBR.Create(host);
        MBR.WritePartition(host, 0, systemId: 0x05, startSector: extStart, sectorCount: extCount);
    }

    private static void ResetHostMbr(IBlockDevice host)
    {
        Span<byte> wipe = new byte[host.BlockSize];
        host.WriteBlock(0, 1, wipe);
        host.WriteBlock(1, 1, wipe);
        MBR.Create(host);
    }

    private static void ResetHostGpt(IBlockDevice host)
    {
        Span<byte> wipe = new byte[host.BlockSize];
        host.WriteBlock(0, 1, wipe);
        host.WriteBlock(1, 1, wipe);
        GPT.Create(host);
    }

    private static void FillPattern(Span<byte> buffer, uint seed)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (byte)((seed + (uint)i * 31u) & 0xFFu);
        }
    }

    private static void WriteEbrSector(
        IBlockDevice device,
        ulong ebrLba,
        uint relativeStart,
        uint sectorCount,
        bool hasNext,
        uint nextRelativeLba)
    {
        Span<byte> sector = new byte[device.BlockSize];

        // Logical-partition entry at offset 446.
        sector[446 + 4] = 0x83;
        BitConverter.TryWriteBytes(sector.Slice(446 + 8, 4), relativeStart);
        BitConverter.TryWriteBytes(sector.Slice(446 + 12, 4), sectorCount);

        // Next-EBR pointer at offset 462.
        if (hasNext)
        {
            sector[462 + 4] = 0x05;
            BitConverter.TryWriteBytes(sector.Slice(462 + 8, 4), nextRelativeLba);
        }

        sector[510] = 0x55;
        sector[511] = 0xAA;

        device.WriteBlock(ebrLba, 1, sector);
    }

    protected override void Run()
    {
        Stop();
    }

    protected override void AfterRun()
    {
        TR.Complete();
        Cosmos.Kernel.System.Power.Halt();
    }
}
