// GC Collector Implementation for NativeAOT Kernel
// Uses reference counting for automatic memory management

using System;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.Heap;

namespace Cosmos.Kernel.Core.Memory.GC;

/// <summary>
/// Garbage collector using reference counting.
/// Objects are automatically freed when their reference count reaches 0.
/// The Collect() method can be used to force cleanup of orphaned objects.
/// </summary>
public static unsafe class Collector
{
    /// <summary>
    /// Perform garbage collection.
    /// With reference counting, this mainly cleans up any orphaned objects
    /// (objects with refcount 0 that weren't freed due to issues).
    /// Returns the number of objects freed.
    /// </summary>
    public static int Collect()
    {
        Serial.WriteString("[GC] Running collection (ref counting cleanup)...\n");

        int freed = 0;

        // Sweep small heap for any objects with refcount 0
        freed += SweepSmallHeap();

        // Sweep large heap
        freed += SweepLargeHeap();

        // Prune empty SMT pages
        freed += SmallHeap.PruneSMT();

        Serial.WriteString("[GC] Collection complete. Freed: ");
        Serial.WriteNumber((ulong)freed);
        Serial.WriteString(" objects\n");

        return freed;
    }

    /// <summary>
    /// Sweep small heap for objects with refcount 0.
    /// </summary>
    private static int SweepSmallHeap()
    {
        int freed = 0;

        SMTPage* smtPage = SmallHeap.SMT;
        while (smtPage != null)
        {
            RootSMTBlock* rootBlock = smtPage->First;
            while (rootBlock != null)
            {
                uint size = rootBlock->Size;
                ulong slotSize = size + SmallHeap.PrefixBytes;
                ulong slotsPerPage = PageAllocator.PageSize / slotSize;

                SMTBlock* block = rootBlock->First;
                while (block != null)
                {
                    byte* pagePtr = block->PagePtr;

                    for (ulong i = 0; i < slotsPerPage; i++)
                    {
                        ushort* heapObject = (ushort*)(pagePtr + i * slotSize);

                        // heapObject[0] = size (0 if free)
                        // heapObject[1] = refcount
                        // If allocated (size != 0) but refcount is 0, free it
                        if (heapObject[0] != 0 && heapObject[1] == 0)
                        {
                            byte* objPtr = (byte*)heapObject + SmallHeap.PrefixBytes;
                            SmallHeap.Free(objPtr);
                            freed++;
                        }
                    }

                    block = block->NextBlock;
                }

                rootBlock = rootBlock->LargerSize;
            }

            smtPage = smtPage->Next;
        }

        return freed;
    }

    /// <summary>
    /// Sweep large heap for objects with refcount 0.
    /// </summary>
    private static int SweepLargeHeap()
    {
        int freed = 0;

        for (ulong i = 0; i < PageAllocator.TotalPageCount; i++)
        {
            byte* pagePtr = PageAllocator.RamStart + i * PageAllocator.PageSize;
            PageType pageType = PageAllocator.GetPageType(pagePtr);

            if (pageType == PageType.HeapMedium || pageType == PageType.HeapLarge)
            {
                LargeHeapHeader* header = (LargeHeapHeader*)pagePtr;

                // Gc.RefCount is the reference count for large objects
                if (header->Gc.RefCount == 0)
                {
                    byte* objPtr = pagePtr + (ulong)sizeof(LargeHeapHeader);
                    Heap.Heap.Free(objPtr);
                    freed++;
                }
            }
        }

        return freed;
    }

    /// <summary>
    /// Print reference counting statistics.
    /// </summary>
    public static void PrintStats()
    {
        Serial.WriteString("\n--- Reference Counting Stats ---\n");

        int totalObjects = 0;
        int zeroRefObjects = 0;
        ulong totalRefCount = 0;

        // Count small heap objects
        SMTPage* smtPage = SmallHeap.SMT;
        while (smtPage != null)
        {
            RootSMTBlock* rootBlock = smtPage->First;
            while (rootBlock != null)
            {
                uint size = rootBlock->Size;
                ulong slotSize = size + SmallHeap.PrefixBytes;
                ulong slotsPerPage = PageAllocator.PageSize / slotSize;

                SMTBlock* block = rootBlock->First;
                while (block != null)
                {
                    byte* pagePtr = block->PagePtr;

                    for (ulong i = 0; i < slotsPerPage; i++)
                    {
                        ushort* heapObject = (ushort*)(pagePtr + i * slotSize);

                        if (heapObject[0] != 0) // Allocated
                        {
                            totalObjects++;
                            totalRefCount += heapObject[1];
                            if (heapObject[1] == 0)
                            {
                                zeroRefObjects++;
                            }
                        }
                    }

                    block = block->NextBlock;
                }

                rootBlock = rootBlock->LargerSize;
            }

            smtPage = smtPage->Next;
        }

        Serial.WriteString("  Total Objects:     ");
        Serial.WriteNumber((ulong)totalObjects);
        Serial.WriteString("\n");

        Serial.WriteString("  Zero-Ref Objects:  ");
        Serial.WriteNumber((ulong)zeroRefObjects);
        Serial.WriteString("\n");

        Serial.WriteString("  Total Ref Count:   ");
        Serial.WriteNumber(totalRefCount);
        Serial.WriteString("\n");

        if (totalObjects > 0)
        {
            Serial.WriteString("  Avg Ref Count:     ");
            Serial.WriteNumber(totalRefCount / (ulong)totalObjects);
            Serial.WriteString("\n");
        }

        Serial.WriteString("--------------------------------\n");
    }
}
