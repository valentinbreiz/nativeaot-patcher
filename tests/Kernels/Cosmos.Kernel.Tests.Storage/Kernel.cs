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

        // Test count is dynamic: 2 meta tests + 12 per controller that bound.
        // x64 (q35) gets both AHCI + NVMe = 26; ARM64 (virt) runs whichever
        // controllers QEMU exposes. Pass 0 so the framework counts on the fly.
        TR.Start("Storage Block Device Tests", expectedTests: 0);

        TR.Run("Test_StorageManager_Initialized", () =>
        {
            Assert.True(StorageManager.IsEnabled);
            Assert.True(StorageManager.IsInitialized);
        });

        IBlockDevice? sata = FindDeviceByName("SATA");
        IBlockDevice? nvme = FindDeviceByName("NVMe");

        TR.Run("Test_AtLeastOneController_Present", () =>
        {
            Assert.True(sata != null || nvme != null);
        });

        if (sata != null)
        {
            RunDeviceSuite("SATA", sata);
        }
        if (nvme != null)
        {
            RunDeviceSuite("NVMe", nvme);
        }

        TR.Finish();

        Serial.WriteString("\n[Tests Complete - System Halting]\n");
    }

    private static IBlockDevice? FindDeviceByName(string name)
    {
        for (int i = 0; i < StorageManager.DeviceCount; i++)
        {
            IBlockDevice? dev = StorageManager.GetDevice(i);
            if (dev != null && dev.Name == name)
            {
                return dev;
            }
        }
        return null;
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
