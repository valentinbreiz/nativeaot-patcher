// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.Logging;
using Cosmos.Kernel.HAL.Pci.Enums;

namespace Cosmos.Kernel.HAL.Pci;

[Logger]
public partial class PciManager
{
    public static PciDevice[]? Devices;

    public static uint Count = 0;

    public static void Setup()
    {
        Log.Debug("Setup");
        Log.Debug("Clearing device list");
        Devices = new PciDevice[64];
        Log.Debug("Cleared device list");
        if ((PciDevice.GetHeaderType(0x0, 0x0, 0x0) & 0x80) == 0)
        {
            CheckBus(0x0);
        }
        else
        {
            for (ushort fn = 0; fn < 8; fn++)
            {
                Log.Debug("Setup fn=" + fn.ToString());
                if (PciDevice.GetVendorId(0x0, 0x0, fn) != 0xFFFF)
                {
                    break;
                }

                CheckBus(fn);
            }
        }

        for (int i = 0; i < Count; i++)
        {
            PciDevice device = Devices[i];
            Log.Info("Found - " + device.GetDeviceString() + " --- " + device.GetTypeString());
        }

        Log.Info("Scan complete");
    }

    /// <summary>
    /// Check bus.
    /// </summary>
    /// <param name="xBus">A bus to check.</param>
    private static void CheckBus(ushort xBus)
    {
        Log.Debug("CheckBus(" + xBus.ToString() + ")");
        for (ushort device = 0; device < 32; device++)
        {
            ushort vendorId = PciDevice.GetVendorId(xBus, device, 0x0);
            Log.Debug("CheckBus - device=" + device.ToString() + " VID=" + vendorId.ToString());
            if (vendorId == 0xFFFF)
            {
                continue;
            }

            CheckFunction(new PciDevice(xBus, device, 0x0));
            if ((PciDevice.GetHeaderType(xBus, device, 0x0) & 0x80) != 0)
            {
                for (ushort fn = 1; fn < 8; fn++)
                {
                    if (PciDevice.GetVendorId(xBus, device, fn) != 0xFFFF)
                    {
                        CheckFunction(new PciDevice(xBus, device, fn));
                    }
                }
            }
        }
    }

    private static void CheckFunction(PciDevice xPCIDevice)
    {
        Log.Debug("CheckFunction - " + xPCIDevice.GetDeviceString() + " --- " + xPCIDevice.GetTypeString());
        Add(xPCIDevice);
        Log.Debug("Cached");
        if (xPCIDevice.ClassCode == 0x6 && xPCIDevice.Subclass == 0x4)
        {
            CheckBus(xPCIDevice.SecondaryBusNumber);
        }
    }

    private static void Add(PciDevice xPciDevice)
    {
        if (Count >= Devices.Length)
        {
            Log.Error("Device array full, cannot add more devices");
            return;
        }
        Devices[Count] = xPciDevice;
        Count++;
    }

    public static bool Exists(PciDevice pciDevice) =>
        GetDevice((VendorId)pciDevice.VendorId, (DeviceId)pciDevice.DeviceId) != null;

    public static bool Exists(VendorId aVendorID, DeviceId aDeviceID) => GetDevice(aVendorID, aDeviceID) != null;

    /// <summary>
    /// Get device.
    /// </summary>
    /// <param name="aVendorID">A vendor ID.</param>
    /// <param name="aDeviceID">A device ID.</param>
    /// <returns></returns>
    public static PciDevice? GetDevice(VendorId aVendorID, DeviceId aDeviceID)
    {
        for (uint i = 0; i < Count; i++)
        {
            PciDevice xDevice = Devices[i];
            if ((VendorId)xDevice.VendorId == aVendorID &&
                (DeviceId)xDevice.DeviceId == aDeviceID)
            {
                return xDevice;
            }
        }

        return null;
    }

    /// <summary>
    /// Get device.
    /// </summary>
    /// <param name="bus">Bus ID.</param>
    /// <param name="slot">Slot position ID.</param>
    /// <param name="function">Function ID.</param>
    /// <returns></returns>
    public static PciDevice? GetDevice(uint bus, uint slot, uint function)
    {
        for (uint i = 0; i < Count; i++)
        {
            PciDevice xDevice = Devices[i];
            if (xDevice.Bus == bus &&
                xDevice.Slot == slot &&
                xDevice.Function == function)
            {
                return xDevice;
            }
        }

        return null;
    }

    public static PciDevice? GetDeviceClass(ClassId Class, SubclassId SubClass)
    {
        for (uint i = 0; i < Count; i++)
        {
            PciDevice xDevice = Devices[i];
            if ((ClassId)xDevice.ClassCode == Class &&
                (SubclassId)xDevice.Subclass == SubClass)
            {
                return xDevice;
            }
        }

        return null;
    }

    public static PciDevice? GetDeviceClass(ClassId aClass, SubclassId aSubClass, ProgramIf aProgIF)
    {
        for (uint i = 0; i < Count; i++)
        {
            PciDevice xDevice = Devices[i];
            if ((ClassId)xDevice.ClassCode == aClass &&
                (SubclassId)xDevice.Subclass == aSubClass &&
                (ProgramIf)xDevice.ProgIf == aProgIF)
            {
                return xDevice;
            }
        }

        return null;
    }
}
