// Reference Counting Implementation for NativeAOT Kernel
// Tracks object references and auto-frees when count reaches 0

using System;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.Heap;

namespace Cosmos.Kernel.Core.Memory.GC;

/// <summary>
/// Reference counting memory management.
/// Each heap object has a reference count in its header.
/// When count reaches 0, the object is automatically freed.
/// </summary>
public static unsafe class RefCount
{
    /// <summary>
    /// Increment the reference count for an object.
    /// Call this when creating a new reference to an object.
    /// </summary>
    /// <param name="obj">Object to increment ref count for</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncRef(void* obj)
    {
        if (obj == null) return;

        PageType pageType = PageAllocator.GetPageType(obj);

        if (pageType == PageType.HeapSmall)
        {
            // SmallHeap: refcount is in heapObject[1] (ushort)
            ushort* heapObject = (ushort*)((byte*)obj - SmallHeap.PrefixBytes);
            if (heapObject[0] != 0) // Check if allocated
            {
                heapObject[1]++;
            }
        }
        else if (pageType == PageType.HeapMedium || pageType == PageType.HeapLarge)
        {
            // Large/Medium heap: refcount in header
            byte* pagePtr = PageAllocator.GetPagePtr(obj);
            LargeHeapHeader* header = (LargeHeapHeader*)pagePtr;
            header->Gc.RefCount++;
        }
    }

    /// <summary>
    /// Decrement the reference count for an object.
    /// If count reaches 0, the object is freed.
    /// Call this when a reference is being removed/overwritten.
    /// </summary>
    /// <param name="obj">Object to decrement ref count for</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DecRef(void* obj)
    {
        if (obj == null) return;

        PageType pageType = PageAllocator.GetPageType(obj);

        if (pageType == PageType.HeapSmall)
        {
            ushort* heapObject = (ushort*)((byte*)obj - SmallHeap.PrefixBytes);
            if (heapObject[0] != 0) // Check if allocated
            {
                if (heapObject[1] > 0)
                {
                    heapObject[1]--;
                    if (heapObject[1] == 0)
                    {
                        // Refcount reached 0 - free the object
                        FreeObjectAndReferences(obj, pageType);
                    }
                }
            }
        }
        else if (pageType == PageType.HeapMedium || pageType == PageType.HeapLarge)
        {
            byte* pagePtr = PageAllocator.GetPagePtr(obj);
            LargeHeapHeader* header = (LargeHeapHeader*)pagePtr;
            if (header->Gc.RefCount > 0)
            {
                header->Gc.RefCount--;
                if (header->Gc.RefCount == 0)
                {
                    FreeObjectAndReferences(obj, pageType);
                }
            }
        }
    }

    /// <summary>
    /// Get the current reference count for an object.
    /// </summary>
    /// <param name="obj">Object to query</param>
    /// <returns>Reference count, or 0 if not a managed object</returns>
    public static uint GetRefCount(void* obj)
    {
        if (obj == null) return 0;

        PageType pageType = PageAllocator.GetPageType(obj);

        if (pageType == PageType.HeapSmall)
        {
            ushort* heapObject = (ushort*)((byte*)obj - SmallHeap.PrefixBytes);
            if (heapObject[0] != 0)
            {
                return heapObject[1];
            }
        }
        else if (pageType == PageType.HeapMedium || pageType == PageType.HeapLarge)
        {
            byte* pagePtr = PageAllocator.GetPagePtr(obj);
            LargeHeapHeader* header = (LargeHeapHeader*)pagePtr;
            return header->Gc.RefCount;
        }

        return 0;
    }

    /// <summary>
    /// Set initial reference count for a newly allocated object.
    /// Called by the allocator after creating an object.
    /// </summary>
    /// <param name="obj">Newly allocated object</param>
    /// <param name="initialCount">Initial reference count (typically 1)</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetInitialRefCount(void* obj, uint initialCount)
    {
        if (obj == null) return;

        PageType pageType = PageAllocator.GetPageType(obj);

        if (pageType == PageType.HeapSmall)
        {
            ushort* heapObject = (ushort*)((byte*)obj - SmallHeap.PrefixBytes);
            heapObject[1] = (ushort)initialCount;
        }
        else if (pageType == PageType.HeapMedium || pageType == PageType.HeapLarge)
        {
            byte* pagePtr = PageAllocator.GetPagePtr(obj);
            LargeHeapHeader* header = (LargeHeapHeader*)pagePtr;
            header->Gc.RefCount = initialCount;
        }
    }

