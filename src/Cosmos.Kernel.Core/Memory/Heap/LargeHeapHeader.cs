// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Memory.Heap;

[StructLayout(LayoutKind.Sequential)]
public struct LargeHeapHeader
{
    public ulong Used;
    public uint Size;
    public uint Padding;
    public ObjectGc Gc;
}
