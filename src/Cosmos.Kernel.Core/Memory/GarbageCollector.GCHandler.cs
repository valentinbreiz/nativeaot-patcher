
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.Memory;

public static unsafe partial class GarbageCollector
{
    private static GCSegment* handlerStore;

    public static void InitializeGCHandleStore()
    {
        handlerStore = AllocateSegment((uint)(PageAllocator.PageSize - (ulong)sizeof(GCSegment)));
        //handlerStore->Bump = (byte*)Align((uint)(handlerStore->Start + sizeof(GCHandle)));
    }

    [MethodImpl(MethodImplOptions.NoOptimization)]
    internal static IntPtr AllocateHandler(GCObject* obj, GCHandleType handleType, nuint extraInfo)
    {
        int size = (int)(handlerStore->TotalSize / sizeof(GCHandle));

        var handles = new Span<GCHandle>((void*)Align((uint)handlerStore->Bump), size);
        for(int i = 0; i < handles.Length; i++)
        {
            //var handle = handles[i];
            if((IntPtr)handles[i].obj == IntPtr.Zero)
            {
                handles[i].obj = obj;
                handles[i].type = handleType;
                handles[i].extraInfo = extraInfo;
                return (nint)(handlerStore->Bump + i * sizeof(GCHandle));
            }
        }
        /*
        var ptr = Align(handlerStore->Current);
        
        while(ptr < handlerStore->End)
        {
            var handle = (GCHandle*)ptr;

            if(handle->obj == null)
            {
                handle->obj = obj;
                handle->type = handleType;
                handle->extraInfo = extraInfo;
                return (nint)handle;
            }

            ptr = Align(ptr + sizeof(GCHandle));
        }
        */
        return IntPtr.Zero;
    }


    internal static void FreeHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero && handlerStore != null)
        {
            // Calculate the handle index from the handle pointer
            nint handleIndex = (nint)(((nint)handle.ToInt64() - ((nint)handlerStore->Bump).ToInt64()) / (nint)sizeof(GCHandle));
            if (handleIndex >= 0)
            {
                int size = (int)(handlerStore->End - handlerStore->Bump) / sizeof(GCHandle);
                if (handleIndex < size)
                {
                    var handles = new Span<GCHandle>((void*)Align((uint)handlerStore->Bump), size);
                    handles[(int)handleIndex].obj = null;
                    handles[(int)handleIndex].type = default;
                    handles[(int)handleIndex].extraInfo = UIntPtr.Zero;
                }
            }
        }
    }

    private struct GCHandle
    {
        public GCObject* obj;
        public GCHandleType type;
        public nuint extraInfo;
    }
}