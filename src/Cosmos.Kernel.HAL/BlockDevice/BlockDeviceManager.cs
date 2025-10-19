// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.BlockDevice;

public static class BlockDeviceManager
{
    public static List<BlockDevice> Devices = new List<BlockDevice>();
    public static List<Partition> Partitions = new List<Partition>();
}
