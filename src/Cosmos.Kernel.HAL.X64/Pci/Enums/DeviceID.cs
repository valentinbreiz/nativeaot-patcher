// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.X64.Pci.Enums;

public enum DeviceId
{
    SvgaiiAdapter = 0x0405,
    Pcnetii = 0x2000,
    Bga = 0x1111,
    Vbvga = 0xBEEF,
    VBoxGuest = 0xCAFE,
    IvshmemPlain = 0x1110  // QEMU ivshmem-plain device for shared memory
}
