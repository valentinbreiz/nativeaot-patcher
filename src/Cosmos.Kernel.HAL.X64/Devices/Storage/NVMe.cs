// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Pci;
using Cosmos.Kernel.HAL.Pci.Enums;

namespace Cosmos.Kernel.HAL.X64.Devices.Storage;

/// <summary>
/// NVMe driver entry point. Scans PCI for the first
/// <see cref="ClassId.MassStorageController"/> /
/// <see cref="SubclassId.NvmController"/> device and brings it up.
/// Mirrors <see cref="AHCI"/>'s static driver shape.
/// </summary>
public static class NVMe
{
    private static List<NVMeNamespace>? _namespaces;
    private static NVMeController? _controller;

    /// <summary>Discovered NVMe namespaces (empty if no controller was found).</summary>
    public static List<NVMeNamespace> Namespaces => _namespaces ?? new List<NVMeNamespace>();

    /// <summary>Initialize the NVMe driver: PCI scan, controller bring-up, namespace discovery.</summary>
    public static void InitDriver()
    {
        Serial.WriteString("[NVMe] Looking for NVMe controller...\n");

        _namespaces = new List<NVMeNamespace>();

        PciDevice? device = PciManager.GetDeviceClass(ClassId.MassStorageController, SubclassId.NvmController);
        if (device == null)
        {
            Serial.WriteString("[NVMe] No NVMe controller found\n");
            return;
        }

        Serial.WriteString("[NVMe] Found NVMe controller\n");

        try
        {
            _controller = new NVMeController(device);
            _controller.Initialize();

            for (int i = 0; i < _controller.Namespaces.Count; i++)
            {
                _namespaces.Add(_controller.Namespaces[i]);
            }
        }
        catch (Exception ex)
        {
            Serial.WriteString("[NVMe] Initialization failed: ");
            Serial.WriteString(ex.Message);
            Serial.WriteString("\n");
        }
    }
}
