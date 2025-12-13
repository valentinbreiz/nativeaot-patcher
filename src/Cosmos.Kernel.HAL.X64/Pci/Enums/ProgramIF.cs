// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.X64.Pci.Enums;

public enum ProgramIf
{
    // MassStorageController:
    SataVendorSpecific = 0x00,
    SataAhci = 0x01,
    SataSerialStorageBus = 0x02,
    SasSerialStorageBus = 0x01,
    NvmNvmhci = 0x01,
    NvmNvmExpress = 0x02
}
