using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Memory;

public static unsafe partial class MemoryOp
{
    public static void InitializeHeap(ulong heapBase, ulong heapSize) =>
        PageAllocator.InitializeHeap((byte*)heapBase, heapSize);

    public static void* Alloc(uint size) => Heap.Heap.Alloc(size);

    public static void Free(void* ptr) => Heap.Heap.Free(ptr);

    #region Native SIMD Imports

    [LibraryImport("*", EntryPoint = "_simd_copy_16")]
    private static partial void SimdCopy16(byte* dest, byte* src);

    [LibraryImport("*", EntryPoint = "_simd_copy_32")]
    private static partial void SimdCopy32(byte* dest, byte* src);

    [LibraryImport("*", EntryPoint = "_simd_copy_64")]
    private static partial void SimdCopy64(byte* dest, byte* src);

    [LibraryImport("*", EntryPoint = "_simd_copy_128")]
    private static partial void SimdCopy128(byte* dest, byte* src);

    [LibraryImport("*", EntryPoint = "_simd_copy_128_blocks")]
    private static partial void SimdCopy128Blocks(byte* dest, byte* src, int blockCount);

    [LibraryImport("*", EntryPoint = "_simd_fill_16_blocks")]
    private static partial void SimdFill16Blocks(byte* dest, int value, int blockCount);

    #endregion

    // Flag to enable/disable SIMD
    private static bool _simdEnabled = true;

    public static void SetSimdEnabled(bool enabled) => _simdEnabled = enabled;

    #region MemSet

    public static void MemSet(byte* dest, byte value, int count)
    {
        if (count <= 0) return;

        // Simple byte-by-byte fill (always works)
        for (int i = 0; i < count; i++)
        {
            dest[i] = value;
        }
    }

    public static void MemSet(uint* dest, uint value, int count)
    {
        for (int i = 0; i < count; i++)
        {
            dest[i] = value;
        }
    }

    #endregion

    #region MemCopy

    /// <summary>
    /// Memory copy. Does NOT handle overlapping regions - use MemMove for that.
    /// </summary>
    public static void MemCopy(byte* dest, byte* src, int count)
    {
        if (count <= 0) return;

        if (_simdEnabled && count >= 16)
        {
            // SIMD path
            CopyWithSimd(dest, src, count);
        }
        else
        {
            // Scalar path for small copies
            CopyScalar(dest, src, count);
        }
    }

    private static void CopyScalar(byte* dest, byte* src, int count)
    {
        // Use 64-bit copies where possible for better performance
        int i = 0;

        // Copy 8 bytes at a time
        while (i + 8 <= count)
        {
            *(ulong*)(dest + i) = *(ulong*)(src + i);
            i += 8;
        }

        // Copy remaining bytes
        while (i < count)
        {
            dest[i] = src[i];
            i++;
        }
    }

    private static void CopyWithSimd(byte* dest, byte* src, int count)
    {
        int offset = 0;

        // Copy 128-byte blocks
        while (count - offset >= 128)
        {
            SimdCopy128(dest + offset, src + offset);
            offset += 128;
        }

        // Copy 64-byte chunk
        if (count - offset >= 64)
        {
            SimdCopy64(dest + offset, src + offset);
            offset += 64;
        }

        // Copy 32-byte chunk
        if (count - offset >= 32)
        {
            SimdCopy32(dest + offset, src + offset);
            offset += 32;
        }

        // Copy 16-byte chunk
        if (count - offset >= 16)
        {
            SimdCopy16(dest + offset, src + offset);
            offset += 16;
        }

        // Copy remaining bytes using scalar
        while (offset < count)
        {
            dest[offset] = src[offset];
            offset++;
        }
    }

    public static void MemCopy(uint* dest, uint* src, int count)
    {
        MemCopy((byte*)dest, (byte*)src, count * sizeof(uint));
    }

    #endregion

    #region MemMove

    /// <summary>
    /// Memory move that handles overlapping regions correctly.
    /// </summary>
    public static void MemMove(byte* dest, byte* src, int count)
    {
        if (dest == src || count == 0)
        {
            return;
        }

        // Check for overlap
        if (dest < src || dest >= src + count)
        {
            // No overlap - safe to use forward copy
            MemCopy(dest, src, count);
        }
        else
        {
            // Overlap detected - use byte-by-byte backward copy
            for (int i = count - 1; i >= 0; i--)
            {
                dest[i] = src[i];
            }
        }
    }

    #endregion

    #region MemCmp

    public static bool MemCmp(uint* dest, uint* src, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (dest[i] != src[i])
            {
                return false;
            }
        }

        return true;
    }

    #endregion
}
