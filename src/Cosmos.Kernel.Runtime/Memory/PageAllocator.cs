using Cosmos.Kernel.Core.Memory.Heap;
using Cosmos.Kernel.System.IO;

namespace Cosmos.Kernel.Core.Memory;

/// <summary>
/// a basic page allocator
/// </summary>
public static unsafe class PageAllocator
{
    /// <summary>
    /// Native Intel page size.
    /// </summary>
    /// <remarks><list type="bullet">
    /// <item>x86 Page Size: 4k, 2m (PAE only), 4m.</item>
    /// <item>x64 Page Size: 4k, 2m</item>
    /// </list></remarks>
    public const ulong PageSize = 4096;

    /// <summary>
    /// Start of area usable for heap, and also start of heap.
    /// </summary>
    public static byte* RamStart;

    /// <summary>
    /// Number of pages in the heap.
    /// </summary>
    /// <remarks>Calculated from mSize.</remarks>
    public static ulong TotalPageCount;

    /// <summary>
    /// Number of pages which are currently not in use
    /// </summary>
    public static ulong FreePageCount { get; private set; }

    /// <summary>
    /// Pointer to the RAT.
    /// </summary>
    /// <remarks>
    /// Covers Data area only.
    /// stored at the end of RAM
    /// </remarks>
    // We need a pointer as the RAT can move around in future with dynamic RAM etc.
    private static byte* mRAT;

    /// <summary>
    /// Pointer to end of the heap
    /// </summary>
    private static byte* HeapEnd;

    /// <summary>
    /// Size of heap.
    /// </summary>
    public static ulong RamSize;

    /// <summary>
    /// Init RAT.
    /// </summary>
    /// <param name="aStartPtr">A pointer to the start of the heap.</param>
    /// <param name="aSize">A heap size, in bytes.</param>
    /// <exception cref="Exception">Thrown if:
    /// <list type="bullet">
    /// <item>RAM start or size is not page aligned.</item>
    /// </list>
    /// </exception>
    public static void InitializeHeap(byte* aStartPtr, ulong aSize)
    {
        Serial.WriteString("[PageAllocator] UART started.\n");

        if ((uint)aStartPtr % PageSize != 0)
        {
            throw new Exception("RAM start must be page aligned.");
        }

        if (aSize % PageSize != 0)
        {
            throw new Exception("RAM size must be page aligned.");
        }

        Serial.WriteString("[PageAllocator] RAM start: 0x");
        Serial.WriteHex((ulong)aStartPtr);
        Serial.WriteString(", size: ");
        Serial.WriteNumber(aSize);
        Serial.WriteString("\n");

        RamStart = aStartPtr;
        RamSize = aSize;
        HeapEnd = aStartPtr + aSize;
        TotalPageCount = aSize / PageSize;
        FreePageCount = TotalPageCount;

        Serial.WriteString("[PageAllocator] Total pages: ");
        Serial.WriteNumber(TotalPageCount);
        Serial.WriteString("\n");

        // We need one status byte for each block.
        // Intel blocks are 4k (10 bits). So for 4GB, this means
        // 32 - 12 = 20 bits, 1 MB for a RAT for 4GB. 0.025%
        ulong xRatPageCount = (TotalPageCount - 1) / PageSize + 1;
        ulong xRatTotalSize = xRatPageCount * PageSize;

        Serial.WriteString("[PageAllocator] RAT pages needed: ");
        Serial.WriteNumber(xRatPageCount);
        Serial.WriteString(", RAT total size: ");
        Serial.WriteNumber(xRatTotalSize);
        Serial.WriteString("\n");

        // Place RAT at the beginning of heap instead of end to avoid memory protection issues
        mRAT = RamStart;

        // Adjust RamStart and RamSize to account for RAT at beginning
        RamStart = RamStart + xRatTotalSize;
        RamSize = RamSize - xRatTotalSize;
        HeapEnd = RamStart + RamSize;

        Serial.WriteString("[PageAllocator] RAT location: 0x");
        Serial.WriteHex((ulong)mRAT);
        Serial.WriteString(", New RamStart: 0x");
        Serial.WriteHex((ulong)RamStart);
        Serial.WriteString("\n");

        if (mRAT > HeapEnd)
        {
            throw new Exception("mRAT is greater than heap. rattotalsize is " + xRatTotalSize);
        }

        // Initialize ALL RAT entries first
        Serial.WriteString("[PageAllocator] Initializing RAT entries...\n");
        for (ulong i = 0; i < TotalPageCount; i++)
        {
            mRAT[i] = (byte)PageType.Empty;
        }

        // Mark the RAT pages as PageAllocator (first xRatPageCount pages)
        Serial.WriteString("[PageAllocator] Marking RAT pages...\n");
        for (ulong i = 0; i < xRatPageCount; i++)
        {
            mRAT[i] = (byte)PageType.PageAllocator;
        }

        // Remove pages needed for RAT table from count
        FreePageCount -= xRatPageCount;

        Serial.WriteString("[PageAllocator] RAT initialization complete. Free pages: ");
        Serial.WriteNumber(FreePageCount);
        Serial.WriteString("\n");

        // Test RAT is writable
        byte testValue = mRAT[0];
        mRAT[0] = 123;
        if (mRAT[0] != 123)
        {
            Serial.WriteString("[PageAllocator] ERROR: RAT is not writable!\n");
        }
        else
        {
            Serial.WriteString("[PageAllocator] RAT write test passed\n");
        }
        mRAT[0] = testValue; // Restore

        SmallHeap.Init();
    }

