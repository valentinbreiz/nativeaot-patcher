namespace Cosmos.Kernel.Core.Memory;

public static unsafe class MemoryOp
{
    private static ulong HeapBase;
    private static ulong HeapEnd;
    private static ulong FreeListHead;

    public static void InitializeHeap(ulong heapBase, ulong heapSize)
    {
        HeapBase = heapBase;
        HeapEnd = heapBase + heapSize;
        FreeListHead = heapBase;

        // Initialize the free list with a single large block
        *(ulong*)FreeListHead = heapSize; // Block size
        *((ulong*)FreeListHead + 1) = 0; // Next block pointer
    }

    public static void* Alloc(uint size)
    {
        size = (uint)((size + 7) & ~7); // Align size to 8 bytes
        ulong prev = 0;
        ulong current = FreeListHead;

        while (current != 0)
        {
            ulong blockSize = *(ulong*)current;
            ulong next = *((ulong*)current + 1);

            if (blockSize >= size + 16) // Enough space for allocation and metadata
            {
                ulong remaining = blockSize - size - 16;
                if (remaining >= 16) // Split block
                {
                    *(ulong*)(current + 16 + size) = remaining;
                    *((ulong*)(current + 16 + size) + 1) = next;
                    *((ulong*)current + 1) = current + 16 + size;
                }
                else // Use entire block
                {
                    size = (uint)blockSize - 16;
                    *((ulong*)current + 1) = next;
                }

                if (prev == 0)
                {
                    FreeListHead = *((ulong*)current + 1);
                }
                else
                {
                    *((ulong*)prev + 1) = *((ulong*)current + 1);
                }

                *(ulong*)current = size; // Store allocated size
                return (void*)(current + 16);
            }

            prev = current;
            current = next;
        }

        return null; // Out of memory
    }

    public static void Free(void* ptr)
    {
        ulong block = (ulong)ptr - 16;
        ulong blockSize = *(ulong*)block;

        *(ulong*)block = blockSize + 16;
        *((ulong*)block + 1) = FreeListHead;
        FreeListHead = block;
    }

    public static void MemSet(byte* dest, byte value, int count)
    {
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

    public static void MemCopy(uint* dest, uint* src, int count)
    {
        for (int i = 0; i < count; i++)
        {
            dest[i] = src[i];
        }
    }

    public static void MemMove(byte* dest, byte* src, int count)
    {
        if (dest == src || count == 0)
        {
            return;
        }

        if (dest < src)
        {
            for (int i = 0; i < count; i++)
            {
                dest[i] = src[i];
            }
        }
        else
        {
            for (int i = count - 1; i >= 0; i--)
            {
                dest[i] = src[i];
            }
        }
    }

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
}

