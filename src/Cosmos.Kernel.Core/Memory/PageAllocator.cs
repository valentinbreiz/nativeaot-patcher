using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.Heap;
using Cosmos.Kernel.Debug;

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
    /// Convert virtual address to physical address for Higher Half Kernel mapping
    /// </summary>
    /// <param name="virtualAddress">Virtual address to convert</param>
    /// <returns>Physical address</returns>
    private static ulong VirtualToPhysical(ulong virtualAddress)
    {
        // Higher Half Kernel mapping: virtual addresses start with 0xFFFF8000
        // Remove the higher half offset to get physical address
        const ulong HigherHalfOffset = 0xFFFF800000000000UL;
        if (virtualAddress >= HigherHalfOffset)
        {
            return virtualAddress - HigherHalfOffset;
        }
        // If not in higher half mapping, assume it's already physical
        return virtualAddress;
    }

    /// <summary>
    /// Check if an address is in a usable memory region according to Limine memory map
    /// </summary>
    /// <param name="address">Virtual address to check</param>
    /// <param name="size">Size of the region</param>
    /// <returns>True if the region is usable, false otherwise</returns>
    private static bool IsUsableMemoryRegion(byte* address, ulong size)
    {
        if (Limine.MemoryMap.Response == null)
        {
            Serial.WriteString("[PageAllocator] Warning: No memory map available, assuming usable\n");
            return true;
        }

        ulong virtualAddressStart = (ulong)address;
        ulong virtualAddressEnd = virtualAddressStart + size;

        // Convert virtual addresses to physical addresses for comparison with memory map
        ulong physicalAddressStart = VirtualToPhysical(virtualAddressStart);
        ulong physicalAddressEnd = VirtualToPhysical(virtualAddressEnd);

        Serial.WriteString("[PageAllocator] Checking virtual 0x");
        Serial.WriteHex(virtualAddressStart);
        Serial.WriteString(" -> physical 0x");
        Serial.WriteHex(physicalAddressStart);
        Serial.WriteString("\n");

        bool foundMatch = false;
        for (ulong i = 0; i < Limine.MemoryMap.Response->EntryCount; i++)
        {
            LimineMemmapEntry* entry = Limine.MemoryMap.Response->Entries[i];
            ulong entryStart = (ulong)entry->Base;
            ulong entryEnd = entryStart + entry->Length;

            // Check if our physical allocation overlaps with this memory region
            if (physicalAddressStart < entryEnd && physicalAddressEnd > entryStart)
            {
                foundMatch = true;
                Serial.WriteString("[PageAllocator] Physical region 0x");
                Serial.WriteHex(physicalAddressStart);
                Serial.WriteString("-0x");
                Serial.WriteHex(physicalAddressEnd);
                Serial.WriteString(" overlaps with entry ");
                Serial.WriteNumber(i);
                Serial.WriteString(" (0x");
                Serial.WriteHex(entryStart);
                Serial.WriteString("-0x");
                Serial.WriteHex(entryEnd);
                Serial.WriteString(") type: ");
                Serial.WriteNumber((uint)entry->Type);
                Serial.WriteString("\n");

                // Only allow allocation in usable memory
                if (entry->Type != LimineMemmapType.Usable)
                {
                    Serial.WriteString("[PageAllocator] ERROR: Attempting to allocate in non-usable memory region!\n");
                    return false;
                }
            }
        }

        if (!foundMatch)
        {
            Serial.WriteString("[PageAllocator] ERROR: Physical address 0x");
            Serial.WriteHex(physicalAddressStart);
            Serial.WriteString(" not found in any usable memory map entry!\n");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Init RAT based on Limine memory map usable regions only.
    /// This follows the Cosmos OS memory structure with RAT at the END of heap.
    /// </summary>
    /// <param name="aStartPtr">A pointer to the start of the heap (ignored, we use memory map).</param>
    /// <param name="aSize">A heap size hint (ignored, we use memory map).</param>
    /// <exception cref="Exception">Thrown if:
    /// <list type="bullet">
    /// <item>No usable memory found.</item>
    /// <item>RAM start or size is not page aligned.</item>
    /// </list>
    /// </exception>
    public static void InitializeHeap(byte* aStartPtr, ulong aSize)
    {
        Serial.WriteString("[PageAllocator] Initializing heap using Limine memory map...\n");

        // Find the largest usable memory region for our heap
        byte* usableStart = null;
        ulong usableSize = 0;

        if (Limine.MemoryMap.Response != null)
        {
            Serial.WriteString("[PageAllocator] Memory map detected with ");
            Serial.WriteNumber(Limine.MemoryMap.Response->EntryCount);
            Serial.WriteString(" entries:\n");

            // Display all memory map entries for debugging
            for (ulong i = 0; i < Limine.MemoryMap.Response->EntryCount; i++)
            {
                LimineMemmapEntry* entry = Limine.MemoryMap.Response->Entries[i];
                Serial.WriteString("[MemMap] Entry ");
                Serial.WriteNumber(i);
                Serial.WriteString(": 0x");
                Serial.WriteHex((ulong)entry->Base);
                Serial.WriteString(" - 0x");
                Serial.WriteHex((ulong)entry->Base + entry->Length);
                Serial.WriteString(" (");
                Serial.WriteNumber(entry->Length / 1024 / 1024);
                Serial.WriteString(" MB) Type: ");
                Serial.WriteNumber((uint)entry->Type);

                // Add type name for clarity
                switch (entry->Type)
                {
                    case LimineMemmapType.Usable:
                        Serial.WriteString(" (Usable)");
                        break;
                    case LimineMemmapType.Reserved:
                        Serial.WriteString(" (Reserved)");
                        break;
                    case LimineMemmapType.AcpiReclaimable:
                        Serial.WriteString(" (ACPI Reclaimable)");
                        break;
                    case LimineMemmapType.AcpiNvs:
                        Serial.WriteString(" (ACPI NVS)");
                        break;
                    case LimineMemmapType.BadMemory:
                        Serial.WriteString(" (Bad Memory)");
                        break;
                    case LimineMemmapType.BootloaderReclaimable:
                        Serial.WriteString(" (Bootloader Reclaimable)");
                        break;
                    case LimineMemmapType.KernelAndModules:
                        Serial.WriteString(" (Kernel and Modules)");
                        break;
                    case LimineMemmapType.Framebuffer:
                        Serial.WriteString(" (Framebuffer)");
                        break;
                    default:
                        Serial.WriteString(" (Unknown)");
                        break;
                }
                Serial.WriteString("\n");
            }

            Serial.WriteString("[PageAllocator] Searching for largest safe usable memory region...\n");

            // First, collect all protected regions (kernel, framebuffer, etc.)
            ulong kernelStart = 0;
            ulong kernelEnd = 0;
            ulong framebufferStart = 0;
            ulong framebufferEnd = 0;

            for (ulong i = 0; i < Limine.MemoryMap.Response->EntryCount; i++)
            {
                LimineMemmapEntry* entry = Limine.MemoryMap.Response->Entries[i];

                if (entry->Type == LimineMemmapType.KernelAndModules)
                {
                    kernelStart = (ulong)entry->Base;
                    kernelEnd = (ulong)entry->Base + entry->Length;
                    Serial.WriteString("[PageAllocator] Protected: Kernel/Modules 0x");
                    Serial.WriteHex(kernelStart);
                    Serial.WriteString(" - 0x");
                    Serial.WriteHex(kernelEnd);
                    Serial.WriteString(" (");
                    Serial.WriteNumber((kernelEnd - kernelStart) / 1024 / 1024);
                    Serial.WriteString(" MB)\n");
                }
                else if (entry->Type == LimineMemmapType.Framebuffer)
                {
                    framebufferStart = (ulong)entry->Base;
                    framebufferEnd = (ulong)entry->Base + entry->Length;
                    Serial.WriteString("[PageAllocator] Protected: Framebuffer 0x");
                    Serial.WriteHex(framebufferStart);
                    Serial.WriteString(" - 0x");
                    Serial.WriteHex(framebufferEnd);
                    Serial.WriteString(" (");
                    Serial.WriteNumber((framebufferEnd - framebufferStart) / 1024 / 1024);
                    Serial.WriteString(" MB)\n");
                }
            }

            // Find the LARGEST usable memory region that doesn't conflict with protected areas
            // Following Cosmos convention: avoid kernel, framebuffer, and other protected regions
            for (ulong i = 0; i < Limine.MemoryMap.Response->EntryCount; i++)
            {
                LimineMemmapEntry* entry = Limine.MemoryMap.Response->Entries[i];
                if (entry->Type == LimineMemmapType.Usable)
                {
                    ulong physStart = (ulong)entry->Base;
                    ulong physEnd = physStart + entry->Length;

                    // ARM64 note: Check if higher-half mapping is enabled
                    // If Limine already provides virtual addresses, don't add offset
                    // x64 Limine uses higher-half, ARM64 might use direct mapping
                    ulong virtualStart;
#if ARCH_ARM64
                    // ARM64: Use physical addresses directly (identity mapping)
                    virtualStart = physStart;
                    Serial.WriteString("[PageAllocator] ARM64: Using identity mapping (phys == virt)\n");
#else
                    // x64: Use higher-half mapping
                    virtualStart = physStart + 0xFFFF800000000000UL;
#endif
                    ulong entrySize = entry->Length;

                    Serial.WriteString("[PageAllocator] Evaluating region: phys 0x");
                    Serial.WriteHex(physStart);
                    Serial.WriteString(" - 0x");
                    Serial.WriteHex(physEnd);
                    Serial.WriteString(" (");
                    Serial.WriteNumber(entrySize / 1024 / 1024);
                    Serial.WriteString(" MB)");

                    // Check if this region overlaps with kernel/modules
                    bool overlapsKernel = false;
                    if (kernelStart > 0 && kernelEnd > 0)
                    {
                        // Regions overlap if one starts before the other ends
                        if (physStart < kernelEnd && physEnd > kernelStart)
                        {
                            overlapsKernel = true;
                            Serial.WriteString(" [OVERLAPS KERNEL]");
                        }
                    }

                    // Check if this region overlaps with framebuffer
                    bool overlapsFramebuffer = false;
                    if (framebufferStart > 0 && framebufferEnd > 0)
                    {
                        if (physStart < framebufferEnd && physEnd > framebufferStart)
                        {
                            overlapsFramebuffer = true;
                            Serial.WriteString(" [OVERLAPS FRAMEBUFFER]");
                        }
                    }

                    Serial.WriteString("\n");

                    // Skip regions that overlap with protected areas
                    if (overlapsKernel || overlapsFramebuffer)
                    {
                        Serial.WriteString("[PageAllocator] ⚠ Region overlaps protected memory, skipping\n");
                        continue;
                    }

                    // Use the largest safe usable region
                    if (entrySize > usableSize)
                    {
                        usableStart = (byte*)virtualStart;
                        usableSize = entrySize;

                        Serial.WriteString("[PageAllocator] ✓ Best candidate so far: ");
                        Serial.WriteNumber(usableSize / 1024 / 1024);
                        Serial.WriteString(" MB\n");
                    }
                }
            }
        }

        if (usableStart == null || usableSize == 0)
        {
            Serial.WriteString("[PageAllocator] ERROR: No usable memory found in memory map!\n");
            throw new Exception("No usable memory found in Limine memory map");
        }

        Serial.WriteString("[PageAllocator] Selected largest usable region:\n");
        Serial.WriteString("  Start (virt): 0x");
        Serial.WriteHex((ulong)usableStart);
        Serial.WriteString("\n  Size: ");
        Serial.WriteNumber(usableSize / 1024 / 1024);
        Serial.WriteString(" MB (");
        Serial.WriteNumber(usableSize);
        Serial.WriteString(" bytes)\n");

        // Check alignment
        if ((ulong)usableStart % PageSize != 0)
        {
            // Align start up to next page boundary
            ulong offset = PageSize - ((ulong)usableStart % PageSize);
            usableStart += offset;
            usableSize -= offset;
            Serial.WriteString("[PageAllocator] Aligned start to page boundary: 0x");
            Serial.WriteHex((ulong)usableStart);
            Serial.WriteString("\n");
        }

        if (usableSize % PageSize != 0)
        {
            // Align size down to page boundary
            usableSize = (usableSize / PageSize) * PageSize;
            Serial.WriteString("[PageAllocator] Aligned size to page boundary: ");
            Serial.WriteNumber(usableSize);
            Serial.WriteString(" bytes\n");
        }

        // Calculate total pages and RAT size
        // RAT needs 1 byte per page
        TotalPageCount = usableSize / PageSize;
        ulong xRatPageCount = (TotalPageCount - 1) / PageSize + 1;
        ulong xRatTotalSize = xRatPageCount * PageSize;

        Serial.WriteString("[PageAllocator] Total pages: ");
        Serial.WriteNumber(TotalPageCount);
        Serial.WriteString(", RAT pages: ");
        Serial.WriteNumber(xRatPageCount);
        Serial.WriteString(", RAT size: ");
        Serial.WriteNumber(xRatTotalSize / 1024);
        Serial.WriteString(" KB\n");

        // IMPORTANT: Following Cosmos OS structure, place RAT at the END of usable memory
        // Heap: [RamStart ... HeapEnd] [RAT]
        mRAT = usableStart + usableSize - xRatTotalSize;
        RamStart = usableStart;
        RamSize = usableSize - xRatTotalSize;
        HeapEnd = mRAT;  // Heap ends where RAT begins

        Serial.WriteString("[PageAllocator] Memory layout (Cosmos-style):\n");
        Serial.WriteString("  RamStart (heap): 0x");
        Serial.WriteHex((ulong)RamStart);
        Serial.WriteString("\n  HeapEnd: 0x");
        Serial.WriteHex((ulong)HeapEnd);
        Serial.WriteString("\n  RAT location: 0x");
        Serial.WriteHex((ulong)mRAT);
        Serial.WriteString("\n  RAT end: 0x");
        Serial.WriteHex((ulong)(mRAT + xRatTotalSize));
        Serial.WriteString("\n  Heap size: ");
        Serial.WriteNumber(RamSize / 1024 / 1024);
        Serial.WriteString(" MB\n");

        // Sanity checks
        if (mRAT < RamStart)
        {
            throw new Exception("RAT is before heap start - invalid memory layout!");
        }
        if ((ulong)mRAT % PageSize != 0)
        {
            throw new Exception("RAT is not page-aligned!");
        }

        // Initialize ALL RAT entries to Empty
        Serial.WriteString("[PageAllocator] Initializing ");
        Serial.WriteNumber(TotalPageCount);
        Serial.WriteString(" RAT entries...\n");

        // Test first write before loop
        Serial.WriteString("[PageAllocator] Testing first RAT write at 0x");
        Serial.WriteHex((ulong)mRAT);
        Serial.WriteString("...\n");
        mRAT[0] = (byte)PageType.Empty;
        Serial.WriteString("[PageAllocator] First write successful, initializing all entries...\n");

        for (ulong i = 0; i < TotalPageCount; i++)
        {
            mRAT[i] = (byte)PageType.Empty;

            // Progress indicator every 10000 pages to confirm loop is progressing
            if (i > 0 && i % 10000 == 0)
            {
                Serial.WriteString("[PageAllocator] Initialized ");
                Serial.WriteNumber(i);
                Serial.WriteString(" / ");
                Serial.WriteNumber(TotalPageCount);
                Serial.WriteString(" entries...\n");
            }
        }

        Serial.WriteString("[PageAllocator] All RAT entries initialized.\n");

        // Mark the RAT pages themselves as PageAllocator type
        // RAT pages are at the END, so we mark the LAST xRatPageCount pages
        ulong ratStartPage = TotalPageCount - xRatPageCount;
        Serial.WriteString("[PageAllocator] Marking RAT pages (");
        Serial.WriteNumber(xRatPageCount);
        Serial.WriteString(" pages starting at index ");
        Serial.WriteNumber(ratStartPage);
        Serial.WriteString(")...\n");

        for (ulong i = ratStartPage; i < TotalPageCount; i++)
        {
            mRAT[i] = (byte)PageType.PageAllocator;
        }

        // Free page count is total minus RAT pages
        FreePageCount = TotalPageCount - xRatPageCount;

        Serial.WriteString("[PageAllocator] Heap initialization complete!\n");
        Serial.WriteString("  Total pages: ");
        Serial.WriteNumber(TotalPageCount);
        Serial.WriteString("\n  Free pages: ");
        Serial.WriteNumber(FreePageCount);
        Serial.WriteString("\n  Usable heap: ");
        Serial.WriteNumber(FreePageCount * PageSize / 1024 / 1024);
        Serial.WriteString(" MB\n");

        // Test RAT is writable
        Serial.WriteString("[PageAllocator] Testing RAT write access...\n");
        byte testValue = mRAT[0];
        mRAT[0] = 123;
        if (mRAT[0] != 123)
        {
            Serial.WriteString("[PageAllocator] ERROR: RAT is not writable!\n");
            throw new Exception("RAT memory is not writable!");
        }
        mRAT[0] = testValue; // Restore
        Serial.WriteString("[PageAllocator] RAT write test passed\n");

        // Initialize small heap
        Serial.WriteString("[PageAllocator] Initializing SmallHeap...\n");
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
                if ((PageType)(*ptr) == PageType.Empty)
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

            if ((ulong)offset >= TotalPageCount)
                return null;

            mRAT[offset] = (byte)aType;

            for (ulong i = 1; i < aPageCount; i++)
            {
                mRAT[(ulong)offset + i] = (byte)PageType.Extension;
            }

            if (zero)
            {
                ulong* ptr = (ulong*)pageAddress;
                ulong count = (PageSize * aPageCount) / sizeof(ulong);
                for (ulong i = 0; i < count; i++)
                {
                    ptr[i] = 0;
                }
            }

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

    #region Debug Interface

    /// <summary>
    /// Update debug buffer with current memory state.
    /// The buffer can be read by debugging tools via GDB.
    /// </summary>
    /// <param name="ratSampleCount">Number of RAT entries to include in the sample</param>
    public static void UpdateDebugState(uint ratSampleCount = 1000)
    {
        MemoryDebug.UpdateMemoryState(
            RamStart,
            HeapEnd,
            mRAT,
            RamSize,
            TotalPageCount,
            FreePageCount,
            mRAT,
            ratSampleCount);
    }

    /// <summary>
    /// Get RAT pointer for debug purposes.
    /// </summary>
    public static byte* GetRatPointer() => mRAT;

    /// <summary>
    /// Get heap end pointer for debug purposes.
    /// </summary>
    public static byte* GetHeapEnd() => HeapEnd;

    #endregion
}
