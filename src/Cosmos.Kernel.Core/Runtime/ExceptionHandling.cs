using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Unsafe = System.Runtime.CompilerServices.Unsafe;

namespace Cosmos.Kernel.Core.Runtime;

#if ARCH_X64
/// <summary>
/// DWARF register numbers for x86-64
/// </summary>
public enum DwarfRegX64 : byte
{
    RAX = 0, RDX = 1, RCX = 2, RBX = 3,
    RSI = 4, RDI = 5, RBP = 6, RSP = 7,
    R8 = 8, R9 = 9, R10 = 10, R11 = 11,
    R12 = 12, R13 = 13, R14 = 14, R15 = 15,
    RA = 16,  // Return address (pseudo-register)
    MAX = 17
}

/// <summary>
/// Register save location during unwind
/// </summary>
public enum RegSaveKind : byte
{
    Undefined = 0,  // Register value unknown
    SameValue = 1,  // Register keeps its value (callee-saved)
    AtCfaOffset = 2, // Register saved at CFA + offset
    InRegister = 3,  // Register value is in another register
}

/// <summary>
/// Tracks where a register is saved during unwind
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public struct RegLocation
{
    [FieldOffset(0)] public RegSaveKind Kind;
    [FieldOffset(4)] public int Offset;  // For AtCfaOffset: offset from CFA
    [FieldOffset(1)] public byte Register; // For InRegister: which register holds the value
}

/// <summary>
/// State of all registers during stack unwinding
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct UnwindState
{
    // CFA (Canonical Frame Address) definition
    public byte CfaRegister;  // Which register CFA is based on
    public int CfaOffset;     // Offset from that register

    // Register save locations
    public fixed byte RegLocations[(int)DwarfRegX64.MAX * 8]; // Array of RegLocation

    // Current register values (unwound)
    public nuint RAX, RDX, RCX, RBX, RSI, RDI, RBP, RSP;
    public nuint R8, R9, R10, R11, R12, R13, R14, R15;
    public nuint ReturnAddress;

    public RegLocation* GetRegLocation(DwarfRegX64 reg)
    {
        fixed (byte* p = RegLocations)
        {
            return (RegLocation*)(p + (int)reg * sizeof(RegLocation));
        }
    }

    public void SetRegLocation(DwarfRegX64 reg, RegSaveKind kind, int offset = 0, byte inReg = 0)
    {
        RegLocation* loc = GetRegLocation(reg);
        loc->Kind = kind;
        loc->Offset = offset;
        loc->Register = inReg;
    }

    public nuint GetRegValue(DwarfRegX64 reg)
    {
        return reg switch
        {
            DwarfRegX64.RAX => RAX, DwarfRegX64.RDX => RDX,
            DwarfRegX64.RCX => RCX, DwarfRegX64.RBX => RBX,
            DwarfRegX64.RSI => RSI, DwarfRegX64.RDI => RDI,
            DwarfRegX64.RBP => RBP, DwarfRegX64.RSP => RSP,
            DwarfRegX64.R8 => R8, DwarfRegX64.R9 => R9,
            DwarfRegX64.R10 => R10, DwarfRegX64.R11 => R11,
            DwarfRegX64.R12 => R12, DwarfRegX64.R13 => R13,
            DwarfRegX64.R14 => R14, DwarfRegX64.R15 => R15,
            DwarfRegX64.RA => ReturnAddress,
            _ => 0
        };
    }

    public void SetRegValue(DwarfRegX64 reg, nuint value)
    {
        switch (reg)
        {
            case DwarfRegX64.RAX: RAX = value; break;
            case DwarfRegX64.RDX: RDX = value; break;
            case DwarfRegX64.RCX: RCX = value; break;
            case DwarfRegX64.RBX: RBX = value; break;
            case DwarfRegX64.RSI: RSI = value; break;
            case DwarfRegX64.RDI: RDI = value; break;
            case DwarfRegX64.RBP: RBP = value; break;
            case DwarfRegX64.RSP: RSP = value; break;
            case DwarfRegX64.R8: R8 = value; break;
            case DwarfRegX64.R9: R9 = value; break;
            case DwarfRegX64.R10: R10 = value; break;
            case DwarfRegX64.R11: R11 = value; break;
            case DwarfRegX64.R12: R12 = value; break;
            case DwarfRegX64.R13: R13 = value; break;
            case DwarfRegX64.R14: R14 = value; break;
            case DwarfRegX64.R15: R15 = value; break;
            case DwarfRegX64.RA: ReturnAddress = value; break;
        }
    }
}
#elif ARCH_ARM64
/// <summary>
/// DWARF register numbers for AArch64 (ARM64)
/// </summary>
public enum DwarfRegARM64 : byte
{
    X0 = 0, X1 = 1, X2 = 2, X3 = 3, X4 = 4, X5 = 5, X6 = 6, X7 = 7,
    X8 = 8, X9 = 9, X10 = 10, X11 = 11, X12 = 12, X13 = 13, X14 = 14, X15 = 15,
    X16 = 16, X17 = 17, X18 = 18, X19 = 19, X20 = 20, X21 = 21, X22 = 22, X23 = 23,
    X24 = 24, X25 = 25, X26 = 26, X27 = 27, X28 = 28,
    FP = 29,  // X29 - Frame Pointer
    LR = 30,  // X30 - Link Register (Return Address)
    SP = 31,  // Stack Pointer
    MAX = 32
}

/// <summary>
/// Register save location during unwind
/// </summary>
public enum RegSaveKind : byte
{
    Undefined = 0,  // Register value unknown
    SameValue = 1,  // Register keeps its value (callee-saved)
    AtCfaOffset = 2, // Register saved at CFA + offset
    InRegister = 3,  // Register value is in another register
}

/// <summary>
/// Tracks where a register is saved during unwind
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public struct RegLocation
{
    [FieldOffset(0)] public RegSaveKind Kind;
    [FieldOffset(4)] public int Offset;  // For AtCfaOffset: offset from CFA
    [FieldOffset(1)] public byte Register; // For InRegister: which register holds the value
}

/// <summary>
/// State of all registers during stack unwinding for ARM64
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct UnwindState
{
    // CFA (Canonical Frame Address) definition
    public byte CfaRegister;  // Which register CFA is based on
    public int CfaOffset;     // Offset from that register

    // Register save locations
    public fixed byte RegLocations[(int)DwarfRegARM64.MAX * 8]; // Array of RegLocation

    // Current register values (unwound) - callee-saved registers
    public nuint X19, X20, X21, X22, X23, X24, X25, X26, X27, X28;
    public nuint FP;   // X29
    public nuint LR;   // X30 - also serves as return address
    public nuint SP;
    public nuint ReturnAddress;

    public RegLocation* GetRegLocation(DwarfRegARM64 reg)
    {
        fixed (byte* p = RegLocations)
        {
            return (RegLocation*)(p + (int)reg * sizeof(RegLocation));
        }
    }

    public void SetRegLocation(DwarfRegARM64 reg, RegSaveKind kind, int offset = 0, byte inReg = 0)
    {
        RegLocation* loc = GetRegLocation(reg);
        loc->Kind = kind;
        loc->Offset = offset;
        loc->Register = inReg;
    }

    public nuint GetRegValue(DwarfRegARM64 reg)
    {
        return reg switch
        {
            DwarfRegARM64.X19 => X19, DwarfRegARM64.X20 => X20,
            DwarfRegARM64.X21 => X21, DwarfRegARM64.X22 => X22,
            DwarfRegARM64.X23 => X23, DwarfRegARM64.X24 => X24,
            DwarfRegARM64.X25 => X25, DwarfRegARM64.X26 => X26,
            DwarfRegARM64.X27 => X27, DwarfRegARM64.X28 => X28,
            DwarfRegARM64.FP => FP, DwarfRegARM64.LR => LR,
            DwarfRegARM64.SP => SP,
            _ => 0
        };
    }

    public void SetRegValue(DwarfRegARM64 reg, nuint value)
    {
        switch (reg)
        {
            case DwarfRegARM64.X19: X19 = value; break;
            case DwarfRegARM64.X20: X20 = value; break;
            case DwarfRegARM64.X21: X21 = value; break;
            case DwarfRegARM64.X22: X22 = value; break;
            case DwarfRegARM64.X23: X23 = value; break;
            case DwarfRegARM64.X24: X24 = value; break;
            case DwarfRegARM64.X25: X25 = value; break;
            case DwarfRegARM64.X26: X26 = value; break;
            case DwarfRegARM64.X27: X27 = value; break;
            case DwarfRegARM64.X28: X28 = value; break;
            case DwarfRegARM64.FP: FP = value; break;
            case DwarfRegARM64.LR: LR = value; ReturnAddress = value; break;
            case DwarfRegARM64.SP: SP = value; break;
        }
    }
}
#endif

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

/// <summary>
/// Exception handling clauses as defined in ICodeManager.h
/// </summary>
public enum EHClauseKind
{
    EH_CLAUSE_TYPED = 0,   // Catch handler for specific exception type
    EH_CLAUSE_FAULT = 1,   // Fault handler (like finally, runs on exception)
    EH_CLAUSE_FILTER = 2,  // Filter expression before catch
}

/// <summary>
/// Exception handling clause structure matching NativeAOT format
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

#if ARCH_X64
/// <summary>
/// PAL_LIMITED_CONTEXT structure matching x64 assembly offsets
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 0x50)]
public unsafe struct PAL_LIMITED_CONTEXT
{
    [FieldOffset(0x00)] public nuint IP;    // Instruction pointer (return address)
    [FieldOffset(0x08)] public nuint Rsp;   // Stack pointer
    [FieldOffset(0x10)] public nuint Rbp;   // Frame pointer
    [FieldOffset(0x18)] public nuint Rax;
    [FieldOffset(0x20)] public nuint Rbx;
    [FieldOffset(0x28)] public nuint Rdx;
    [FieldOffset(0x30)] public nuint R12;
    [FieldOffset(0x38)] public nuint R13;
    [FieldOffset(0x40)] public nuint R14;
    [FieldOffset(0x48)] public nuint R15;
}

