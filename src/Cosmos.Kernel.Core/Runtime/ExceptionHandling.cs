using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Unsafe = System.Runtime.CompilerServices.Unsafe;

namespace Cosmos.Kernel.Core.Runtime;

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
/// REGDISPLAY structure for ARM64 funclet calls - matches assembly offsets exactly
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 0x60)]
public unsafe struct REGDISPLAY
{
    // Stack pointer for resume
    [FieldOffset(0x00)] public nuint SP;

    // Pointers to callee-saved registers
    [FieldOffset(0x08)] public nuint* pFP;
    [FieldOffset(0x10)] public nuint* pX19;
    [FieldOffset(0x18)] public nuint* pX20;
    [FieldOffset(0x20)] public nuint* pX21;
    [FieldOffset(0x28)] public nuint* pX22;
    [FieldOffset(0x30)] public nuint* pX23;
    [FieldOffset(0x38)] public nuint* pX24;
    [FieldOffset(0x40)] public nuint* pX25;
    [FieldOffset(0x48)] public nuint* pX26;
    [FieldOffset(0x50)] public nuint* pX27;
    [FieldOffset(0x58)] public nuint* pX28;
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

        // Print exception info - AVOID complex operations that could cause faults
        Serial.WriteString("\n=== DOTNET EXCEPTION THROWN ===\n");

        // Only print basic info - avoid GetType() and Message as they may allocate or cause faults
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

        if (pExInfo != null)
        {
            PAL_LIMITED_CONTEXT* pContext = *(PAL_LIMITED_CONTEXT**)((byte*)pExInfo + 0x08);
            if (pContext != null)
            {
                pRegDisplay = &regDisplay;

#if ARCH_X64
                // Copy register values from PAL_LIMITED_CONTEXT to REGDISPLAY storage
                regDisplay.Rbx = pContext->Rbx;
                regDisplay.Rbp = pContext->Rbp;
                regDisplay.R12 = pContext->R12;
                regDisplay.R13 = pContext->R13;
                regDisplay.R14 = pContext->R14;
                regDisplay.R15 = pContext->R15;
                regDisplay.SP = pContext->Rsp;

                // Set up pointers to storage locations
                regDisplay.pRbx = &pRegDisplay->Rbx;
                regDisplay.pRbp = &pRegDisplay->Rbp;
                regDisplay.pRsi = &pRegDisplay->Rsi;
                regDisplay.pRdi = &pRegDisplay->Rdi;
                regDisplay.pR12 = &pRegDisplay->R12;
                regDisplay.pR13 = &pRegDisplay->R13;
                regDisplay.pR14 = &pRegDisplay->R14;
                regDisplay.pR15 = &pRegDisplay->R15;
#elif ARCH_ARM64
                regDisplay.SP = pContext->SP;
                regDisplay.pFP = &pContext->FP;
                regDisplay.pX19 = &pContext->X19;
                regDisplay.pX20 = &pContext->X20;
                regDisplay.pX21 = &pContext->X21;
                regDisplay.pX22 = &pContext->X22;
                regDisplay.pX23 = &pContext->X23;
                regDisplay.pX24 = &pContext->X24;
                regDisplay.pX25 = &pContext->X25;
                regDisplay.pX26 = &pContext->X26;
                regDisplay.pX27 = &pContext->X27;
                regDisplay.pX28 = &pContext->X28;
#endif
            }
        }

        // Pass 1: Walk stack to find a handler
        StackFrame catchFrame = default;
        EHClause catchClause = default;
        bool foundHandler = false;

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

            // Check if this frame has an exception handler for our exception
            // Pass REGDISPLAY for filter evaluation
            if (TryFindHandler(ex, frame.ReturnAddress, out EHClause clause, pRegDisplay))
            {
                Serial.WriteString("[EH] FOUND HANDLER at 0x");
                Serial.WriteNumber((nuint)clause.HandlerAddress);
                Serial.WriteString("\n");

                catchFrame = frame;
                catchClause = clause;
                foundHandler = true;
                break;
            }

