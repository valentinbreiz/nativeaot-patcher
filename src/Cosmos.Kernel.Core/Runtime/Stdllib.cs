using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.Memory;
using Internal.Runtime;

#region Things needed by ILC
namespace System
{
    namespace Runtime
    {
        internal enum InternalGCCollectionMode
        {
            Default,
            Forced,
            Optimized
        }

        public sealed class RuntimeExportAttribute(string entry) : Attribute
        {
        }

        public sealed class RuntimeImportAttribute : Attribute
        {
            public string DllName { get; }
            public string EntryPoint { get; }

            public RuntimeImportAttribute(string entry)
            {
                EntryPoint = entry;
            }

            public RuntimeImportAttribute(string dllName, string entry)
            {
                EntryPoint = entry;
                DllName = dllName;
            }
        }
    }
}

namespace System.Runtime.CompilerServices
{
    // Calls to methods or references to fields marked with this attribute may be replaced at
    // some call sites with jit intrinsic expansions.
    // Types marked with this attribute may be specially treated by the runtime/compiler.
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Field, Inherited = false)]
    internal sealed class IntrinsicAttribute : Attribute
    {
    }
}
namespace Cosmos.Kernel.Core.Runtime
{
    // A class that the compiler looks for that has helpers to initialize the
    // process. The compiler can gracefully handle the helpers not being present,
    // but the class itself being absent is unhandled. Let's add an empty class.
    internal static unsafe partial class StartupCodeHelpers
    {
        [RuntimeExport("RhpReversePInvoke")]
        private static void RhpReversePInvoke(IntPtr frame) { }
        [RuntimeExport("RhpReversePInvokeReturn")]
        private static void RhpReversePInvokeReturn(IntPtr frame) { }
        [RuntimeExport("RhpPInvoke")]
        private static void RhpPInvoke(IntPtr frame) { }
        [RuntimeExport("RhpPInvokeReturn")]
        private static void RhpPInvokeReturn(IntPtr frame) { }

        [RuntimeExport("RhpFallbackFailFast")]
        private static void RhpFallbackFailFast()
        {
            ExceptionHelper.FailFast("Fallback fail fast called");
        }

        [RuntimeExport("InitializeModules")]
        private static unsafe void InitializeModules(IntPtr osModule, IntPtr* pModuleHeaders, int count, IntPtr* pClasslibFunctions, int nClasslibFunctions) { }

        // RhpThrowEx is now implemented in assembly (CPU/ExceptionHandling.asm)
        // The assembly stub saves register context, creates ExInfo, then calls RhThrowEx

        /// <summary>
        /// Managed exception dispatcher called from assembly RhpThrowEx.
        /// This is the entry point for the two-pass exception handling.
        /// </summary>
        [RuntimeExport("RhThrowEx")]
        private static void RhThrowEx(Exception ex, void* pExInfo)
        {
            // Get throw address and context from ExInfo
            nuint throwAddress = 0;
            nuint throwFp = 0;  // Frame pointer (RBP on x64, FP/x29 on ARM64)
            nuint throwSp = 0;  // Stack pointer

            if (pExInfo != null)
            {
                // ExInfo.m_pExContext points to PAL_LIMITED_CONTEXT
                void* pContext = *(void**)((byte*)pExInfo + 0x08); // OFFSETOF__ExInfo__m_pExContext
                if (pContext != null)
                {
#if ARCH_X64
                    // x64 PAL_LIMITED_CONTEXT layout:
                    // 0x00: IP (instruction pointer / return address)
                    // 0x08: Rsp
                    // 0x10: Rbp
                    throwAddress = *(nuint*)((byte*)pContext + 0x00);
                    throwSp = *(nuint*)((byte*)pContext + 0x08);
                    throwFp = *(nuint*)((byte*)pContext + 0x10);
#elif ARCH_ARM64
                    // ARM64 PAL_LIMITED_CONTEXT layout:
                    // 0x00: SP (stack pointer)
                    // 0x08: IP (instruction pointer / LR)
                    // 0x10: FP (frame pointer / x29)
                    throwSp = *(nuint*)((byte*)pContext + 0x00);
                    throwAddress = *(nuint*)((byte*)pContext + 0x08);
                    throwFp = *(nuint*)((byte*)pContext + 0x10);
#endif
                }
            }

            // Use our exception handling infrastructure
            ExceptionHelper.ThrowExceptionWithContext(ex, throwAddress, throwFp, throwSp, pExInfo);
        }

        [RuntimeExport("RhpAssignRef")]
        private static unsafe void RhpAssignRef(void** location, void* value)
        {
            *location = value;
        }

