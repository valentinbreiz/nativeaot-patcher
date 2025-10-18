// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Pci.Enums;
using Cosmos.Kernel.System.IO;

namespace Cosmos.Kernel.HAL.Pci;

public class PciManager
{
    public static PciDevice[] Devices;

    public static uint Count = 0;

    public static void Setup()
    {
        Serial.WriteString("[PciManager] Setup .\n");
        Serial.WriteString("[PciManager] Setup Clearing List.\n");
        Devices = new PciDevice[20];
        Serial.WriteString("[PciManager] Setup Cleared List.\n");
        if ((PciDevice.GetHeaderType(0x0, 0x0, 0x0) & 0x80) == 0)
        {
            CheckBus(0x0);
        }
        else
        {
            for (ushort fn = 0; fn < 8; fn++)
            {
                Serial.WriteString("[PciManager] Setup ");
                Serial.WriteNumber(fn);
                Serial.WriteString("\n");
                if (PciDevice.GetVendorId(0x0, 0x0, fn) != 0xFFFF)
                {
                    break;
                }

                CheckBus(fn);
            }
        }
        Serial.WriteString("[PciManager] Found Count ");
        Serial.WriteNumber(Count);
        Serial.WriteString("\n");
    }

    /// <summary>
    /// Check bus.
    /// </summary>
    /// <param name="xBus">A bus to check.</param>
    private static void CheckBus(ushort xBus)
    {
        Serial.WriteString("[PciManager] CheckBus(");
        Serial.WriteNumber(xBus);
        Serial.WriteString(")\n");
        for (ushort device = 0; device < 32; device++)
        {
            Serial.WriteString("[PciManager] CheckBus - ");
            Serial.WriteNumber(device);
            Serial.WriteString("\n");
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
        Serial.WriteString("[PciManager] CheckFunction - ");
        Serial.WriteNumber(xPCIDevice.DeviceId);
        Serial.WriteString(",");
        Serial.WriteNumber(xPCIDevice.Function);
        Serial.WriteString(")\n");
        Add(xPCIDevice);
        Serial.WriteString("[PciManager] Cached\n");
        if (xPCIDevice.ClassCode == 0x6 && xPCIDevice.Subclass == 0x4)
        {
            CheckBus(xPCIDevice.SecondaryBusNumber);
        }
    }

    private static void Add(PciDevice xPciDevice)
    {
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
