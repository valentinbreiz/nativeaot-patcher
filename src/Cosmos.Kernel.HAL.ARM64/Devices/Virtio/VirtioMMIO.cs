// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;

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
    /// Converts a kernel virtual address (HHDM) to a guest physical address for DMA.
    /// Virtio devices perform DMA using guest physical addresses, not kernel virtual addresses.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe ulong VirtToPhys(ulong virtAddr)
    {
        ulong hhdmOffset = Limine.HHDM.Response != null ? Limine.HHDM.Response->Offset : 0;
        if (hhdmOffset != 0 && virtAddr >= hhdmOffset)
        {
            return virtAddr - hhdmOffset;
        }

        return virtAddr;
    }

    /// <summary>
    /// Converts a physical MMIO address to the HHDM virtual address (TTBR1).
    /// DeviceMapper maps MMIO regions as Device memory in TTBR1, so all register
    /// accesses must go through the HHDM address to hit Device-nGnRnE attributes.
    /// Accessing via raw physical addresses uses TTBR0 (identity mapping) which
    /// has Normal WB (cacheable) attributes — writes get cached and never reach hardware.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe ulong PhysToVirt(ulong phys)
    {
        ulong hhdmOffset = Limine.HHDM.Response != null ? Limine.HHDM.Response->Offset : 0;
        if (hhdmOffset != 0 && phys < hhdmOffset)
        {
            return phys + hhdmOffset;
        }

        return phys;
    }

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
            Serial.Write("[VirtioMMIO] Slot ");
            Serial.WriteNumber(i);
            Serial.Write(": base=0x");
            Serial.WriteHex(baseAddr);
            Serial.Write(", magic=0x");
            Serial.WriteHex(magic);

            if (magic != VIRTIO_MAGIC)
            {
                Serial.Write(" (not virtio)\n");
                continue;
            }

            uint version = Read32(baseAddr, REG_VERSION);
            uint deviceId = Read32(baseAddr, REG_DEVICE_ID);
            uint vendorId = Read32(baseAddr, REG_VENDOR_ID);

            Serial.Write(", deviceId=");
            Serial.WriteNumber(deviceId);
            Serial.Write(", vendorId=0x");
            Serial.WriteHex(vendorId);
            Serial.Write(", version=");
            Serial.WriteNumber(version);

            if (deviceId == 0)
            {
                Serial.Write(" (empty)\n");
                continue;  // No device at this slot
            }

            Serial.Write(" (type: ");
            WriteDeviceTypeName(deviceId);
            Serial.Write(") IRQ=");
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
            {
                continue;
            }

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

    internal static void WriteDeviceTypeName(uint deviceId)
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
        return Native.MMIO.Read32(PhysToVirt(baseAddr + offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write32(ulong baseAddr, uint offset, uint value)
    {
        Native.MMIO.Write32(PhysToVirt(baseAddr + offset), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Read16(ulong baseAddr, uint offset)
    {
        return Native.MMIO.Read16(PhysToVirt(baseAddr + offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write16(ulong baseAddr, uint offset, ushort value)
    {
        Native.MMIO.Write16(PhysToVirt(baseAddr + offset), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Read8(ulong baseAddr, uint offset)
    {
        return Native.MMIO.Read8(PhysToVirt(baseAddr + offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write8(ulong baseAddr, uint offset, byte value)
    {
        Native.MMIO.Write8(PhysToVirt(baseAddr + offset), value);
    }
}