/// <summary>
/// REGDISPLAY structure for x64 funclet calls - matches assembly offsets exactly
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 0x88)]
public unsafe struct REGDISPLAY
{
    // Storage for register values
    [FieldOffset(0x00)] public nuint Rbx;
    [FieldOffset(0x08)] public nuint Rbp;
    [FieldOffset(0x10)] public nuint Rsi;

    // Pointers to register values
    [FieldOffset(0x18)] public nuint* pRbx;
    [FieldOffset(0x20)] public nuint* pRbp;
    [FieldOffset(0x28)] public nuint* pRsi;
    [FieldOffset(0x30)] public nuint* pRdi;

    // More storage
    [FieldOffset(0x38)] public nuint Rdi;
    [FieldOffset(0x40)] public nuint R12;
    [FieldOffset(0x48)] public nuint R13;
    [FieldOffset(0x50)] public nuint R14;

    // More pointers
    [FieldOffset(0x58)] public nuint* pR12;
    [FieldOffset(0x60)] public nuint* pR13;
    [FieldOffset(0x68)] public nuint* pR14;
    [FieldOffset(0x70)] public nuint* pR15;

    // Stack pointer for resume
    [FieldOffset(0x78)] public nuint SP;

    // R15 storage
    [FieldOffset(0x80)] public nuint R15;
}
#elif ARCH_ARM64

/// <summary>
/// PAL_LIMITED_CONTEXT structure matching ARM64 assembly offsets
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 0x70)]
public unsafe struct PAL_LIMITED_CONTEXT
{
    [FieldOffset(0x00)] public nuint SP;    // Stack pointer
    [FieldOffset(0x08)] public nuint IP;    // Instruction pointer (PC/LR)
    [FieldOffset(0x10)] public nuint FP;    // Frame pointer (x29)
    [FieldOffset(0x18)] public nuint LR;    // Link register (x30)
    [FieldOffset(0x20)] public nuint X19;
    [FieldOffset(0x28)] public nuint X20;
    [FieldOffset(0x30)] public nuint X21;
    [FieldOffset(0x38)] public nuint X22;
    [FieldOffset(0x40)] public nuint X23;
    [FieldOffset(0x48)] public nuint X24;
    [FieldOffset(0x50)] public nuint X25;
    [FieldOffset(0x58)] public nuint X26;
    [FieldOffset(0x60)] public nuint X27;
    [FieldOffset(0x68)] public nuint X28;
}

/// <summary>
/// REGDISPLAY structure for ARM64 funclet calls
/// Uses direct values instead of pointers to avoid stack corruption
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 0x68)]
public unsafe struct REGDISPLAY
{
    // Stack pointer for resume
    [FieldOffset(0x00)] public nuint SP;

    // Direct values for callee-saved registers (not pointers)
    [FieldOffset(0x08)] public nuint FP;    // x29
    [FieldOffset(0x10)] public nuint X19;
    [FieldOffset(0x18)] public nuint X20;
    [FieldOffset(0x20)] public nuint X21;
    [FieldOffset(0x28)] public nuint X22;
    [FieldOffset(0x30)] public nuint X23;
    [FieldOffset(0x38)] public nuint X24;
    [FieldOffset(0x40)] public nuint X25;
    [FieldOffset(0x48)] public nuint X26;
    [FieldOffset(0x50)] public nuint X27;
    [FieldOffset(0x58)] public nuint X28;
    [FieldOffset(0x60)] public nuint LR;    // x30
}
#endif

/// <summary>
/// Stack frame information for unwinding
/// </summary>
public unsafe struct StackFrame
{
    public nuint ReturnAddress;   // Where this frame returns to
    public nuint FramePointer;    // RBP value for this frame
    public nuint StackPointer;    // RSP value for this frame
}

/// <summary>
/// Core exception handling implementation
/// </summary>
public static unsafe partial class ExceptionHelper
{
    // Maximum stack frames to walk
    private const int MAX_STACK_FRAMES = 64;

    // LSDA parsing constants (from UnixNativeCodeManager.cpp)
    private const byte UBF_FUNC_KIND_MASK = 0x03;
    private const byte UBF_FUNC_KIND_ROOT = 0x00;
    private const byte UBF_FUNC_HAS_EHINFO = 0x04;
    private const byte UBF_FUNC_HAS_ASSOCIATED_DATA = 0x10;

    // Assembly funclet callers - use nint for object reference since P/Invoke doesn't support object
    [LibraryImport("*", EntryPoint = "RhpCallCatchFunclet")]
    [SuppressGCTransition]
    private static partial void* RhpCallCatchFunclet(nint exceptionPtr, void* handlerAddress, void* pRegDisplay, void* pExInfo);

    [LibraryImport("*", EntryPoint = "RhpCallFilterFunclet")]
    [SuppressGCTransition]
    private static partial nint RhpCallFilterFunclet(nint exceptionPtr, void* filterAddress, void* pRegDisplay);

    // C functions from kmain.c that return eh_frame section addresses
    [LibraryImport("*", EntryPoint = "get_eh_frame_start")]
    [SuppressGCTransition]
    private static partial byte* GetEhFrameStart();

    [LibraryImport("*", EntryPoint = "get_eh_frame_end")]
    [SuppressGCTransition]
    private static partial byte* GetEhFrameEnd();

    // Guard against recursive exception handling
    private static bool s_isHandlingException = false;

    /// <summary>
    /// Main exception dispatcher called from RhThrowEx (managed entry from assembly)
    /// This version receives context from the assembly stub.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowExceptionWithContext(Exception ex, nuint throwAddress, nuint throwRbp, nuint throwRsp, void* pExInfo)
    {
        if (ex == null)
        {
            Serial.WriteString("[EH] Null exception thrown\n");
            FailFast("Null exception", ex);
            return;
        }

        // Check for recursive exception handling
        if (s_isHandlingException)
        {
            Serial.WriteString("[EH] ERROR: Recursive exception detected!\n");
            FailFast("Recursive exception", ex);
            return;
        }

        // Set guard
        s_isHandlingException = true;

        // Print exception info
        Serial.WriteString("\n=== DOTNET EXCEPTION THROWN ===\n");

        // Print message first (before stack walk which may crash)
        // Avoid GetType().Name as it causes heap allocations
        string? msg = ex.Message;
        if (msg != null)
        {
            Serial.WriteString("Message: ");
            Serial.WriteString(msg);
            Serial.WriteString("\n");
        }

        Serial.WriteString("Exception at 0x");
        Serial.WriteNumber(throwAddress);
        Serial.WriteString("\n");
        Serial.WriteString("RBP: 0x");
        Serial.WriteNumber(throwRbp);
        Serial.WriteString("\n");
        Serial.WriteString("RSP: 0x");
        Serial.WriteNumber(throwRsp);
        Serial.WriteString("\n");

        // Try to find and invoke a handler using the context from assembly
        bool handled = DispatchExceptionWithContext(ex, throwAddress, throwRbp, throwRsp, pExInfo);

        if (!handled)
        {
            Serial.WriteString("\n*** UNHANDLED EXCEPTION ***\n");
            Serial.WriteString("No catch handler found. System halting...\n");
            FailFast("Unhandled exception", ex);
        }

        // Clear guard (only reached if exception was handled)
        s_isHandlingException = false;
    }

