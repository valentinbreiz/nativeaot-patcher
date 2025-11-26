using System;

namespace Cosmos.TestRunner.Protocol
{
    /// <summary>
    /// Protocol constants for test runner communication.
    /// Ported from CosmosOS Cosmos.Debug.DebugConnectors with test-specific extensions.
    /// </summary>
    public class Consts
    {
        public const string EngineGUID = "DFE8F1F6-691C-4c08-8FFA-54551AD8FEAF";
        public static uint SerialSignature = 0x19740807;
    }

    /// <summary>
    /// Messages from Guest (Kernel) to Host (Test Runner)
    /// Extended with test-specific message types (100-106)
    /// </summary>
    public static class Ds2Vs
    {
        // Original CosmosOS debug messages (0-25)
        public const byte Noop = 0;
        public const byte TracePoint = 1;
        public const byte Message = 192;
        public const byte BreakPoint = 3;
        public const byte Error = 4;
        public const byte Pointer = 5;
        public const byte Started = 6;
        public const byte MethodContext = 7;
        public const byte MemoryData = 8;
        public const byte CmdCompleted = 9;
        public const byte Registers = 10;
        public const byte Frame = 11;
        public const byte Stack = 12;
        public const byte Pong = 13;
        public const byte BreakPointAsm = 14;
        public const byte StackCorruptionOccurred = 15;
        public const byte MessageBox = 16;
        public const byte NullReferenceOccurred = 17;
        public const byte SimpleNumber = 18;
        public const byte SimpleLongNumber = 19;
        public const byte ComplexNumber = 20;
        public const byte ComplexLongNumber = 21;
        public const byte StackOverflowOccurred = 22;
        public const byte InterruptOccurred = 23;
        public const byte CoreDump = 24;
        public const byte KernelPanic = 25;

        // Test runner specific messages (100-106)
        /// <summary>
        /// Sent when test suite starts. Payload: string (suite name)
        /// </summary>
        public const byte TestSuiteStart = 100;

        /// <summary>
        /// Sent when individual test starts. Payload: ushort (test number) + string (test name)
        /// </summary>
        public const byte TestStart = 101;

        /// <summary>
        /// Sent when test passes. Payload: ushort (test number) + uint (duration in milliseconds)
        /// </summary>
        public const byte TestPass = 102;

        /// <summary>
        /// Sent when test fails. Payload: ushort (test number) + string (error message)
        /// </summary>
        public const byte TestFail = 103;

        /// <summary>
        /// Sent when test is skipped. Payload: ushort (test number) + string (skip reason)
        /// </summary>
        public const byte TestSkip = 104;

        /// <summary>
        /// Sent when test suite ends. Payload: ushort (total) + ushort (passed) + ushort (failed)
        /// </summary>
        public const byte TestSuiteEnd = 105;

        /// <summary>
        /// Sent on kernel startup to identify architecture. Payload: byte (arch_id) + byte (cpu_count)
        /// Architecture IDs: 1=x86, 2=x64, 3=ARM32, 4=ARM64
        /// </summary>
        public const byte ArchitectureInfo = 106;
    }

    /// <summary>
    /// Messages from Host (Test Runner) to Guest (Kernel)
    /// For test runner, we primarily receive messages, so this is minimal.
    /// </summary>
    public static class Vs2Ds
    {
        public const byte Noop = 0;
        public const byte Continue = 4;
        public const byte Ping = 17;

        // Make sure this is always the last entry
        public const byte Max = 21;
    }

    /// <summary>
    /// Architecture identifiers for ArchitectureInfo message
    /// </summary>
    public enum Architecture : byte
    {
        Unknown = 0,
        x86 = 1,
        x64 = 2,
        ARM32 = 3,
        ARM64 = 4
    }
}
