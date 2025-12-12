// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Memory.Heap;

[StructLayout(LayoutKind.Sequential)]
public struct ObjectGc
{
    /// <summary>
    /// GC status flag (None = not marked, Hit = marked as reachable)
    /// </summary>
    public ObjectGcStatus Status;

    /// <summary>
    /// Padding byte (unused)
    /// </summary>
    public byte Padding;

    /// <summary>
    /// Reference count - number of references to this object.
    /// When this reaches 0, the object can be freed.
    /// </summary>
    public uint RefCount;

    /// <summary>
    /// Reserved for future use (e.g., MethodTable pointer for type info)
    /// </summary>
    public unsafe nint* Reserved;
}
