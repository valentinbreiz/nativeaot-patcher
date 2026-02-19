// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

public static unsafe partial class GarbageCollector
{
    // --- Nested types ---

    private struct GCHandle
    {
        public GCObject* obj;
        public GCHandleType type;
        public nuint extraInfo;
    }

    // --- Static fields ---

    private static GCSegment* s_handlerStore;

    // --- Methods ---

    public static void InitializeGCHandleStore()
    {
        s_handlerStore = AllocateSegment((uint)(PageAllocator.PageSize - (ulong)sizeof(GCSegment)));
    }

    [MethodImpl(MethodImplOptions.NoOptimization)]
    internal static IntPtr AllocateHandler(GCObject* obj, GCHandleType handleType, nuint extraInfo)
    {
        int size = (int)(s_handlerStore->TotalSize / sizeof(GCHandle));

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

    private static void FreeWeakHandles()
    {
        if (s_handlerStore == null)
        {
            return;
        }

        /*
        Serial.WriteString("Start: ");
        Serial.WriteHex((ulong)s_gcHeapMin);
        Serial.WriteString("\nEnd: ");
        Serial.WriteHex((ulong)s_gcHeapMax);
        Serial.WriteString("\n");
        */

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
