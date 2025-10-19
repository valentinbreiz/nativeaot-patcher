// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.BlockDevice.Enums;

public enum BlockDeviceType
{
    /// <summary>
    /// This block device is a hard drive
    /// </summary>
    HardDrive,

    /// <summary>
    /// This block device is a CD or DVD
    /// </summary>
    RemovableCd,

    /// <summary>
    /// This block device is a removable device. For example, USB flash drive.
    /// </summary>
    Removable
}
