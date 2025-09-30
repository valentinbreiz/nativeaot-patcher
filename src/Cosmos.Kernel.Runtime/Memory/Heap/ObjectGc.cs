// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Memory.Heap;

[StructLayout(LayoutKind.Sequential)]
public struct ObjectGc
{
    public ObjectGcStatus Status;
    public byte Padding;
    public uint Flags;
    public unsafe nint* MethodTable;
}