    /// <summary>
    /// Alloc a given number of pages, all of the same type.
    /// </summary>
    /// <param name="aType">A type of pages to alloc.</param>
    /// <param name="aPageCount">Number of pages to alloc. (default = 1)</param>
    /// <param name="zero"></param>
    /// <returns>A pointer to the first page on success, null on failure.</returns>
    public static void* AllocPages(PageType aType, ulong aPageCount = 1, bool zero = false)
    {
        Serial.WriteString("[PageAllocator] AllocPages - Type: ");
        Serial.WriteNumber((uint)aType);
        Serial.WriteString(", Count: ");
        Serial.WriteNumber(aPageCount);
        Serial.WriteString(", Free: ");
        Serial.WriteNumber(FreePageCount);
        Serial.WriteString("\n");

        byte* startPage = null;

        // Could combine with an external method or delegate, but will slow things down
        // unless we can force it to be inlined.
        // Alloc single blocks at bottom, larger blocks at top to help reduce fragmentation.
        uint xCount = 0;
        if (aPageCount == 1)
        {
            for (byte* ptr = mRAT; ptr < mRAT + TotalPageCount; ptr++)
            {
                ulong currentOffset = (ulong)(ptr - mRAT);
                PageType currentType = (PageType)(*ptr);

                if (currentOffset < 10)  // Only log first 10 entries to avoid spam
                {
                    Serial.WriteString("[PageAllocator] RAT[");
                    Serial.WriteNumber(currentOffset);
                    Serial.WriteString("] = ");
                    Serial.WriteNumber((uint)currentType);
                    Serial.WriteString("\n");
                }

                if (currentType == PageType.Empty)
                {
                    startPage = ptr;
                    Serial.WriteString("[PageAllocator] Found empty page at offset ");
                    Serial.WriteNumber(currentOffset);
                    Serial.WriteString("\n");
                    break;
                }
            }
        }
        else
        {
            // This loop will FAIL if mRAT is ever 0. This should be impossible though
            // so we don't bother to account for such a case. xPos would also have issues.
            for (byte* ptr = mRAT + TotalPageCount - 1; ptr >= mRAT; ptr--)
            {
                if (*ptr == (byte)PageType.Empty)
                {
                    if (++xCount == aPageCount)
                    {
                        startPage = ptr;
                        break;
                    }
                }
                else
                {
                    xCount = 0;
                }
            }
        }

        // If we found enough space, mark it as used.
        if (startPage != null)
        {
            long offset = startPage - mRAT;
            byte* pageAddress = RamStart + (ulong)offset * PageSize;

            Serial.WriteString("[PageAllocator] Allocating at offset ");
            Serial.WriteNumber((uint)offset);
            Serial.WriteString(", address: 0x");
            Serial.WriteHex((ulong)pageAddress);
            Serial.WriteString("\n");

            // Bounds check before writing to RAT
            if ((ulong)offset >= TotalPageCount)
            {
                Serial.WriteString("[PageAllocator] ERROR: Offset out of bounds!\n");
                return null;
            }

            // Diagnostic: Test if we can write to the RAT at all
            Serial.WriteString("[PageAllocator] Testing RAT write at offset ");
            Serial.WriteNumber((uint)offset);
            Serial.WriteString("\n");

            // Try writing a test value first
            mRAT[offset] = 99;
            if (mRAT[offset] != 99)
            {
                Serial.WriteString("[PageAllocator] ERROR: Cannot write to RAT!\n");
                return null;
            }

            // Now try the actual value
            mRAT[offset] = (byte)aType;

            // Test adjacent memory locations
            if (offset > 0)
            {
                Serial.WriteString("[PageAllocator] Adjacent RAT values - Prev: ");
                Serial.WriteNumber((uint)mRAT[offset - 1]);
                Serial.WriteString(", Current: ");
                Serial.WriteNumber((uint)mRAT[offset]);
                if (offset + 1 < (long)TotalPageCount)
                {
                    Serial.WriteString(", Next: ");
                    Serial.WriteNumber((uint)mRAT[offset + 1]);
                }
                Serial.WriteString("\n");
            }

            for (ulong i = 1; i < aPageCount; i++)
            {
                mRAT[(ulong)offset + i] = (byte)PageType.Extension;
            }

            // Verify the RAT entry was set correctly
            Serial.WriteString("[PageAllocator] RAT entry set to: ");
            Serial.WriteNumber((uint)mRAT[offset]);
            Serial.WriteString("\n");

            if (zero)
            {
                Span<byte> span = new(pageAddress, (int)(PageSize * aPageCount));
                span.Fill(0x00);
            }

            // Decrement free page count
            FreePageCount -= aPageCount;

            Serial.WriteString("[PageAllocator] Allocation successful. Remaining free pages: ");
            Serial.WriteNumber(FreePageCount);
            Serial.WriteString("\n");

            return pageAddress;
        }

        Serial.WriteString("[PageAllocator] No free pages found!\n");
        return null;
    }

