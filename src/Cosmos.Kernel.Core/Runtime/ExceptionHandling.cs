using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Runtime.GcInfo;
using Unsafe = System.Runtime.CompilerServices.Unsafe;

namespace Cosmos.Kernel.Core.Runtime;

/// <summary>
/// How the CFI rule table describes where a register's caller-side value lives.
/// </summary>
internal enum RegSaveKind : byte
{
    Undefined = 0,    // Register value is unknown after the unwind.
    SameValue = 1,    // Register keeps its value (the callee-saved default).
    AtCfaOffset = 2,  // Register was spilled to memory at CFA + Offset.
    InRegister = 3,   // Register's value is currently held in another register.
}

/// <summary>
/// One row of the CFI rule table: how a register is saved at the current PC in a function.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
internal struct RegLocation
{
    [FieldOffset(0)] public RegSaveKind Kind;
    [FieldOffset(1)] public byte Register;   // For InRegister: the source DWARF register.
    [FieldOffset(4)] public int Offset;      // For AtCfaOffset: offset (typically negative) from the CFA.
}

#if ARCH_X64
/// <summary>
/// DWARF register numbers for x86-64.
/// </summary>
internal enum DwarfRegX64 : byte
{
    RAX = 0, RDX = 1, RCX = 2, RBX = 3,
    RSI = 4, RDI = 5, RBP = 6, RSP = 7,
    R8 = 8, R9 = 9, R10 = 10, R11 = 11,
    R12 = 12, R13 = 13, R14 = 14, R15 = 15,
    RA = 16,  // Return address (pseudo-register; x86-64 has no architectural RA reg).
    MAX = 17
}

/// <summary>
/// Compile-time knobs the (otherwise arch-neutral) DWARF CFI unwinder needs to drive
/// <see cref="UnwindState"/>: how big the register table is, which slot is the SP, which slot is
/// the return-address column, and what the default rules at function entry are.
/// </summary>
internal static class CfiArch
{
    public const int RegCount = (int)DwarfRegX64.MAX;               // 17 slots (0..15 + RA pseudo)
    public const byte CfaRegAtEntry = (byte)DwarfRegX64.RSP;        // CFA = RSP + 8 at function entry
    public const int CfaOffsetAtEntry = 8;
    public const int StackPointerReg = (int)DwarfRegX64.RSP;        // Regs[7] = unwound caller SP
    public const int RaColumn = (int)DwarfRegX64.RA;                // 16 — default return-address register
    public const int DefaultCodeAlignFactor = 1;
    public const RegSaveKind RaInitRule = RegSaveKind.AtCfaOffset;  // return address at CFA - 8 by default
    public const int RaInitOffset = -8;
}
#elif ARCH_ARM64
/// <summary>
/// DWARF register numbers for AArch64 (ARM64).
/// </summary>
internal enum DwarfRegARM64 : byte
{
    X0 = 0, X1 = 1, X2 = 2, X3 = 3, X4 = 4, X5 = 5, X6 = 6, X7 = 7,
    X8 = 8, X9 = 9, X10 = 10, X11 = 11, X12 = 12, X13 = 13, X14 = 14, X15 = 15,
    X16 = 16, X17 = 17, X18 = 18, X19 = 19, X20 = 20, X21 = 21, X22 = 22, X23 = 23,
    X24 = 24, X25 = 25, X26 = 26, X27 = 27, X28 = 28,
    FP = 29,  // X29 — Frame Pointer
    LR = 30,  // X30 — Link Register (return address)
    SP = 31,  // Stack Pointer
    MAX = 32
}

/// <summary>
/// Compile-time knobs the (otherwise arch-neutral) DWARF CFI unwinder needs to drive
/// <see cref="UnwindState"/>: how big the register table is, which slot is the SP, which slot is
/// the return-address column, and what the default rules at function entry are.
/// </summary>
internal static class CfiArch
{
    public const int RegCount = (int)DwarfRegARM64.MAX;             // 32 slots
    public const byte CfaRegAtEntry = (byte)DwarfRegARM64.SP;       // CFA = SP at function entry (AAPCS64)
    public const int CfaOffsetAtEntry = 0;
    public const int StackPointerReg = (int)DwarfRegARM64.SP;       // Regs[31] = unwound caller SP
    public const int RaColumn = (int)DwarfRegARM64.LR;              // 30 — LR is the RA reg (no pseudo-reg)
    public const int DefaultCodeAlignFactor = 4;                    // every ARM64 instruction is 4 bytes
    public const RegSaveKind RaInitRule = RegSaveKind.SameValue;    // LR is a normal reg; SameValue = the seeded default
    public const int RaInitOffset = 0;
}
#endif

