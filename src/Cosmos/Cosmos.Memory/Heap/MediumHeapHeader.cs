// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;

namespace Cosmos.Memory.Heap;

[StructLayout(LayoutKind.Explicit)]
public struct MediumHeapHeader
{
    [FieldOffset(0)] public ushort Size;
    [FieldOffset(2)] public ObjectGc Gc;
}
