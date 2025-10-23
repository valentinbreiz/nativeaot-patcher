using System.Runtime;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.System.IO;
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
    internal static unsafe class StartupCodeHelpers
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
        private static void RhpFallbackFailFast() { while (true) ; }

        [RuntimeExport("InitializeModules")]
        private static unsafe void InitializeModules(IntPtr osModule, IntPtr* pModuleHeaders, int count, IntPtr* pClasslibFunctions, int nClasslibFunctions) { }

        [RuntimeExport("RhpThrowEx")]
        private static void RhpThrowEx(Exception ex)
        {
            if (ex == null)
            {
                Console.WriteLine("Null exception thrown");
                Serial.WriteString("Null exception thrown \n");
                return;
            }
            Serial.WriteString("Unhandled exception: ");
            Serial.WriteString(ex.GetType().Name);
            Serial.WriteString("\n");
            Console.WriteLine($"Unhandled exception: {ex.GetType().Name}");
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

        [RuntimeExport("RhTypeCast_CheckCastClass")]
        static unsafe object RhTypeCast_CheckCastClass(object obj, int typeHandle)
        {
            // This is 100% WRONG
            return obj;
        }

        // Essential runtime functions needed by the linker
        [RuntimeExport("RhTypeCast_IsInstanceOfClass")]
        static unsafe object RhTypeCast_IsInstanceOfClass(object obj, int classTypeHandle)
        {
            return obj; // Simplified implementation
        }


        [RuntimeExport("RhpTrapThreads")]
        static void RhpTrapThreads() { }

        [RuntimeExport("RhpGcPoll")]
        static void RhpGcPoll() { }

        [RuntimeExport("RhBoxAny")]
        static unsafe object? RhBoxAny(int typeHandle, void* data)
        {
            return null; // Simplified implementation
        }

        [RuntimeExport("RhGetOSModuleFromPointer")]
        static IntPtr RhGetOSModuleFromPointer(IntPtr ptr)
        {
            return IntPtr.Zero;
        }

        [RuntimeExport("RhGetRuntimeVersion")]
        static int RhGetRuntimeVersion()
        {
            return 0;
        }

        [RuntimeExport("RhBulkMoveWithWriteBarrier")]
        static unsafe void RhBulkMoveWithWriteBarrier(void* dest, void* src, UIntPtr len)
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

        [RuntimeExport("RhTypeCast_AreTypesAssignable")]
        static bool RhTypeCast_AreTypesAssignable(int typeHandleSrc, int typeHandleDest)
        {
            return true; // Simplified implementation
        }

        [RuntimeExport("RhTypeCast_IsInstanceOfAny")]
        static unsafe object RhTypeCast_IsInstanceOfAny(object obj, int* pTypeHandles, int count)
        {
            return obj; // Simplified implementation
        }

        [RuntimeExport("RhBox")]
        static unsafe object? RhBox(int typeHandle, void* data)
        {
            return null; // Simplified implementation
        }

        // Additional missing runtime exports
        [RuntimeExport("RhUnbox")]
        static unsafe void* RhUnbox(object obj)
        {
            // Get the data pointer after the method table
            return (byte*)Unsafe.AsPointer(ref obj) + sizeof(IntPtr);
        }

        [RuntimeExport("RhUnbox2")]
        static unsafe object RhUnbox2(object obj)
        {
            return obj;
        }

        [RuntimeExport("RhpStelemRef")]
        static unsafe void RhpStelemRef(object array, int index, object value)
        {
            // Simplified array element store
            ((object[])array)[index] = value;
        }

        [RuntimeExport("RhpResolveInterfaceMethod")]
        static unsafe IntPtr RhpResolveInterfaceMethod(object obj, int methodHandle)
        {
            return IntPtr.Zero;
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

        [RuntimeExport("RhTypeCast_IsInstanceOfInterface")]
        static bool RhTypeCast_IsInstanceOfInterface(object obj, int interfaceTypeHandle)
        {
            return obj != null;
        }

        [RuntimeExport("RhTypeCast_CheckCastInterface")]
        static unsafe object RhTypeCast_CheckCastInterface(object obj, int interfaceTypeHandle)
        {
            return obj;
        }

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
        [RuntimeImport("*", "RhpByRefAssignRefArm64")]
        private static extern unsafe void RhpByRefAssignRefArm64(void** location, void* value);
#endif

        [RuntimeExport("RhpNewFinalizable")]
        static unsafe void* RhpNewFinalizable(MethodTable* pEEType)
        {
            // TODO: Should actually be Memory.RhAllocateNewObject with finalizable flag
            return Memory.RhpNewFast(pEEType); // Simplified implementation (Should set gc flag)
        }

        [RuntimeExport("RhpRethrow")]
        static void RhpRethrow()
        {
            while (true) ;
        }

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

        [RuntimeExport("RhYield")]
        static int RhYield()
        {
            return 0;
        }

        [RuntimeExport("RhpHandleAlloc")]
        static IntPtr RhpHandleAlloc(object obj, bool fPinned)
        {
            return IntPtr.Zero;
        }

        [RuntimeExport("RhpHandleAllocDependent")]
        static IntPtr RhpHandleAllocDependent(IntPtr primary, object secondary)
        {
            return IntPtr.Zero;
        }

        [RuntimeExport("RhBuffer_BulkMoveWithWriteBarrier")]
        static unsafe void RhBuffer_BulkMoveWithWriteBarrier(void* dest, void* src, UIntPtr len)
        {
            memmove((byte*)dest, (byte*)src, len);
        }

        [RuntimeExport("RhTypeCast_CheckCastClassSpecial")]
        static unsafe object RhTypeCast_CheckCastClassSpecial(object obj, int typeHandle, byte fThrow)
        {
            return obj;
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
        static unsafe void* RhpLdelemaRef(object array, int index, int typeHandle)
        {
            // Get address of array element
            object[] objArray = (object[])array;
            fixed (object* ptr = &objArray[index])
            {
                return ptr;
            }
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

        [RuntimeExport("RhFindBlob")]
        static unsafe bool RhFindBlob(IntPtr hOsModule, uint blobId, void** ppbBlob, uint* pcbBlob)
        {
            if (ppbBlob != null)
                *ppbBlob = null;
            if (pcbBlob != null)
                *pcbBlob = 0;
            return false;
        }

        [RuntimeExport("RhTypeCast_CheckCastAny")]
        static unsafe object RhTypeCast_CheckCastAny(object obj, int typeHandle)
        {
            return obj;
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
                [RuntimeExport("__security_cookie")]
                static void __security_cookie() { }
                [RuntimeExport("RhSpanHelpers_MemZero")]
                static unsafe void RhSpanHelpers_MemZero(byte* dest, int len) { }
                [RuntimeExport("RhBoxAny")]
                static unsafe object RhBoxAny(int typeHandle, void* data) { throw null; }
                [RuntimeExport("RhGetOSModuleFromPointer")]
                static IntPtr RhGetOSModuleFromPointer(IntPtr ptr) { throw null; }
                [RuntimeExport("RhGetRuntimeVersion")]
                static int RhGetRuntimeVersion() { return 0; }
                [RuntimeExport("RhGetKnobValues")]
                static unsafe void RhGetKnobValues(int* pKnobValues) { }
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
                [RuntimeExport("RhBox")]
                static unsafe object RhBox(int typeHandle, void* data) { throw null; }
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
                [RuntimeExport("cos")]
                static double cos(double x) { throw null; }
                [RuntimeExport("sin")]
                static double sin(double x) { throw null; }
                [RuntimeExport("tan")]
                static double tan(double x) { throw null; }
                [RuntimeExport("pow")]
                static double pow(double x, double y) { throw null; }
                [RuntimeExport("RhTypeCast_IsInstanceOfAny")]
                static unsafe object RhTypeCast_IsInstanceOfAny(object obj, int* pTypeHandles, int count) { throw null; }
                [RuntimeExport("RhUnbox")]
                static unsafe void* RhUnbox(object obj) { throw null; }
                [RuntimeExport("RhpStelemRef")]
                static unsafe void RhpStelemRef(object array, int index, object value) { }
                [RuntimeExport("RhTypeCast_CheckArrayStore")]
                static unsafe void RhTypeCast_CheckArrayStore(object array, object value) { }
                [RuntimeExport("RhpResolveInterfaceMethod")]
                static unsafe IntPtr RhpResolveInterfaceMethod(object obj, int methodHandle) { throw null; }
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
