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

        TR.Start("Storage Block Device Tests", expectedTests: 9);

        TR.Run("Test_StorageManager_Initialized", () =>
        {
            Assert.True(StorageManager.IsEnabled);
            Assert.True(StorageManager.IsInitialized);
        });

        TR.Run("Test_PrimaryDevice_Present", () =>
        {
            Assert.True(StorageManager.DeviceCount >= 1);
            Assert.NotNull(StorageManager.PrimaryDevice);
        });

        TR.Run("Test_BlockGeometry_Sane", () =>
        {
            IBlockDevice dev = StorageManager.PrimaryDevice!;
            Assert.Equal<ulong>(512, dev.BlockSize);
            Assert.True(dev.BlockCount > 0);
        });

        TR.Run("Test_WriteRead_SingleBlock", () =>
        {
            IBlockDevice dev = StorageManager.PrimaryDevice!;
            const ulong lba = 100;
            ulong size = dev.BlockSize;

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

        TR.Run("Test_WriteRead_MultiBlock", () =>
        {
            IBlockDevice dev = StorageManager.PrimaryDevice!;
            const ulong lba = 200;
            const ulong blocks = 4;
            ulong total = blocks * dev.BlockSize;

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

        TR.Run("Test_WriteRead_Idempotent", () =>
        {
            IBlockDevice dev = StorageManager.PrimaryDevice!;
            const ulong lba = 250;
            ulong size = dev.BlockSize;

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

        TR.Run("Test_ReReadStable", () =>
        {
            IBlockDevice dev = StorageManager.PrimaryDevice!;
            const ulong lba = 300;
            ulong size = dev.BlockSize;

            Span<byte> first = new byte[size];
            dev.ReadBlock(lba, 1, first);

            Span<byte> second = new byte[size];
            dev.ReadBlock(lba, 1, second);

            for (int i = 0; i < (int)size; i++)
            {
                Assert.Equal(first[i], second[i]);
            }
        });

        TR.Run("Test_LargeTransfer", () =>
        {
            IBlockDevice dev = StorageManager.PrimaryDevice!;
            const ulong lba = 1000;
            const ulong blocks = 32;
            ulong total = blocks * dev.BlockSize;

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

        TR.Run("Test_BoundaryLBA", () =>
        {
            IBlockDevice dev = StorageManager.PrimaryDevice!;
            ulong lba = dev.BlockCount - 1;
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
        Cosmos.Kernel.System.Power.Halt();
    }
}
