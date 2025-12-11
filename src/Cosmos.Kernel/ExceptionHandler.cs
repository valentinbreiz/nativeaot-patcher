using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Graphics;
using Cosmos.Kernel.HAL.Cpu;
using Cosmos.Kernel.HAL.Cpu.Data;

namespace Cosmos.Kernel;

/// <summary>
/// Handles CPU exceptions.
/// </summary>
public static class ExceptionHandler
{
    /// <summary>
    /// Initializes CPU exception handlers.
    /// Called automatically via ModuleInitializer after runtime is ready.
    /// </summary>
    [ModuleInitializer]
    public static void Initialize()
    {
#if ARCH_ARM64
        // ARM64 exception types: 0=Sync, 1=IRQ, 2=FIQ, 3=SError
        InterruptManager.SetHandler(0x00, SynchronousException);
        InterruptManager.SetHandler(0x01, IrqException);
        InterruptManager.SetHandler(0x02, FiqException);
        InterruptManager.SetHandler(0x03, SErrorException);

        Serial.WriteString("[ExceptionHandler] ARM64 exception handlers registered\n");
#elif ARCH_X64
        // Register handlers for common CPU exceptions
        // Vector 0x00: Divide by Zero
        InterruptManager.SetHandler(0x00, DivideByZero);

        // Vector 0x01: Debug - SKIP (can trigger debug exceptions during registration)
        // InterruptManager.SetHandler(0x01, Debug);

        // Vector 0x02: Non-Maskable Interrupt (NMI)
        InterruptManager.SetHandler(0x02, NonMaskableInterrupt);

        // Vector 0x03: Breakpoint
        InterruptManager.SetHandler(0x03, Breakpoint);

        // Vector 0x04: Overflow
        InterruptManager.SetHandler(0x04, Overflow);

        // Vector 0x05: Bounds Check
        InterruptManager.SetHandler(0x05, BoundsCheck);

        // Vector 0x06: Invalid Opcode
        InterruptManager.SetHandler(0x06, InvalidOpcode);

        // Vector 0x07: Device Not Available (FPU)
        InterruptManager.SetHandler(0x07, DeviceNotAvailable);

        // Vector 0x08: Double Fault
        InterruptManager.SetHandler(0x08, DoubleFault);

        // Vector 0x0A: Invalid TSS
        InterruptManager.SetHandler(0x0A, InvalidTss);

        // Vector 0x0B: Segment Not Present
        InterruptManager.SetHandler(0x0B, SegmentNotPresent);

        // Vector 0x0C: Stack Segment Fault
        InterruptManager.SetHandler(0x0C, StackSegmentFault);

        // Vector 0x0D: General Protection Fault (GPF)
        InterruptManager.SetHandler(0x0D, GeneralProtectionFault);

        // Vector 0x0E: Page Fault
        InterruptManager.SetHandler(0x0E, PageFault);

        // Vector 0x10: Floating Point Exception
        InterruptManager.SetHandler(0x10, FloatingPointException);

        // Vector 0x11: Alignment Check
        InterruptManager.SetHandler(0x11, AlignmentCheck);

        // Vector 0x12: Machine Check
        InterruptManager.SetHandler(0x12, MachineCheck);

        // Vector 0x13: SIMD Floating Point
        InterruptManager.SetHandler(0x13, SimdFloatingPoint);

        Serial.WriteString("[ExceptionHandler] CPU exception handlers registered\n");
#else
        Serial.WriteString("[ExceptionHandler] No exception handlers for this architecture\n");
#endif
    }

#if ARCH_ARM64
    private static void SynchronousException(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("ARM64 EXCEPTION: Synchronous");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }

    private static void IrqException(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("ARM64 EXCEPTION: IRQ");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
    }

    private static void FiqException(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("ARM64 EXCEPTION: FIQ");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
    }