    /// <summary>
    /// Two-pass exception dispatch with context from assembly
    /// Pass 1: Find a handler
    /// Pass 2: Execute finally handlers and then the catch handler
    /// </summary>
    private static bool DispatchExceptionWithContext(Exception ex, nuint throwAddress, nuint throwRbp, nuint throwRsp, void* pExInfo)
    {
        Serial.WriteString("[EH] Starting exception dispatch with context\n");

        // If we don't have valid RBP from context, we can't walk the stack properly
        if (throwRbp == 0)
        {
            Serial.WriteString("[EH] WARNING: No valid RBP from context, cannot walk stack\n");
            return false;
        }

        // Build a REGDISPLAY from the context for filter evaluation
        REGDISPLAY regDisplay = default;
        REGDISPLAY* pRegDisplay = null;

#if ARCH_X64
        // Initialize unwind state from throw-site context for CFI-based unwinding
        UnwindState unwindState = default;
        PAL_LIMITED_CONTEXT* pThrowContext = null;

        if (pExInfo != null)
        {
            pThrowContext = *(PAL_LIMITED_CONTEXT**)((byte*)pExInfo + 0x08);
            if (pThrowContext != null)
            {
                InitUnwindStateFromContext(ref unwindState, pThrowContext);
            }
        }
#elif ARCH_ARM64
        // Initialize unwind state from throw-site context for CFI-based unwinding
        UnwindState unwindState = default;
        PAL_LIMITED_CONTEXT* pThrowContext = null;

        if (pExInfo != null)
        {
            pThrowContext = *(PAL_LIMITED_CONTEXT**)((byte*)pExInfo + 0x08);
            if (pThrowContext != null)
            {
                InitUnwindStateFromContext(ref unwindState, pThrowContext);
            }
        }
#endif

        // Pass 1: Walk stack to find a handler, unwinding registers at each frame
        StackFrame catchFrame = default;
        EHClause catchClause = default;
        bool foundHandler = false;
        int catchFrameIndex = 0;

        // Walk the stack looking for handlers
        StackFrame frame;
        frame.FramePointer = throwRbp;
        frame.StackPointer = throwRsp;
        frame.ReturnAddress = throwAddress;

        int frameCount = 0;
        while (frameCount < MAX_STACK_FRAMES && frame.FramePointer != 0)
        {
            Serial.WriteString("[EH] Frame ");
            Serial.WriteNumber((nuint)frameCount);
            Serial.WriteString(": RBP=0x");
            Serial.WriteNumber(frame.FramePointer);
            Serial.WriteString(" RIP=0x");
            Serial.WriteNumber(frame.ReturnAddress);
            Serial.WriteString("\n");

            // Update REGDISPLAY from current UnwindState for filter evaluation
            if (pThrowContext != null)
            {
                pRegDisplay = &regDisplay;
#if ARCH_X64
                regDisplay.Rbx = unwindState.RBX;
                regDisplay.Rbp = frame.FramePointer;
                regDisplay.R12 = unwindState.R12;
                regDisplay.R13 = unwindState.R13;
                regDisplay.R14 = unwindState.R14;
                regDisplay.R15 = unwindState.R15;
                regDisplay.SP = frame.FramePointer;

                regDisplay.pRbx = &pRegDisplay->Rbx;
                regDisplay.pRbp = &pRegDisplay->Rbp;
                regDisplay.pR12 = &pRegDisplay->R12;
                regDisplay.pR13 = &pRegDisplay->R13;
                regDisplay.pR14 = &pRegDisplay->R14;
                regDisplay.pR15 = &pRegDisplay->R15;
#elif ARCH_ARM64
                regDisplay.SP = frame.FramePointer;
                regDisplay.FP = frame.FramePointer;
                regDisplay.X19 = unwindState.X19;
                regDisplay.X20 = unwindState.X20;
                regDisplay.X21 = unwindState.X21;
                regDisplay.X22 = unwindState.X22;
                regDisplay.X23 = unwindState.X23;
                regDisplay.X24 = unwindState.X24;
                regDisplay.X25 = unwindState.X25;
                regDisplay.X26 = unwindState.X26;
                regDisplay.X27 = unwindState.X27;
                regDisplay.X28 = unwindState.X28;
                regDisplay.LR = unwindState.LR;
#endif
            }

            // Check if this frame has an exception handler for our exception
            if (TryFindHandler(ex, frame.ReturnAddress, out EHClause clause, pRegDisplay))
            {
                Serial.WriteString("[EH] FOUND HANDLER at 0x");
                Serial.WriteNumber((nuint)clause.HandlerAddress);
                Serial.WriteString("\n");

                catchFrame = frame;
                catchClause = clause;
                catchFrameIndex = frameCount;
                foundHandler = true;
                break;
            }

            // Save current frame's IP for CFI unwinding BEFORE moving to next frame
            nuint currentFrameIP = frame.ReturnAddress;

            // Move to caller's frame using simple frame pointer chain
            if (!UnwindOneFrame(ref frame))
            {
                Serial.WriteString("[EH] Cannot unwind further\n");
                break;
            }

            // Unwind register state using CFI for the frame we just left
            // This restores registers to the values they had in the caller (next frame)
            if (pThrowContext != null)
            {
                UnwindOneFrameWithCFI(ref unwindState, currentFrameIP);
            }

            frameCount++;
        }

        if (!foundHandler)
        {
            Serial.WriteString("[EH] No handler found after ");
            Serial.WriteNumber((nuint)frameCount);
            Serial.WriteString(" frames\n");
            return false;
        }

#if ARCH_X64
        // Build REGDISPLAY from the frame chain (more reliable than CFI for RBP)
        // CFI says RBP is "SameValue" because function may use -fomit-frame-pointer,
        // but the frame chain walk correctly reads the saved RBP from the stack.
        if (pThrowContext != null)
        {
            // Use frame chain's RBP (correct) instead of CFI-unwound RBP (may be wrong)
            nuint handlerRbp = catchFrame.FramePointer;

            // For SP, read from the handler frame's stack to get the correct value
            // In a standard frame: [RBP] = saved caller RBP, [RBP+8] = return addr
            // The frame's own SP is typically close to RBP for the resume point
            nuint handlerSp = handlerRbp;  // Start with RBP as baseline

            Serial.WriteString("[EH] Frame chain RBP=0x");
            Serial.WriteHex(handlerRbp);
            Serial.WriteString(" CFI RBP=0x");
            Serial.WriteHex(unwindState.RBP);
            Serial.WriteString("\n");

            Serial.WriteString("[EH] Unwound registers - RBX=0x");
            Serial.WriteHex(unwindState.RBX);
            Serial.WriteString(" R12=0x");
            Serial.WriteHex(unwindState.R12);
            Serial.WriteString(" R13=0x");
            Serial.WriteHex(unwindState.R13);
            Serial.WriteString("\n");

            pRegDisplay = &regDisplay;

            // Use frame chain's RBP, CFI's other registers
            regDisplay.Rbx = unwindState.RBX;
            regDisplay.Rbp = handlerRbp;  // Use frame chain RBP
            regDisplay.R12 = unwindState.R12;
            regDisplay.R13 = unwindState.R13;
            regDisplay.R14 = unwindState.R14;
            regDisplay.R15 = unwindState.R15;

            // For SP, we need to use the stack pointer value that the resumed code expects
            // In NativeAOT, the continuation after catch expects RSP to be at the handler
            // frame's stack position. Using RBP directly works because the epilogue code
            // typically does "leave; ret" which expects RSP at or near RBP.
            // Read the return address at [RBP+8] to verify the stack is sane
            regDisplay.SP = handlerRbp;

            // Set up pointers to storage locations
            regDisplay.pRbx = &pRegDisplay->Rbx;
            regDisplay.pRbp = &pRegDisplay->Rbp;
            // pRsi and pRdi intentionally left null - not callee-saved
            regDisplay.pR12 = &pRegDisplay->R12;
            regDisplay.pR13 = &pRegDisplay->R13;
            regDisplay.pR14 = &pRegDisplay->R14;
            regDisplay.pR15 = &pRegDisplay->R15;
        }
#elif ARCH_ARM64
        // Build REGDISPLAY from frame chain and CFI unwound register values
        // Similar to x64: use frame chain for FP, CFI for callee-saved registers
        if (pThrowContext != null)
        {
            // Use frame chain's FP (correct) instead of CFI-unwound FP (may be wrong due to SameValue)
            nuint handlerFp = catchFrame.FramePointer;

            Serial.WriteString("[EH] Frame chain FP=0x");
            Serial.WriteHex(handlerFp);
            Serial.WriteString(" CFI FP=0x");
            Serial.WriteHex(unwindState.FP);
            Serial.WriteString("\n");

            Serial.WriteString("[EH] Unwound registers:\n");
            Serial.WriteString("  X19=0x"); Serial.WriteHex(unwindState.X19);
            Serial.WriteString(" X20=0x"); Serial.WriteHex(unwindState.X20);
            Serial.WriteString(" X21=0x"); Serial.WriteHex(unwindState.X21);
            Serial.WriteString("\n");
            Serial.WriteString("  X22=0x"); Serial.WriteHex(unwindState.X22);
            Serial.WriteString(" X23=0x"); Serial.WriteHex(unwindState.X23);
            Serial.WriteString(" X24=0x"); Serial.WriteHex(unwindState.X24);
            Serial.WriteString("\n");
            Serial.WriteString("  X25=0x"); Serial.WriteHex(unwindState.X25);
            Serial.WriteString(" X26=0x"); Serial.WriteHex(unwindState.X26);
            Serial.WriteString(" X27=0x"); Serial.WriteHex(unwindState.X27);
            Serial.WriteString(" X28=0x"); Serial.WriteHex(unwindState.X28);
            Serial.WriteString("\n");
            Serial.WriteString("  SP=0x"); Serial.WriteHex(unwindState.SP);
            Serial.WriteString(" LR=0x"); Serial.WriteHex(unwindState.LR);
            Serial.WriteString("\n");

            pRegDisplay = &regDisplay;

            // Store values directly in REGDISPLAY structure (no pointers/stackalloc)
            // This avoids stack corruption issues
            regDisplay.SP = handlerFp;           // Use frame chain FP (simulating SP)
            regDisplay.FP = handlerFp;           // Use frame chain FP
            regDisplay.X19 = unwindState.X19;
            regDisplay.X20 = unwindState.X20;
            regDisplay.X21 = unwindState.X21;
            regDisplay.X22 = unwindState.X22;
            regDisplay.X23 = unwindState.X23;
            regDisplay.X24 = unwindState.X24;
            regDisplay.X25 = unwindState.X25;
            regDisplay.X26 = unwindState.X26;
            regDisplay.X27 = unwindState.X27;
            regDisplay.X28 = unwindState.X28;
            regDisplay.LR = unwindState.LR;

            Serial.WriteString("[EH] REGDISPLAY setup: SP=0x");
            Serial.WriteHex(regDisplay.SP);
            Serial.WriteString(" FP=0x");
            Serial.WriteHex(regDisplay.FP);
            Serial.WriteString(" X20=0x");
            Serial.WriteHex(regDisplay.X20);
            Serial.WriteString("\n");
        }
#endif

        // Pass 2: Execute finally handlers between throw and catch, then invoke catch
        Serial.WriteString("[EH] Pass 2: Executing handlers\n");

        // For now, just invoke the catch handler directly
        // TODO: Execute finally handlers first
        InvokeCatchHandler(ex, ref catchFrame, ref catchClause, pExInfo, pRegDisplay);

        // InvokeCatchHandler should not return - it jumps to the resume address
        // If we reach here, something went wrong
        Serial.WriteString("[EH] ERROR: InvokeCatchHandler returned unexpectedly!\n");
        return false;
    }

    /// <summary>
    /// Unwind one stack frame using frame pointer chain
    /// </summary>
    private static bool UnwindOneFrame(ref StackFrame frame)
    {
        // Read caller's saved RBP from current frame
        // Stack layout: [saved_rbp][return_address]...
        nuint* rbpPtr = (nuint*)frame.FramePointer;

        if (rbpPtr == null || frame.FramePointer < 0x1000)
        {
            Serial.WriteString("[EH] Invalid frame pointer\n");
            return false;
        }

        // Saved RBP is at [RBP+0]
        nuint savedRbp = rbpPtr[0];

        // Return address is at [RBP+8]
        nuint returnAddr = rbpPtr[1];

        Serial.WriteString("[EH] Unwinding: savedRbp=0x");
        Serial.WriteNumber(savedRbp);
        Serial.WriteString(" returnAddr=0x");
        Serial.WriteNumber(returnAddr);
        Serial.WriteString("\n");

        // Validate the saved RBP
        // savedRbp of 0 means end of chain
        if (savedRbp == 0)
        {
            Serial.WriteString("[EH] End of frame chain (savedRbp=0)\n");
            return false;
        }

        // Validate return address - should be in kernel code range
        // Kernel code typically at 0xFFFFFFFF80000000 and above
        if (returnAddr == 0 || returnAddr < 0x1000)
        {
            Serial.WriteString("[EH] Invalid return address\n");
            return false;
        }

        // Update frame
        frame.FramePointer = savedRbp;
        frame.StackPointer = frame.FramePointer + 16;  // Approximate RSP after pop rbp; ret
        frame.ReturnAddress = returnAddr;

        return true;
    }

