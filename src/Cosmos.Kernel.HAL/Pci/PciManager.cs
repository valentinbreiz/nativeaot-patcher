// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Pci.Enums;

namespace Cosmos.Kernel.HAL.Pci;

public class PciManager
{
    public static List<PciDevice> Devices = null!;

    public static uint Count => (uint)Devices.Count;

    public static void Setup()
    {
        Devices = new List<PciDevice>();
        if ((PciDevice.GetHeaderType(0x0, 0x0, 0x0) & 0x80) == 0)
        {
            CheckBus(0x0);
        }
        else
        {
            for (ushort fn = 0; fn < 8; fn++)
            {
                if (PciDevice.GetVendorId(0x0, 0x0, fn) != 0xFFFF)
                {
                    break;
                }

                CheckBus(fn);
            }
        }
    }

    /// <summary>
    /// Check bus.
    /// </summary>
    /// <param name="xBus">A bus to check.</param>
    private static void CheckBus(ushort xBus)
    {
        for (ushort device = 0; device < 32; device++)
        {
            if (PciDevice.GetVendorId(xBus, device, 0x0) == 0xFFFF)
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
        Devices.Add(xPCIDevice);

        if (xPCIDevice.ClassCode == 0x6 && xPCIDevice.Subclass == 0x4)
        {
            CheckBus(xPCIDevice.SecondaryBusNumber);
        }
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
        foreach (PciDevice xDevice in Devices)
        {
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
        foreach (PciDevice xDevice in Devices)
        {
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
        foreach (PciDevice xDevice in Devices)
        {
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
        foreach (PciDevice xDevice in Devices)
        {
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
