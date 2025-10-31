// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.BlockDevice;

namespace Cosmos.Kernel.Services.VFS;

public class Partition
{
    public string Name { get; set; }
    public ulong Offset { get; protected set; }
    public ulong Size { get; protected set; }
    public BaseBlockDevice BlockDevice { get; protected set; }
}