/// <summary>
/// Register state during a DWARF CFI stack unwind. Indexed by DWARF register number — the unwound
/// register values live in <c>Regs</c>, the per-register save-location rules in <c>RegLocations</c>.
/// The DWARF interpreter (<see cref="ExceptionHelper.ParseCFIInstructions"/> etc.) is single-source
/// across architectures; only the slot count, the CFA seed at function entry, and the return-address
/// column differ — those come from <see cref="CfiArch"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct UnwindState
{
    public byte CfaRegister;   // DWARF register number the CFA is expressed relative to.
    public int CfaOffset;      // CFA = Regs[CfaRegister] + CfaOffset.

    // Per-register save-location rules. `fixed` sizes need a compile-time constant; CfiArch.RegCount is one.
    public fixed byte RegLocations[CfiArch.RegCount * 8];   // RegLocation[CfiArch.RegCount]

    // Unwound register values, indexed by DWARF register number. Stored as ulong because C# `fixed`
    // buffers only accept primitive types — nuint isn't on the list (CS1663). On the kernel's two
    // targets (x64 and ARM64) nuint and ulong are byte-identical, so the casts at the accessor
    // boundaries below are runtime no-ops. Scratch slots the unwind never touches stay zero — fine,
    // the CFI rules wouldn't reference them anyway.
    public fixed ulong Regs[CfiArch.RegCount];

    /// <summary>
    /// IP to unwind from; after a successful unwind, the caller's return address.
    /// Also lands in <c>Regs[CfiArch.RaColumn]</c> via <see cref="ExceptionHelper.ApplyUnwindRules"/>;
    /// kept here as an explicit field so callers don't need to know the RA column.
    /// </summary>
    public nuint ReturnAddress;

    public RegLocation* GetRegLocation(int reg)
    {
        if ((uint)reg >= CfiArch.RegCount)
        {
            reg = 0;   // paranoia: a malformed FDE reg# must not index past the buffer
        }
        fixed (byte* p = RegLocations)
        {
            return (RegLocation*)(p + reg * sizeof(RegLocation));
        }
    }

    public void SetRegLocation(int reg, RegSaveKind kind, int offset = 0, byte inReg = 0)
    {
        if ((uint)reg >= CfiArch.RegCount)
        {
            return;   // ignore out-of-range reg# from a malformed FDE
        }
        RegLocation* loc = GetRegLocation(reg);
        loc->Kind = kind;
        loc->Offset = offset;
        loc->Register = inReg;
    }

    public nuint GetRegValue(int reg)
    {
        return (uint)reg < CfiArch.RegCount ? (nuint)Regs[reg] : 0;
    }

    public void SetRegValue(int reg, nuint value)
    {
        if ((uint)reg < CfiArch.RegCount)
        {
            Regs[reg] = (ulong)value;
        }
    }

    /// <summary>
    /// Arch-neutral name for the caller-side stack pointer the unwind resolves to
    /// (DWARF reg 7 on x64, reg 31 on ARM64).
    /// </summary>
    public nuint StackPointer
    {
        get => (nuint)Regs[CfiArch.StackPointerReg];
        set => Regs[CfiArch.StackPointerReg] = (ulong)value;
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
            // Filter funclets run on the current frame's establisher, so we pin the frame pointer
            // and SP to `frame.FramePointer` regardless of what the CFI unwind resolved them to.
            if (pThrowContext != null)
            {
                pRegDisplay = &regDisplay;
                ProjectRegDisplay(ref unwindState, pRegDisplay);
#if ARCH_X64
                regDisplay.Rbp = frame.FramePointer;
#elif ARCH_ARM64
                regDisplay.FP = frame.FramePointer;
#endif
                regDisplay.SP = frame.FramePointer;
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

        // Build the REGDISPLAY the catch funclet runs with — and that RhpCallCatchFunclet resumes
        // the catching method from once the funclet returns. The CFI walk that landed on the
        // catching frame already reconstructed its body register state at the call that threw, so
        // `unwindState` carries the callee-saved registers and the resume SP directly — that's what
        // `ProjectRegDisplay` copies in (and, on x64, what it wires the pRxx pointers to point at).
        // The frame pointer is the one exception: it comes from the frame-pointer chain (`[RBP]` on
        // x64, `[FP]` on ARM64), because CFI can legitimately report it "SameValue" for a
        // -fomit-frame-pointer caller — so we override it here after the projection.
        if (pThrowContext != null)
        {
            pRegDisplay = &regDisplay;
            ProjectRegDisplay(ref unwindState, pRegDisplay);
#if ARCH_X64
            // SP stays at unwindState.StackPointer = the CFA (the catching frame's top, NOT its RBP).
            // RhpCallCatchFunclet sets RSP to this before jumping to the funclet's resume IP, so the
            // catching method finds its frame intact and its epilogue restores the caller's regs.
            regDisplay.Rbp = catchFrame.FramePointer;
#elif ARCH_ARM64
            // NOTE: SP is set to the frame pointer here, not the CFA — RhpCallCatchFunclet's resume
            // tail assumes SP == FP in the handler frame. x64 uses the CFA instead (PR #351);
            // aligning ARM64 is a known follow-up (catch handlers in methods with a non-trivial frame).
            nuint handlerFp = catchFrame.FramePointer;
            regDisplay.FP = handlerFp;
            regDisplay.SP = handlerFp;
#endif
        }

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

    // DWARF CFI opcodes (DWARF 5 §6.4.2 — architecturally agnostic).
    private const byte DW_CFA_advance_loc = 0x40;       // 0x40 + delta (high 2 bits = 01)
    private const byte DW_CFA_offset = 0x80;            // 0x80 + reg   (high 2 bits = 10), then ULEB128 offset
    private const byte DW_CFA_restore = 0xC0;           // 0xC0 + reg   (high 2 bits = 11)
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
    /// Decode a CIE (Common Information Entry): returns the code/data alignment factors, the
    /// return-address register, and the byte range of the CIE's "initial instructions" (which the
    /// caller will replay to seed the unwind state before applying the FDE's instructions).
    /// </summary>
    private static bool ParseCIE(byte* cie, out int codeAlignFactor, out int dataAlignFactor,
                                 out byte returnAddressReg, out byte* initialInstructions, out byte* instructionsEnd)
    {
        codeAlignFactor = CfiArch.DefaultCodeAlignFactor;
        dataAlignFactor = -8;
        returnAddressReg = (byte)CfiArch.RaColumn;
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
        if (cieId != 0)
        {
            return false;   // Not a CIE.
        }
        p += 4;

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
        p++;   // skip the augmentation string's null terminator

        // DWARF 4 added address_size + segment_selector_size between the augmentation string and the
        // alignment factors. ILC emits v3 on x64 and v4 on ARM64; accepting v4 on both is harmless.
        if (version == 4)
        {
            p++;   // address_size
            p++;   // segment_selector_size
        }

        codeAlignFactor = (int)MethodGcInfoLookup.ReadULEB128(ref p);
        dataAlignFactor = ReadSLEB128(ref p);

        // Version-1 CIEs encode the return-address register as a single byte; v3/v4 use ULEB128.
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
    /// Replay a stream of DWARF CFI instructions, updating <paramref name="state"/>'s CFA definition
    /// and per-register save-location rules. Stops once the synthetic PC has caught up with
    /// <paramref name="targetPC"/>; the resulting rules describe the register state at that PC.
    /// Out-of-range register numbers from a malformed FDE are silently dropped by
    /// <see cref="UnwindState.SetRegLocation"/>.
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

            // The DW_CFA_advance_loc / DW_CFA_offset / DW_CFA_restore opcodes pack a 6-bit operand
            // into the high 2 bits of the byte; everything else uses the byte verbatim.
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
                state.SetRegLocation(reg, RegSaveKind.AtCfaOffset, (int)(offset * dataAlignFactor));
            }
            else if (highBits == DW_CFA_restore)
            {
                byte reg = lowBits;
                state.SetRegLocation(reg, RegSaveKind.SameValue);
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
                        state.SetRegLocation(reg, RegSaveKind.AtCfaOffset, (int)(offset * dataAlignFactor));
                    }
                    break;

                    case DW_CFA_offset_extended_sf:
                    {
                        byte reg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                        int offset = ReadSLEB128(ref p);
                        state.SetRegLocation(reg, RegSaveKind.AtCfaOffset, offset * dataAlignFactor);
                    }
                    break;

                    case DW_CFA_same_value:
                    {
                        byte reg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                        state.SetRegLocation(reg, RegSaveKind.SameValue);
                    }
                    break;

                    case DW_CFA_register:
                    {
                        byte reg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                        byte inReg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                        state.SetRegLocation(reg, RegSaveKind.InRegister, 0, inReg);
                    }
                    break;

                    case DW_CFA_undefined:
                    {
                        byte reg = (byte)MethodGcInfoLookup.ReadULEB128(ref p);
                        state.SetRegLocation(reg, RegSaveKind.Undefined);
                    }
                    break;

                    case DW_CFA_remember_state:
                    case DW_CFA_restore_state:
                        // Not used by ILC's emitted CFI; a rule-state stack would push/pop here.
                        break;

                    case DW_CFA_def_cfa_expression:
                    case DW_CFA_expression:
                    case DW_CFA_val_expression:
                    {
                        // DWARF expressions aren't evaluated — skip the length-prefixed body.
                        uint exprLen = MethodGcInfoLookup.ReadULEB128(ref p);
                        p += exprLen;
                    }
                    break;

                    default:
                        // Unknown opcode — best effort: skip; the next opcode boundary may still be valid.
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Resolve the accumulated CFI rules into actual caller-side register values. Computes the CFA,
    /// then for each register applies its <see cref="RegLocation"/> rule. Finally pins the caller
    /// SP slot to the CFA and exposes the caller's return address through
    /// <see cref="UnwindState.ReturnAddress"/>.
    /// </summary>
    private static void ApplyUnwindRules(ref UnwindState state)
    {
        // CfaOffset is signed (DW_CFA_def_cfa_offset_sf and SLEB128 forms can be negative).
        long cfaBase = (long)state.GetRegValue(state.CfaRegister);
        nuint cfa = (nuint)(cfaBase + state.CfaOffset);

        for (int i = 0; i < CfiArch.RegCount; i++)
        {
            RegLocation* loc = state.GetRegLocation(i);

            switch (loc->Kind)
            {
                case RegSaveKind.AtCfaOffset:
                    nuint* savedLoc = (nuint*)((long)cfa + loc->Offset);
                    state.SetRegValue(i, *savedLoc);
                    break;

                case RegSaveKind.InRegister:
                    state.SetRegValue(i, state.GetRegValue(loc->Register));
                    break;

                case RegSaveKind.SameValue:
                case RegSaveKind.Undefined:
                default:
                    // Leave Regs[i] alone — same value, or its value at this PC is undefined.
                    break;
            }
        }

        // Standard CFI convention: the caller's SP equals the CFA we just resolved.
        state.SetRegValue(CfiArch.StackPointerReg, cfa);
        // After the loop, the return-address column holds the caller's IP — surface it explicitly.
        state.ReturnAddress = state.GetRegValue(CfiArch.RaColumn);
    }

    /// <summary>
    /// Unwind one frame using DWARF CFI: looks up the method's FDE/CIE via the
    /// <c>.eh_frame</c> walker, seeds the CFA + RA rule for function entry, replays the CIE initial
    /// instructions then the FDE's body up to <paramref name="returnAddress"/>, and resolves the
    /// resulting rules into caller-side register values.
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

        // Default CFA + RA rule at function entry. On x64 the RA pseudo-reg lives at CFA-8; on
        // ARM64 LR is a normal reg and SameValue is the seeded default, so the SetRegLocation call
        // is a no-op there, letting the FDE's prologue rules describe where LR was spilled.
        state.CfaRegister = CfiArch.CfaRegAtEntry;
        state.CfaOffset = CfiArch.CfaOffsetAtEntry;
        state.SetRegLocation(returnAddressReg, CfiArch.RaInitRule, CfiArch.RaInitOffset);

        if (initialInstructions != null && initialInstructionsEnd != null)
        {
            ParseCFIInstructions(initialInstructions, initialInstructionsEnd,
                                 cfi.MethodStart, returnAddress,
                                 codeAlignFactor, dataAlignFactor, ref state);
        }
        ParseCFIInstructions(cfi.pFDEInstrs, cfi.pFDEInstrsEnd,
                             cfi.MethodStart, returnAddress,
                             codeAlignFactor, dataAlignFactor, ref state);

        ApplyUnwindRules(ref state);
        return true;
    }

    /// <summary>
    /// Reset every register's rule to <c>SameValue</c> (the callee-saved default). Shared by every
    /// site that builds a fresh <see cref="UnwindState"/> before any FDE rules are applied.
    /// </summary>
    private static void InitRegRulesSameValue(ref UnwindState state)
    {
        for (int i = 0; i < CfiArch.RegCount; i++)
        {
            state.SetRegLocation(i, RegSaveKind.SameValue);
        }
    }

#if ARCH_X64
    /// <summary>
    /// Seed an <see cref="UnwindState"/> from the throw-site <see cref="PAL_LIMITED_CONTEXT"/>
    /// captured by <c>RhpThrowEx</c>: copy the callee-saved registers (plus RSP and IP) into the
    /// register table, then set every per-register rule to <c>SameValue</c>.
    /// </summary>
    private static void InitUnwindStateFromContext(ref UnwindState state, PAL_LIMITED_CONTEXT* pContext)
    {
        state.SetRegValue((int)DwarfRegX64.RBX, pContext->Rbx);
        state.SetRegValue((int)DwarfRegX64.RBP, pContext->Rbp);
        state.SetRegValue((int)DwarfRegX64.RSP, pContext->Rsp);
        state.SetRegValue((int)DwarfRegX64.R12, pContext->R12);
        state.SetRegValue((int)DwarfRegX64.R13, pContext->R13);
        state.SetRegValue((int)DwarfRegX64.R14, pContext->R14);
        state.SetRegValue((int)DwarfRegX64.R15, pContext->R15);
        state.ReturnAddress = pContext->IP;
        InitRegRulesSameValue(ref state);
    }

    /// <summary>
    /// Fill a <see cref="REGDISPLAY"/> from the current register values in <paramref name="s"/>.
    /// The <c>pRxx</c> save-location pointers point at the value slots inside <paramref name="rd"/>
    /// itself (so they stay valid as long as <paramref name="rd"/> is alive) — they must be wired
    /// after the value slots are populated. Used by the precise GC stack scan and by the exception
    /// dispatcher when it hands a REGDISPLAY to a filter/catch funclet.
    /// </summary>
    internal static void ProjectRegDisplay(ref UnwindState s, REGDISPLAY* rd)
    {
        rd->Rbx = s.GetRegValue((int)DwarfRegX64.RBX);
        rd->Rbp = s.GetRegValue((int)DwarfRegX64.RBP);
        rd->Rsi = s.GetRegValue((int)DwarfRegX64.RSI);
        rd->Rdi = s.GetRegValue((int)DwarfRegX64.RDI);
        rd->R12 = s.GetRegValue((int)DwarfRegX64.R12);
        rd->R13 = s.GetRegValue((int)DwarfRegX64.R13);
        rd->R14 = s.GetRegValue((int)DwarfRegX64.R14);
        rd->R15 = s.GetRegValue((int)DwarfRegX64.R15);
        rd->SP = s.StackPointer;

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
        s.SetRegValue((int)DwarfRegX64.RBX, rd->Rbx);
        s.SetRegValue((int)DwarfRegX64.RBP, rd->Rbp);
        s.SetRegValue((int)DwarfRegX64.RSI, rd->Rsi);
        s.SetRegValue((int)DwarfRegX64.RDI, rd->Rdi);
        s.SetRegValue((int)DwarfRegX64.R12, rd->R12);
        s.SetRegValue((int)DwarfRegX64.R13, rd->R13);
        s.SetRegValue((int)DwarfRegX64.R14, rd->R14);
        s.SetRegValue((int)DwarfRegX64.R15, rd->R15);
        s.StackPointer = rd->SP;
        s.ReturnAddress = ip;
        InitRegRulesSameValue(ref s);
    }
#elif ARCH_ARM64
    /// <summary>
    /// Seed an <see cref="UnwindState"/> from the throw-site <see cref="PAL_LIMITED_CONTEXT"/>
    /// captured by <c>RhpThrowEx</c>: copy the callee-saved registers (plus SP and IP) into the
    /// register table, then set every per-register rule to <c>SameValue</c>.
    /// </summary>
    private static void InitUnwindStateFromContext(ref UnwindState state, PAL_LIMITED_CONTEXT* pContext)
    {
        state.SetRegValue((int)DwarfRegARM64.SP, pContext->SP);
        state.SetRegValue((int)DwarfRegARM64.FP, pContext->FP);
        state.SetRegValue((int)DwarfRegARM64.LR, pContext->LR);
        state.SetRegValue((int)DwarfRegARM64.X19, pContext->X19);
        state.SetRegValue((int)DwarfRegARM64.X20, pContext->X20);
        state.SetRegValue((int)DwarfRegARM64.X21, pContext->X21);
        state.SetRegValue((int)DwarfRegARM64.X22, pContext->X22);
        state.SetRegValue((int)DwarfRegARM64.X23, pContext->X23);
        state.SetRegValue((int)DwarfRegARM64.X24, pContext->X24);
        state.SetRegValue((int)DwarfRegARM64.X25, pContext->X25);
        state.SetRegValue((int)DwarfRegARM64.X26, pContext->X26);
        state.SetRegValue((int)DwarfRegARM64.X27, pContext->X27);
        state.SetRegValue((int)DwarfRegARM64.X28, pContext->X28);
        state.ReturnAddress = pContext->IP;
        InitRegRulesSameValue(ref state);
    }

    /// <summary>
    /// Fill a <see cref="REGDISPLAY"/> from the current register values in <paramref name="s"/>.
    /// ARM64's REGDISPLAY stores values directly — no save-location pointer wiring. Used by the
    /// precise GC stack scan and by the exception dispatcher when it hands a REGDISPLAY to a filter
    /// or catch funclet.
    /// </summary>
    internal static void ProjectRegDisplay(ref UnwindState s, REGDISPLAY* rd)
    {
        rd->SP = s.StackPointer;
        rd->FP = s.GetRegValue((int)DwarfRegARM64.FP);
        rd->X19 = s.GetRegValue((int)DwarfRegARM64.X19);
        rd->X20 = s.GetRegValue((int)DwarfRegARM64.X20);
        rd->X21 = s.GetRegValue((int)DwarfRegARM64.X21);
        rd->X22 = s.GetRegValue((int)DwarfRegARM64.X22);
        rd->X23 = s.GetRegValue((int)DwarfRegARM64.X23);
        rd->X24 = s.GetRegValue((int)DwarfRegARM64.X24);
        rd->X25 = s.GetRegValue((int)DwarfRegARM64.X25);
        rd->X26 = s.GetRegValue((int)DwarfRegARM64.X26);
        rd->X27 = s.GetRegValue((int)DwarfRegARM64.X27);
        rd->X28 = s.GetRegValue((int)DwarfRegARM64.X28);
        rd->LR = s.GetRegValue((int)DwarfRegARM64.LR);
    }

    /// <summary>
    /// Seed an <see cref="UnwindState"/> from a <see cref="REGDISPLAY"/> and an instruction pointer,
    /// ready for <see cref="UnwindOneFrameWithCFI"/>. All register rules start as <c>SameValue</c>.
    /// </summary>
    internal static void SeedUnwindStateFromRegDisplay(ref UnwindState s, REGDISPLAY* rd, nuint ip)
    {
        s.StackPointer = rd->SP;
        s.SetRegValue((int)DwarfRegARM64.FP, rd->FP);
        s.SetRegValue((int)DwarfRegARM64.LR, rd->LR);
        s.SetRegValue((int)DwarfRegARM64.X19, rd->X19);
        s.SetRegValue((int)DwarfRegARM64.X20, rd->X20);
        s.SetRegValue((int)DwarfRegARM64.X21, rd->X21);
        s.SetRegValue((int)DwarfRegARM64.X22, rd->X22);
        s.SetRegValue((int)DwarfRegARM64.X23, rd->X23);
        s.SetRegValue((int)DwarfRegARM64.X24, rd->X24);
        s.SetRegValue((int)DwarfRegARM64.X25, rd->X25);
        s.SetRegValue((int)DwarfRegARM64.X26, rd->X26);
        s.SetRegValue((int)DwarfRegARM64.X27, rd->X27);
        s.SetRegValue((int)DwarfRegARM64.X28, rd->X28);
        s.ReturnAddress = ip;
        InitRegRulesSameValue(ref s);
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
