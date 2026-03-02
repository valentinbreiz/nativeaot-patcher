// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

/// <summary>
/// Represents a managed object header in the GC heap.
/// Uses the LSB of the MethodTable pointer as a mark bit during collection.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GCObject
{
    // --- Fields ---

    /// <summary>
    /// Pointer to the object's MethodTable. The LSB is used as a mark bit during GC.
    /// </summary>
    public MethodTable* MethodTable;

    /// <summary>
    /// Component count for variable-length types (arrays and strings).
    /// </summary>
    public int Length;

    // --- Properties ---

    /// <summary>
    /// Gets the MethodTable pointer with the mark bit masked off.
    /// </summary>
    /// <returns>The clean MethodTable pointer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly MethodTable* GetMethodTable()
    {
        return (MethodTable*)((nint)MethodTable & ~(nint)1);
    }

    /// <summary>
    /// Gets a value indicating whether this object is marked as reachable.
    /// </summary>
    /// <value><c>true</c> if the mark bit (LSB) is set; otherwise, <c>false</c>.</value>
    public readonly bool IsMarked
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return ((nint)MethodTable & 1) != 0;
        }
    }

    // --- Methods ---

    /// <summary>
    /// Sets the mark bit on this object, indicating it is reachable.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Mark()
    {
        MethodTable = (MethodTable*)((nint)MethodTable | 1);
    }

    /// <summary>
    /// Clears the mark bit on this object, resetting it for the next collection cycle.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Unmark()
    {
        MethodTable = GetMethodTable();
    }

    /// <summary>
    /// Computes the total size of this object in bytes, including the base size and any component data.
    /// </summary>
    /// <returns>The object size in bytes.</returns>
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