        [RuntimeExport("RhpCheckedAssignRef")]
        private static unsafe void RhpCheckedAssignRef(void** location, void* value)
        {
            *location = value;
        }

        [RuntimeExport("RhpCheckedXchg")]
        private static void* InterlockedExchange(void** location1, void* value)
        {
            void* original = *location1;
            *location1 = value;
            return original;
        }


        [RuntimeExport("RhpTrapThreads")]
        static void RhpTrapThreads() { }

        [RuntimeExport("RhpGcPoll")]
        static void RhpGcPoll() { }

        [RuntimeExport("RhpStackProbe")]
        static void RhpStackProbe()
        {

        }

        [RuntimeExport("RhGetRuntimeVersion")]
        static int RhGetRuntimeVersion()
        {
            return 0;
        }

        [RuntimeExport("RhBulkMoveWithWriteBarrier")]
        internal static unsafe void RhBulkMoveWithWriteBarrier(void* dest, void* src, UIntPtr len)
        {
            memmove((byte*)dest, (byte*)src, len);
        }

        [RuntimeExport("RhpCheckedLockCmpXchg")]
        static unsafe object RhpCheckedLockCmpXchg(object* location, object value, object comparand, int typeHandle)
        {
            object original = *location;
            if (original == comparand)
                *location = value;
            return original;
        }

        [RuntimeExport("RhGetProcessCpuCount")]
        static int RhGetProcessCpuCount()
        {
            return 1;
        }

        [RuntimeExport("RhSuppressFinalize")]
        static void RhSuppressFinalize(object obj) { }

        [RuntimeExport("RhReRegisterForFinalize")]
        static void RhReRegisterForFinalize(object obj) { }

        [RuntimeExport("RhGetMemoryInfo")]
        static void RhGetMemoryInfo(IntPtr pMemInfo) { }

        [RuntimeExport("RhNewArray")]
        static unsafe void* RhNewArray(MethodTable* pEEType, int length)
        {
            void* result;
            Memory.RhAllocateNewArray(pEEType, (uint)length, 0, out result);
            return result;
        }

        /// <summary>
        /// Returns the MethodTable* for System.Array. This is used by the runtime when
        /// it needs to determine the base type of array types.
        /// </summary>
        [RuntimeExport("GetSystemArrayEEType")]
        static unsafe MethodTable* GetSystemArrayEEType()
        {
            return MethodTable.Of<Array>();
        }

        [RuntimeExport("RhNewObject")]
        static unsafe void* RhNewObject(MethodTable* pEEType)
        {
            return Memory.RhpNewFast(pEEType); // Simplified implementation
        }

        [RuntimeExport("RhHandleSet")]
        static IntPtr RhHandleSet(object obj)
        {
            return IntPtr.Zero;
        }

        [RuntimeExport("RhHandleFree")]
        static void RhHandleFree(IntPtr handle) { }


        [RuntimeExport("RhpStelemRef")]
        static unsafe void RhpStelemRef(object?[] array, nint index, object? obj)
        {
            if (array is null)
                throw new NullReferenceException();

            ref object rawData = ref MemoryMarshal.GetArrayDataReference(array)!;
            ref object element = ref Unsafe.Add(ref rawData, index);

            if (obj == null)
            {
                element = null!;
                return;
            }

            void* objPtr = Unsafe.AsPointer(ref obj);  // Get address of the obj parameter
            void* actualObjPtr = *(void**)objPtr;       // Dereference to get the actual object pointer
            RhpAssignRef((void**)Unsafe.AsPointer(ref element), actualObjPtr);
        }

        [RuntimeExport("RhCurrentOSThreadId")]
        static ulong RhCurrentOSThreadId()
        {
            return 1; // Single thread for now
        }

        [RuntimeExport("RhGetCrashInfoBuffer")]
        static IntPtr RhGetCrashInfoBuffer()
        {
            return IntPtr.Zero;
        }

        [RuntimeExport("RhCreateCrashDumpIfEnabled")]
        static void RhCreateCrashDumpIfEnabled(IntPtr exceptionRecord, IntPtr contextRecord) { }

        [RuntimeExport("RhpByRefAssignRef")]
        static unsafe void RhpByRefAssignRef(void** location, void* value)
        {
#if ARCH_ARM64
            RhpByRefAssignRefArm64(location, value);
#else
            *location = value;
#endif
        }

#if ARCH_ARM64
        [DllImport("*", EntryPoint = "RhpByRefAssignRefArm64")]
        private static extern unsafe void RhpByRefAssignRefArm64(void** location, void* value);
#endif

