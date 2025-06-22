// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;

namespace Cosmos.Memory.Heap;

[StructLayout(LayoutKind.Explicit)]
public struct ObjectGc
{
    [FieldOffset(0)]
    public ObjectGcStatus Status;
    [FieldOffset(1)]
    public byte Padding;
    [FieldOffset(2)]
    public ushort Count;
}
