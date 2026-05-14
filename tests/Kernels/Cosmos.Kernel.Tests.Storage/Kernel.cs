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

        // The engine attaches exactly one disk per profile (tests/profiles.json)
        // and runs the suite once per profile. Per-device tests are gated on
        // dev != null via TR.RunIf, so a profile whose driver did not bind
        // (e.g. AHCI on arm64 today) produces visible Skips instead of either
        // vanished tests or noisy failures unrelated to the change under test.
        TR.Start("Storage Block Device Tests", expectedTests: 0);

        TR.Run("Test_StorageManager_Initialized", () =>
        {
            Assert.True(StorageManager.IsEnabled);
            Assert.True(StorageManager.IsInitialized);
        });

        bool hasDevice = StorageManager.DeviceCount > 0;
        IBlockDevice? dev = hasDevice ? StorageManager.GetDevice(0) : null;

        TR.RunIf(hasDevice, "Test_ExactlyOneDevice_Present",
            () => Assert.Equal(1, StorageManager.DeviceCount),
            "no block device bound for this profile");

        // Label reflects what actually bound; falls back to a generic name when
        // nothing did (so the per-device tests below register under a
        // predictable name regardless of profile).
        string label = dev?.Name switch
        {
            "SATA" => "AHCI",
            "NVMe" => "NVMe",
            null => "BlockDevice",
            _ => dev.Name
        };

        RunDeviceSuite(label, dev);

        // Partition table tests run last because they overwrite LBA 0..33.
        RunPartitionTableSuite(dev);

        TR.Finish();

        Serial.WriteString("\n[Tests Complete - System Halting]\n");
    }

    private static void RunDeviceSuite(string label, IBlockDevice? dev)
    {
        // Each per-device test is gated on `dev != null` so a profile whose
        // driver did not bind (e.g. AHCI on arm64) shows the suite as Skipped
        // instead of either failing or vanishing. The body still receives a
        // dev reference; `dev!` is safe because the gate prevents the lambda
        // from running when dev is null.
        bool present = dev != null;
        const string skip = "no block device bound for this profile";
        void Run(string name, Action body) => TR.RunIf(present, name, body, skip);

        Run($"Test_{label}_BlockGeometry_Sane", () =>
        {
            Assert.Equal<ulong>(512, dev!.BlockSize);
            Assert.True(dev.BlockCount > 0);
        });

        Run($"Test_{label}_WriteRead_SingleBlock", () =>
        {
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

        Run($"Test_{label}_WriteRead_MultiBlock", () =>
        {
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

        Run($"Test_{label}_WriteRead_Idempotent", () =>
        {
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

        Run($"Test_{label}_ReReadStable", () =>
        {
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

        Run($"Test_{label}_LargeTransfer", () =>
        {
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

        Run($"Test_{label}_BoundaryLBA", () =>
        {
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
        Run($"Test_{label}_LBA_Zero_RoundTrip", () =>
        {
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
        Run($"Test_{label}_CrossBlock_Isolation", () =>
        {
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
        Run($"Test_{label}_LBA_Stride_Sweep", () =>
        {
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
        Run($"Test_{label}_RandomOrder_ReadAfterWrite", () =>
        {
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
        Run($"Test_{label}_Multiblock_TailBoundary", () =>
        {
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

    private static void RunPartitionTableSuite(IBlockDevice? host)
    {
        // Same gating story as RunDeviceSuite: when no device bound, every
        // partition-table test reports as Skipped.
        bool present = host != null;
        const string skip = "no block device bound for partition-table tests";
        void Run(string name, Action body) => TR.RunIf(present, name, body, skip);

        Run("Test_MBR_RoundTrip", () =>
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

        Run("Test_GPT_RoundTrip", () =>
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

        Run("Test_StorageManager_RescanPartitions", () =>
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

        Run("Test_Partition_ReadWrite_TranslatesLba", () =>
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

        Run("Test_Partition_OutOfBounds_Throws", () =>
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