        [RuntimeExport("RhpNewFinalizable")]
        static unsafe void* RhpNewFinalizable(MethodTable* pEEType)
        {
            // TODO: Should actually be Memory.RhAllocateNewObject with finalizable flag
            return Memory.RhpNewFast(pEEType); // Simplified implementation (Should set gc flag)
        }

        // RhpRethrow is now implemented in assembly (CPU/ExceptionHandling.asm)

        [RuntimeExport("RhSpinWait")]
        static void RhSpinWait(int iterations)
        {
            // Simple spin wait
            for (int i = 0; i < iterations; i++)
            {
                // Spin
            }
        }

        [RuntimeExport("RhSetThreadExitCallback")]
        static void RhSetThreadExitCallback(IntPtr callback) { }

        [RuntimeExport("RhCompatibleReentrantWaitAny")]
        static uint RhCompatibleReentrantWaitAny(int alertable, uint timeout, uint handleCount, IntPtr pHandles)
        {
            // Single-threaded kernel: always return success immediately
            return 0x00000000; // WAIT_OBJECT_0 (SUCCESS)
        }

        [RuntimeExport("RhYield")]
        static int RhYield()
        {
            return 0;
        }

        [RuntimeExport("RhpHandleAlloc")]
        static IntPtr RhpHandleAlloc(object obj, bool fPinned)
        {
            //TODO: Implement GC
            return (IntPtr)Unsafe.AsPointer(ref obj);
        }

        [RuntimeExport("RhpHandleAllocDependent")]
        static IntPtr RhpHandleAllocDependent(IntPtr primary, object secondary)
        {
            //TODO: Implement GC
            return primary;
        }

        [RuntimeExport("RhBuffer_BulkMoveWithWriteBarrier")]
        static unsafe void RhBuffer_BulkMoveWithWriteBarrier(void* dest, void* src, UIntPtr len)
        {
            memmove((byte*)dest, (byte*)src, len);
        }

        [RuntimeExport("RhFindMethodStartAddress")]
        static IntPtr RhFindMethodStartAddress(IntPtr ip)
        {
            return ip;
        }

        [RuntimeExport("RhGetCurrentThreadStackTrace")]
        static IntPtr RhGetCurrentThreadStackTrace(int skipFrames, int maxFrames, out int pFrameCount)
        {
            pFrameCount = 0;
            return IntPtr.Zero;
        }

        [RuntimeExport("RhpLdelemaRef")]
        public static unsafe ref object? RhpLdelemaRef(object?[] array, nint index, MethodTable* elementType)
        {

            ref object rawData = ref MemoryMarshal.GetArrayDataReference(array)!;
            ref object element = ref Unsafe.Add(ref rawData, index);

            MethodTable* arrayElemType = array.GetMethodTable()->RelatedParameterType;

            /* This is causing issues, disabling for now.
            if (elementType != arrayElemType)
                throw new ArrayTypeMismatchException();
            */

            return ref element;
        }

        [RuntimeExport("RhGetCodeTarget")]
        static IntPtr RhGetCodeTarget(IntPtr pCode)
        {
            return pCode;
        }

        [RuntimeExport("RhGetTargetOfUnboxingAndInstantiatingStub")]
        static IntPtr RhGetTargetOfUnboxingAndInstantiatingStub(IntPtr pCode)
        {
            return pCode;
        }

        [RuntimeExport("RhSpanHelpers_MemZero")]
        static unsafe void RhSpanHelpers_MemZero(byte* dest, nuint len)
        {
            MemoryOp.MemSet(dest, 0, (int)len);
        }

        [RuntimeExport("RhpDbl2Lng")]
        static long RhpDbl2Lng(double value)
        {
            return (long)value;
        }

        [RuntimeExport("RhpDbl2Int")]
        static int RhpDbl2Int(double value)
        {
            return (int)value;
        }

        private static unsafe void memmove(byte* dest, byte* src, UIntPtr len)
        {
            MemoryOp.MemMove(dest, src, (int)len);
        }

    }
}

