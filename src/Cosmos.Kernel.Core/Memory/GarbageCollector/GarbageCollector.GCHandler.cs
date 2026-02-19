// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

/// <summary>
/// GC handle table: allocation, freeing, and weak handle cleanup for Weak, Normal, and Pinned handles.
/// </summary>
public static unsafe partial class GarbageCollector
{
    // --- Nested types ---

    /// <summary>
    /// Represents a single GC handle entry in the handle store.
    /// </summary>
    private struct GCHandle
    {
        /// <summary>
        /// Pointer to the managed object this handle references.
        /// </summary>
        public GCObject* obj;

        /// <summary>
        /// The type of this handle (Weak, Normal, Pinned, etc.).
        /// </summary>
        public GCHandleType type;

        /// <summary>
        /// Additional info associated with this handle (e.g., weak track resurrection flag).
        /// </summary>
        public nuint extraInfo;
    }

    // --- Static fields ---

    /// <summary>
    /// Segment used as the backing store for all GC handles.
    /// </summary>
    private static GCSegment* s_handlerStore;

    // --- Methods ---

    /// <summary>
    /// Initializes the GC handle store by allocating a dedicated segment.
    /// </summary>
    private static void InitializeGCHandleStore()
    {
        s_handlerStore = AllocateSegment((uint)(PageAllocator.PageSize - (ulong)sizeof(GCSegment)));
    }

    /// <summary>
    /// Allocates a new GC handle for the specified object.
    /// </summary>
    /// <param name="obj">Pointer to the managed object to track.</param>
    /// <param name="handleType">The type of handle to allocate.</param>
    /// <param name="extraInfo">Additional info associated with the handle.</param>
    /// <returns>An opaque handle value, or <see cref="IntPtr.Zero"/> if the store is full.</returns>
    [MethodImpl(MethodImplOptions.NoOptimization)]
    internal static IntPtr AllocateHandler(GCObject* obj, GCHandleType handleType, nuint extraInfo)
    {
        int size = (int)(s_handlerStore->End - s_handlerStore->Bump) / sizeof(GCHandle);

        var handles = new Span<GCHandle>((void*)Align((uint)s_handlerStore->Bump), size);
        for (int i = 0; i < handles.Length; i++)
        {
            if ((IntPtr)handles[i].obj == IntPtr.Zero)
            {
                handles[i].obj = obj;
                handles[i].type = handleType;
                handles[i].extraInfo = extraInfo;
                return (nint)(s_handlerStore->Bump + i * sizeof(GCHandle));
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Clears weak handles whose target objects were not marked during the mark phase.
    /// Called between mark and sweep to allow weak references to be collected.
    /// </summary>
    private static void FreeWeakHandles()
    {
        if (s_handlerStore == null)
        {
            return;
        }

        int size = (int)(s_handlerStore->End - s_handlerStore->Bump) / sizeof(GCHandle);

        var handles = new Span<GCHandle>((void*)Align((uint)s_handlerStore->Bump), size);
        for (int i = 0; i < handles.Length; i++)
        {
            if ((IntPtr)handles[i].obj != IntPtr.Zero)
            {
                if (handles[i].type < GCHandleType.Normal && !handles[i].obj->IsMarked)
                {
                    handles[i].obj = null;
                }
            }
        }
    }

    /// <summary>
    /// Frees a previously allocated GC handle, releasing the slot for reuse.
    /// </summary>
    /// <param name="handle">The handle to free. No-op if <see cref="IntPtr.Zero"/>.</param>
    internal static void FreeHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero && s_handlerStore != null)
        {
            // Calculate the handle index from the handle pointer
            nint handleIndex = (nint)(((nint)handle.ToInt64() - ((nint)s_handlerStore->Bump).ToInt64()) / (nint)sizeof(GCHandle));
            if (handleIndex >= 0)
            {
                int size = (int)(s_handlerStore->End - s_handlerStore->Bump) / sizeof(GCHandle);
                if (handleIndex < size)
                {
                    var handles = new Span<GCHandle>((void*)Align((uint)s_handlerStore->Bump), size);
                    handles[(int)handleIndex].obj = null;
                    handles[(int)handleIndex].type = default;
                    handles[(int)handleIndex].extraInfo = UIntPtr.Zero;
                }
            }
        }
    }
}
