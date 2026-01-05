// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core;

namespace Cosmos.Kernel.HAL.ARM64.Devices.Virtio;

/// <summary>
/// Virtio MMIO transport layer for ARM64.
/// QEMU virt machine provides virtio devices via MMIO starting at 0x0a000000.
/// </summary>
public static class VirtioMMIO
{
    // QEMU virt virtio MMIO base address
    public const ulong VIRTIO_MMIO_BASE = 0x0a000000;
    public const ulong VIRTIO_MMIO_SIZE = 0x200;
    public const int VIRTIO_MMIO_MAX_DEVICES = 32;

    // Virtio MMIO magic value ("virt" in little endian)
    public const uint VIRTIO_MAGIC = 0x74726976;

    // Virtio device types
    public const uint VIRTIO_DEV_NET = 1;
    public const uint VIRTIO_DEV_BLOCK = 2;
    public const uint VIRTIO_DEV_CONSOLE = 3;
    public const uint VIRTIO_DEV_RNG = 4;
    public const uint VIRTIO_DEV_GPU = 16;
    public const uint VIRTIO_DEV_INPUT = 18;

    // Virtio MMIO register offsets
    public const uint REG_MAGIC = 0x00;
    public const uint REG_VERSION = 0x04;
    public const uint REG_DEVICE_ID = 0x08;
    public const uint REG_VENDOR_ID = 0x0c;
    public const uint REG_DEVICE_FEATURES = 0x10;
    public const uint REG_DEVICE_FEATURES_SEL = 0x14;
    public const uint REG_DRIVER_FEATURES = 0x20;
    public const uint REG_DRIVER_FEATURES_SEL = 0x24;
    public const uint REG_QUEUE_SEL = 0x30;
    public const uint REG_QUEUE_NUM_MAX = 0x34;
    public const uint REG_QUEUE_NUM = 0x38;
    public const uint REG_QUEUE_ALIGN = 0x3c;     // Legacy: queue alignment (usually 4096)
    public const uint REG_QUEUE_PFN = 0x40;       // Legacy: queue page frame number
    public const uint REG_QUEUE_READY = 0x44;
    public const uint REG_QUEUE_NOTIFY = 0x50;
    public const uint REG_GUEST_PAGE_SIZE = 0x28; // Legacy: must set before using PFN
    public const uint REG_INTERRUPT_STATUS = 0x60;
    public const uint REG_INTERRUPT_ACK = 0x64;
    public const uint REG_STATUS = 0x70;
    public const uint REG_QUEUE_DESC_LOW = 0x80;
    public const uint REG_QUEUE_DESC_HIGH = 0x84;
    public const uint REG_QUEUE_DRIVER_LOW = 0x90;
    public const uint REG_QUEUE_DRIVER_HIGH = 0x94;
    public const uint REG_QUEUE_DEVICE_LOW = 0xa0;
    public const uint REG_QUEUE_DEVICE_HIGH = 0xa4;
    public const uint REG_CONFIG_GENERATION = 0xfc;
    public const uint REG_CONFIG = 0x100;

    // Device status bits
    public const uint STATUS_ACKNOWLEDGE = 1;
    public const uint STATUS_DRIVER = 2;
    public const uint STATUS_DRIVER_OK = 4;
    public const uint STATUS_FEATURES_OK = 8;
    public const uint STATUS_FAILED = 128;

    // IRQ base for virtio devices on QEMU virt (SPI 16 = INTID 48)
    public const uint VIRTIO_IRQ_BASE = 48;

    /// <summary>
    /// Scans for virtio devices and returns information about found devices.
    /// </summary>
    public static void ScanDevices()
    {
        Serial.Write("[VirtioMMIO] Scanning for virtio devices...\n");

        for (uint i = 0; i < VIRTIO_MMIO_MAX_DEVICES; i++)
        {
            ulong baseAddr = VIRTIO_MMIO_BASE + (i * VIRTIO_MMIO_SIZE);

            uint magic = Read32(baseAddr, REG_MAGIC);
            if (magic != VIRTIO_MAGIC)
                continue;

            uint version = Read32(baseAddr, REG_VERSION);
            uint deviceId = Read32(baseAddr, REG_DEVICE_ID);
            uint vendorId = Read32(baseAddr, REG_VENDOR_ID);

            if (deviceId == 0)
                continue;  // No device at this slot

            Serial.Write("[VirtioMMIO] Device ");
            Serial.WriteNumber((uint)i);
            Serial.Write(" at 0x");
            Serial.WriteHex(baseAddr);
            Serial.Write(": type=");
            Serial.WriteNumber(deviceId);
            Serial.Write(" (");
            WriteDeviceTypeName(deviceId);
            Serial.Write(") version=");
            Serial.WriteNumber(version);
            Serial.Write(" vendor=0x");
            Serial.WriteHex(vendorId);
            Serial.Write(" IRQ=");
            Serial.WriteNumber(VIRTIO_IRQ_BASE + (uint)i);
            Serial.Write("\n");
        }
    }

    /// <summary>
    /// Finds a virtio device by type and returns its base address.
    /// </summary>
    /// <param name="deviceType">The virtio device type to find.</param>
    /// <param name="baseAddress">The base address of the found device.</param>
    /// <param name="irq">The IRQ number for the found device.</param>
    /// <returns>True if device found, false otherwise.</returns>
    public static bool FindDevice(uint deviceType, out ulong baseAddress, out uint irq)
    {
        for (uint i = 0; i < VIRTIO_MMIO_MAX_DEVICES; i++)
        {
            ulong baseAddr = VIRTIO_MMIO_BASE + (i * VIRTIO_MMIO_SIZE);

            uint magic = Read32(baseAddr, REG_MAGIC);
            if (magic != VIRTIO_MAGIC)
                continue;

            uint devId = Read32(baseAddr, REG_DEVICE_ID);
            if (devId == deviceType)
            {
                baseAddress = baseAddr;
                irq = VIRTIO_IRQ_BASE + (uint)i;
                return true;
            }
        }

        baseAddress = 0;
        irq = 0;
        return false;
    }

    private static void WriteDeviceTypeName(uint deviceId)
    {
        switch (deviceId)
        {
            case VIRTIO_DEV_NET: Serial.Write("network"); break;
            case VIRTIO_DEV_BLOCK: Serial.Write("block"); break;
            case VIRTIO_DEV_CONSOLE: Serial.Write("console"); break;
            case VIRTIO_DEV_RNG: Serial.Write("rng"); break;
            case VIRTIO_DEV_GPU: Serial.Write("gpu"); break;
            case VIRTIO_DEV_INPUT: Serial.Write("input"); break;
            default: Serial.Write("unknown"); break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Read32(ulong baseAddr, uint offset)
    {
        return Native.MMIO.Read32(baseAddr + offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write32(ulong baseAddr, uint offset, uint value)
    {
        Native.MMIO.Write32(baseAddr + offset, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Read8(ulong baseAddr, uint offset)
    {
        return Native.MMIO.Read8(baseAddr + offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write8(ulong baseAddr, uint offset, byte value)
    {
        Native.MMIO.Write8(baseAddr + offset, value);
    }
}
