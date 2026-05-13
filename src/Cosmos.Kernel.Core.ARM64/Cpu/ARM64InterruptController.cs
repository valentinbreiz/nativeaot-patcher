// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.ARM64.Bridge;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.ARM64.Cpu;

/// <summary>
/// ARM64 interrupt controller. Owns exception vectors, GIC bring-up, and
/// the ARM64 dispatch path: GIC acknowledge, dense SPI/PPI/SGI lookup,
/// sparse GICv3 LPI lookup (INTID &gt;= 8192), EOI, and synchronous-exception
/// fatal handling. Native imports live in Cosmos.Kernel.Core.ARM64/Bridge/Import/Arm64ExceptionVectorNative.cs.
/// </summary>
public class ARM64InterruptController : IInterruptController
{
    private bool _initialized;
    private uint _lastAckedIntId;

    // ARM64 LPIs (GICv3 ITS) live in INTID space starting at 8192. The
    // dense InterruptManager.s_irqHandlers[256] table can't index them — we
    // keep a separate sparse array sized to the LPI window the kernel
    // commits to. 1024 covers many devices' worth of MSI-X vectors and
    // matches the default PROPBASER allocation in GICv3Lpi.
    public const uint LpiBase = 8192;
    public const int LpiCount = 1024;
    private static InterruptManager.IrqDelegate[]? s_lpiHandlers;
    private static int s_nextLpiOffset;

    // Guards s_lpiHandlers RMW in AllocateLpi so concurrent device probes
    // can't both grab the same slot and silently drop one handler.
    private static Scheduler.SpinLock s_lpiLock;

    public bool IsInitialized => _initialized;

    public void Initialize()
    {
        Serial.Write("[ARM64InterruptController] Starting exception vector initialization...\n");

        // Initialize exception vectors (VBAR_EL1)
        Arm64ExceptionVectorNative.InitExceptionVectors();
        Serial.Write("[ARM64InterruptController] Exception vectors initialized\n");

        // Allocate the LPI handler table before the GIC (and therefore ITS)
        // comes online — Arm64MsiBinder can call AllocateLpi mid-bring-up.
        s_lpiHandlers = new InterruptManager.IrqDelegate[LpiCount];

        // Initialize the GIC (Generic Interrupt Controller)
        GIC.Initialize();

        // Enable timer interrupts (PPI 30 = non-secure physical timer)
        GIC.SetPriority(GIC.TIMER_NONSEC_PHYS, 0x80);  // Medium priority
        GIC.EnableInterrupt(GIC.TIMER_NONSEC_PHYS);

        Serial.Write("[ARM64InterruptController] ARM64 interrupt system ready\n");

        _initialized = true;
    }

    public void RouteIrq(byte irqNo, byte vector, bool startMasked)
    {
        // On ARM64, irqNo is the GIC interrupt ID.
        if (!startMasked)
        {
            GIC.EnableInterrupt(irqNo);
        }

        Serial.Write("[ARM64InterruptController] Routed IRQ ");
        Serial.WriteNumber(irqNo);
        Serial.Write(" -> vector ");
        Serial.WriteNumber(vector);
        Serial.Write("\n");
    }

    /// <summary>
    /// Allocates an unused LPI (GICv3 ITS), registers <paramref name="handler"/>,
    /// and returns the absolute INTID (&gt;= <see cref="LpiBase"/>). The matching
    /// LPI must still be enabled in the redistributor's PROPBASER table by
    /// <see cref="GICv3Lpi"/> before it can fire. Throws on exhaustion.
    /// </summary>
    public static uint AllocateLpi(InterruptManager.IrqDelegate handler)
    {
        if (s_lpiHandlers == null)
        {
            throw new System.InvalidOperationException("ARM64InterruptController.Initialize must be called before AllocateLpi");
        }

        s_lpiLock.Acquire();
        try
        {
            for (int o = s_nextLpiOffset; o < s_lpiHandlers.Length; o++)
            {
                if (s_lpiHandlers[o] == null)
                {
                    s_lpiHandlers[o] = handler;
                    s_nextLpiOffset = o + 1;
                    return LpiBase + (uint)o;
                }
            }
            for (int o = 0; o < s_nextLpiOffset; o++)
            {
                if (s_lpiHandlers[o] == null)
                {
                    s_lpiHandlers[o] = handler;
                    s_nextLpiOffset = o + 1;
                    return LpiBase + (uint)o;
                }
            }
        }
        finally
        {
            s_lpiLock.Release();
        }
        throw new System.InvalidOperationException("ARM64InterruptController: LPI range exhausted");
    }