    /// <summary>
    /// Free an object and decrement references to objects it points to.
    /// This handles cascading frees for object graphs.
    /// </summary>
    private static void FreeObjectAndReferences(void* obj, PageType pageType)
    {
        // First, decrement refcounts of any objects this object references
        // This requires knowing the object's type and field layout
        DecRefChildObjects(obj);

        // Now free this object
        if (pageType == PageType.HeapSmall)
        {
            SmallHeap.Free(obj);
        }
        else if (pageType == PageType.HeapMedium)
        {
            MediumHeap.Free(obj);
        }
        else if (pageType == PageType.HeapLarge)
        {
            LargeHeap.Free(obj);
        }
    }

    /// <summary>
    /// Decrement reference counts of all objects referenced by this object.
    /// Uses MethodTable to find reference fields.
    /// </summary>
    private static void DecRefChildObjects(void* obj)
    {
        if (obj == null) return;

        // Get MethodTable to find reference fields
        Internal.Runtime.MethodTable* mt = *(Internal.Runtime.MethodTable**)obj;
        if (mt == null) return;

        // If object doesn't contain GC pointers, nothing to do
        if (!mt->ContainsGCPointers) return;

        // For arrays of references, handle specially
        if (mt->IsArray && mt->ComponentSize == sizeof(nint))
        {
            // Array of references
            int length = *(int*)((byte*)obj + sizeof(nint));
            if (length > 0)
            {
                byte* elementsStart = (byte*)obj + sizeof(nint) + sizeof(int);
                // Align to pointer boundary
                nint alignment = (nint)elementsStart % sizeof(nint);
                if (alignment != 0)
                    elementsStart += sizeof(nint) - alignment;

                for (int i = 0; i < length; i++)
                {
                    void* element = *(void**)(elementsStart + i * sizeof(nint));
                    if (element != null)
                    {
                        DecRef(element);
                    }
                }
            }
            return;
        }

        // For regular objects, use GCDesc to find reference fields
        // GCDesc is stored before MethodTable
        nint* pNumSeries = (nint*)mt - 1;
        nint numSeries = *pNumSeries;

        if (numSeries <= 0) return;

        uint baseSize = mt->RawBaseSize;
        GCDescSeries* series = (GCDescSeries*)pNumSeries - 1;

        for (nint i = 0; i < numSeries; i++)
        {
            nint seriesSize = series->SeriesSize + (nint)baseSize;
            nint startOffset = series->StartOffset;
            nint numPointers = seriesSize / sizeof(nint);

            byte* fieldPtr = (byte*)obj + startOffset;
            for (nint j = 0; j < numPointers; j++)
            {
                void* referencedObj = *(void**)fieldPtr;
                if (referencedObj != null)
                {
                    DecRef(referencedObj);
                }
                fieldPtr += sizeof(nint);
            }

            series--;
        }
    }

    /// <summary>
    /// Handle reference assignment: decrement old value, increment new value.
    /// This is the core of reference counting - called on every reference store.
    /// </summary>
    /// <param name="location">Location being written to</param>
    /// <param name="newValue">New reference value</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AssignRef(void** location, void* newValue)
    {
        void* oldValue = *location;

        // Increment new value first (prevents premature free if same object)
        if (newValue != null)
        {
            IncRef(newValue);
        }

        // Store new value
        *location = newValue;

        // Decrement old value (may trigger free)
        if (oldValue != null)
        {
            DecRef(oldValue);
        }
    }
}

/// <summary>
/// GCDesc series structure for finding reference fields.
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
internal struct GCDescSeries
{
    public nint SeriesSize;
    public nint StartOffset;
}
