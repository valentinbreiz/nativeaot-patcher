// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.X64.Pci.Enums;

public enum VendorId
{
    Intel = 0x8086,
    Amd = 0x1022,
    VmWare = 0x15AD,
    Bochs = 0x1234,
    VirtualBox = 0x80EE,
    RedHat = 0x1AF4  // QEMU/KVM virtio devices
}
