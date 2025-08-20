// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Debug.Kernel;

namespace Cosmos.Memory.Heap;

/// <summary>
/// a basic Heap that uses PageAllocator
/// </summary>
public static unsafe class Heap
{
    /// <summary>
    /// Re-allocates or "re-sizes" data asigned to a pointer.
    /// The pointer specified must be the start of an allocated block in the heap.
    /// This shouldn't be used with objects as a new address is given when realocating memory.
    /// </summary>
    /// <param name="aPtr">Existing pointer</param>
    /// <param name="newSize">Size to extend to</param>
    /// <returns>New pointer with specified size while maintaining old data.</returns>
    public static byte* Realloc(byte* aPtr, uint newSize)
    {
        PageType currentType = PageAllocator.GetPageType(aPtr);

#if COSMOSDEBUG
        if (currentType != PageType.HeapSmall && currentType != PageType.HeapMedium &&
            currentType != PageType.HeapLarge)
        {
            Debugger.DoSendNumber(newSize);
            Debugger.DoSendNumber((uint)aPtr);
            Debugger.SendKernelPanic(Panics.NonManagedPage);
        }
#endif

        if (newSize > MediumHeap.MinSize && newSize <= MediumHeap.MaxSize)
        {
            if (currentType == PageType.HeapMedium)
            {
                return MediumHeap.Realloc(aPtr, newSize);
            }

            int oldSize = 0;
            if (currentType == PageType.HeapLarge)
            {
                LargeHeapHeader* header = LargeHeap.GetHeader(aPtr);
                oldSize = (int)header->Size;
            }
            else if (currentType == PageType.HeapSmall)
            {
                SmallHeapHeader* header = SmallHeap.GetHeader(aPtr);
                oldSize = (int)header->Size;
            }
            else
            {
                throw new Exception();
            }

            byte* newPtr = MediumHeap.Alloc(newSize);
            Span<byte> span = new(aPtr, oldSize);
            span.CopyTo(new Span<byte>(newPtr, (int)newSize));
            return newPtr;
        }

        if (newSize >= LargeHeap.MinSize)
        {
            return LargeHeap.Alloc(newSize);
        }

        return (byte*)0;
    }

    /// <summary>
    /// Alloc memory block, of a given size.
    /// </summary>
    /// <param name="aSize">A size of block to alloc, in bytes.</param>
    /// <returns>Byte pointer to the start of the block.</returns>
    public static byte* Alloc(uint aSize)
    {
        if (aSize > MediumHeap.MinSize && aSize <= MediumHeap.MaxSize)
        {
            return MediumHeap.Alloc(aSize);
        }

        if (aSize > LargeHeap.MinSize)
        {
            return LargeHeap.Alloc(aSize);
        }

        return SmallHeap.Alloc(aSize);
    }

    // Keep as void* and not byte* or other. Reduces typecasting from callers
    // who may have typed the pointer to their own needs.
    /// <summary>
    /// Free a heap item.
    /// </summary>
    /// <param name="aPtr">A pointer to the heap item to be freed.</param>
    /// <exception cref="Exception">Thrown if:
    /// <list type="bullet">
    /// <item>Page type is not found.</item>
    /// <item>Heap item not found in RAT.</item>
    /// </list>
    /// </exception>
    public static void Free(void* aPtr)
    {
        PageType currentType = PageAllocator.GetPageType(aPtr);
        switch (currentType)
        {
            case PageType.HeapLarge:
                LargeHeap.Free(aPtr);
                break;
            case PageType.HeapMedium:
                MediumHeap.Free(aPtr);
                break;
            case PageType.HeapSmall:
                SmallHeap.Free(aPtr);
                break;
            default:
                Debugger.SendKernelPanic(Panics.NonManagedPage);
                throw new NotSupportedException("This is not a managed page");
        }
    }

    /// <summary>
    /// Collects all unreferenced objects after identifying them first
    /// </summary>
    /// <returns>Number of objects freed</returns>
    public static int Collect() => SmallHeap.PruneSMT() + LargeHeap.Collect() + MediumHeap.Collect();
}