    /// <summary>
    /// Try to find an exception handler for the given IP
    /// Uses LSDA (Language Specific Data Area) parsing to find EH clauses
    /// </summary>
    private static bool TryFindHandler(Exception ex, nuint instructionPointer, out EHClause clause)
    {
        return TryFindHandler(ex, instructionPointer, out clause, null);
    }

    /// <summary>
    /// Try to find an exception handler for the given IP with context for filter evaluation
    /// Uses LSDA (Language Specific Data Area) parsing to find EH clauses
    /// </summary>
    private static bool TryFindHandler(Exception ex, nuint instructionPointer, out EHClause clause, REGDISPLAY* pRegDisplay)
    {
        clause = default;

        Serial.WriteString("[EH] Looking for handler for IP 0x");
        Serial.WriteHex(instructionPointer);
        Serial.WriteString("\n");

        // Try to find the method info and LSDA for this IP
        if (!TryGetMethodLSDA(instructionPointer, out nuint methodStart, out byte* pLSDA))
        {
            Serial.WriteString("[EH] Could not find LSDA for IP\n");
            return false;
        }

        Serial.WriteString("[EH] Found method at 0x");
        Serial.WriteHex(methodStart);
        Serial.WriteString(", LSDA at 0x");
        Serial.WriteHex((nuint)pLSDA);
        Serial.WriteString("\n");

        if (StackTraceMetadata.IsSupported)
        {
            if (StackTraceMetadata.TryGetMethodNameFromStartAddress(methodStart, out string methodName))
            {
                ref string? stackTraceString = ref StackTraceMetadata.GetStackTraceString(ex);
                if (stackTraceString == null)
                {
                    stackTraceString = methodName;
                }
                else
                {
                    stackTraceString += Environment.NewLine + "at " + methodName;
                }
            }
        }

        // Calculate offset within method
        uint codeOffset = (uint)(instructionPointer - methodStart);
        Serial.WriteString("[EH] Code offset: 0x");
        Serial.WriteHex(codeOffset);
        Serial.WriteString("\n");

        // Parse LSDA and enumerate EH clauses
        return TryFindHandlerInLSDA(ex, pLSDA, methodStart, codeOffset, out clause, pRegDisplay);
    }

    /// <summary>
    /// Try to find the LSDA for a given IP address by parsing eh_frame
    /// </summary>
    private static bool TryGetMethodLSDA(nuint ip, out nuint methodStart, out byte* pLSDA)
    {
        methodStart = 0;
        pLSDA = null;

        // Get eh_frame section bounds from C helper functions
        byte* ehFrameStart = GetEhFrameStart();
        byte* ehFrameEnd = GetEhFrameEnd();

        if (ehFrameStart == null || ehFrameEnd == null || ehFrameStart >= ehFrameEnd)
        {
            Serial.WriteString("[EH] No eh_frame section available\n");
            return false;
        }

        Serial.WriteString("[EH] Searching eh_frame from 0x");
        Serial.WriteHex((nuint)ehFrameStart);
        Serial.WriteString(" to 0x");
        Serial.WriteHex((nuint)ehFrameEnd);
        Serial.WriteString("\n");

        // Parse eh_frame to find the FDE (Frame Description Entry) containing our IP
        // eh_frame uses DWARF CFI format
        return ParseEhFrameForIP(ehFrameStart, ehFrameEnd, ip, out methodStart, out pLSDA);
    }

    /// <summary>
    /// Parse eh_frame section to find method info for a given IP
    /// eh_frame contains CIE (Common Information Entry) and FDE (Frame Description Entry) records
    /// </summary>
    private static bool ParseEhFrameForIP(byte* ehFrameStart, byte* ehFrameEnd, nuint ip,
                                           out nuint methodStart, out byte* pLSDA)
    {
        methodStart = 0;
        pLSDA = null;

        byte* p = ehFrameStart;

        while (p < ehFrameEnd)
        {
            // Read length (4 bytes, or 0xFFFFFFFF for extended length)
            uint length = *(uint*)p;
            if (length == 0)
                break; // End of eh_frame
            if (length == 0xFFFFFFFF)
            {
                // Extended length - skip for now
                Serial.WriteString("[EH] Extended length FDE not supported\n");
                break;
            }

            byte* recordStart = p;
            byte* recordEnd = p + 4 + length;
            p += 4;

            // Read CIE pointer (0 = this is a CIE, non-zero = offset to CIE, this is an FDE)
            uint ciePointer = *(uint*)p;
            p += 4;

            if (ciePointer == 0)
            {
                // This is a CIE - skip it
                p = recordEnd;
                continue;
            }

            // This is an FDE - parse it
            // ciePointer is relative offset back to the CIE
            byte* cie = (recordStart + 4) - ciePointer;

            // Read PC begin (encoded, usually as pc-relative)
            // For simplicity, assume sdata4 encoding (signed 4-byte PC-relative)
            int pcBeginRel = *(int*)p;
            nuint pcBegin = (nuint)(p + pcBeginRel);
            p += 4;

            // Read PC range
            uint pcRange = *(uint*)p;
            p += 4;

            nuint pcEnd = pcBegin + pcRange;

            // Check if IP is in this FDE's range
            if (ip >= pcBegin && ip < pcEnd)
            {
                Serial.WriteString("[EH] Found FDE: PC 0x");
                Serial.WriteHex(pcBegin);
                Serial.WriteString(" - 0x");
                Serial.WriteHex(pcEnd);
                Serial.WriteString("\n");

                methodStart = pcBegin;

                // Read augmentation length (ULEB128) to find LSDA pointer
                // For now, skip augmentation data parsing - LSDA pointer is typically at the start
                uint augLen = ReadULEB128(ref p);

                if (augLen > 0)
                {
                    // First item in augmentation data is usually the LSDA pointer (if CIE has 'L' in augmentation string)
                    // Assuming sdata4 encoding for LSDA pointer
                    int lsdaRel = *(int*)p;
                    if (lsdaRel != 0)
                    {
                        pLSDA = p + lsdaRel;
                        Serial.WriteString("[EH] LSDA at 0x");
                        Serial.WriteHex((nuint)pLSDA);
                        Serial.WriteString("\n");
                    }
                }

                return pLSDA != null;
            }

            p = recordEnd;
        }

        Serial.WriteString("[EH] IP not found in eh_frame\n");
        return false;
    }

