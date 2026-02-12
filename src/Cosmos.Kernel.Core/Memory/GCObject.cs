// This code is licensed under MIT license (see LICENSE for details)
// Clean GC implementation with free list allocation

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Memory;

/// <summary>
/// Represents a managed object in the GC heap.
/// Uses LSB of MethodTable pointer for marking.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GCObject
{
    public MethodTable* MethodTable;
    public int Length;  // For arrays/strings

    /// <summary>
    /// Gets the MethodTable with mark bit masked off.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly MethodTable* GetMethodTable() => (MethodTable*)((nint)MethodTable & ~(nint)1);

    /// <summary>
    /// Checks if this object is marked.
    /// </summary>
    public readonly bool IsMarked
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ((nint)MethodTable & 1) != 0;
    }

    /// <summary>
    /// Marks this object.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Mark() => MethodTable = (MethodTable*)((nint)MethodTable | 1);

    /// <summary>
    /// Unmarks this object.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Unmark() => MethodTable = GetMethodTable();

    /// <summary>
    /// Computes the size of this object.
    /// </summary>
    public readonly uint ComputeSize()
    {
        var mt = GetMethodTable();
        if (mt->HasComponentSize)
        {
            return mt->BaseSize + (uint)Length * mt->ComponentSize;
        }
        return mt->RawBaseSize;
    }
}
