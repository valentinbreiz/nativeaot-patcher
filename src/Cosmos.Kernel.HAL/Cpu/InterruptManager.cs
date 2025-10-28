// This code is licensed under MIT license (see LICENSE for details)


using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.HAL.Cpu;

/// <summary>
/// Provides interrupt registration and dispatch for all architectures.
/// </summary>
public static partial class InterruptManager
{
    /// <summary>
    /// Interrupt delegate signature.
    /// </summary>
    /// <param name="context">The interrupt context captured by the CPU.</param>
    public delegate void IrqDelegate(ref IRQContext context);

    internal static IrqDelegate[] s_irqHandlers = new IrqDelegate[256];

    /// <summary>
    /// Initializes the platform interrupt system (IDT for x86, GIC for ARM).
    /// </summary>
    public static partial void Initialize();

    /// <summary>
    /// Registers a handler for an interrupt vector.
    /// </summary>
    /// <param name="vector">Interrupt vector index.</param>
    /// <param name="handler">Delegate to handle the interrupt.</param>
    public static void SetHandler(byte vector, IrqDelegate handler)
    {
        if (s_irqHandlers == null)
        {
            Serial.Write("[InterruptManager] ERROR: s_irqHandlers is null! Initialize() must be called first.\n");
            return;
        }
        s_irqHandlers[vector] = handler;
    }

    /// <summary>
    /// Registers a handler for a hardware IRQ.
    /// </summary>
    /// <param name="irqNo">IRQ index.</param>
    /// <param name="handler">IRQ handler delegate.</param>
    public static void SetIrqHandler(byte irqNo, IrqDelegate handler)
        => SetHandler((byte)(0x20 + irqNo), handler);

    private const string NewLine = "\n";

    /// <summary>
    /// Called by native bridge from ASM stubs to invoke the proper handler.
    /// </summary>
    /// <param name="ctx">Context structure.</param>
    public static void Dispatch(ref IRQContext ctx)
    {
        Serial.Write("[INT] ", ctx.interrupt, " START", NewLine);
        Serial.Write("[INT] cpu_flags ", ctx.cpu_flags, NewLine);
        Serial.Write("[INT] interrupt ", ctx.interrupt, NewLine);
        Serial.Write("[INT] rax ", ctx.rax, NewLine);
        Serial.Write("[INT] rcx ", ctx.rcx, NewLine);
        Serial.Write("[INT] rdx ", ctx.rdx, NewLine);
        Serial.Write("[INT] rbx ", ctx.rbx, NewLine);
        Serial.Write("[INT] rbp ", ctx.rbp, NewLine);
        Serial.Write("[INT] rsi ", ctx.rsi, NewLine);
        Serial.Write("[INT] rdi ", ctx.rdi, NewLine);
        Serial.Write("[INT] r8  ", ctx.r8, NewLine);
        Serial.Write("[INT] r9  ", ctx.r9, NewLine);
        Serial.Write("[INT] r10 ", ctx.r10, NewLine);
        Serial.Write("[INT] r11 ", ctx.r11, NewLine);
        Serial.Write("[INT] r12 ", ctx.r12, NewLine);
        Serial.Write("[INT] r13 ", ctx.r13, NewLine);
        Serial.Write("[INT] r14 ", ctx.r14, NewLine);
        Serial.Write("[INT] r15 ", ctx.r15, NewLine);

        // Check if handlers array is initialized and interrupt is in valid range
        if (s_irqHandlers != null && ctx.interrupt < (ulong)s_irqHandlers.Length)
        {
            IrqDelegate handler = s_irqHandlers[(int)ctx.interrupt];
            if (handler != null)
            {
                handler(ref ctx);
            }
        }
    }
}
