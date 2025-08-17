#region A couple very basic things

using System;
using System.Runtime;

namespace System
{
    public sealed class AttributeUsageAttribute : Attribute
    {
        public AttributeUsageAttribute(AttributeTargets validOn) { }
        public bool AllowMultiple { get; set; }
        public bool Inherited { get; set; }
    }

    namespace Runtime.CompilerServices
    {
        public class RuntimeHelpers
        {
            public static unsafe int OffsetToStringData => sizeof(IntPtr) + sizeof(int);
        }

        public static class RuntimeFeature
        {
            public const string UnmanagedSignatureCallingConvention = nameof(UnmanagedSignatureCallingConvention);
        }
    }
}

namespace System.Runtime.InteropServices
{
    public class UnmanagedType { }

    public class MarshalDirectiveException : Exception
    {
        public MarshalDirectiveException()
        { }
        public MarshalDirectiveException(string message) : base(message) { }
    }

    sealed class StructLayoutAttribute : Attribute
    {
        public StructLayoutAttribute(LayoutKind layoutKind)
        {
        }
    }

    public sealed class DllImportAttribute : Attribute
    {
        public string EntryPoint;
        public CharSet CharSet;
        public bool SetLastError;
        public bool ExactSpelling;
        public CallingConvention CallingConvention;
        public bool BestFitMapping;
        public bool PreserveSig;
        public bool ThrowOnUnmappableChar;

        public string Value { get; }

        public DllImportAttribute(string dllName)
        {
            Value = dllName;
        }
    }

    internal enum LayoutKind
    {
        Sequential = 0, // 0x00000008,
        Explicit = 2, // 0x00000010,
        Auto = 3, // 0x00000000,
    }

    public enum CharSet
    {
        None = 1,       // User didn't specify how to marshal strings.
        Ansi = 2,       // Strings should be marshalled as ANSI 1 byte chars.
        Unicode = 3,    // Strings should be marshalled as Unicode 2 byte chars.
        Auto = 4,       // Marshal Strings in the right way for the target system.
    }

    public enum CallingConvention
    {
        Winapi = 1,
        Cdecl = 2,
        StdCall = 3,
        ThisCall = 4,
        FastCall = 5,
    }
}
#endregion

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
    }
}

namespace Internal.Runtime.CompilerHelpers
{
    // A class that the compiler looks for that has helpers to initialize the
    // process. The compiler can gracefully handle the helpers not being present,
    // but the class itself being absent is unhandled. Let's add an empty class.
    class StartupCodeHelpers
    {
        [RuntimeExport("RhpReversePInvoke")]
        static void RhpReversePInvoke(IntPtr frame) { }
        [RuntimeExport("RhpReversePInvokeReturn")]
        static void RhpReversePInvokeReturn(IntPtr frame) { }
        [RuntimeExport("RhpPInvoke")]
        static void RhpPInvoke(IntPtr frame) { }
        [RuntimeExport("RhpPInvokeReturn")]
        static void RhpPInvokeReturn(IntPtr frame) { }

        [RuntimeExport("RhpFallbackFailFast")]
        static void RhpFallbackFailFast() { while (true) ; }

        [RuntimeExport("InitializeModules")]
        static unsafe void InitializeModules(IntPtr osModule, IntPtr* pModuleHeaders, int count, IntPtr* pClasslibFunctions, int nClasslibFunctions) { }

        [RuntimeExport("RhpThrowEx")]
        static void RhpThrowEx(object ex) { while (true) ; }

        [RuntimeExport("RhpNewArray")]
        static unsafe object RhpNewArray(int elementTypeHandle, int length) { throw null; }

        [RuntimeExport("RhpAssignRef")]
        static unsafe void RhpAssignRef(object* location, object value) { }
        [RuntimeExport("RhpNewFast")]
        static unsafe object RhpNewFast(int typeHandle) { throw null; }
        /*
                [RuntimeExport("RhTypeCast_CheckCastAny")]
                static unsafe object RhTypeCast_CheckCastAny(object obj, int typeHandle) { throw null; }
                [RuntimeExport("RhUnbox2")]
                static unsafe object RhUnbox2(object obj) { throw null; }
                [RuntimeExport("RhpCheckedAssignRef")]
                static unsafe void RhpCheckedAssignRef(object* location, object value, int typeHandle) { }
                [RuntimeExport("RhTypeCast_CheckCastClass")]
                static unsafe object RhTypeCast_CheckCastClass(object obj, int typeHandle) { throw null; }
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
                [RuntimeExport("memmove")]
                static unsafe void memmove(byte* dest, byte* src, UIntPtr len) { }
                [RuntimeExport("memset")]
                static unsafe void memset(byte* dest, int value, UIntPtr len) { }
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
                [RuntimeExport("RhNewArray")]
                static unsafe object RhNewArray(int typeHandle, int length) { throw null; }
                [RuntimeExport("RhNewString")]
                static unsafe string RhNewString(char* data, int length) { throw null; }
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
                static unsafe void RhpByRefAssignRef(object* location, object value) { }
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
                static int RhGetLastGCPercentTimeInGC(int generation) { throw null; }*/
    }

    public static class ThrowHelpers
    {
        public static void ThrowNotImplementedException()
        {
            while (true) ;
        }

        public static void ThrowNullReferenceException()
        {
            while (true) ;
        }

        public static void ThrowIndexOutOfRangeException()
        {
            while (true) ;
        }

        public static void ThrowInvalidProgramException()
        {
            while (true) ;
        }

        public static void ThrowTypeLoadException()
        {
            while (true) ;
        }

        public static void ThrowTypeLoadExceptionWithArgument()
        {
            while (true) ;
        }

        public static void ThrowInvalidProgramExceptionWithArgument()
        {
            while (true) ;
        }

        public static void ThrowOverflowException()
        {
            while (true) ;
        }
    }
}

namespace Internal.Runtime
{
    internal abstract class ThreadStatics
    {
        public static unsafe object GetThreadStaticBaseForType(TypeManagerSlot* pModuleData, int typeTlsIndex)
        {
            return null;
        }
    }

    internal struct TypeManagerSlot { }
}
#endregion
