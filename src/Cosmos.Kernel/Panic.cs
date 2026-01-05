using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Cpu.Data;
using CorePanic = Cosmos.Kernel.Core.Panic;

namespace Cosmos.Kernel;

/// <summary>
/// Extended kernel panic handler with CPU exception support.
/// </summary>
public static class Panic
{
    /// <summary>
    /// Triggers a kernel panic with the specified message.
    /// Disables interrupts and halts the CPU.
    /// </summary>
    /// <param name="message">The panic message describing the error.</param>
    public static void Halt(string message)
    {
        // Use core panic for basic halt
        CorePanic.Halt(message);
    }

    /// <summary>
    /// Triggers a CPU exception panic with context information.
    /// This method is allocation-free to avoid recursive faults.
    /// Disables interrupts and halts the CPU.
    /// </summary>
    /// <param name="exceptionName">The name of the CPU exception.</param>
    /// <param name="ctx">The interrupt context with register state.</param>
    public static void CpuException(string exceptionName, ref IRQContext ctx)
    {
        InternalCpu.DisableInterrupts();

        // Use only Serial - allocation-free, no KernelConsole which may allocate
        Serial.WriteString("\n========================================\n");
        Serial.WriteString("CPU EXCEPTION: ");
        Serial.WriteString(exceptionName);
        Serial.WriteString("\n========================================\n");
        PrintCpuContext(ref ctx);
        Serial.WriteString("========================================\n");
        Serial.WriteString("System halted.\n");

        HaltCpu();
    }

    private static void PrintCpuContext(ref IRQContext ctx)
    {
        Serial.WriteString("Interrupt Vector: ");
        Serial.WriteNumber(ctx.interrupt);
        Serial.WriteString("\nCPU Flags (ESR): 0x");
        Serial.WriteHex(ctx.cpu_flags);
        Serial.WriteString("\n\nRegisters:\n");
#if ARCH_ARM64
        Serial.WriteString("  X0:  0x"); Serial.WriteHex(ctx.x0);
        Serial.WriteString("  X1:  0x"); Serial.WriteHex(ctx.x1); Serial.WriteString("\n");
        Serial.WriteString("  X2:  0x"); Serial.WriteHex(ctx.x2);
        Serial.WriteString("  X3:  0x"); Serial.WriteHex(ctx.x3); Serial.WriteString("\n");
        Serial.WriteString("  X4:  0x"); Serial.WriteHex(ctx.x4);
        Serial.WriteString("  X5:  0x"); Serial.WriteHex(ctx.x5); Serial.WriteString("\n");
        Serial.WriteString("  X6:  0x"); Serial.WriteHex(ctx.x6);
        Serial.WriteString("  X7:  0x"); Serial.WriteHex(ctx.x7); Serial.WriteString("\n");
        Serial.WriteString("  X8:  0x"); Serial.WriteHex(ctx.x8);
        Serial.WriteString("  X9:  0x"); Serial.WriteHex(ctx.x9); Serial.WriteString("\n");
        Serial.WriteString("  X10: 0x"); Serial.WriteHex(ctx.x10);
        Serial.WriteString("  X11: 0x"); Serial.WriteHex(ctx.x11); Serial.WriteString("\n");
        Serial.WriteString("  X29: 0x"); Serial.WriteHex(ctx.x29);
        Serial.WriteString("  X30: 0x"); Serial.WriteHex(ctx.x30); Serial.WriteString("\n");
        Serial.WriteString("  SP:  0x"); Serial.WriteHex(ctx.sp);
        Serial.WriteString("  ELR: 0x"); Serial.WriteHex(ctx.elr); Serial.WriteString("\n");
        Serial.WriteString("  SPSR: 0x"); Serial.WriteHex(ctx.spsr); Serial.WriteString("\n");
#elif ARCH_X64
        Serial.WriteString("  RAX: 0x"); Serial.WriteHex(ctx.rax);
        Serial.WriteString("  RBX: 0x"); Serial.WriteHex(ctx.rbx); Serial.WriteString("\n");
        Serial.WriteString("  RCX: 0x"); Serial.WriteHex(ctx.rcx);
        Serial.WriteString("  RDX: 0x"); Serial.WriteHex(ctx.rdx); Serial.WriteString("\n");
        Serial.WriteString("  RSI: 0x"); Serial.WriteHex(ctx.rsi);
        Serial.WriteString("  RDI: 0x"); Serial.WriteHex(ctx.rdi); Serial.WriteString("\n");
        Serial.WriteString("  RBP: 0x"); Serial.WriteHex(ctx.rbp);
        Serial.WriteString("  R8:  0x"); Serial.WriteHex(ctx.r8); Serial.WriteString("\n");
        Serial.WriteString("  R9:  0x"); Serial.WriteHex(ctx.r9);
        Serial.WriteString("  R10: 0x"); Serial.WriteHex(ctx.r10); Serial.WriteString("\n");
        Serial.WriteString("  R11: 0x"); Serial.WriteHex(ctx.r11);
        Serial.WriteString("  R12: 0x"); Serial.WriteHex(ctx.r12); Serial.WriteString("\n");
        Serial.WriteString("  R13: 0x"); Serial.WriteHex(ctx.r13);
        Serial.WriteString("  R14: 0x"); Serial.WriteHex(ctx.r14); Serial.WriteString("\n");
        Serial.WriteString("  R15: 0x"); Serial.WriteHex(ctx.r15); Serial.WriteString("\n");
        Serial.WriteString("  CR2: 0x"); Serial.WriteHex(ctx.cr2); Serial.WriteString("\n");
#endif
    }

    private static void HaltCpu()
    {
        // Infinite loop with halt to save power
        while (true)
        {
            InternalCpu.Halt();
        }
    }
}
