// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Pci.Enums;

public enum PciHeaderType : byte
{
    Normal = 0x00,
    Bridge = 0x01,
    Cardbus = 0x02
};
