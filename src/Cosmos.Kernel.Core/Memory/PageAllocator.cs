// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.Memory.Heap;

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
        if ((uint)aStartPtr % PageSize != 0)
        {
            throw new Exception("RAM start must be page aligned.");
        }

        if (aSize % PageSize != 0)
        {
            throw new Exception("RAM size must be page aligned.");
        }

        RamStart = aStartPtr;
        RamSize = aSize;
        HeapEnd = aStartPtr + aSize;
        TotalPageCount = aSize / PageSize;
        FreePageCount = TotalPageCount;

        // We need one status byte for each block.
        // Intel blocks are 4k (10 bits). So for 4GB, this means
        // 32 - 12 = 20 bits, 1 MB for a RAT for 4GB. 0.025%
        ulong xRatPageCount = (TotalPageCount - 1) / PageSize + 1;
        ulong xRatTotalSize = xRatPageCount * PageSize;
        mRAT = RamStart + RamSize - xRatTotalSize;

        if (mRAT > HeapEnd)
        {
            throw new Exception("mRAT is greater than heap. rattotalsize is " + xRatTotalSize);
        }

        // Mark empty pages as such in the RAT Table
        new Span<byte>(mRAT, (int)xRatPageCount)
            .Fill((byte)PageType.Empty);
        // for (byte* p = mRAT; p < mRAT + TotalPageCount - xRatPageCount; p++)
        // {
        //     *p = (byte)PageType.Empty;
        // }

        // Mark the PageAllocator pages as such
        new Span<byte>(mRAT + TotalPageCount - xRatPageCount, (int)xRatTotalSize)
            .Fill((byte)PageType.PageAllocator);
        // for (byte* p = mRAT + TotalPageCount - xRatPageCount; p < mRAT + xRatTotalSize; p++)
        // {
        //     *p = (byte)PageType.PageAllocator;
        // }

        // Remove pages needed for RAT table from count
        FreePageCount -= xRatPageCount;

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
        byte* startPage = null;

        // Could combine with an external method or delegate, but will slow things down
        // unless we can force it to be inlined.
        // Alloc single blocks at bottom, larger blocks at top to help reduce fragmentation.
        uint xCount = 0;
        if (aPageCount == 1)
        {
            for (byte* ptr = mRAT; ptr < mRAT + TotalPageCount; ptr++)
            {
                if (*ptr == (byte)PageType.Empty)
                {
                    startPage = ptr;
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
            *startPage = (byte)aType;
            for (byte* p = startPage + 1; p < startPage + xCount; p++)
            {
                *p = (byte)PageType.Extension;
            }

            if (zero)
            {
                Span<byte> span = new(pageAddress, (int)(PageSize * aPageCount));
                span.Fill(0x00);
            }

            // Decrement free page count
            FreePageCount -= aPageCount;

            return pageAddress;
        }

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
