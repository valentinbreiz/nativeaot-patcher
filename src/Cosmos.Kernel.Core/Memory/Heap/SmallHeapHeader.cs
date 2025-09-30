// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Memory.Heap;

[StructLayout(LayoutKind.Sequential)]
public struct SmallHeapHeader
{
    public ushort Size;
    public ObjectGc Gc;
}
