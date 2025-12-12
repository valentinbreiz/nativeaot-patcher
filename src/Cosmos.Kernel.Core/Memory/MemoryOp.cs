using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.GC;
using Cosmos.Kernel.Core.Memory.Heap;

namespace Cosmos.Kernel.Core.Memory;

public static unsafe partial class MemoryOp
{
    public static void InitializeHeap(ulong heapBase, ulong heapSize)
    {
        PageAllocator.InitializeHeap((byte*)heapBase, heapSize);

        // Initialize GC handle table after page allocator is ready
        HandleTable.Initialize();
    }

    public static void* Alloc(uint size) => Heap.Heap.Alloc(size);

    public static void Free(void* ptr) => Heap.Heap.Free(ptr);

    #region Memory Info

    /// <summary>
    /// Print comprehensive memory and heap information to serial output.
    /// </summary>
    public static void PrintMemoryInfo()
    {
        Serial.WriteString("\n========== MEMORY INFORMATION ==========\n");

        // Page Allocator Info
        PrintPageAllocatorInfo();

        // Heap Info
        PrintHeapInfo();

        // GC Handle Table Info
        PrintHandleTableInfo();

        Serial.WriteString("=========================================\n\n");
    }

    /// <summary>
    /// Print page allocator statistics.
    /// </summary>
    public static void PrintPageAllocatorInfo()
    {
        Serial.WriteString("\n--- Page Allocator ---\n");

        Serial.WriteString("  RAM Start:      0x");
        Serial.WriteHex((ulong)PageAllocator.RamStart);
        Serial.WriteString("\n");

        Serial.WriteString("  RAM Size:       ");
        Serial.WriteNumber(PageAllocator.RamSize / 1024 / 1024);
        Serial.WriteString(" MB (");
        Serial.WriteNumber(PageAllocator.RamSize);
        Serial.WriteString(" bytes)\n");

        Serial.WriteString("  Page Size:      ");
        Serial.WriteNumber(PageAllocator.PageSize);
        Serial.WriteString(" bytes\n");

        Serial.WriteString("  Total Pages:    ");
        Serial.WriteNumber(PageAllocator.TotalPageCount);
        Serial.WriteString("\n");

        Serial.WriteString("  Free Pages:     ");
        Serial.WriteNumber(PageAllocator.FreePageCount);
        Serial.WriteString("\n");

        ulong usedPages = PageAllocator.TotalPageCount - PageAllocator.FreePageCount;
        Serial.WriteString("  Used Pages:     ");
        Serial.WriteNumber(usedPages);
        Serial.WriteString("\n");

        // Memory usage
        ulong freeMemory = PageAllocator.FreePageCount * PageAllocator.PageSize;
        ulong usedMemory = usedPages * PageAllocator.PageSize;

        Serial.WriteString("  Free Memory:    ");
        Serial.WriteNumber(freeMemory / 1024 / 1024);
        Serial.WriteString(" MB (");
        Serial.WriteNumber(freeMemory / 1024);
        Serial.WriteString(" KB)\n");

        Serial.WriteString("  Used Memory:    ");
        Serial.WriteNumber(usedMemory / 1024 / 1024);
        Serial.WriteString(" MB (");
        Serial.WriteNumber(usedMemory / 1024);
        Serial.WriteString(" KB)\n");

        // Page type breakdown
        PrintPageTypeBreakdown();
    }