            // Move to caller's frame
            if (!UnwindOneFrame(ref frame))
            {
                Serial.WriteString("[EH] Cannot unwind further\n");
                break;
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

        // Pass 2: Execute finally handlers between throw and catch, then invoke catch
        Serial.WriteString("[EH] Pass 2: Executing handlers\n");

        // For now, just invoke the catch handler directly
        // TODO: Execute finally handlers first
        InvokeCatchHandler(ex, ref catchFrame, ref catchClause, pExInfo);

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
    private static void InvokeCatchHandler(Exception ex, ref StackFrame frame, ref EHClause clause, void* pExInfo)
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

        // Build REGDISPLAY from the saved context in ExInfo
        // We allocate it on the stack and set up pointers to the register storage within it
        REGDISPLAY regDisplay = default;

        if (pExInfo != null)
        {
            // Get the PAL_LIMITED_CONTEXT from ExInfo
            PAL_LIMITED_CONTEXT* pContext = *(PAL_LIMITED_CONTEXT**)((byte*)pExInfo + 0x08);

            if (pContext != null)
            {
                Serial.WriteString("[EH] Building REGDISPLAY from context at 0x");
                Serial.WriteHex((nuint)pContext);
                Serial.WriteString("\n");

                // Get pointer to regDisplay for setting up internal pointers
                REGDISPLAY* pRegDisplay = &regDisplay;

#if ARCH_X64
                // x64: Copy register values from PAL_LIMITED_CONTEXT to REGDISPLAY storage
                regDisplay.Rbx = pContext->Rbx;
                regDisplay.Rbp = pContext->Rbp;
                regDisplay.R12 = pContext->R12;
                regDisplay.R13 = pContext->R13;
                regDisplay.R14 = pContext->R14;
                regDisplay.R15 = pContext->R15;
                regDisplay.SP = pContext->Rsp;

                // Set up pointers to storage locations
                regDisplay.pRbx = &pRegDisplay->Rbx;
                regDisplay.pRbp = &pRegDisplay->Rbp;
                regDisplay.pRsi = &pRegDisplay->Rsi;
                regDisplay.pRdi = &pRegDisplay->Rdi;
                regDisplay.pR12 = &pRegDisplay->R12;
                regDisplay.pR13 = &pRegDisplay->R13;
                regDisplay.pR14 = &pRegDisplay->R14;
                regDisplay.pR15 = &pRegDisplay->R15;

                Serial.WriteString("[EH] REGDISPLAY: SP=0x");
                Serial.WriteHex(regDisplay.SP);
                Serial.WriteString(" RBP=0x");
                Serial.WriteHex(regDisplay.Rbp);
                Serial.WriteString("\n");
#elif ARCH_ARM64
                // ARM64: Set resume SP from context
                regDisplay.SP = pContext->SP;

                // ARM64 uses x19-x28 as callee-saved registers
                // The context has X19-X28, we store pointers to them
                // Note: For ARM64, we don't have storage in REGDISPLAY,
                // we point directly to the context values
                regDisplay.pFP = &pContext->FP;
                regDisplay.pX19 = &pContext->X19;
                regDisplay.pX20 = &pContext->X20;
                regDisplay.pX21 = &pContext->X21;
                regDisplay.pX22 = &pContext->X22;
                regDisplay.pX23 = &pContext->X23;
                regDisplay.pX24 = &pContext->X24;
                regDisplay.pX25 = &pContext->X25;
                regDisplay.pX26 = &pContext->X26;
                regDisplay.pX27 = &pContext->X27;
                regDisplay.pX28 = &pContext->X28;

                Serial.WriteString("[EH] REGDISPLAY: SP=0x");
                Serial.WriteHex(regDisplay.SP);
                Serial.WriteString(" FP=0x");
                Serial.WriteHex(pContext->FP);
                Serial.WriteString("\n");
#endif

                // Clear the exception handling flag BEFORE calling funclet
                s_isHandlingException = false;

                // Call the assembly helper that properly restores registers and jumps to resume
                // This function never returns - it resets RSP and jumps to the resume address
                RhpCallCatchFunclet(exceptionPtr, clause.HandlerAddress, pRegDisplay, pExInfo);
            }
        }

        // Should not reach here - RhpCallCatchFunclet never returns
        Serial.WriteString("[EH] ERROR: No valid context for funclet call\n");
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
