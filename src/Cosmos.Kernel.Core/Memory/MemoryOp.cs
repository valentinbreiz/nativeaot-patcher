namespace Cosmos.Kernel.Core.Memory;

public static unsafe class MemoryOp
{
    public static void InitializeHeap(ulong heapBase, ulong heapSize) =>
        PageAllocator.InitializeHeap((byte*)heapBase, heapSize);

    public static void* Alloc(uint size) => Heap.Heap.Alloc(size);

    public static void Free(void* ptr) => Heap.Heap.Free(ptr);

    [Obsolete("Use new Span<byte>(dest, count).Fill(value) instead")]
    public static void MemSet(byte* dest, byte value, int count) => new Span<byte>(dest, count).Fill(value);

    [Obsolete("Use new Span<byte>(dest, count).Fill(value) instead")]
    public static void MemSet(uint* dest, uint value, int count) => new Span<uint>(dest, count).Fill(value);

    [Obsolete("Use new Span<uint>(src, count).CopyTo(new Span<uint>(dest, count)) instead")]
    public static void MemCopy(uint* dest, uint* src, int count) =>
        new Span<uint>(src, count).CopyTo(new Span<uint>(dest, count));

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