    /// <summary>
    /// Print breakdown of page types in use.
    /// </summary>
    private static void PrintPageTypeBreakdown()
    {
        Serial.WriteString("\n  Page Type Breakdown:\n");

        ulong emptyCount = 0;
        ulong smallHeapCount = 0;
        ulong mediumHeapCount = 0;
        ulong largeHeapCount = 0;
        ulong unmanagedCount = 0;
        ulong smtCount = 0;
        ulong pageAllocatorCount = 0;
        ulong extensionCount = 0;
        ulong otherCount = 0;

        // Count page types by scanning RAT
        for (ulong i = 0; i < PageAllocator.TotalPageCount; i++)
        {
            PageType type = PageAllocator.GetPageType(PageAllocator.RamStart + i * PageAllocator.PageSize);
            switch (type)
            {
                case PageType.Empty:
                    emptyCount++;
                    break;
                case PageType.HeapSmall:
                    smallHeapCount++;
                    break;
                case PageType.HeapMedium:
                    mediumHeapCount++;
                    break;
                case PageType.HeapLarge:
                    largeHeapCount++;
                    break;
                case PageType.Unmanaged:
                    unmanagedCount++;
                    break;
                case PageType.SMT:
                    smtCount++;
                    break;
                case PageType.PageAllocator:
                    pageAllocatorCount++;
                    break;
                case PageType.Extension:
                    extensionCount++;
                    break;
                default:
                    otherCount++;
                    break;
            }
        }

        Serial.WriteString("    Empty:         ");
        Serial.WriteNumber(emptyCount);
        Serial.WriteString(" pages\n");

        Serial.WriteString("    HeapSmall:     ");
        Serial.WriteNumber(smallHeapCount);
        Serial.WriteString(" pages\n");

        Serial.WriteString("    HeapMedium:    ");
        Serial.WriteNumber(mediumHeapCount);
        Serial.WriteString(" pages\n");

        Serial.WriteString("    HeapLarge:     ");
        Serial.WriteNumber(largeHeapCount);
        Serial.WriteString(" pages\n");

        Serial.WriteString("    Unmanaged:     ");
        Serial.WriteNumber(unmanagedCount);
        Serial.WriteString(" pages\n");

        Serial.WriteString("    SMT:           ");
        Serial.WriteNumber(smtCount);
        Serial.WriteString(" pages\n");

        Serial.WriteString("    PageAllocator: ");
        Serial.WriteNumber(pageAllocatorCount);
        Serial.WriteString(" pages\n");

        Serial.WriteString("    Extension:     ");
        Serial.WriteNumber(extensionCount);
        Serial.WriteString(" pages\n");

        if (otherCount > 0)
        {
            Serial.WriteString("    Other:         ");
            Serial.WriteNumber(otherCount);
            Serial.WriteString(" pages\n");
        }
    }

    /// <summary>
    /// Print heap statistics.
    /// </summary>
    public static void PrintHeapInfo()
    {
        Serial.WriteString("\n--- Heap Statistics ---\n");

        // SmallHeap info
        Serial.WriteString("  SmallHeap:\n");
        Serial.WriteString("    Max Item Size:      ");
        Serial.WriteNumber(SmallHeap.mMaxItemSize);
        Serial.WriteString(" bytes\n");

        Serial.WriteString("    Prefix Bytes:       ");
        Serial.WriteNumber(SmallHeap.PrefixBytes);
        Serial.WriteString(" bytes\n");

        int allocatedObjects = SmallHeap.GetAllocatedObjectCount();
        Serial.WriteString("    Allocated Objects:  ");
        Serial.WriteNumber((ulong)allocatedObjects);
        Serial.WriteString("\n");

        // MediumHeap info
        Serial.WriteString("  MediumHeap:\n");
        Serial.WriteString("    Min Size:           ");
        Serial.WriteNumber(MediumHeap.MinSize);
        Serial.WriteString(" bytes\n");
        Serial.WriteString("    Max Size:           ");
        Serial.WriteNumber(MediumHeap.MaxSize);
        Serial.WriteString(" bytes\n");

        // LargeHeap info
        Serial.WriteString("  LargeHeap:\n");
        Serial.WriteString("    Min Size:           ");
        Serial.WriteNumber(LargeHeap.MinSize);
        Serial.WriteString(" bytes\n");
    }

    /// <summary>
    /// Print GC handle table statistics.
    /// </summary>
    public static void PrintHandleTableInfo()
    {
        Serial.WriteString("\n--- GC Handle Table ---\n");

        Serial.WriteString("  Initialized:    ");
        Serial.WriteString(HandleTable.IsInitialized ? "Yes" : "No");
        Serial.WriteString("\n");

        if (HandleTable.IsInitialized)
        {
            Serial.WriteString("  Capacity:       ");
            Serial.WriteNumber((ulong)HandleTable.Capacity);
            Serial.WriteString(" handles\n");

            Serial.WriteString("  In Use:         ");
            Serial.WriteNumber((ulong)HandleTable.Count);
            Serial.WriteString(" handles\n");

            Serial.WriteString("  Free:           ");
            Serial.WriteNumber((ulong)(HandleTable.Capacity - HandleTable.Count));
            Serial.WriteString(" handles\n");
        }
    }

