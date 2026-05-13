using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Runtime.GcInfo;
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
            DwarfRegX64.RAX => RAX,
            DwarfRegX64.RDX => RDX,
            DwarfRegX64.RCX => RCX,
            DwarfRegX64.RBX => RBX,
            DwarfRegX64.RSI => RSI,
            DwarfRegX64.RDI => RDI,
            DwarfRegX64.RBP => RBP,
            DwarfRegX64.RSP => RSP,
            DwarfRegX64.R8 => R8,
            DwarfRegX64.R9 => R9,
            DwarfRegX64.R10 => R10,
            DwarfRegX64.R11 => R11,
            DwarfRegX64.R12 => R12,
            DwarfRegX64.R13 => R13,
            DwarfRegX64.R14 => R14,
            DwarfRegX64.R15 => R15,
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

    // Assembly funclet callers - use nint for object reference since P/Invoke doesn't support object
    [LibraryImport("*", EntryPoint = "RhpCallCatchFunclet")]
    [SuppressGCTransition]
    private static partial void* RhpCallCatchFunclet(nint exceptionPtr, void* handlerAddress, void* pRegDisplay, void* pExInfo);

    [LibraryImport("*", EntryPoint = "RhpCallFilterFunclet")]
    [SuppressGCTransition]
    private static partial nint RhpCallFilterFunclet(nint exceptionPtr, void* filterAddress, void* pRegDisplay);

    // Guard against recursive exception handling
    private static bool s_isHandlingException = false;

    /// <summary>
    /// Managed entry point for a thrown exception, called from <c>RhThrowEx</c> (the assembly stub)
    /// with the throw-site register context. Walks the stack, runs filters, and transfers to the
    /// catch handler; if nothing catches it, halts.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowExceptionWithContext(Exception ex, nuint throwAddress, nuint throwRbp, nuint throwRsp, void* pExInfo)
    {
        if (ex == null)
        {
            FailFast("Null exception", ex);
            return;
        }

        // Guard against an exception thrown while we're already dispatching one.
        if (s_isHandlingException)
        {
            FailFast("Recursive exception", ex);
            return;
        }
        s_isHandlingException = true;

        Serial.WriteString("\n=== DOTNET EXCEPTION THROWN ===\n");
        // Print the message before the stack walk, in case the walk crashes. Avoid GetType().Name —
        // it allocates.
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

        DispatchExceptionWithContext(ex, throwAddress, throwRbp, throwRsp, pExInfo);

        // DispatchExceptionWithContext transfers to the handler on success; it only returns here
        // when no handler covered the throw.
        Serial.WriteString("\n*** UNHANDLED EXCEPTION ***\n");
        Serial.WriteString("No catch handler found. System halting...\n");
        FailFast("Unhandled exception", ex);
    }

    /// <summary>
    /// Two-pass exception dispatch from the throw-site context: pass 1 walks the stack to find a
    /// handler (unwinding the register state at each frame so filters can run); pass 2 transfers to
    /// the catch handler via <see cref="InvokeCatchHandler"/>, which does not return. Returns only
    /// when no handler was found (or the throw-site frame pointer was missing).
    /// </summary>
    private static void DispatchExceptionWithContext(Exception ex, nuint throwAddress, nuint throwRbp, nuint throwRsp, void* pExInfo)
    {
        // No frame pointer from the throw-site context → can't walk the stack.
        if (throwRbp == 0)
        {
            return;
        }

        // REGDISPLAY built from the unwound register state, passed to filter / catch funclets.
        REGDISPLAY regDisplay = default;
        REGDISPLAY* pRegDisplay = null;

        // Seed the CFI unwind state from the throw-site context.
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

        // Pass 1: walk the frame-pointer chain looking for a handler, unwinding registers as we go.
        StackFrame catchFrame = default;
        EHClause catchClause = default;
        bool foundHandler = false;

        StackFrame frame;
        frame.FramePointer = throwRbp;
        frame.StackPointer = throwRsp;
        frame.ReturnAddress = throwAddress;

        int frameCount = 0;
        while (frameCount < MAX_STACK_FRAMES && frame.FramePointer != 0)
        {
            // Refresh the REGDISPLAY from the current unwind state so a filter can be evaluated.
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

            // Does this frame have a handler covering the call that threw?
            if (TryFindHandler(ex, frame.ReturnAddress, out EHClause clause, pRegDisplay))
            {
                Serial.WriteString("[EH] Handler found at 0x");
                Serial.WriteHex((nuint)clause.HandlerAddress);
                Serial.WriteString("\n");

                catchFrame = frame;
                catchClause = clause;
                foundHandler = true;
                break;
            }

            // Capture this frame's IP before stepping up — CFI unwinds it below.
            nuint currentFrameIP = frame.ReturnAddress;

            // Step to the caller via the frame-pointer chain; stop if it runs out.
            if (!UnwindOneFrame(ref frame))
            {
                break;
            }

            // Unwind the register state with CFI for the frame we just left, so it reflects the
            // caller's values on the next iteration (needed for filter evaluation).
            if (pThrowContext != null)
            {
                UnwindOneFrameWithCFI(ref unwindState, currentFrameIP);
            }

            frameCount++;
        }

        if (!foundHandler)
        {
            return;
        }

#if ARCH_X64
        // Build the REGDISPLAY the catch funclet runs with — and that RhpCallCatchFunclet resumes
        // the catching method from once the funclet returns. The CFI walk that landed on the
        // catching frame already reconstructed its body register state at the call that threw, so
        // `unwindState` carries the callee-saved registers and the resume SP directly. RBP is the
        // one exception: it comes from the frame-pointer chain (`[RBP]`), because CFI can legitimately
        // report RBP "SameValue" for a -fomit-frame-pointer caller. The pRxx fields point at our
        // value copies here so RhpCallCatchFunclet can read them and the funclet can write through.
        if (pThrowContext != null)
        {
            pRegDisplay = &regDisplay;

            regDisplay.Rbx = unwindState.RBX;
            regDisplay.Rbp = catchFrame.FramePointer;
            regDisplay.R12 = unwindState.R12;
            regDisplay.R13 = unwindState.R13;
            regDisplay.R14 = unwindState.R14;
            regDisplay.R15 = unwindState.R15;

            // Resume SP = the CFA the CFI unwind landed on (the catching frame's top), NOT its RBP.
            // RhpCallCatchFunclet sets RSP to this before jumping to the funclet's resume IP, so the
            // catching method finds its frame intact and its epilogue restores the caller's regs.
            regDisplay.SP = unwindState.RSP;

            regDisplay.pRbx = &pRegDisplay->Rbx;
            regDisplay.pRbp = &pRegDisplay->Rbp;
            // pRsi / pRdi stay null — not callee-saved under the System V x86-64 ABI.
            regDisplay.pR12 = &pRegDisplay->R12;
            regDisplay.pR13 = &pRegDisplay->R13;
            regDisplay.pR14 = &pRegDisplay->R14;
            regDisplay.pR15 = &pRegDisplay->R15;
        }
#elif ARCH_ARM64
        // Build the REGDISPLAY for the catch funclet. As on x64, FP comes from the frame-pointer
        // chain (CFI can report it "SameValue" for a -fomit-frame-pointer caller) and the callee-
        // saved registers come from the CFI unwind state. ARM64's REGDISPLAY holds register values
        // inline (no pRxx pointer fields like x64), so there's nothing to point at a temporary.
        // NOTE: SP is set to the frame pointer here, not the CFA — RhpCallCatchFunclet's resume tail
        // assumes SP == FP in the handler frame. x64 uses the CFA instead (PR #351); aligning ARM64
        // is a known follow-up (catch handlers in methods with a non-trivial frame).
        if (pThrowContext != null)
        {
            nuint handlerFp = catchFrame.FramePointer;

            pRegDisplay = &regDisplay;

            regDisplay.SP = handlerFp;
            regDisplay.FP = handlerFp;
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
        }
#endif

        // Pass 2: transfer to the catch handler.
        // TODO: execute finally handlers between the throw and the catch first.
        InvokeCatchHandler(ex, ref catchClause, pExInfo, pRegDisplay);
        // InvokeCatchHandler jumps to the resume address and does not return.
    }

    /// <summary>
    /// Steps one frame up the frame-pointer chain: <c>[RBP]</c> holds the caller's saved RBP and
    /// <c>[RBP+8]</c> the return address. Returns <c>false</c> at the bottom of the chain or on a
    /// frame pointer / return address that doesn't look valid.
    /// </summary>
    private static bool UnwindOneFrame(ref StackFrame frame)
    {
        nuint* rbpPtr = (nuint*)frame.FramePointer;
        if (rbpPtr == null || frame.FramePointer < 0x1000)
        {
            Serial.WriteString("[EH] Corrupt frame pointer, stopping stack walk\n");
            return false;
        }

        nuint savedRbp = rbpPtr[0];
        nuint returnAddr = rbpPtr[1];

        // savedRbp == 0 is the bottom of the chain.
        if (savedRbp == 0)
        {
            return false;
        }

        if (returnAddr == 0 || returnAddr < 0x1000)
        {
            Serial.WriteString("[EH] Corrupt return address, stopping stack walk\n");
            return false;
        }

        frame.FramePointer = savedRbp;
        frame.StackPointer = savedRbp + 16;  // approximate RSP after `pop rbp; ret`
        frame.ReturnAddress = returnAddr;
        return true;
    }

    /// <summary>
    /// Looks for an exception handler covering <paramref name="instructionPointer"/> by parsing the
    /// method's LSDA. <paramref name="pRegDisplay"/> (if non-null) lets a <c>when</c>-filter clause
    /// be evaluated. Also appends the method name to the exception's stack trace.
    /// </summary>
    private static bool TryFindHandler(Exception ex, nuint instructionPointer, out EHClause clause, REGDISPLAY* pRegDisplay)
    {
        clause = default;

        if (!TryGetMethodLSDA(instructionPointer, out nuint methodStart, out byte* pLSDA))
        {
            return false;
        }

        if (StackTraceMetadata.IsSupported
            && StackTraceMetadata.TryGetMethodNameFromStartAddress(methodStart, out string methodName))
        {
            ref string? stackTraceString = ref StackTraceMetadata.GetStackTraceString(ex);
            stackTraceString = stackTraceString == null
                ? methodName
                : stackTraceString + Environment.NewLine + "at " + methodName;
        }

        uint codeOffset = (uint)(instructionPointer - methodStart);
        return TryFindHandlerInLSDA(ex, pLSDA, methodStart, codeOffset, out clause, pRegDisplay);
    }

    /// <summary>
    /// Try to find the FDE PC-begin and LSDA pointer for a given IP. Delegates to the shared
    /// .eh_frame / LSDA-header parser in <see cref="MethodGcInfoLookup"/>.
    /// </summary>
    private static bool TryGetMethodLSDA(nuint ip, out nuint methodStart, out byte* pLSDA)
        => MethodGcInfoLookup.TryGetMethodLSDA(ip, out methodStart, out pLSDA);

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
        {
            result |= ~0 << shift;
        }

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
        {
            return false;
        }

        byte* cieEnd = p + 4 + length;
        p += 4;

        // Read CIE ID (should be 0)
        uint cieId = *(uint*)p;
        if (cieId != 0)
        {
            return false;  // Not a CIE
        }

        p += 4;

        // Read version
        byte version = *p++;
        if (version != 1 && version != 3)
        {
            return false;
        }

        // Read augmentation string
        byte* augString = p;
        while (*p != 0)
        {
            p++;
        }

        p++;  // Skip null terminator

        // Read code alignment factor (ULEB128)
        codeAlignFactor = (int)MethodGcInfoLookup.ReadULEB128(ref p);

        // Read data alignment factor (SLEB128)
        dataAlignFactor = ReadSLEB128(ref p);

        // Read return address register
        if (version == 1)
        {
            returnAddressReg = *p++;
        }
        else
        {
            returnAddressReg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
        }

        // Handle augmentation data if present
        if (*augString == 'z')
        {
            uint augLen = MethodGcInfoLookup.ReadULEB128(ref p);
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
                uint offset = MethodGcInfoLookup.ReadULEB128(ref p);
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
                        state.CfaRegister = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                        state.CfaOffset = (int)MethodGcInfoLookup.ReadULEB128(ref p);
                        break;

                    case DW_CFA_def_cfa_register:
                        state.CfaRegister = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                        break;

                    case DW_CFA_def_cfa_offset:
                        state.CfaOffset = (int)MethodGcInfoLookup.ReadULEB128(ref p);
                        break;

                    case DW_CFA_def_cfa_offset_sf:
                        state.CfaOffset = ReadSLEB128(ref p) * dataAlignFactor;
                        break;

                    case DW_CFA_offset_extended:
                    {
                        byte reg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                        uint offset = MethodGcInfoLookup.ReadULEB128(ref p);
                        if (reg < (byte)DwarfRegX64.MAX)
                        {
                            state.SetRegLocation((DwarfRegX64)reg, RegSaveKind.AtCfaOffset,
                                                 (int)(offset * dataAlignFactor));
                        }
                    }
                    break;

                    case DW_CFA_offset_extended_sf:
                    {
                        byte reg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
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
                        byte reg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                        if (reg < (byte)DwarfRegX64.MAX)
                        {
                            state.SetRegLocation((DwarfRegX64)reg, RegSaveKind.SameValue);
                        }
                    }
                    break;

                    case DW_CFA_register:
                    {
                        byte reg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                        byte inReg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                        if (reg < (byte)DwarfRegX64.MAX)
                        {
                            state.SetRegLocation((DwarfRegX64)reg, RegSaveKind.InRegister, 0, inReg);
                        }
                    }
                    break;

                    case DW_CFA_undefined:
                    {
                        byte reg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
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
                        uint exprLen = MethodGcInfoLookup.ReadULEB128(ref p);
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
    /// Unwind one frame using DWARF CFI information.
    /// <para>
    /// FDE lookup delegates to <see cref="MethodGcInfoLookup.TryGetMethodCFI"/> — the single
    /// <c>.eh_frame</c> walker in the kernel. The arch-specific CFI rule execution
    /// (<see cref="ParseCIE"/> / <see cref="ParseCFIInstructions"/> / <see cref="ApplyUnwindRules"/>)
    /// stays here because it touches the x86-64 register file directly.
    /// </para>
    /// </summary>
    internal static bool UnwindOneFrameWithCFI(ref UnwindState state, nuint returnAddress)
    {
        if (!MethodGcInfoLookup.TryGetMethodCFI(returnAddress, out MethodGcInfoLookup.MethodCFIInfo cfi))
        {
            return false;
        }

        if (!ParseCIE(cfi.pCIE, out int codeAlignFactor, out int dataAlignFactor,
                      out byte returnAddressReg, out byte* initialInstructions, out byte* initialInstructionsEnd))
        {
            return false;
        }

        // Default CFA at function entry on x86-64 is RSP + 8, with the return address at CFA - 8.
        state.CfaRegister = (byte)DwarfRegX64.RSP;
        state.CfaOffset = 8;
        state.SetRegLocation((DwarfRegX64)returnAddressReg, RegSaveKind.AtCfaOffset, -8);

        // Replay the CIE initial instructions, then the FDE instructions up to returnAddress.
        if (initialInstructions != null && initialInstructionsEnd != null)
        {
            ParseCFIInstructions(initialInstructions, initialInstructionsEnd,
                                 cfi.MethodStart, returnAddress,
                                 codeAlignFactor, dataAlignFactor, ref state);
        }
        ParseCFIInstructions(cfi.pFDEInstrs, cfi.pFDEInstrsEnd,
                             cfi.MethodStart, returnAddress,
                             codeAlignFactor, dataAlignFactor, ref state);

        // Resolve the accumulated rules into the caller's register values.
        ApplyUnwindRules(ref state);
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

    /// <summary>
    /// Fill a <see cref="REGDISPLAY"/> from the current callee-saved register values in
    /// <paramref name="s"/>. The <c>pRxx</c> save-location pointers point at the value slots inside
    /// <paramref name="rd"/> itself (so they stay valid as long as <paramref name="rd"/> is alive).
    /// Used by the precise GC stack scan; mirrors the projection the exception pass-1 loop does.
    /// </summary>
    internal static void ProjectRegDisplay(ref UnwindState s, REGDISPLAY* rd)
    {
        rd->Rbx = s.RBX;
        rd->Rbp = s.RBP;
        rd->Rsi = s.RSI;
        rd->Rdi = s.RDI;
        rd->R12 = s.R12;
        rd->R13 = s.R13;
        rd->R14 = s.R14;
        rd->R15 = s.R15;
        rd->SP = s.RSP;

        rd->pRbx = &rd->Rbx;
        rd->pRbp = &rd->Rbp;
        rd->pRsi = &rd->Rsi;
        rd->pRdi = &rd->Rdi;
        rd->pR12 = &rd->R12;
        rd->pR13 = &rd->R13;
        rd->pR14 = &rd->R14;
        rd->pR15 = &rd->R15;
    }

    /// <summary>
    /// Seed an <see cref="UnwindState"/> from a <see cref="REGDISPLAY"/> and an instruction pointer,
    /// ready for <see cref="UnwindOneFrameWithCFI"/>. All register rules start as <c>SameValue</c>.
    /// </summary>
    internal static void SeedUnwindStateFromRegDisplay(ref UnwindState s, REGDISPLAY* rd, nuint ip)
    {
        s.RBX = rd->Rbx;
        s.RBP = rd->Rbp;
        s.RSI = rd->Rsi;
        s.RDI = rd->Rdi;
        s.R12 = rd->R12;
        s.R13 = rd->R13;
        s.R14 = rd->R14;
        s.R15 = rd->R15;
        s.RSP = rd->SP;
        s.ReturnAddress = ip;

        for (int i = 0; i < (int)DwarfRegX64.MAX; i++)
        {
            s.SetRegLocation((DwarfRegX64)i, RegSaveKind.SameValue);
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
        {
            return false;
        }

        byte* cieEnd = p + 4 + length;
        p += 4;

        uint cieId = *(uint*)p;
        p += 4;

        if (cieId != 0)
        {
            return false;
        }

        byte version = *p++;
        if (version != 1 && version != 3 && version != 4)
        {
            return false;
        }

        byte* augString = p;
        while (*p != 0)
        {
            p++;
        }

        p++;

        if (version == 4)
        {
            p++;  // address_size
            p++;  // segment_selector_size
        }

        codeAlignFactor = (int)MethodGcInfoLookup.ReadULEB128(ref p);
        dataAlignFactor = ReadSLEB128(ref p);

        if (version == 1)
        {
            returnAddressReg = *p++;
        }
        else
        {
            returnAddressReg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
        }

        if (*augString == 'z')
        {
            uint augLen = MethodGcInfoLookup.ReadULEB128(ref p);
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
                uint offset = MethodGcInfoLookup.ReadULEB128(ref p);
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
                        state.CfaRegister = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                        state.CfaOffset = (int)MethodGcInfoLookup.ReadULEB128(ref p);
                        break;

                    case DW_CFA_def_cfa_register:
                        state.CfaRegister = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                        break;

                    case DW_CFA_def_cfa_offset:
                        state.CfaOffset = (int)MethodGcInfoLookup.ReadULEB128(ref p);
                        break;

                    case DW_CFA_def_cfa_offset_sf:
                        state.CfaOffset = ReadSLEB128(ref p) * dataAlignFactor;
                        break;

                    case DW_CFA_offset_extended:
                        {
                            byte reg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                            uint offset = MethodGcInfoLookup.ReadULEB128(ref p);
                            if (reg < (byte)DwarfRegARM64.MAX)
                            {
                                state.SetRegLocation((DwarfRegARM64)reg, RegSaveKind.AtCfaOffset,
                                                     (int)(offset * dataAlignFactor));
                            }
                        }
                        break;

                    case DW_CFA_offset_extended_sf:
                        {
                            byte reg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
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
                            byte reg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                            if (reg < (byte)DwarfRegARM64.MAX)
                            {
                                state.SetRegLocation((DwarfRegARM64)reg, RegSaveKind.SameValue);
                            }
                        }
                        break;

                    case DW_CFA_register:
                        {
                            byte reg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                            byte inReg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                            if (reg < (byte)DwarfRegARM64.MAX)
                            {
                                state.SetRegLocation((DwarfRegARM64)reg, RegSaveKind.InRegister, 0, inReg);
                            }
                        }
                        break;

                    case DW_CFA_undefined:
                        {
                            byte reg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
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
                            uint exprLen = MethodGcInfoLookup.ReadULEB128(ref p);
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
    /// Unwind one frame using DWARF CFI information (ARM64).
    /// <para>
    /// FDE lookup delegates to <see cref="MethodGcInfoLookup.TryGetMethodCFI"/> — the single
    /// <c>.eh_frame</c> walker in the kernel. The arch-specific CFI rule execution
    /// (<see cref="ParseCIE"/> / <see cref="ParseCFIInstructions"/> / <see cref="ApplyUnwindRules"/>)
    /// stays here because it touches the ARM64 register file directly.
    /// </para>
    /// </summary>
    internal static bool UnwindOneFrameWithCFI(ref UnwindState state, nuint returnAddress)
    {
        if (!MethodGcInfoLookup.TryGetMethodCFI(returnAddress, out MethodGcInfoLookup.MethodCFIInfo cfi))
        {
            return false;
        }

        if (!ParseCIE(cfi.pCIE, out int codeAlignFactor, out int dataAlignFactor,
                      out byte returnAddressReg, out byte* initialInstructions, out byte* initialInstructionsEnd))
        {
            return false;
        }

        // Default CFA for ARM64 is SP + 0 at function entry.
        state.CfaRegister = (byte)DwarfRegARM64.SP;
        state.CfaOffset = 0;

        // Replay the CIE initial instructions, then the FDE instructions up to returnAddress.
        if (initialInstructions != null && initialInstructionsEnd != null)
        {
            ParseCFIInstructions(initialInstructions, initialInstructionsEnd,
                                 cfi.MethodStart, returnAddress,
                                 codeAlignFactor, dataAlignFactor, ref state);
        }
        ParseCFIInstructions(cfi.pFDEInstrs, cfi.pFDEInstrsEnd,
                             cfi.MethodStart, returnAddress,
                             codeAlignFactor, dataAlignFactor, ref state);

        // Resolve the accumulated rules into the caller's register values.
        ApplyUnwindRules(ref state);
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

    /// <summary>
    /// Fill a <see cref="REGDISPLAY"/> from the current callee-saved register values in
    /// <paramref name="s"/>. ARM64's REGDISPLAY stores values directly (no save-location pointers).
    /// Used by the precise GC stack scan; mirrors the projection the exception pass-1 loop does.
    /// </summary>
    internal static void ProjectRegDisplay(ref UnwindState s, REGDISPLAY* rd)
    {
        rd->SP = s.SP;
        rd->FP = s.FP;
        rd->X19 = s.X19;
        rd->X20 = s.X20;
        rd->X21 = s.X21;
        rd->X22 = s.X22;
        rd->X23 = s.X23;
        rd->X24 = s.X24;
        rd->X25 = s.X25;
        rd->X26 = s.X26;
        rd->X27 = s.X27;
        rd->X28 = s.X28;
        rd->LR = s.LR;
    }

    /// <summary>
    /// Seed an <see cref="UnwindState"/> from a <see cref="REGDISPLAY"/> and an instruction pointer,
    /// ready for <see cref="UnwindOneFrameWithCFI"/>. All register rules start as <c>SameValue</c>.
    /// </summary>
    internal static void SeedUnwindStateFromRegDisplay(ref UnwindState s, REGDISPLAY* rd, nuint ip)
    {
        s.SP = rd->SP;
        s.FP = rd->FP;
        s.LR = rd->LR;
        s.ReturnAddress = ip;
        s.X19 = rd->X19;
        s.X20 = rd->X20;
        s.X21 = rd->X21;
        s.X22 = rd->X22;
        s.X23 = rd->X23;
        s.X24 = rd->X24;
        s.X25 = rd->X25;
        s.X26 = rd->X26;
        s.X27 = rd->X27;
        s.X28 = rd->X28;

        for (int i = 0; i < (int)DwarfRegARM64.MAX; i++)
        {
            s.SetRegLocation((DwarfRegARM64)i, RegSaveKind.SameValue);
        }
    }
#endif

    /// <summary>
    /// Parses the LSDA EH-clause table for <paramref name="codeOffset"/> and returns the first
    /// clause that covers it: a typed catch, or a <c>when</c>-filter clause whose filter funclet
    /// (called via <see cref="RhpCallFilterFunclet"/>) returns non-zero. Fault clauses are
    /// recognised but not yet executed. Returns <c>false</c> if no clause matches.
    /// </summary>
    private static bool TryFindHandlerInLSDA(Exception ex, byte* pLSDA, nuint methodStart, uint codeOffset, out EHClause clause, REGDISPLAY* pRegDisplay)
    {
        clause = default;

        if (pLSDA == null)
        {
            return false;
        }

        byte* p = pLSDA;
        byte unwindBlockFlags = *p++;

        // Skip the funclet→main redirect (present only in a funclet's LSDA).
        if ((unwindBlockFlags & MethodGcInfoLookup.UBF_FUNC_KIND_MASK) != MethodGcInfoLookup.UBF_FUNC_KIND_ROOT)
        {
            p += sizeof(int); // main-LSDA offset
            p += sizeof(int); // method-start offset
        }

        // Skip the associated-data offset if present.
        if ((unwindBlockFlags & MethodGcInfoLookup.UBF_FUNC_HAS_ASSOCIATED_DATA) != 0)
        {
            p += sizeof(int);
        }

        if ((unwindBlockFlags & MethodGcInfoLookup.UBF_FUNC_HAS_EHINFO) == 0)
        {
            return false;
        }

        // Follow the EH-info offset to the clause table.
        int ehInfoOffset = *(int*)p;
        byte* pEHInfo = p + ehInfoOffset;

        uint nClauses = ReadUnsigned(ref pEHInfo);
        for (uint i = 0; i < nClauses; i++)
        {
            uint tryStartOffset = ReadUnsigned(ref pEHInfo);
            uint tryEndDeltaAndKind = ReadUnsigned(ref pEHInfo);

            EHClauseKind kind = (EHClauseKind)(tryEndDeltaAndKind & 0x3);
            uint tryEndOffset = tryStartOffset + (tryEndDeltaAndKind >> 2);

            uint handlerOffset = 0;
            byte* filterAddress = null;
            void* targetType = null;

            switch (kind)
            {
                case EHClauseKind.EH_CLAUSE_TYPED:
                    handlerOffset = ReadUnsigned(ref pEHInfo);
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

            if (codeOffset < tryStartOffset || codeOffset >= tryEndOffset)
            {
                continue;
            }

            // Fault handlers run during unwinding, not as a catch — not implemented yet.
            if (kind == EHClauseKind.EH_CLAUSE_FAULT)
            {
                continue;
            }

            // A `when`-filter clause matches only if its filter funclet returns non-zero, and that
            // needs a register context to run the funclet against.
            if (kind == EHClauseKind.EH_CLAUSE_FILTER)
            {
                if (pRegDisplay == null || filterAddress == null)
                {
                    continue;
                }

                nint exceptionPtr = Unsafe.As<Exception, nint>(ref ex);
                if (RhpCallFilterFunclet(exceptionPtr, filterAddress, pRegDisplay) == 0)
                {
                    continue;
                }
            }

            clause.ClauseKind = kind;
            clause.TryStartOffset = tryStartOffset;
            clause.TryEndOffset = tryEndOffset;
            clause.HandlerAddress = (byte*)(methodStart + handlerOffset);
            clause.FilterAddress = filterAddress;
            clause.TargetType = targetType;
            return true;
        }

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
    /// Hands the in-flight exception to its catch funclet via <see cref="RhpCallCatchFunclet"/>,
    /// which restores the establisher frame's registers, runs the funclet, then resumes the catching
    /// method at the address the funclet returns. Does not return.
    /// </summary>
    private static void InvokeCatchHandler(Exception ex, ref EHClause clause, void* pExInfo, REGDISPLAY* pRegDisplay)
    {
        if (pRegDisplay == null)
        {
            Serial.WriteString("[EH] No register context for the catch funclet\n");
            FailFast("Invalid exception context", ex);
            return;
        }

        nint exceptionPtr = Unsafe.As<Exception, nint>(ref ex);

        // The exception is about to be handled — clear the recursion guard before transferring out
        // (RhpCallCatchFunclet jumps to the resume address and never comes back here).
        s_isHandlingException = false;
        RhpCallCatchFunclet(exceptionPtr, clause.HandlerAddress, pRegDisplay, pExInfo);
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
