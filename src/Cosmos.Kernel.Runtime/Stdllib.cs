using System;
using System.Runtime;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.Memory;

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

        public sealed class RuntimeExportAttribute : Attribute
        {
            public RuntimeExportAttribute(string entry) { }
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

        internal unsafe struct MethodTable
        {
            internal ushort _usComponentSize;
            private ushort _usFlags;
            internal uint _uBaseSize;
            internal MethodTable* _relatedType;
            private ushort _usNumVtableSlots;
            private ushort _usNumInterfaces;
            private uint _uHashCode;
        }
    }
}

namespace Cosmos.Kernel.Runtime
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
        private static void RhpThrowEx(object ex) { while (true) ; }

        [RuntimeExport("RhpNewArray")]
        private static unsafe void* RhpNewArray(MethodTable* pMT, int length)
        {
            if (length < 0)
                return null;

            uint size = pMT->_uBaseSize + (uint)length * pMT->_usComponentSize;
            MethodTable** result = AllocObject(size);
            *result = pMT;
            *(int*)(result + 1) = length;
            return result;
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

        [RuntimeExport("RhpNewFast")]
        private static unsafe void* RhpNewFast(MethodTable* pMT)
        {
            MethodTable** result = AllocObject(pMT->_uBaseSize);
            *result = pMT;
            return result;
        }

        private static unsafe MethodTable** AllocObject(uint size)
        {
            return (MethodTable**)MemoryOp.Alloc(size);
        }

        private static unsafe MethodTable* GetMethodTable(object obj)
        {
            TypedReference tr = __makeref(obj);
            return (MethodTable*)*(IntPtr*)&tr;
        }

        [RuntimeExport("memmove")]
        private static unsafe void memmove(byte* dest, byte* src, UIntPtr len)
        {
            MemoryOp.MemMove(dest, src, (int)len);
        }

        [RuntimeExport("memset")]
        private static unsafe void memset(byte* dest, int value, UIntPtr len)
        {
            MemoryOp.MemSet(dest, (byte)value, (int)len);
        }

        [RuntimeExport("RhNewString")]
        private static unsafe void* RhNewString(MethodTable* pEEType, int length)
        {
            return RhpNewArray(pEEType, length);
        }

        [RuntimeExport("RhpCheckedXchg")]
        private static void* InterlockedExchange(void** location1, void* value)
        {
            void* original = *location1;
            *location1 = value;
            return original;
        }
        [RuntimeExport("RhTypeCast_CheckCastAny")]
        static unsafe object RhTypeCast_CheckCastAny(object obj, int typeHandle) { return obj; }
        [RuntimeExport("RhUnbox2")]
        static unsafe object RhUnbox2(object obj) { return obj; }
        [RuntimeExport("RhTypeCast_CheckCastClass")]
        static unsafe object RhTypeCast_CheckCastClass(object obj, int typeHandle) { return obj; }
        [RuntimeExport("RhpTrapThreads")]
        static void RhpTrapThreads() { }
        [RuntimeExport("RhpGcPoll")]
        static void RhpGcPoll() { }
        [RuntimeExport("__security_cookie")]
        static void __security_cookie() { }
        [RuntimeExport("RhSpanHelpers_MemZero")]
        static unsafe void RhSpanHelpers_MemZero(byte* dest, int len) { MemoryOp.MemSet(dest, 0, len); }
        [RuntimeExport("RhBoxAny")]
        static unsafe object RhBoxAny(int typeHandle, void* data) { return null; }
        [RuntimeExport("RhGetOSModuleFromPointer")]
        static IntPtr RhGetOSModuleFromPointer(IntPtr ptr) { return IntPtr.Zero; }
        [RuntimeExport("RhGetRuntimeVersion")]
        static int RhGetRuntimeVersion() { return 0; }
        [RuntimeExport("RhGetKnobValues")]
        static unsafe void RhGetKnobValues(int* pKnobValues) { }
        [RuntimeExport("RhBulkMoveWithWriteBarrier")]
        static unsafe void RhBulkMoveWithWriteBarrier(void* dest, void* src, UIntPtr len) { MemoryOp.MemMove((byte*)dest, (byte*)src, (int)len); }
        [RuntimeExport("RhHandleFree")]
        static void RhHandleFree(IntPtr handle) { }
        [RuntimeExport("RhHandleSet")]
        static IntPtr RhHandleSet(object obj) { return IntPtr.Zero; }
        [RuntimeExport("RhpNewFinalizable")]
        static unsafe object RhpNewFinalizable(int typeHandle) { return null; }
        [RuntimeExport("RhTypeCast_AreTypesAssignable")]
        static bool RhTypeCast_AreTypesAssignable(int typeHandleSrc, int typeHandleDest) { return true; }
        [RuntimeExport("RhTypeCast_IsInstanceOfInterface")]
        static bool RhTypeCast_IsInstanceOfInterface(object obj, int interfaceTypeHandle) { return false; }
        [RuntimeExport("RhNewObject")]
        static unsafe object RhNewObject(int typeHandle) { return null; }
        [RuntimeExport("RhBox")]
        static unsafe object RhBox(int typeHandle, void* data) { return null; }
        [RuntimeExport("RhpCheckedLockCmpXchg")]
        static unsafe object RhpCheckedLockCmpXchg(object* location, object value, object comparand, int typeHandle) { 
            object original = *location;
            if (original == comparand) *location = value;
            return original;
        }
        [RuntimeExport("RhGetProcessCpuCount")]
        static int RhGetProcessCpuCount() { return 1; }
        [RuntimeExport("RhGetGeneration")]
        static int RhGetGeneration(object obj) { return 0; }
        [RuntimeExport("RhSuppressFinalize")]
        static void RhSuppressFinalize(object obj) { }
        [RuntimeExport("RhReRegisterForFinalize")]
        static void RhReRegisterForFinalize(object obj) { }
        [RuntimeExport("RhGetGcCollectionCount")]
        static int RhGetGcCollectionCount(int generation) { return 0; }
        [RuntimeExport("RhGetGcTotalMemory")]
        static long RhGetGcTotalMemory(bool forceFullCollection) { return 0; }
        [RuntimeExport("RhCollect")]
        static void RhCollect(int generation, InternalGCCollectionMode mode) { }
        [RuntimeExport("RhWaitForPendingFinalizers")]
        static void RhWaitForPendingFinalizers() { }
        [RuntimeExport("RhGetMemoryInfo")]
        static void RhGetMemoryInfo(IntPtr pMemInfo) { }
        [RuntimeExport("RhTypeCast_IsInstanceOfAny")]
        static unsafe object RhTypeCast_IsInstanceOfAny(object obj, int* pTypeHandles, int count) { return obj; }
        [RuntimeExport("RhUnbox")]
        static unsafe void* RhUnbox(object obj) { 
            TypedReference tr = __makeref(obj);
            return (void*)*(IntPtr*)&tr;
        }
        [RuntimeExport("RhpStelemRef")]
        static unsafe void RhpStelemRef(object array, int index, object value) { }
        [RuntimeExport("RhTypeCast_CheckArrayStore")]
        static unsafe void RhTypeCast_CheckArrayStore(object array, object value) { }
        [RuntimeExport("RhpResolveInterfaceMethod")]
        static unsafe IntPtr RhpResolveInterfaceMethod(object obj, int methodHandle) { return IntPtr.Zero; }
        [RuntimeExport("RhCurrentOSThreadId")]
        static int RhCurrentOSThreadId() { return 1; }
        [RuntimeExport("RhGetCrashInfoBuffer")]
        static IntPtr RhGetCrashInfoBuffer() { return IntPtr.Zero; }
        [RuntimeExport("RhTypeCast_IsInstanceOfClass")]
        static unsafe object RhTypeCast_IsInstanceOfClass(object obj, int classTypeHandle) { return obj; }
        [RuntimeExport("RhTypeCast_CheckCastInterface")]
        static unsafe object RhTypeCast_CheckCastInterface(object obj, int interfaceTypeHandle) { return obj; }
        [RuntimeExport("RhpRethrow")]
        static void RhpRethrow() { while (true) ; }
        [RuntimeExport("RhpStackProbe")]
        static void RhpStackProbe() { }
        [RuntimeExport("RhSpanHelpers_MemCopy")]
        static unsafe void RhSpanHelpers_MemCopy(byte* dest, byte* src, int len) { MemoryOp.MemMove(dest, src, len); }
        [RuntimeExport("RhTypeCast_CheckCastClassSpecial")]
        static unsafe object RhTypeCast_CheckCastClassSpecial(object obj, int typeHandle, byte fThrow) { return obj; }
        [RuntimeExport("RhpLdelemaRef")]
        static unsafe object* RhpLdelemaRef(object array, int index, int typeHandle) { return null; }
        [RuntimeExport("RhpByRefAssignRef")]
        static unsafe void RhpByRefAssignRef(object* location, object value) { *location = value; }
        [RuntimeExport("RhSpinWait")]
        static void RhSpinWait(int iterations) { }
        [RuntimeExport("RhSetThreadExitCallback")]
        static void RhSetThreadExitCallback(IntPtr callback) { }
        [RuntimeExport("RhYield")]
        static void RhYield() { }
        
        // Additional runtime exports needed by the linker        
        [RuntimeExport("RhCreateCrashDumpIfEnabled")]
        static void RhCreateCrashDumpIfEnabled() { }
        
        [RuntimeExport("RhpGetTickCount64")]
        static ulong RhpGetTickCount64() { return 0; }
        
        [RuntimeExport("RhpHandleAlloc")]
        static IntPtr RhpHandleAlloc(object value, int type) { return IntPtr.Zero; }
        
        [RuntimeExport("RhpHandleAllocDependent")]
        static IntPtr RhpHandleAllocDependent(object primary, object secondary) { return IntPtr.Zero; }
        
        [RuntimeExport("RhRegisterForGCReporting")]
        static void RhRegisterForGCReporting(IntPtr reportingCallback) { }
        
        [RuntimeExport("RhUnregisterForGCReporting")]
        static void RhUnregisterForGCReporting() { }
        
        [RuntimeExport("RhBuffer_BulkMoveWithWriteBarrier")]
        static unsafe void RhBuffer_BulkMoveWithWriteBarrier(byte* dmem, byte* smem, nuint len) 
        {
            for (nuint i = 0; i < len; i++)
                dmem[i] = smem[i];
        }
        
        [RuntimeExport("RhFindMethodStartAddress")]
        static IntPtr RhFindMethodStartAddress(IntPtr ip) { return IntPtr.Zero; }
        
        [RuntimeExport("RhGetCurrentThreadStackTrace")]
        static int RhGetCurrentThreadStackTrace(IntPtr[] outputBuffer) { return 0; }
        
        [RuntimeExport("RhRegisterFrozenSegment")]
        static IntPtr RhRegisterFrozenSegment(IntPtr pSegmentStart, nuint length, IntPtr pRootsMask, nuint cbRootsMask) { return IntPtr.Zero; }
        
        [RuntimeExport("RhUpdateFrozenSegment")]
        static void RhUpdateFrozenSegment(IntPtr handle, IntPtr pSegmentStart, IntPtr pSegmentEnd) { }
        
        [RuntimeExport("RhGetThreadStaticStorage")]
        static unsafe object** RhGetThreadStaticStorage() { return null; }
        
        [RuntimeExport("RhpGetModuleSection")]
        static IntPtr RhpGetModuleSection(IntPtr module, int sectionId, out int length) 
        {
            length = 0;
            return IntPtr.Zero;
        }
        
        [RuntimeExport("RhNewInterfaceDispatchCell")]
        static IntPtr RhNewInterfaceDispatchCell(IntPtr pCell, IntPtr pTarget) { return IntPtr.Zero; }
        
        [RuntimeExport("RhNewArray")]
        static unsafe object RhNewArray(IntPtr pEEType, int numElements) 
        {
            // Simplified array allocation - would need proper implementation
            var arraySize = sizeof(IntPtr) + sizeof(int) + (numElements * 8); // Assuming 8 bytes per element
            var memory = MemoryOp.Alloc((uint)arraySize);
            if (memory == null) return null;
            return Unsafe.AsRef<object>(memory);
        }
        
        [RuntimeExport("RhAllocateNewArray")]
        static unsafe object RhAllocateNewArray(IntPtr pEEType, int numElements, uint flags) 
        {
            return RhNewArray(pEEType, numElements);
        }
        
        [RuntimeExport("RhGetRuntimeHelperForType")]
        static IntPtr RhGetRuntimeHelperForType(IntPtr pEEType, int helperKind) { return IntPtr.Zero; }
        
        [RuntimeExport("RhGetGCDescSize")]
        static int RhGetGCDescSize(IntPtr pEEType) { return 0; }
        
        [RuntimeExport("RhFindBlob")]
        static unsafe bool RhFindBlob(IntPtr hOsModule, uint blobId, byte** ppbBlob, uint* pcbBlob) 
        { 
            if (ppbBlob != null) *ppbBlob = null;
            if (pcbBlob != null) *pcbBlob = 0;
            return false; 
        }
        
        [RuntimeExport("RhHandleGetDependent")]
        static object RhHandleGetDependent(IntPtr handle) { return null; }
        
        [RuntimeExport("RhpInitialDynamicInterfaceDispatch")]
        static IntPtr RhpInitialDynamicInterfaceDispatch() { return IntPtr.Zero; }
        
        [RuntimeExport("RhResolveStaticDispatchOnType")]
        static IntPtr RhResolveStaticDispatchOnType(IntPtr pTargetType, IntPtr pInterfaceType, int slot) { return IntPtr.Zero; }
        
        [RuntimeExport("RhGetModuleFileName")]
        static unsafe int RhGetModuleFileName(IntPtr module, char* buffer, int bufferLength) { return 0; }
        
        [RuntimeExport("RhResolveDispatch")]
        static IntPtr RhResolveDispatch(object pObject, IntPtr pInterfaceType, int slot) { return IntPtr.Zero; }
        
        [RuntimeExport("RhGetCodeTarget")]
        static IntPtr RhGetCodeTarget(IntPtr pCode) { return IntPtr.Zero; }
        
        [RuntimeExport("RhGetTargetOfUnboxingAndInstantiatingStub")]
        static IntPtr RhGetTargetOfUnboxingAndInstantiatingStub(IntPtr pCode) { return IntPtr.Zero; }
    }
}
#endregion