/*
                [RuntimeExport("RhUnbox2")]
                static unsafe object RhUnbox2(object obj) { throw null; }
                [RuntimeExport("RhpCheckedAssignRef")]
                static unsafe void RhpCheckedAssignRef(object* location, object value, int typeHandle) { }
                [RuntimeExport("RhpTrapThreads")]
                static void RhpTrapThreads() { }
                [RuntimeExport("RhpGcPoll")]
                static void RhpGcPoll() { }
                [RuntimeExport("RhSpanHelpers_MemZero")]
                static unsafe void RhSpanHelpers_MemZero(byte* dest, int len) { }
                [RuntimeExport("RhGetOSModuleFromPointer")]
                static IntPtr RhGetOSModuleFromPointer(IntPtr ptr) { throw null; }
                [RuntimeExport("RhGetRuntimeVersion")]
                static int RhGetRuntimeVersion() { return 0; }
                [RuntimeExport("RhBulkMoveWithWriteBarrier")]
                static unsafe void RhBulkMoveWithWriteBarrier(void* dest, void* src, UIntPtr len) { }
                [RuntimeExport("RhHandleFree")]
                static void RhHandleFree(IntPtr handle) { }
                [RuntimeExport("RhHandleSet")]
                static IntPtr RhHandleSet(object obj) { throw null; }
                [RuntimeExport("RhpNewFinalizable")]
                static unsafe object RhpNewFinalizable(int typeHandle) { throw null; }
                [RuntimeExport("RhTypeCast_AreTypesAssignable")]
                static bool RhTypeCast_AreTypesAssignable(int typeHandleSrc, int typeHandleDest) { throw null; }
                [RuntimeExport("RhTypeCast_IsInstanceOfInterface")]
                static bool RhTypeCast_IsInstanceOfInterface(object obj, int interfaceTypeHandle) { throw null; }
                [RuntimeExport("RhNewObject")]
                static unsafe object RhNewObject(int typeHandle) { throw null; }
                [RuntimeExport("RhpCheckedLockCmpXchg")]
                static unsafe object RhpCheckedLockCmpXchg(object* location, object value, object comparand, int typeHandle) { throw null; }
                [RuntimeExport("RhGetProcessCpuCount")]
                static int RhGetProcessCpuCount() { throw null; }
                [RuntimeExport("RhSuppressFinalize")]
                static void RhSuppressFinalize(object obj) { }
                [RuntimeExport("RhReRegisterForFinalize")]
                static void RhReRegisterForFinalize(object obj) { }
                [RuntimeExport("RhGetGcCollectionCount")]
                static int RhGetGcCollectionCount(int generation) { throw null; }
                [RuntimeExport("RhGetGcTotalMemory")]
                static long RhGetGcTotalMemory(bool forceFullCollection) { throw null; }
                [RuntimeExport("RhCollect")]
                static void RhCollect(int generation, InternalGCCollectionMode mode) { }
                [RuntimeExport("RhWaitForPendingFinalizers")]
                static void RhWaitForPendingFinalizers() { }
                [RuntimeExport("RhGetMemoryInfo")]
                static void RhGetMemoryInfo(IntPtr pMemInfo) { }
                [RuntimeExport("RhTypeCast_IsInstanceOfAny")]
                static unsafe object RhTypeCast_IsInstanceOfAny(object obj, int* pTypeHandles, int count) { throw null; }
                [RuntimeExport("RhUnbox")]
                static unsafe void* RhUnbox(object obj) { throw null; }
                [RuntimeExport("RhTypeCast_CheckArrayStore")]
                static unsafe void RhTypeCast_CheckArrayStore(object array, object value) { }
                [RuntimeExport("RhCurrentOSThreadId")]
                static int RhCurrentOSThreadId() { throw null; }
                [RuntimeExport("RhGetCrashInfoBuffer")]
                static IntPtr RhGetCrashInfoBuffer() { throw null; }
                [RuntimeExport("RhTypeCast_IsInstanceOfClass")]
                static unsafe object RhTypeCast_IsInstanceOfClass(object obj, int classTypeHandle) { throw null; }
                [RuntimeExport("RhTypeCast_CheckCastInterface")]
                static unsafe object RhTypeCast_CheckCastInterface(object obj, int interfaceTypeHandle) { throw null; }
                [RuntimeExport("RhpRethrow")]
                static void RhpRethrow() { while (true) ; }
                [RuntimeExport("RhpStackProbe")]
                static void RhpStackProbe() { }
                [RuntimeExport("RhSpanHelpers_MemCopy")]
                static unsafe void RhSpanHelpers_MemCopy(byte* dest, byte* src, int len) { }
                [RuntimeExport("RhTypeCast_CheckCastClassSpecial")]
                static unsafe object RhTypeCast_CheckCastClassSpecial(object obj, int typeHandle, byte fThrow) { throw null; }
                [RuntimeExport("RhpLdelemaRef")]
                static unsafe object* RhpLdelemaRef(object array, int index, int typeHandle) { throw null; }
                [RuntimeExport("RhpByRefAssignRef")]
                static unsafe void RhpByRefAssignRef(object* location, object value) {
#if ARM64
                    RhpByRefAssignRefArm64(location, value);
#else
                    *location = value;
#endif
                }

#if ARM64
                [System.Runtime.InteropServices.DllImport("*", EntryPoint = "RhpByRefAssignRefArm64")]
                private static extern unsafe void RhpByRefAssignRefArm64(object* location, object value);
#endif
                [RuntimeExport("RhSpinWait")]
                static void RhSpinWait(int iterations) { }
                [RuntimeExport("RhSetThreadExitCallback")]
                static void RhSetThreadExitCallback(IntPtr callback) { }
                [RuntimeExport("RhYield")]
                static void RhYield() { }
                [RuntimeExport("NativeRuntimeEventSource_LogWaitHandleWaitStop")]
                static void NativeRuntimeEventSource_LogWaitHandleWaitStop(int id, IntPtr handle) { }
                [RuntimeExport("RhCompatibleReentrantWaitAny")]
                static unsafe int RhCompatibleReentrantWaitAny(int* pHandles, int count, int millisecondsTimeout, bool fWaitAll) { throw null; }
                [RuntimeExport("NativeRuntimeEventSource_LogThreadPoolMinMaxThreads")]
                static void NativeRuntimeEventSource_LogThreadPoolMinMaxThreads(int workerMin, int workerMax, int ioMin, int ioMax) { }
                [RuntimeExport("RhpHandleAlloc")]
                static IntPtr RhpHandleAlloc(object obj, bool fPinned) { throw null; }
                [RuntimeExport("RhpHandleAllocDependent")]
                static IntPtr RhpHandleAllocDependent(IntPtr primary, object secondary) { throw null; }
                [RuntimeExport("RhBuffer_BulkMoveWithWriteBarrier")]
                static unsafe void RhBuffer_BulkMoveWithWriteBarrier(void* dest, void* src, UIntPtr len) { }
                [RuntimeExport("RhFindMethodStartAddress")]
                static IntPtr RhFindMethodStartAddress(IntPtr ip) { throw null; }
                [RuntimeExport("RhGetCurrentThreadStackTrace")]
                static IntPtr RhGetCurrentThreadStackTrace(int skipFrames, int maxFrames, out int pFrameCount) { throw null; }
                [RuntimeExport("EventPipeInternal_CreateProvider")]
                static IntPtr EventPipeInternal_CreateProvider(string providerName, IntPtr callback, IntPtr callbackContext) { throw null; }
                [RuntimeExport("EventPipeInternal_DeleteProvider")]
                static void EventPipeInternal_DeleteProvider(IntPtr provider) { }
                [RuntimeExport("EventPipeInternal_WriteEventData")]
                static bool EventPipeInternal_WriteEventData(IntPtr provider, int eventID, int eventVersion, int eventLevel, long keywords, IntPtr pMetadata, int metadataLength, IntPtr pData, int dataLength) { throw null; }
                [RuntimeExport("EventPipeInternal_EventActivityIdControl")]
                static bool EventPipeInternal_EventActivityIdControl(int controlCode, ref Guid activityId) { throw null; }
                [RuntimeExport("EventPipeInternal_DefineEvent")]
                static int EventPipeInternal_DefineEvent(IntPtr provider, string eventName, int eventVersion, int eventLevel, long keywords, IntPtr pMetadata, int metadataLength) { throw null; }
                [RuntimeExport("NativeRuntimeEventSource_LogContentionLockCreated")]
                static void NativeRuntimeEventSource_LogContentionLockCreated(IntPtr lockID, IntPtr declaringTypeID, string methodName, int lockLevel) { }
                [RuntimeExport("NativeRuntimeEventSource_LogContentionStart")]
                static void NativeRuntimeEventSource_LogContentionStart(int id, IntPtr lockID, IntPtr declaringTypeID, string methodName, int lockLevel) { }
                [RuntimeExport("NativeRuntimeEventSource_LogContentionStop")]
                static void NativeRuntimeEventSource_LogContentionStop(int id, IntPtr lockID, IntPtr declaringTypeID, string methodName, int lockLevel) { }
                [RuntimeExport("NativeRuntimeEventSource_LogWaitHandleWaitStart")]
                static void NativeRuntimeEventSource_LogWaitHandleWaitStart(int id, IntPtr handle) { }
                [RuntimeExport("RhGetGenerationBudget")]
                static int RhGetGenerationBudget(int generation) { throw null; }
                [RuntimeExport("RhGetTotalAllocatedBytes")]
                static long RhGetTotalAllocatedBytes() { throw null; }
                [RuntimeExport("RhGetLastGCPercentTimeInGC")]
                static int RhGetLastGCPercentTimeInGC(int generation) { throw null; }
    }
}

*/

#endregion