    /// <summary>
    /// Read unsigned LEB128 encoded value
    /// </summary>
    private static uint ReadULEB128(ref byte* p)
    {
        uint result = 0;
        int shift = 0;
        byte b;

        do
        {
            b = *p++;
            result |= (uint)(b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);

        return result;
    }

    /// <summary>
    /// Read signed LEB128 encoded value
    /// </summary>
    private static int ReadSLEB128(ref byte* p)
    {
        int result = 0;
        int shift = 0;
        byte b;

        do
        {
            b = *p++;
            result |= (b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);

        // Sign extend if negative
        if (shift < 32 && (b & 0x40) != 0)
            result |= ~0 << shift;

        return result;
    }

#if ARCH_X64
    // DWARF CFI opcodes
    private const byte DW_CFA_advance_loc = 0x40;      // 0x40 + delta (high 2 bits = 01)
    private const byte DW_CFA_offset = 0x80;           // 0x80 + reg (high 2 bits = 10), followed by ULEB128 offset
    private const byte DW_CFA_restore = 0xC0;          // 0xC0 + reg (high 2 bits = 11)
    private const byte DW_CFA_nop = 0x00;
    private const byte DW_CFA_set_loc = 0x01;
    private const byte DW_CFA_advance_loc1 = 0x02;
    private const byte DW_CFA_advance_loc2 = 0x03;
    private const byte DW_CFA_advance_loc4 = 0x04;
    private const byte DW_CFA_offset_extended = 0x05;
    private const byte DW_CFA_restore_extended = 0x06;
    private const byte DW_CFA_undefined = 0x07;
    private const byte DW_CFA_same_value = 0x08;
    private const byte DW_CFA_register = 0x09;
    private const byte DW_CFA_remember_state = 0x0A;
    private const byte DW_CFA_restore_state = 0x0B;
    private const byte DW_CFA_def_cfa = 0x0C;
    private const byte DW_CFA_def_cfa_register = 0x0D;
    private const byte DW_CFA_def_cfa_offset = 0x0E;
    private const byte DW_CFA_def_cfa_expression = 0x0F;
    private const byte DW_CFA_expression = 0x10;
    private const byte DW_CFA_offset_extended_sf = 0x11;
    private const byte DW_CFA_def_cfa_sf = 0x12;
    private const byte DW_CFA_def_cfa_offset_sf = 0x13;
    private const byte DW_CFA_val_offset = 0x14;
    private const byte DW_CFA_val_offset_sf = 0x15;
    private const byte DW_CFA_val_expression = 0x16;

    /// <summary>
    /// Parse CIE (Common Information Entry) to get initial unwind rules
    /// </summary>
    private static bool ParseCIE(byte* cie, out int codeAlignFactor, out int dataAlignFactor,
                                  out byte returnAddressReg, out byte* initialInstructions, out byte* instructionsEnd)
    {
        codeAlignFactor = 1;
        dataAlignFactor = -8;  // Default for x86-64
        returnAddressReg = (byte)DwarfRegX64.RA;
        initialInstructions = null;
        instructionsEnd = null;

        byte* p = cie;

        // Read length
        uint length = *(uint*)p;
        if (length == 0 || length == 0xFFFFFFFF)
            return false;

        byte* cieEnd = p + 4 + length;
        p += 4;

        // Read CIE ID (should be 0)
        uint cieId = *(uint*)p;
        if (cieId != 0)
            return false;  // Not a CIE
        p += 4;

        // Read version
        byte version = *p++;
        if (version != 1 && version != 3)
            return false;

        // Read augmentation string
        byte* augString = p;
        while (*p != 0) p++;
        p++;  // Skip null terminator

        // Read code alignment factor (ULEB128)
        codeAlignFactor = (int)ReadULEB128(ref p);

        // Read data alignment factor (SLEB128)
        dataAlignFactor = ReadSLEB128(ref p);

        // Read return address register
        if (version == 1)
            returnAddressReg = *p++;
        else
            returnAddressReg = (byte)ReadULEB128(ref p);

        // Handle augmentation data if present
        if (*augString == 'z')
        {
            uint augLen = ReadULEB128(ref p);
            p += augLen;  // Skip augmentation data
        }

        // Remaining bytes are initial instructions
        initialInstructions = p;
        instructionsEnd = cieEnd;

        return true;
    }

    /// <summary>
    /// Parse CFI instructions and update unwind state
    /// </summary>
    private static void ParseCFIInstructions(byte* instructions, byte* instructionsEnd,
                                              nuint pcBegin, nuint targetPC,
                                              int codeAlignFactor, int dataAlignFactor,
                                              ref UnwindState state)
    {
        byte* p = instructions;
        nuint currentPC = pcBegin;

        while (p < instructionsEnd && currentPC <= targetPC)
        {
            byte opcode = *p++;

            // High 2 bits determine opcode type for some instructions
            byte highBits = (byte)(opcode & 0xC0);
            byte lowBits = (byte)(opcode & 0x3F);

            if (highBits == DW_CFA_advance_loc)
            {
                // Advance location by delta * code_align_factor
                currentPC += (uint)(lowBits * codeAlignFactor);
            }
            else if (highBits == DW_CFA_offset)
            {
                // Register is saved at CFA + offset * data_align_factor
                byte reg = lowBits;
                uint offset = ReadULEB128(ref p);
                if (reg < (byte)DwarfRegX64.MAX)
                {
                    state.SetRegLocation((DwarfRegX64)reg, RegSaveKind.AtCfaOffset,
                                         (int)(offset * dataAlignFactor));
                }
            }
            else if (highBits == DW_CFA_restore)
            {
                // Restore register to initial rule
                byte reg = lowBits;
                if (reg < (byte)DwarfRegX64.MAX)
                {
                    state.SetRegLocation((DwarfRegX64)reg, RegSaveKind.SameValue);
                }
            }
            else
            {
                // Low 6 bits are the actual opcode
                switch (opcode)
                {
                    case DW_CFA_nop:
                        break;

                    case DW_CFA_set_loc:
                        currentPC = *(nuint*)p;
                        p += sizeof(nuint);
                        break;

                    case DW_CFA_advance_loc1:
                        currentPC += (uint)(*p++ * codeAlignFactor);
                        break;

                    case DW_CFA_advance_loc2:
                        currentPC += (uint)(*(ushort*)p * codeAlignFactor);
                        p += 2;
                        break;

                    case DW_CFA_advance_loc4:
                        currentPC += (uint)(*(uint*)p * codeAlignFactor);
                        p += 4;
                        break;

                    case DW_CFA_def_cfa:
                        state.CfaRegister = (byte)ReadULEB128(ref p);
                        state.CfaOffset = (int)ReadULEB128(ref p);
                        break;

                    case DW_CFA_def_cfa_register:
                        state.CfaRegister = (byte)ReadULEB128(ref p);
                        break;

                    case DW_CFA_def_cfa_offset:
                        state.CfaOffset = (int)ReadULEB128(ref p);
                        break;

                    case DW_CFA_def_cfa_offset_sf:
                        state.CfaOffset = ReadSLEB128(ref p) * dataAlignFactor;
                        break;

                    case DW_CFA_offset_extended:
                        {
                            byte reg = (byte)ReadULEB128(ref p);
                            uint offset = ReadULEB128(ref p);
                            if (reg < (byte)DwarfRegX64.MAX)
                            {
                                state.SetRegLocation((DwarfRegX64)reg, RegSaveKind.AtCfaOffset,
                                                     (int)(offset * dataAlignFactor));
                            }
                        }
                        break;

                    case DW_CFA_offset_extended_sf:
                        {
                            byte reg = (byte)ReadULEB128(ref p);
                            int offset = ReadSLEB128(ref p);
                            if (reg < (byte)DwarfRegX64.MAX)
                            {
                                state.SetRegLocation((DwarfRegX64)reg, RegSaveKind.AtCfaOffset,
                                                     offset * dataAlignFactor);
                            }
                        }
                        break;

                    case DW_CFA_same_value:
                        {
                            byte reg = (byte)ReadULEB128(ref p);
                            if (reg < (byte)DwarfRegX64.MAX)
                            {
                                state.SetRegLocation((DwarfRegX64)reg, RegSaveKind.SameValue);
                            }
                        }
                        break;

                    case DW_CFA_register:
                        {
                            byte reg = (byte)ReadULEB128(ref p);
                            byte inReg = (byte)ReadULEB128(ref p);
                            if (reg < (byte)DwarfRegX64.MAX)
                            {
                                state.SetRegLocation((DwarfRegX64)reg, RegSaveKind.InRegister, 0, inReg);
                            }
                        }
                        break;

                    case DW_CFA_undefined:
                        {
                            byte reg = (byte)ReadULEB128(ref p);
                            if (reg < (byte)DwarfRegX64.MAX)
                            {
                                state.SetRegLocation((DwarfRegX64)reg, RegSaveKind.Undefined);
                            }
                        }
                        break;

                    case DW_CFA_remember_state:
                    case DW_CFA_restore_state:
                        // TODO: Implement state stack if needed
                        break;

                    case DW_CFA_def_cfa_expression:
                    case DW_CFA_expression:
                    case DW_CFA_val_expression:
                        {
                            // Skip expression - read ULEB128 length and skip bytes
                            uint exprLen = ReadULEB128(ref p);
                            p += exprLen;
                        }
                        break;

                    default:
                        // Unknown opcode - skip
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Apply unwind rules to restore register values
    /// </summary>
    private static void ApplyUnwindRules(ref UnwindState state)
    {
        // Calculate CFA (use signed arithmetic for offset since it can be negative)
        long cfaBase = (long)state.GetRegValue((DwarfRegX64)state.CfaRegister);
        nuint cfa = (nuint)(cfaBase + state.CfaOffset);

        // Apply rules for each register
        for (int i = 0; i < (int)DwarfRegX64.MAX; i++)
        {
            DwarfRegX64 reg = (DwarfRegX64)i;
            RegLocation* loc = state.GetRegLocation(reg);

            switch (loc->Kind)
            {
                case RegSaveKind.AtCfaOffset:
                    // Register is saved at CFA + offset (offset is typically negative)
                    nuint* savedLoc = (nuint*)((long)cfa + loc->Offset);
                    state.SetRegValue(reg, *savedLoc);
                    break;

                case RegSaveKind.InRegister:
                    // Register value is in another register
                    state.SetRegValue(reg, state.GetRegValue((DwarfRegX64)loc->Register));
                    break;

                case RegSaveKind.SameValue:
                    // Register keeps its value - no change needed
                    break;

                case RegSaveKind.Undefined:
                default:
                    // Value is undefined - keep current value
                    break;
            }
        }

        // Update RSP to CFA (standard convention: RSP at call site = CFA)
        state.RSP = cfa;
    }

    /// <summary>
    /// Find and parse FDE for a given IP, returning CFI data pointers
    /// </summary>
    private static bool FindFDEForIP(nuint ip, out nuint methodStart, out byte* pCIE,
                                      out byte* pFDEInstructions, out byte* pFDEInstructionsEnd,
                                      out byte* pLSDA)
    {
        methodStart = 0;
        pCIE = null;
        pFDEInstructions = null;
        pFDEInstructionsEnd = null;
        pLSDA = null;

        byte* ehFrameStart = GetEhFrameStart();
        byte* ehFrameEnd = GetEhFrameEnd();

        if (ehFrameStart == null || ehFrameEnd == null)
            return false;

        byte* p = ehFrameStart;

        while (p < ehFrameEnd)
        {
            uint length = *(uint*)p;
            if (length == 0) break;
            if (length == 0xFFFFFFFF) break;

            byte* recordStart = p;
            byte* recordEnd = p + 4 + length;
            p += 4;

            uint ciePointer = *(uint*)p;
            p += 4;

            if (ciePointer == 0)
            {
                // CIE - skip
                p = recordEnd;
                continue;
            }

            // FDE - parse it
            pCIE = (recordStart + 4) - ciePointer;

            int pcBeginRel = *(int*)p;
            nuint pcBegin = (nuint)(p + pcBeginRel);
            p += 4;

            uint pcRange = *(uint*)p;
            p += 4;

            nuint pcEnd = pcBegin + pcRange;

            if (ip >= pcBegin && ip < pcEnd)
            {
                methodStart = pcBegin;

                // Read augmentation length
                uint augLen = ReadULEB128(ref p);

                if (augLen > 0)
                {
                    // LSDA pointer
                    int lsdaRel = *(int*)p;
                    if (lsdaRel != 0)
                        pLSDA = p + lsdaRel;
                    p += (int)augLen;
                }

                pFDEInstructions = p;
                pFDEInstructionsEnd = recordEnd;
                return true;
            }

            p = recordEnd;
        }

        return false;
    }

    /// <summary>
    /// Unwind one frame using DWARF CFI information
    /// </summary>
    private static bool UnwindOneFrameWithCFI(ref UnwindState state, nuint returnAddress)
    {
        Serial.WriteString("[CFI] Unwinding for IP 0x");
        Serial.WriteHex(returnAddress);
        Serial.WriteString("\n");

        // Find FDE for this return address
        if (!FindFDEForIP(returnAddress, out nuint methodStart, out byte* pCIE,
                          out byte* pFDEInstructions, out byte* pFDEInstructionsEnd, out _))
        {
            Serial.WriteString("[CFI] FDE not found\n");
            return false;
        }

        Serial.WriteString("[CFI] Found FDE for method at 0x");
        Serial.WriteHex(methodStart);
        Serial.WriteString("\n");

        // Parse CIE
        if (!ParseCIE(pCIE, out int codeAlignFactor, out int dataAlignFactor,
                      out byte returnAddressReg, out byte* initialInstructions, out byte* initialInstructionsEnd))
        {
            Serial.WriteString("[CFI] CIE parse failed\n");
            return false;
        }

        Serial.WriteString("[CFI] CIE: codeAlign=");
        Serial.WriteNumber((nuint)codeAlignFactor);
        Serial.WriteString(" dataAlign=");
        Serial.WriteNumber((nuint)(uint)dataAlignFactor);
        Serial.WriteString(" RA reg=");
        Serial.WriteNumber(returnAddressReg);
        Serial.WriteString("\n");

        // Initialize default CFA (typically RSP + 8 at function entry on x86-64)
        state.CfaRegister = (byte)DwarfRegX64.RSP;
        state.CfaOffset = 8;

        // Mark return address register as saved at CFA-8 (typical x86-64 convention)
        state.SetRegLocation((DwarfRegX64)returnAddressReg, RegSaveKind.AtCfaOffset, -8);

        // Parse initial instructions from CIE
        if (initialInstructions != null && initialInstructionsEnd != null)
        {
            ParseCFIInstructions(initialInstructions, initialInstructionsEnd,
                                 methodStart, returnAddress,
                                 codeAlignFactor, dataAlignFactor, ref state);
        }

        // Parse FDE instructions
        ParseCFIInstructions(pFDEInstructions, pFDEInstructionsEnd,
                             methodStart, returnAddress,
                             codeAlignFactor, dataAlignFactor, ref state);

        // Debug: show register save rules
        Serial.WriteString("[CFI] RBP rule: kind=");
        RegLocation* rbpLoc = state.GetRegLocation(DwarfRegX64.RBP);
        Serial.WriteNumber((nuint)rbpLoc->Kind);
        if (rbpLoc->Kind == RegSaveKind.AtCfaOffset)
        {
            Serial.WriteString(" offset=");
            Serial.WriteNumber((nuint)(uint)rbpLoc->Offset);
        }
        Serial.WriteString("\n");

        Serial.WriteString("[CFI] R12 rule: kind=");
        RegLocation* r12Loc = state.GetRegLocation(DwarfRegX64.R12);
        Serial.WriteNumber((nuint)r12Loc->Kind);
        if (r12Loc->Kind == RegSaveKind.AtCfaOffset)
        {
            Serial.WriteString(" offset=");
            Serial.WriteNumber((nuint)(uint)r12Loc->Offset);
        }
        Serial.WriteString("\n");

        Serial.WriteString("[CFI] RA rule: kind=");
        RegLocation* raLoc = state.GetRegLocation(DwarfRegX64.RA);
        Serial.WriteNumber((nuint)raLoc->Kind);
        if (raLoc->Kind == RegSaveKind.AtCfaOffset)
        {
            Serial.WriteString(" offset=");
            Serial.WriteNumber((nuint)(uint)raLoc->Offset);
        }
        Serial.WriteString("\n");

        Serial.WriteString("[CFI] Before apply: CFA reg=");
        Serial.WriteNumber(state.CfaRegister);
        Serial.WriteString(" offset=");
        Serial.WriteNumber((nuint)(uint)state.CfaOffset);
        Serial.WriteString(" RSP=0x");
        Serial.WriteHex(state.RSP);
        Serial.WriteString(" RBP=0x");
        Serial.WriteHex(state.RBP);
        Serial.WriteString("\n");

        // Apply unwind rules
        ApplyUnwindRules(ref state);

        Serial.WriteString("[CFI] After apply: RSP=0x");
        Serial.WriteHex(state.RSP);
        Serial.WriteString(" RBP=0x");
        Serial.WriteHex(state.RBP);
        Serial.WriteString(" R12=0x");
        Serial.WriteHex(state.R12);
        Serial.WriteString("\n");

        return true;
    }

    /// <summary>
    /// Initialize unwind state from throw-site context
    /// </summary>
    private static void InitUnwindStateFromContext(ref UnwindState state, PAL_LIMITED_CONTEXT* pContext)
    {
        // Copy register values from context
        state.RBX = pContext->Rbx;
        state.RBP = pContext->Rbp;
        state.RSP = pContext->Rsp;
        state.R12 = pContext->R12;
        state.R13 = pContext->R13;
        state.R14 = pContext->R14;
        state.R15 = pContext->R15;
        state.ReturnAddress = pContext->IP;

        // Initialize all registers to SameValue (callee-saved by default)
        for (int i = 0; i < (int)DwarfRegX64.MAX; i++)
        {
            state.SetRegLocation((DwarfRegX64)i, RegSaveKind.SameValue);
        }
    }
#elif ARCH_ARM64
    // DWARF CFI opcodes (same across architectures)
    private const byte DW_CFA_advance_loc = 0x40;
    private const byte DW_CFA_offset = 0x80;
    private const byte DW_CFA_restore = 0xC0;
    private const byte DW_CFA_nop = 0x00;
    private const byte DW_CFA_set_loc = 0x01;
    private const byte DW_CFA_advance_loc1 = 0x02;
    private const byte DW_CFA_advance_loc2 = 0x03;
    private const byte DW_CFA_advance_loc4 = 0x04;
    private const byte DW_CFA_offset_extended = 0x05;
    private const byte DW_CFA_restore_extended = 0x06;
    private const byte DW_CFA_undefined = 0x07;
    private const byte DW_CFA_same_value = 0x08;
    private const byte DW_CFA_register = 0x09;
    private const byte DW_CFA_remember_state = 0x0A;
    private const byte DW_CFA_restore_state = 0x0B;
    private const byte DW_CFA_def_cfa = 0x0C;
    private const byte DW_CFA_def_cfa_register = 0x0D;
    private const byte DW_CFA_def_cfa_offset = 0x0E;
    private const byte DW_CFA_def_cfa_expression = 0x0F;
    private const byte DW_CFA_expression = 0x10;
    private const byte DW_CFA_offset_extended_sf = 0x11;
    private const byte DW_CFA_def_cfa_sf = 0x12;
    private const byte DW_CFA_def_cfa_offset_sf = 0x13;
    private const byte DW_CFA_val_offset = 0x14;
    private const byte DW_CFA_val_offset_sf = 0x15;
    private const byte DW_CFA_val_expression = 0x16;

    /// <summary>
    /// Parse CIE for ARM64
    /// </summary>
    private static bool ParseCIE(byte* cie, out int codeAlignFactor, out int dataAlignFactor,
                                  out byte returnAddressReg, out byte* initialInstructions, out byte* instructionsEnd)
    {
        codeAlignFactor = 4;   // ARM64 instructions are 4 bytes
        dataAlignFactor = -8;  // Default for AArch64
        returnAddressReg = (byte)DwarfRegARM64.LR;  // X30 is return address
        initialInstructions = null;
        instructionsEnd = null;

        byte* p = cie;
        uint length = *(uint*)p;
        if (length == 0 || length == 0xFFFFFFFF)
            return false;

        byte* cieEnd = p + 4 + length;
        p += 4;

        uint cieId = *(uint*)p;
        p += 4;

        if (cieId != 0)
            return false;

        byte version = *p++;
        if (version != 1 && version != 3 && version != 4)
            return false;

        byte* augString = p;
        while (*p != 0) p++;
        p++;

        if (version == 4)
        {
            p++;  // address_size
            p++;  // segment_selector_size
        }

        codeAlignFactor = (int)ReadULEB128(ref p);
        dataAlignFactor = ReadSLEB128(ref p);

        if (version == 1)
            returnAddressReg = *p++;
        else
            returnAddressReg = (byte)ReadULEB128(ref p);

        if (*augString == 'z')
        {
            uint augLen = ReadULEB128(ref p);
            p += augLen;
        }

        initialInstructions = p;
        instructionsEnd = cieEnd;

        return true;
    }

    /// <summary>
    /// Parse CFI instructions for ARM64
    /// </summary>
    private static void ParseCFIInstructions(byte* instructions, byte* instructionsEnd,
                                              nuint pcBegin, nuint targetPC,
                                              int codeAlignFactor, int dataAlignFactor,
                                              ref UnwindState state)
    {
        byte* p = instructions;
        nuint currentPC = pcBegin;

        while (p < instructionsEnd && currentPC <= targetPC)
        {
            byte opcode = *p++;
            byte highBits = (byte)(opcode & 0xC0);
            byte lowBits = (byte)(opcode & 0x3F);

            if (highBits == DW_CFA_advance_loc)
            {
                currentPC += (uint)(lowBits * codeAlignFactor);
            }
            else if (highBits == DW_CFA_offset)
            {
                byte reg = lowBits;
                uint offset = ReadULEB128(ref p);
                if (reg < (byte)DwarfRegARM64.MAX)
                {
                    state.SetRegLocation((DwarfRegARM64)reg, RegSaveKind.AtCfaOffset,
                                         (int)(offset * dataAlignFactor));
                }
            }
            else if (highBits == DW_CFA_restore)
            {
                byte reg = lowBits;
                if (reg < (byte)DwarfRegARM64.MAX)
                {
                    state.SetRegLocation((DwarfRegARM64)reg, RegSaveKind.SameValue);
                }
            }
            else
            {
                switch (opcode)
                {
                    case DW_CFA_nop:
                        break;

                    case DW_CFA_set_loc:
                        currentPC = *(nuint*)p;
                        p += sizeof(nuint);
                        break;

                    case DW_CFA_advance_loc1:
                        currentPC += (uint)(*p++ * codeAlignFactor);
                        break;

                    case DW_CFA_advance_loc2:
                        currentPC += (uint)(*(ushort*)p * codeAlignFactor);
                        p += 2;
                        break;

                    case DW_CFA_advance_loc4:
                        currentPC += (uint)(*(uint*)p * codeAlignFactor);
                        p += 4;
                        break;

                    case DW_CFA_def_cfa:
                        state.CfaRegister = (byte)ReadULEB128(ref p);
                        state.CfaOffset = (int)ReadULEB128(ref p);
                        break;

                    case DW_CFA_def_cfa_register:
                        state.CfaRegister = (byte)ReadULEB128(ref p);
                        break;

                    case DW_CFA_def_cfa_offset:
                        state.CfaOffset = (int)ReadULEB128(ref p);
                        break;

                    case DW_CFA_def_cfa_offset_sf:
                        state.CfaOffset = ReadSLEB128(ref p) * dataAlignFactor;
                        break;

                    case DW_CFA_offset_extended:
                        {
                            byte reg = (byte)ReadULEB128(ref p);
                            uint offset = ReadULEB128(ref p);
                            if (reg < (byte)DwarfRegARM64.MAX)
                            {
                                state.SetRegLocation((DwarfRegARM64)reg, RegSaveKind.AtCfaOffset,
                                                     (int)(offset * dataAlignFactor));
                            }
                        }
                        break;

                    case DW_CFA_offset_extended_sf:
                        {
                            byte reg = (byte)ReadULEB128(ref p);
                            int offset = ReadSLEB128(ref p);
                            if (reg < (byte)DwarfRegARM64.MAX)
                            {
                                state.SetRegLocation((DwarfRegARM64)reg, RegSaveKind.AtCfaOffset,
                                                     offset * dataAlignFactor);
                            }
                        }
                        break;

                    case DW_CFA_same_value:
                        {
                            byte reg = (byte)ReadULEB128(ref p);
                            if (reg < (byte)DwarfRegARM64.MAX)
                            {
                                state.SetRegLocation((DwarfRegARM64)reg, RegSaveKind.SameValue);
                            }
                        }
                        break;

                    case DW_CFA_register:
                        {
                            byte reg = (byte)ReadULEB128(ref p);
                            byte inReg = (byte)ReadULEB128(ref p);
                            if (reg < (byte)DwarfRegARM64.MAX)
                            {
                                state.SetRegLocation((DwarfRegARM64)reg, RegSaveKind.InRegister, 0, inReg);
                            }
                        }
                        break;

                    case DW_CFA_undefined:
                        {
                            byte reg = (byte)ReadULEB128(ref p);
                            if (reg < (byte)DwarfRegARM64.MAX)
                            {
                                state.SetRegLocation((DwarfRegARM64)reg, RegSaveKind.Undefined);
                            }
                        }
                        break;

                    case DW_CFA_remember_state:
                    case DW_CFA_restore_state:
                        break;

                    case DW_CFA_def_cfa_expression:
                    case DW_CFA_expression:
                    case DW_CFA_val_expression:
                        {
                            uint exprLen = ReadULEB128(ref p);
                            p += exprLen;
                        }
                        break;

                    default:
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Apply unwind rules for ARM64
    /// </summary>
    private static void ApplyUnwindRules(ref UnwindState state)
    {
        // Calculate CFA
        long cfaBase = (long)state.GetRegValue((DwarfRegARM64)state.CfaRegister);
        nuint cfa = (nuint)(cfaBase + state.CfaOffset);

        // Apply rules for callee-saved registers (X19-X28, FP, LR, SP)
        for (int i = 0; i < (int)DwarfRegARM64.MAX; i++)
        {
            DwarfRegARM64 reg = (DwarfRegARM64)i;
            RegLocation* loc = state.GetRegLocation(reg);

            switch (loc->Kind)
            {
                case RegSaveKind.AtCfaOffset:
                    nuint* savedLoc = (nuint*)((long)cfa + loc->Offset);
                    state.SetRegValue(reg, *savedLoc);
                    break;

                case RegSaveKind.InRegister:
                    state.SetRegValue(reg, state.GetRegValue((DwarfRegARM64)loc->Register));
                    break;

                case RegSaveKind.SameValue:
                case RegSaveKind.Undefined:
                default:
                    break;
            }
        }

        // Update SP to CFA
        state.SP = cfa;
    }

    /// <summary>
    /// Find FDE for ARM64
    /// </summary>
    private static bool FindFDEForIP(nuint ip, out nuint methodStart, out byte* pCIE,
                                      out byte* pFDEInstructions, out byte* pFDEInstructionsEnd,
                                      out byte* pLSDA)
    {
        methodStart = 0;
        pCIE = null;
        pFDEInstructions = null;
        pFDEInstructionsEnd = null;
        pLSDA = null;

        byte* ehFrameStart = GetEhFrameStart();
        byte* ehFrameEnd = GetEhFrameEnd();

        if (ehFrameStart == null || ehFrameEnd == null)
            return false;

        byte* p = ehFrameStart;

        while (p < ehFrameEnd)
        {
            uint length = *(uint*)p;
            if (length == 0) break;
            if (length == 0xFFFFFFFF) break;

            byte* recordStart = p;
            byte* recordEnd = p + 4 + length;
            p += 4;

            uint ciePointer = *(uint*)p;
            p += 4;

            if (ciePointer == 0)
            {
                p = recordEnd;
                continue;
            }

            pCIE = (recordStart + 4) - ciePointer;

            int pcBeginRel = *(int*)p;
            nuint pcBegin = (nuint)(p + pcBeginRel);
            p += 4;

            uint pcRange = *(uint*)p;
            p += 4;

            nuint pcEnd = pcBegin + pcRange;

            if (ip >= pcBegin && ip < pcEnd)
            {
                methodStart = pcBegin;

                uint augLen = ReadULEB128(ref p);
                if (augLen > 0)
                {
                    int lsdaRel = *(int*)p;
                    if (lsdaRel != 0)
                        pLSDA = p + lsdaRel;
                    p += (int)augLen;
                }

                pFDEInstructions = p;
                pFDEInstructionsEnd = recordEnd;
                return true;
            }

            p = recordEnd;
        }

        return false;
    }

    /// <summary>
    /// Unwind one frame using CFI for ARM64
    /// </summary>
    private static bool UnwindOneFrameWithCFI(ref UnwindState state, nuint returnAddress)
    {
        Serial.WriteString("[CFI] Unwinding for IP 0x");
        Serial.WriteHex(returnAddress);
        Serial.WriteString("\n");

        if (!FindFDEForIP(returnAddress, out nuint methodStart, out byte* pCIE,
                          out byte* pFDEInstructions, out byte* pFDEInstructionsEnd, out _))
        {
            Serial.WriteString("[CFI] FDE not found\n");
            return false;
        }

        Serial.WriteString("[CFI] Found FDE for method at 0x");
        Serial.WriteHex(methodStart);
        Serial.WriteString("\n");

        if (!ParseCIE(pCIE, out int codeAlignFactor, out int dataAlignFactor,
                      out byte returnAddressReg, out byte* initialInstructions, out byte* initialInstructionsEnd))
        {
            Serial.WriteString("[CFI] CIE parse failed\n");
            return false;
        }

        Serial.WriteString("[CFI] CIE: codeAlign=");
        Serial.WriteNumber((nuint)codeAlignFactor);
        Serial.WriteString(" dataAlign=");
        Serial.WriteNumber((nuint)(uint)dataAlignFactor);
        Serial.WriteString(" RA reg=");
        Serial.WriteNumber(returnAddressReg);
        Serial.WriteString("\n");

        // Initialize default CFA for ARM64 (SP + 0 at function entry)
        state.CfaRegister = (byte)DwarfRegARM64.SP;
        state.CfaOffset = 0;

        // Parse initial instructions from CIE
        if (initialInstructions != null && initialInstructionsEnd != null)
        {
            ParseCFIInstructions(initialInstructions, initialInstructionsEnd,
                                 methodStart, returnAddress,
                                 codeAlignFactor, dataAlignFactor, ref state);
        }

        // Parse FDE instructions
        ParseCFIInstructions(pFDEInstructions, pFDEInstructionsEnd,
                             methodStart, returnAddress,
                             codeAlignFactor, dataAlignFactor, ref state);

        // Debug output
        Serial.WriteString("[CFI] CFA: reg=");
        Serial.WriteNumber(state.CfaRegister);
        Serial.WriteString(" offset=");
        Serial.WriteNumber((nuint)(uint)state.CfaOffset);
        Serial.WriteString("\n");

        Serial.WriteString("[CFI] FP rule: kind=");
        RegLocation* fpLoc = state.GetRegLocation(DwarfRegARM64.FP);
        Serial.WriteNumber((nuint)fpLoc->Kind);
        if (fpLoc->Kind == RegSaveKind.AtCfaOffset)
        {
            Serial.WriteString(" offset=");
            Serial.WriteNumber((nuint)(uint)fpLoc->Offset);
        }
        Serial.WriteString("\n");

        Serial.WriteString("[CFI] LR rule: kind=");
        RegLocation* lrLoc = state.GetRegLocation(DwarfRegARM64.LR);
        Serial.WriteNumber((nuint)lrLoc->Kind);
        if (lrLoc->Kind == RegSaveKind.AtCfaOffset)
        {
            Serial.WriteString(" offset=");
            Serial.WriteNumber((nuint)(uint)lrLoc->Offset);
        }
        Serial.WriteString("\n");

        // Apply unwind rules
        ApplyUnwindRules(ref state);

        // Debug: show unwound values
        Serial.WriteString("[CFI] Unwound: SP=0x");
        Serial.WriteHex(state.SP);
        Serial.WriteString(" FP=0x");
        Serial.WriteHex(state.FP);
        Serial.WriteString(" LR=0x");
        Serial.WriteHex(state.LR);
        Serial.WriteString("\n");

        Serial.WriteString("[CFI] X19=0x");
        Serial.WriteHex(state.X19);
        Serial.WriteString(" X20=0x");
        Serial.WriteHex(state.X20);
        Serial.WriteString(" X21=0x");
        Serial.WriteHex(state.X21);
        Serial.WriteString("\n");

        return true;
    }

    /// <summary>
    /// Initialize unwind state from throw-site context for ARM64
    /// </summary>
    private static void InitUnwindStateFromContext(ref UnwindState state, PAL_LIMITED_CONTEXT* pContext)
    {
        // Copy register values from context
        state.SP = pContext->SP;
        state.FP = pContext->FP;
        state.LR = pContext->LR;
        state.ReturnAddress = pContext->IP;
        state.X19 = pContext->X19;
        state.X20 = pContext->X20;
        state.X21 = pContext->X21;
        state.X22 = pContext->X22;
        state.X23 = pContext->X23;
        state.X24 = pContext->X24;
        state.X25 = pContext->X25;
        state.X26 = pContext->X26;
        state.X27 = pContext->X27;
        state.X28 = pContext->X28;

        // Initialize all registers to SameValue (callee-saved by default)
        for (int i = 0; i < (int)DwarfRegARM64.MAX; i++)
        {
            state.SetRegLocation((DwarfRegARM64)i, RegSaveKind.SameValue);
        }
    }
#endif

    /// <summary>
    /// Parse LSDA and find a handler for the given code offset
    /// </summary>
    private static bool TryFindHandlerInLSDA(Exception ex, byte* pLSDA, nuint methodStart, uint codeOffset, out EHClause clause, REGDISPLAY* pRegDisplay)
    {
        clause = default;

        if (pLSDA == null)
            return false;

        byte* p = pLSDA;

        // Read unwind block flags
        byte unwindBlockFlags = *p++;

        // Skip funclet reference if not root
        if ((unwindBlockFlags & UBF_FUNC_KIND_MASK) != UBF_FUNC_KIND_ROOT)
        {
            // Funclet - skip relative offsets
            p += sizeof(int); // mainLSDA offset
            p += sizeof(int); // method start offset
        }

        // Skip associated data if present
        if ((unwindBlockFlags & UBF_FUNC_HAS_ASSOCIATED_DATA) != 0)
            p += sizeof(int);

        // Check if method has EH info
        if ((unwindBlockFlags & UBF_FUNC_HAS_EHINFO) == 0)
        {
            Serial.WriteString("[EH] Method has no EH info\n");
            return false;
        }

        // Read EH info offset and follow it
        int ehInfoOffset = *(int*)p;
        byte* pEHInfo = p + ehInfoOffset;
        p += sizeof(int);

        // Read number of clauses
        uint nClauses = ReadUnsigned(ref pEHInfo);
        Serial.WriteString("[EH] Method has ");
        Serial.WriteNumber(nClauses);
        Serial.WriteString(" EH clauses\n");

        // Enumerate clauses looking for a match
        for (uint i = 0; i < nClauses; i++)
        {
            uint tryStartOffset = ReadUnsigned(ref pEHInfo);
            uint tryEndDeltaAndKind = ReadUnsigned(ref pEHInfo);

            EHClauseKind kind = (EHClauseKind)(tryEndDeltaAndKind & 0x3);
            uint tryEndOffset = tryStartOffset + (tryEndDeltaAndKind >> 2);

            Serial.WriteString("[EH] Clause ");
            Serial.WriteNumber(i);
            Serial.WriteString(": try 0x");
            Serial.WriteHex(tryStartOffset);
            Serial.WriteString("-0x");
            Serial.WriteHex(tryEndOffset);
            Serial.WriteString(" kind=");
            Serial.WriteNumber((uint)kind);
            Serial.WriteString("\n");

            // Read handler address
            uint handlerOffset = 0;
            byte* filterAddress = null;
            void* targetType = null;

            switch (kind)
            {
                case EHClauseKind.EH_CLAUSE_TYPED:
                    handlerOffset = ReadUnsigned(ref pEHInfo);
                    // Read type RVA
                    byte* pTypeBase = pEHInfo;
                    int typeRelAddr = ReadInt32(ref pEHInfo);
                    targetType = pTypeBase + typeRelAddr;
                    break;

                case EHClauseKind.EH_CLAUSE_FAULT:
                    handlerOffset = ReadUnsigned(ref pEHInfo);
                    break;

                case EHClauseKind.EH_CLAUSE_FILTER:
                    handlerOffset = ReadUnsigned(ref pEHInfo);
                    uint filterOffset = ReadUnsigned(ref pEHInfo);
                    filterAddress = (byte*)(methodStart + filterOffset);
                    break;
            }

            // Check if this clause covers our code offset
            if (codeOffset >= tryStartOffset && codeOffset < tryEndOffset)
            {
                Serial.WriteString("[EH] Found matching clause!\n");

                // Skip fault handlers for now - they need different handling
                // Fault handlers (like finally) should run during unwinding, not catch the exception
                if (kind == EHClauseKind.EH_CLAUSE_FAULT)
                {
                    Serial.WriteString("[EH] Skipping fault handler (not implemented yet)\n");
                    continue;
                }

                // For filter clauses, we need to evaluate the filter to see if it matches
                if (kind == EHClauseKind.EH_CLAUSE_FILTER)
                {
                    Serial.WriteString("[EH] Evaluating filter at 0x");
                    Serial.WriteHex((nuint)filterAddress);
                    Serial.WriteString("\n");

                    // If we have a valid REGDISPLAY, call the filter funclet
                    if (pRegDisplay != null && filterAddress != null)
                    {
                        nint exceptionPtr = Unsafe.As<Exception, nint>(ref ex);
                        nint filterResult = RhpCallFilterFunclet(exceptionPtr, filterAddress, pRegDisplay);

                        Serial.WriteString("[EH] Filter returned: ");
                        Serial.WriteNumber((nuint)filterResult);
                        Serial.WriteString("\n");

                        // If filter returned 0, this clause doesn't match - continue to next clause
                        if (filterResult == 0)
                        {
                            Serial.WriteString("[EH] Filter did not match, continuing search\n");
                            continue;
                        }

                        Serial.WriteString("[EH] Filter matched!\n");
                    }
                    else
                    {
                        // Without REGDISPLAY we can't call the filter - skip this clause
                        Serial.WriteString("[EH] No REGDISPLAY available for filter evaluation, skipping\n");
                        continue;
                    }
                }

                clause.ClauseKind = kind;
                clause.TryStartOffset = tryStartOffset;
                clause.TryEndOffset = tryEndOffset;
                clause.HandlerAddress = (byte*)(methodStart + handlerOffset);
                clause.FilterAddress = filterAddress;
                clause.TargetType = targetType;

                Serial.WriteString("[EH] Handler offset: 0x");
                Serial.WriteHex(handlerOffset);
                Serial.WriteString(" methodStart: 0x");
                Serial.WriteHex((nuint)methodStart);
                Serial.WriteString("\n");
                Serial.WriteString("[EH] Handler at 0x");
                Serial.WriteHex((nuint)clause.HandlerAddress);
                Serial.WriteString("\n");

                return true;
            }
        }

        Serial.WriteString("[EH] No matching clause found\n");
        return false;
    }

    /// <summary>
    /// Read variable-length encoded unsigned integer (NativePrimitiveDecoder format)
    /// </summary>
    private static uint ReadUnsigned(ref byte* p)
    {
        uint val = *p;
        uint value;

        if ((val & 1) == 0)
        {
            value = val >> 1;
            p += 1;
        }
        else if ((val & 2) == 0)
        {
            value = (val >> 2) | ((uint)*(p + 1) << 6);
            p += 2;
        }
        else if ((val & 4) == 0)
        {
            value = (val >> 3) | ((uint)*(p + 1) << 5) | ((uint)*(p + 2) << 13);
            p += 3;
        }
        else if ((val & 8) == 0)
        {
            value = (val >> 4) | ((uint)*(p + 1) << 4) | ((uint)*(p + 2) << 12) | ((uint)*(p + 3) << 20);
            p += 4;
        }
        else
        {
            value = (uint)(*(p + 1) | (*(p + 2) << 8) | (*(p + 3) << 16) | (*(p + 4) << 24));
            p += 5;
        }

        return value;
    }

    /// <summary>
    /// Read 32-bit signed integer
    /// </summary>
    private static int ReadInt32(ref byte* p)
    {
        int value = *p | (*(p + 1) << 8) | (*(p + 2) << 16) | (*(p + 3) << 24);
        p += 4;
        return value;
    }

    /// <summary>
    /// Invoke a catch handler funclet using the assembly helper that properly restores
    /// the stack and registers before jumping to the resume address.
    /// </summary>
    private static void InvokeCatchHandler(Exception ex, ref StackFrame frame, ref EHClause clause, void* pExInfo, REGDISPLAY* pRegDisplay)
    {
        Serial.WriteString("[EH] Invoking catch handler\n");
        Serial.WriteString("[EH] Handler at: 0x");
        Serial.WriteHex((nuint)clause.HandlerAddress);
        Serial.WriteString("\n");

        // Convert exception object to pointer for funclet
        nint exceptionPtr = Unsafe.As<Exception, nint>(ref ex);

        Serial.WriteString("[EH] Calling funclet with exception at 0x");
        Serial.WriteHex((nuint)exceptionPtr);
        Serial.WriteString("\n");

        if (pRegDisplay != null)
        {
#if ARCH_X64
            Serial.WriteString("[EH] REGDISPLAY: SP=0x");
            Serial.WriteHex(pRegDisplay->SP);
            Serial.WriteString(" RBP=0x");
            Serial.WriteHex(pRegDisplay->Rbp);
            Serial.WriteString(" R12=0x");
            Serial.WriteHex(pRegDisplay->R12);
            Serial.WriteString(" R13=0x");
            Serial.WriteHex(pRegDisplay->R13);
            Serial.WriteString("\n");
#elif ARCH_ARM64
            Serial.WriteString("[EH] REGDISPLAY: SP=0x");
            Serial.WriteHex(pRegDisplay->SP);
            Serial.WriteString("\n");
#endif

            // Clear the exception handling flag BEFORE calling funclet
            s_isHandlingException = false;

            // Call the assembly helper that invokes the catch funclet and resumes execution
            // This function never returns - it jumps directly to the resume address
            RhpCallCatchFunclet(exceptionPtr, clause.HandlerAddress, pRegDisplay, pExInfo);
        }

        // Should not reach here - RhpCallCatchFunclet never returns
        Serial.WriteString("[EH] ERROR: No valid REGDISPLAY for funclet call\n");
        FailFast("Invalid exception context", ex);
    }

    /// <summary>
    /// Fail fast - terminate the system
    /// </summary>
    public static void FailFast(string message, Exception? exception = null)
    {
        Serial.WriteString("[FAILFAST] ");
        Serial.WriteString(message);
        Serial.WriteString("\n");

        if (exception is not null)
        {
            Serial.WriteString("Exception: ");
            Serial.WriteString(exception.Message);
            Serial.WriteString("\n");
            if (exception.StackTrace is not null)
            {
                Serial.WriteString("Stack Trace:\n");
                Serial.WriteString(exception.StackTrace);
                Serial.WriteString("\n");
            }
        }

        // Halt the system - infinite loop
        while (true) { }
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
