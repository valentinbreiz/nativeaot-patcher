// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;

namespace Cosmos.Memory.Heap;

[StructLayout(LayoutKind.Explicit)]
public struct LargeHeapHeader
{
    [FieldOffset(0)] public uint Used;
    [FieldOffset(4)] public uint Size;
    [FieldOffset(8)] public uint Padding;
    [FieldOffset(10)] public ObjectGc Gc;
}