    private static void SErrorException(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("ARM64 EXCEPTION: SError");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }
#endif

#if ARCH_X64
    private static void DivideByZero(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: Divide by Zero (#DE)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }

    private static void Debug(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: Debug (#DB)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
    }

    private static void NonMaskableInterrupt(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: Non-Maskable Interrupt (NMI)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }

    private static void Breakpoint(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: Breakpoint (#BP)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
    }

    private static void Overflow(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: Overflow (#OF)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }

    private static void BoundsCheck(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: Bounds Check (#BR)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }

    private static void InvalidOpcode(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: Invalid Opcode (#UD)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }

    private static void DeviceNotAvailable(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: Device Not Available (#NM)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }

    private static void DoubleFault(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: Double Fault (#DF)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }

    private static void InvalidTss(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: Invalid TSS (#TS)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }

    private static void SegmentNotPresent(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: Segment Not Present (#NP)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }

    private static void StackSegmentFault(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: Stack Segment Fault (#SS)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }

    private static void GeneralProtectionFault(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: General Protection Fault (#GP)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }

    private static void PageFault(ref IRQContext ctx)
    {
        // Use Serial.WriteString directly - avoid any allocations or complex calls
        // that could trigger another fault
        Serial.WriteString("\n========================================\n");
        Serial.WriteString("EXCEPTION: Page Fault (#PF)\n");
        Serial.WriteString("========================================\n");
        Serial.WriteString("Interrupt: ");
        Serial.WriteNumber(ctx.interrupt);
        Serial.WriteString("\nRBP: 0x");
        Serial.WriteHex(ctx.rbp);
        Serial.WriteString("\nCPU Flags: 0x");
        Serial.WriteHex(ctx.cpu_flags);
        Serial.WriteString("\n========================================\n");
        Serial.WriteString("System halted.\n");
        while (true) { }
    }

    private static void FloatingPointException(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: Floating Point Exception (#MF)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }

    private static void AlignmentCheck(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: Alignment Check (#AC)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }

    private static void MachineCheck(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: Machine Check (#MC)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }

    private static void SimdFloatingPoint(ref IRQContext ctx)
    {
        WriteDebugLine("");
        WriteDebugLine("========================================");
        WriteDebugLine("EXCEPTION: SIMD Floating Point Exception (#XM)");
        WriteDebugLine("========================================");
        PrintExceptionInfo(ref ctx);
        Halt();
    }
#endif

    private static void PrintExceptionInfo(ref IRQContext ctx)
    {
        // Output to serial
        WriteDebugLine("Interrupt Vector: " + ctx.interrupt.ToString());
        WriteDebugLine("CPU Flags (ESR): 0x" + ctx.cpu_flags.ToString("X"));
        WriteDebugLine("");
        WriteDebugLine("Registers:");
#if ARCH_ARM64
        WriteDebugLine("  X0:  0x" + ctx.x0.ToString("X16") + "  X1:  0x" + ctx.x1.ToString("X16"));
        WriteDebugLine("  X2:  0x" + ctx.x2.ToString("X16") + "  X3:  0x" + ctx.x3.ToString("X16"));
        WriteDebugLine("  X4:  0x" + ctx.x4.ToString("X16") + "  X5:  0x" + ctx.x5.ToString("X16"));
        WriteDebugLine("  X6:  0x" + ctx.x6.ToString("X16") + "  X7:  0x" + ctx.x7.ToString("X16"));
        WriteDebugLine("  X8:  0x" + ctx.x8.ToString("X16") + "  X9:  0x" + ctx.x9.ToString("X16"));
        WriteDebugLine("  X10: 0x" + ctx.x10.ToString("X16") + "  X11: 0x" + ctx.x11.ToString("X16"));
        WriteDebugLine("  X29: 0x" + ctx.x29.ToString("X16") + "  X30: 0x" + ctx.x30.ToString("X16"));
        WriteDebugLine("  SP:  0x" + ctx.sp.ToString("X16") + "  ELR: 0x" + ctx.elr.ToString("X16"));
        WriteDebugLine("  SPSR: 0x" + ctx.spsr.ToString("X16"));
#else
        WriteDebugLine("  RAX: 0x" + ctx.rax.ToString("X16") + "  RBX: 0x" + ctx.rbx.ToString("X16"));
        WriteDebugLine("  RCX: 0x" + ctx.rcx.ToString("X16") + "  RDX: 0x" + ctx.rdx.ToString("X16"));
        WriteDebugLine("  RSI: 0x" + ctx.rsi.ToString("X16") + "  RDI: 0x" + ctx.rdi.ToString("X16"));
        WriteDebugLine("  RBP: 0x" + ctx.rbp.ToString("X16") + "  R8:  0x" + ctx.r8.ToString("X16"));
        WriteDebugLine("  R9:  0x" + ctx.r9.ToString("X16") + "  R10: 0x" + ctx.r10.ToString("X16"));
        WriteDebugLine("  R11: 0x" + ctx.r11.ToString("X16") + "  R12: 0x" + ctx.r12.ToString("X16"));
        WriteDebugLine("  R13: 0x" + ctx.r13.ToString("X16") + "  R14: 0x" + ctx.r14.ToString("X16"));
        WriteDebugLine("  R15: 0x" + ctx.r15.ToString("X16"));
#endif
        WriteDebugLine("========================================");
    }

    private static void WriteDebugLine(string message)
    {
        // Write to serial
        Serial.WriteString(message);
        Serial.WriteString("\n");

        // Write to screen
        KernelConsole.WriteLine(message);
    }

    private static void Halt()
    {
        WriteDebugLine("");
        WriteDebugLine("System halted.");
        while (true) { }
    }
}
