// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.X64.Pci.Enums;
using Cosmos.Kernel.HAL.Devices.SharedMemory;

namespace Cosmos.Kernel.HAL.X64.Pci;

/// <summary>
/// QEMU ivshmem-plain device driver.
/// Provides zero-pause bidirectional memory sharing between kernel and host.
/// BAR2 contains the shared memory region.
/// </summary>
public unsafe class IvshmemDevice : SharedMemoryDevice
{
    private readonly PciDevice _pciDevice;
    private void* _sharedMemory;
    private uint _sharedMemorySize;

    private IvshmemDevice(PciDevice pciDevice)
    {
        _pciDevice = pciDevice;
        _sharedMemory = null;
        _sharedMemorySize = 0;

        Serial.WriteString("[IvshmemDevice] Found device at ");
        Serial.WriteNumber(pciDevice.Bus);
        Serial.WriteString(":");
        Serial.WriteNumber(pciDevice.Slot);
        Serial.WriteString(":");
        Serial.WriteNumber(pciDevice.Function);
        Serial.WriteString("\n");
    }

    /// <summary>
    /// Initialize the ivshmem device and map shared memory.
    /// </summary>
    private bool Initialize()
    {
        // Enable memory and bus master
        _pciDevice.EnableMemory(true);
        _pciDevice.EnableBusMaster(true);

        // BAR2 contains the shared memory region for ivshmem-plain
        if (_pciDevice.BaseAddressBar == null || _pciDevice.BaseAddressBar.Length < 3)
        {
            Serial.WriteString("[IvshmemDevice] ERROR: BAR array too small\n");
            return false;
        }

        var bar2 = _pciDevice.BaseAddressBar[2];
        if (bar2 == null || bar2.IsIo)
        {
            Serial.WriteString("[IvshmemDevice] ERROR: BAR2 is not memory-mapped\n");
            return false;
        }

        ulong bar2Address = bar2.BaseAddress;

        // Handle 64-bit BAR (BAR2 + BAR3 combined)
        if (bar2.Is64Bit && _pciDevice.BaseAddressBar.Length >= 4)
        {
            var bar3 = _pciDevice.BaseAddressBar[3];
            if (bar3 != null)
            {
                bar2Address |= ((ulong)bar3.BaseAddress) << 32;
            }
        }

        if (bar2Address == 0)
        {
            Serial.WriteString("[IvshmemDevice] ERROR: BAR2 address is zero\n");
            return false;
        }

        // Map the shared memory (direct physical address access)
        _sharedMemory = (void*)bar2Address;

        // For ivshmem-plain, we know the size from QEMU configuration (4KB)
        // In a real implementation, you'd read the size from PCI config space
        _sharedMemorySize = 4096;

        Serial.WriteString("[IvshmemDevice] Shared memory at 0x");
        Serial.WriteNumber(bar2Address, true);
        Serial.WriteString(" size=");
        Serial.WriteNumber(_sharedMemorySize);
        Serial.WriteString("\n");

        // Register this device as the active shared memory device
        Register();

        return true;
    }

    /// <summary>
    /// Get the base address of the shared memory region.
    /// </summary>
    public override nint GetSharedMemory() => (nint)_sharedMemory;

    /// <summary>
    /// Get the size of the shared memory region.
    /// </summary>
    public override uint GetSharedMemorySize() => _sharedMemorySize;

    /// <summary>
    /// Scan PCI bus for ivshmem-plain device and initialize it.
    /// </summary>
    public static IvshmemDevice? FindAndInitialize()
    {
        Serial.WriteString("[IvshmemDevice] Scanning for device...\n");

        // Look for RedHat vendor (0x1AF4) and ivshmem device (0x1110)
        var device = PciManager.GetDevice(VendorId.RedHat, DeviceId.IvshmemPlain);
        if (device == null)
        {
            Serial.WriteString("[IvshmemDevice] Not found\n");
            return null;
        }

        var ivshmem = new IvshmemDevice(device);
        if (!ivshmem.Initialize())
        {
            Serial.WriteString("[IvshmemDevice] Failed to initialize\n");
            return null;
        }

        Serial.WriteString("[IvshmemDevice] Initialized successfully\n");
        return ivshmem;
    }
}
