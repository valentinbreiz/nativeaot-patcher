// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Pci.Enums;

public enum SubclassId
{
    // MassStorageController:
    ScsiStorageController = 0x00,
    IdeInterface = 0x01,
    FloppyDiskController = 0x02,
    IpiBusController = 0x03,
    RaidController = 0x04,
    AtaController = 0x05,
    SataController = 0x06,
    SasController = 0x07,
    NvmController = 0x08,
    UnknownMassStorage = 0x09
}
