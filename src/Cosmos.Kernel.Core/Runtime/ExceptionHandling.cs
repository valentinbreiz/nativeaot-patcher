using System;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Internal.Runtime;
using Unsafe = System.Runtime.CompilerServices.Unsafe;

namespace Cosmos.Kernel.Core.Runtime;

/// <summary>
/// Exception handling clauses as defined in ICodeManager.h
/// </summary>
public enum EHClauseKind
{
    EH_CLAUSE_TYPED = 0,   // Catch handler for specific exception type
    EH_CLAUSE_FAULT = 1,   // Fault handler (like finally, runs on exception)
    EH_CLAUSE_FILTER = 2,  // Filter expression before catch
    EH_CLAUSE_UNUSED = 3,
}

/// <summary>
/// Exception handling clause structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EHClause
{
    public EHClauseKind ClauseKind;
    public uint TryStartOffset;
    public uint TryEndOffset;
    public byte* FilterAddress;
    public byte* HandlerAddress;
    public void* TargetType;  // MethodTable* for the exception type to catch
}

/// <summary>
/// Exception context for tracking active exceptions
/// </summary>
public unsafe class ExceptionContext
{
    public Exception? CurrentException;
    public nuint ThrowAddress;  // RIP where exception was thrown
    public nuint* StackPointer;
    public nuint* BasePointer;

    public ExceptionContext()
    {
        CurrentException = null;
        ThrowAddress = 0;
        StackPointer = null;
        BasePointer = null;
    }
}

/// <summary>
/// Core exception handling implementation
/// </summary>
public static unsafe class ExceptionHelper
{
    // Thread-local exception context (simplified - one per system for now)
    private static ExceptionContext s_exceptionContext = new ExceptionContext();

    // Maximum stack frames to walk
    private const int MAX_STACK_FRAMES = 64;

    // Guard against recursive exception handling
    private static bool s_isHandlingException = false;

    /// <summary>
    /// Get the current exception context
    /// </summary>
    public static ExceptionContext GetCurrentContext()
    {
        return s_exceptionContext;
    }

    /// <summary>
    /// Throw a managed exception and propagate through the stack
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowException(Exception ex, nuint throwAddress)
    {
        if (ex == null)
        {
            Serial.WriteString("[EH] Null exception thrown\n");
            FailFast("Null exception");
            return;
        }

        // Check for recursive exception handling
        if (s_isHandlingException)
        {
            Serial.WriteString("[EH] ERROR: Recursive exception detected!\n");
            Serial.WriteString("[EH] Exception handler tried to throw while handling exception\n");
            FailFast("Recursive exception");
            return;
        }

        // Set guard
        s_isHandlingException = true;

        // Store exception in context
        s_exceptionContext.CurrentException = ex;
        s_exceptionContext.ThrowAddress = throwAddress;

        // Print to both serial and console
        string header = "\n=== DOTNET EXCEPTION THROWN ===";
        Serial.WriteString(header);
        Serial.WriteString("\n");
        Console.WriteLine(header);

        // Print message
        if (ex.Message != null)
        {
            string msg = "Message: " + ex.Message;
            Serial.WriteString(msg);
            Serial.WriteString("\n");
            Console.WriteLine(msg);
        }

        // Print throw address
        Serial.WriteString("Throw address: 0x");
        Serial.WriteNumber(throwAddress);
        Serial.WriteString("\n");

        // For now, we can't get exact stack pointers without assembly
        // Start unwinding with approximate values
        bool handled = UnwindStack(ex, throwAddress, 0, 0);

        if (!handled)
        {
            string unhandled = "\n*** UNHANDLED EXCEPTION ***";
            Serial.WriteString(unhandled);
            Serial.WriteString("\n");
            Console.WriteLine(unhandled);

            string halting = "System halting...";
            Serial.WriteString(halting);
            Serial.WriteString("\n");
            Console.WriteLine(halting);

            // Call unhandled exception handler
            OnUnhandledException(ex);
        }

        // Clear guard (though we'll never reach here if unhandled)
        s_isHandlingException = false;
    }

    /// <summary>
    /// Unwind the stack looking for exception handlers
    /// </summary>
    private static bool UnwindStack(Exception ex, nuint throwAddress, nuint rbp, nuint rsp)
    {
        // Stack unwinding not yet implemented (needs RuntimeFunctions parsing)
        // For now, just return false (no handler found)
        return false;
    }

    /// <summary>
    /// Try to find an exception handler for the given instruction pointer
    /// </summary>
    private static bool TryFindExceptionHandler(Exception ex, void* instructionPointer, out EHClause clause)
    {
        clause = default;

        // EH metadata reading not yet implemented
        // Would need to:
        // 1. Find which module contains this IP
        // 2. Parse RuntimeFunctions section
        // 3. Find method and its LSDA
        // 4. Enumerate EH clauses
        // 5. Match exception type

        return false;
    }

    /// <summary>
    /// Execute an exception handler
    /// </summary>
    private static void ExecuteHandler(Exception ex, EHClause clause, nuint* rbp, nuint* rsp)
    {
        Serial.WriteString("[EH] Executing handler\n");

        // TODO: Implement handler execution
        // This requires:
        // 1. Setting up the stack for the handler
        // 2. Placing the exception object in the right location (register/stack)
        // 3. Jumping to the handler address
        // 4. Handling finally blocks along the way

        // For now, just log
        Serial.WriteString("[EH] Handler execution not yet implemented\n");
    }

    /// <summary>
    /// Fail fast - terminate the system
    /// </summary>
    public static void FailFast(string message)
    {
        // Halt the system - infinite loop
        while (true) { }
    }

    /// <summary>
    /// Called when an exception is unhandled
    /// </summary>
    private static void OnUnhandledException(Exception ex)
    {
        // Just halt - message already printed
        FailFast("Unhandled exception");
    }

    /// <summary>
    /// Create a runtime exception from exception ID
    /// </summary>
    public static Exception GetRuntimeException(ExceptionIDs id)
    {
        return id switch
        {
            ExceptionIDs.NullReference => new NullReferenceException(),
            ExceptionIDs.DivideByZero => new DivideByZeroException(),
            ExceptionIDs.IndexOutOfRange => new IndexOutOfRangeException(),
            ExceptionIDs.InvalidCast => new InvalidCastException(),
            ExceptionIDs.Overflow => new OverflowException(),
            ExceptionIDs.ArrayTypeMismatch => new ArrayTypeMismatchException(),
            _ => new Exception($"Unknown runtime exception: {id}"),
        };
    }
}

/// <summary>
/// Runtime exception IDs
/// </summary>
public enum ExceptionIDs
{
    OutOfMemory = 1,
    NullReference = 2,
    DivideByZero = 3,
    InvalidCast = 4,
    IndexOutOfRange = 5,
    ArrayTypeMismatch = 6,
    Overflow = 7,
    Arithmetic = 8,
}
