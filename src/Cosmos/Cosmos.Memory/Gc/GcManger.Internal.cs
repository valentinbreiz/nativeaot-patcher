// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Memory.Heap;
using Internal.Runtime;

namespace Cosmos.Memory.Gc;

public static unsafe partial class GcManger
{

    internal static void SetEeType(byte* ptr, MethodTable* pEEType)
    {
        PageType pageType = PageAllocator.GetPageType(ptr);
        switch (pageType)
        {
            case PageType.HeapSmall:
            case PageType.SMT:
                SmallHeap.GetHeader(ptr)->Gc.MethodTable = (nint*)pEEType;
                break;
            case PageType.HeapMedium:
                MediumHeap.GetHeader(ptr)->Gc.MethodTable = (nint*)pEEType;
                break;
            case PageType.HeapLarge:
                LargeHeap.GetHeader(ptr)->Gc.MethodTable = (nint*)pEEType;
                break;
            case PageType.Empty:
            case PageType.Unmanaged:
            case PageType.PageDirectory:
            case PageType.PageAllocator:
            case PageType.Extension:
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    internal static MethodTable* GetEeType(byte* ptr)
    {
        PageType pageType = PageAllocator.GetPageType(ptr);
        switch (pageType)
        {
            case PageType.HeapSmall:
            case PageType.SMT:
                return (MethodTable*)SmallHeap.GetHeader(ptr)->Gc.MethodTable;
            case PageType.HeapMedium:
                return (MethodTable*)MediumHeap.GetHeader(ptr)->Gc.MethodTable;
            case PageType.HeapLarge:
                return (MethodTable*)LargeHeap.GetHeader(ptr)->Gc.MethodTable;
            case PageType.Empty:
            case PageType.Unmanaged:
            case PageType.PageDirectory:
            case PageType.PageAllocator:
            case PageType.Extension:
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    internal static void SetFlags(byte* ptr, uint flags)
    {
        PageType pageType = PageAllocator.GetPageType(ptr);
        switch (pageType)
        {
            case PageType.HeapSmall:
            case PageType.SMT:
                SmallHeap.GetHeader(ptr)->Gc.Flags = flags;
                break;
            case PageType.HeapMedium:
                MediumHeap.GetHeader(ptr)->Gc.Flags = flags;
                break;
            case PageType.HeapLarge:
                LargeHeap.GetHeader(ptr)->Gc.Flags = flags;
                break;
            case PageType.Empty:
            case PageType.Unmanaged:
            case PageType.PageDirectory:
            case PageType.PageAllocator:
            case PageType.Extension:
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    internal static uint GetFlags(byte* ptr)
    {
        PageType pageType = PageAllocator.GetPageType(ptr);
        switch (pageType)
        {
            case PageType.HeapSmall:
            case PageType.SMT:
                return SmallHeap.GetHeader(ptr)->Gc.Flags;
            case PageType.HeapMedium:
                return MediumHeap.GetHeader(ptr)->Gc.Flags;
            case PageType.HeapLarge:
                return LargeHeap.GetHeader(ptr)->Gc.Flags;
            case PageType.Empty:
            case PageType.Unmanaged:
            case PageType.PageDirectory:
            case PageType.PageAllocator:
            case PageType.Extension:
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    internal static object* AllocInternal(MethodTable* pEEType, uint uFlags, uint numElements)
    {
        if (pEEType->ContainsGCPointers)
        {
            uFlags |= GC_ALLOC_FLAGS.GC_ALLOC_CONTAINS_REF;
            uFlags &= ~GC_ALLOC_FLAGS.GC_ALLOC_ZEROING_OPTIONAL;
        }


        if (pEEType->HasComponentSize)
        {
            if (pEEType->IsSzArray)
            {
                if (numElements > Array.MaxLength)
                    return null;
            }
        }

        uint size = pEEType->BaseSize + (numElements * pEEType->ComponentSize);
        byte* ptr = Heap.Heap.Alloc(size);
        SetEeType(ptr, pEEType);
        SetFlags(ptr, uFlags);
        return (object*)ptr;
    }

}