    public void Dispatch(ref IRQContext ctx)
    {
        // ARM64 exception types: 0 = sync, 1 = IRQ (GIC), 2 = FIQ, 3 = SError.
        if (ctx.interrupt == 1)
        {
            uint intId = GIC.AcknowledgeInterrupt();
            _lastAckedIntId = intId;

            // INTIDs 1020..1023 are GICv3-reserved (1023 = spurious); LPIs
            // start at 8192 and must NOT be treated as spurious.
            if (intId >= 1020 && intId < LpiBase)
            {
                return;
            }

            // SPI/PPI/SGI -> dense InterruptManager handlers; LPI -> sparse local table.
            InterruptManager.IrqDelegate? handler = null;
            if (intId >= LpiBase)
            {
                uint off = intId - LpiBase;
                if (s_lpiHandlers != null && off < (uint)s_lpiHandlers.Length)
                {
                    handler = s_lpiHandlers[(int)off];
                }
            }
            else
            {
                InterruptManager.IrqDelegate[]? handlers = InterruptManager.s_irqHandlers;
                if (handlers != null && intId < (uint)handlers.Length)
                {
                    handler = handlers[(int)intId];
                }
            }

            if (handler != null)
            {
                ctx.interrupt = intId;
                handler(ref ctx);
            }

            SendEOI();
            return;
        }

        if (ctx.interrupt == 0)  // Synchronous exception
        {
            InterruptManager.IrqDelegate[]? handlers = InterruptManager.s_irqHandlers;
            if (handlers != null)
            {
                InterruptManager.IrqDelegate handler = handlers[0];
                if (handler != null)
                {
                    handler(ref ctx);
                    return;
                }
            }

            HandleFatalException(ctx.interrupt, ctx.cpu_flags, ctx.fault_address);
            return;
        }

        // FIQ or SError - log and halt
        Serial.Write("[INT] Unexpected exception type: ", ctx.interrupt, "\n");
        HandleFatalException(ctx.interrupt, ctx.cpu_flags, ctx.fault_address);
    }

    private void SendEOI()
    {
        // INTIDs 1020..1023 are GIC-reserved (1023 = spurious); writing those
        // to ICC_EOIR1_EL1 is UNPREDICTABLE per ARM IHI 0069G. Everything
        // else — SPI/PPI/SGI in [0..1019] *and* LPIs in [8192..16777215]
        // delivered by the ITS — must be EOI'd or the CPU interface keeps
        // the priority active and silently drops every subsequent IRQ at
        // equal/lower priority.
        if (_lastAckedIntId < 1020 || _lastAckedIntId >= 1024)
        {
            GIC.EndOfInterrupt(_lastAckedIntId);
        }
    }

    private static void HandleFatalException(ulong interrupt, ulong cpuFlags, ulong faultAddress)
    {
        // Decode ESR_EL1 to get exception class.
        uint ec = (uint)(cpuFlags >> 26) & 0x3F;

        Serial.Write("[INT] FATAL: Synchronous exception\n");
        Serial.Write("[INT] ESR_EL1: 0x");
        Serial.WriteHex(cpuFlags);
        Serial.Write(" (EC=0x");
        Serial.WriteHex(ec);
        Serial.Write(")\n");
        Serial.Write("[INT] FAR_EL1: 0x");
        Serial.WriteHex(faultAddress);
        Serial.Write("\n");

        switch (ec)
        {
            case 0x00: Serial.Write("[INT] Unknown exception\n"); break;
            case 0x15: Serial.Write("[INT] SVC from AArch64\n"); break;
            case 0x20: Serial.Write("[INT] Instruction abort from lower EL\n"); break;
            case 0x21: Serial.Write("[INT] Instruction abort from current EL\n"); break;
            case 0x22: Serial.Write("[INT] PC alignment fault\n"); break;
            case 0x24: Serial.Write("[INT] Data abort from lower EL\n"); break;
            case 0x25: Serial.Write("[INT] Data abort from current EL\n"); break;
            case 0x26: Serial.Write("[INT] SP alignment fault\n"); break;
            default: Serial.Write("[INT] Exception class: 0x"); Serial.WriteHex(ec); Serial.Write("\n"); break;
        }

        Serial.Write("[INT] System halted.\n");
        while (true) { }
    }
}
