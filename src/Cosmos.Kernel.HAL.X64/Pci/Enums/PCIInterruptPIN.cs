// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.X64.Pci.Enums;

public enum PciInterruptPin : byte
{
    None = 0x00,
    Inta = 0x01,
    Intb = 0x02,
    Intc = 0x03,
    Intd = 0x04
};
