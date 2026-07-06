// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Pci;
using Cosmos.Kernel.HAL.Pci.Enums;

namespace Cosmos.Kernel.HAL.Devices.Storage;

/// <summary>
/// NVMe driver entry point. Scans PCI for every
/// <see cref="ClassId.MassStorageController"/> /
/// <see cref="SubclassId.NvmController"/> device and brings each one up
/// independently — a machine with two M.2 SSDs gets two
/// <see cref="NvmeController"/> instances and all their namespaces show up
/// in <see cref="Namespaces"/>.
/// </summary>
public static class Nvme
{
    private static List<NvmeController>? _controllers;
    private static List<NvmeNamespace>? _namespaces;
    private static bool s_initialized;

    /// <summary>Discovered NVMe controllers (empty if none were found).</summary>
    public static IReadOnlyList<NvmeController> Controllers =>
        (IReadOnlyList<NvmeController>?)_controllers ?? Array.Empty<NvmeController>();

    /// <summary>All namespaces across every controller this driver bound to.</summary>
    public static IReadOnlyList<NvmeNamespace> Namespaces =>
        (IReadOnlyList<NvmeNamespace>?)_namespaces ?? Array.Empty<NvmeNamespace>();

    /// <summary>
    /// Initialize the NVMe driver: PCI scan, controller bring-up, namespace
    /// discovery. Idempotent — later calls return immediately so live
    /// controllers are never reset twice.
    /// </summary>
    public static void Initialize()
    {
        if (s_initialized)
        {
            return;
        }
        s_initialized = true;

        Serial.WriteString("[NVMe] Looking for NVMe controllers...\n");

        _controllers = new List<NvmeController>();
        _namespaces = new List<NvmeNamespace>();

        List<PciDevice> devices = PciManager.GetAllDevicesClass(ClassId.MassStorageController, SubclassId.NvmController);
        if (devices.Count == 0)
        {
            Serial.WriteString("[NVMe] No NVMe controllers found\n");
            return;
        }

        Serial.WriteString("[NVMe] Found ");
        Serial.WriteNumber((uint)devices.Count);
        Serial.WriteString(" controller(s)\n");

        for (int i = 0; i < devices.Count; i++)
        {
            PciDevice device = devices[i];
            try
            {
                NvmeController controller = new(device, index: i);
                controller.Initialize();
                _controllers.Add(controller);

                for (int n = 0; n < controller.Namespaces.Count; n++)
                {
                    _namespaces.Add(controller.Namespaces[n]);
                }
            }
            catch (Exception ex)
            {
                Serial.WriteString("[NVMe] Controller #");
                Serial.WriteNumber((uint)i);
                Serial.WriteString(" init failed: ");
                Serial.WriteString(ex.Message);
                Serial.WriteString("\n");
            }
        }
    }
}