    /// <summary>
    /// Get the first PageAllocator address.
    /// </summary>
    /// <param name="aPtr">A pointer to the block.</param>
    /// <returns>The index in RAT to which this pointer belongs</returns>
    /// <exception cref="Exception">Thrown if page type is not found.</exception>
    public static uint GetFirstPageAllocatorIndex(void* aPtr)
    {
        ulong xPos = (ulong)((byte*)aPtr - RamStart) / PageSize;
        // See note about when mRAT = 0 in Alloc.
        for (byte* p = mRAT + xPos; p >= mRAT; p--)
        {
            if (*p != (byte)PageType.Extension)
            {
                return (uint)(p - mRAT);
            }
        }

        throw new Exception("Page type not found. Likely RAT is rotten.");
    }

    /// <summary>
    /// Get the pointer to the start of the page containing the pointer's address
    /// </summary>
    /// <param name="aPtr"></param>
    /// <returns></returns>
    public static byte* GetPagePtr(void* aPtr) => (byte*)aPtr - (ulong)((byte*)aPtr - RamStart) % PageSize;

    /// <summary>
    /// Get the page type pointed by a pointer to the RAT entry.
    /// </summary>
    /// <param name="aPtr">A pointer to the page to get the type of.</param>
    /// <returns>byte value.</returns>
    /// <exception cref="Exception">Thrown if page type is not found.</exception>
    public static PageType GetPageType(void* aPtr)
    {
        if (aPtr < RamStart || aPtr > HeapEnd)
        {
            return PageType.Empty;
        }

        return (PageType)mRAT[GetFirstPageAllocatorIndex(aPtr)];
    }

    /// <summary>
    /// Free page.
    /// </summary>
    /// <param name="aPageIdx">A index to the page to be freed.</param>
    public static void Free(uint aPageIdx)
    {
        byte* p = mRAT + aPageIdx;
        *p = (byte)PageType.Empty;
        FreePageCount++;
        for (; p < mRAT + TotalPageCount;)
        {
            if (*++p != (byte)PageType.Extension)
            {
                break;
            }

            *p = (byte)PageType.Empty;
            FreePageCount++;
        }
    }

    /// <summary>
    /// Free the page this pointer points to
    /// </summary>
    /// <param name="aPtr"></param>
    public static void Free(void* aPtr) => Free(GetFirstPageAllocatorIndex(aPtr));
}