    /// <summary>
    /// Get total used memory in bytes.
    /// </summary>
    public static ulong GetUsedMemory()
    {
        ulong usedPages = PageAllocator.TotalPageCount - PageAllocator.FreePageCount;
        return usedPages * PageAllocator.PageSize;
    }

    /// <summary>
    /// Get total free memory in bytes.
    /// </summary>
    public static ulong GetFreeMemory()
    {
        return PageAllocator.FreePageCount * PageAllocator.PageSize;
    }

    /// <summary>
    /// Get total memory in bytes.
    /// </summary>
    public static ulong GetTotalMemory()
    {
        return PageAllocator.RamSize;
    }

    #endregion

    #region Native SIMD Imports

    [LibraryImport("*", EntryPoint = "_simd_copy_16")]
    [SuppressGCTransition]
    private static partial void SimdCopy16(byte* dest, byte* src);

    [LibraryImport("*", EntryPoint = "_simd_copy_32")]
    [SuppressGCTransition]
    private static partial void SimdCopy32(byte* dest, byte* src);

    [LibraryImport("*", EntryPoint = "_simd_copy_64")]
    [SuppressGCTransition]
    private static partial void SimdCopy64(byte* dest, byte* src);

    [LibraryImport("*", EntryPoint = "_simd_copy_128")]
    [SuppressGCTransition]
    private static partial void SimdCopy128(byte* dest, byte* src);

    [LibraryImport("*", EntryPoint = "_simd_copy_128_blocks")]
    [SuppressGCTransition]
    private static partial void SimdCopy128Blocks(byte* dest, byte* src, int blockCount);

    [LibraryImport("*", EntryPoint = "_simd_fill_16_blocks")]
    [SuppressGCTransition]
    private static partial void SimdFill16Blocks(byte* dest, int value, int blockCount);

    #endregion

    #region MemSet

    public static void MemSet(byte* dest, byte value, int count)
    {
        if (count <= 0) return;

        if (count >= 16)
        {
            // SIMD path - broadcast byte to 32-bit value for SIMD fill
            uint fillValue = (uint)(value | (value << 8) | (value << 16) | (value << 24));
            FillWithSimd(dest, fillValue, count);
        }
        else
        {
            // Scalar path for small fills
            for (int i = 0; i < count; i++)
            {
                dest[i] = value;
            }
        }
    }

    public static void MemSet(uint* dest, uint value, int count)
    {
        if (count <= 0) return;

        if (count >= 4)
        {
            // SIMD path
            FillWithSimd((byte*)dest, value, count * sizeof(uint));
        }
        else
        {
            // Scalar path
            for (int i = 0; i < count; i++)
            {
                dest[i] = value;
            }
        }
    }

    private static void FillWithSimd(byte* dest, uint value, int count)
    {
        int offset = 0;

        // Fill 16-byte blocks using SIMD
        int blockCount = count / 16;
        if (blockCount > 0)
        {
            SimdFill16Blocks(dest, (int)value, blockCount);
            offset = blockCount * 16;
        }

        // Fill remaining bytes using scalar (with the byte value extracted)
        byte byteValue = (byte)(value & 0xFF);
        while (offset < count)
        {
            dest[offset] = byteValue;
            offset++;
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

        if (count >= 16)
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
            // No overlap - safe to use forward copy (with SIMD)
            MemCopy(dest, src, count);
        }
        else
        {
            // Overlap detected - use backward copy
            CopyBackward(dest, src, count);
        }
    }

    private static void CopyBackward(byte* dest, byte* src, int count)
    {
        // Use 64-bit copies where possible for better performance
        int i = count;

        // Copy 8 bytes at a time from the end
        while (i >= 8)
        {
            i -= 8;
            *(ulong*)(dest + i) = *(ulong*)(src + i);
        }

        // Copy remaining bytes
        while (i > 0)
        {
            i--;
            dest[i] = src[i];
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
