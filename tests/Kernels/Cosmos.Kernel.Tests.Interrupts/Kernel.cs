using System;
using System.Diagnostics;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Timer;
using Cosmos.TestRunner.Framework;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;
using SysThread = System.Threading.Thread;
#if ARCH_X64
using Cosmos.Kernel.Core.X64.Cpu;
#else
using Cosmos.Kernel.Core.ARM64.Cpu;
#endif

namespace Cosmos.Kernel.Tests.Interrupts;

// Exercises the interrupt subsystem itself (no device drivers): the controller
// is up, the dynamic MSI vector allocator behaves, and an interrupt actually
// fires and is dispatched end-to-end. On arm64 the suite opts into the gicv2
// and gicv3 QEMU profiles (tests/profiles.json) so BOTH interrupt-controller
// paths are covered deterministically rather than depending on the machine
// default. End-to-end MSI *delivery* (a device raising an MSI through the ITS)
// stays covered by the Storage suite's NVMe assertions.
public class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
        Serial.WriteString("[Interrupts] BeforeRun() reached!\n");

        // 5 cross-arch + 5 arch-specific = 10 tests per cell.
        TR.Start("Interrupt System Tests", expectedTests: 10);

        // ==================== Cross-arch ====================
        TR.Run("InterruptManager_Enabled", TestInterruptManagerEnabled);
        TR.Run("TimerSource_Registered", TestTimerSourceRegistered);
        TR.Run("VectorAllocator_ReturnsDistinctDynamicVectors", TestVectorAllocatorDistinct);
        TR.Run("VectorAllocator_ReusesFreedSlots", TestVectorAllocatorReusesFreedSlots);
        TR.Run("TimerInterrupt_WakesSleepingThread", TestTimerInterruptWakesSleepingThread);

#if ARCH_X64
        // ==================== x64 (LAPIC + MSI) ====================
        TR.Run("Lapic_Initialized", TestLapicInitialized);
        TR.Run("Lapic_TimerCalibrated", TestLapicTimerCalibrated);
        TR.Run("Msi_RoutingAvailable", TestMsiRoutingAvailableX64);
        TR.Run("IrqExit_ReschedulesSignaledWaiter", TestIrqExitReschedulesSignaledWaiter);
        TR.Run("Msi_AddressTargetsRunningLapic", TestMsiAddressTargetsRunningLapic);
#else
        // ==================== arm64 (GIC, per cell's gic-version) ====================
        TR.Run("Gic_Initialized", TestGicInitialized);

        if (TR.ProfileContains("gicv3"))
        {
            // GICv3 + ITS: the MSI delivery path must be fully up.
            TR.Run("Its_Lpi_BroughtUp", TestItsLpiUp);
            TR.Run("Msi_RoutingAvailable", TestMsiRoutingAvailableArm64);
        }
        else if (TR.ProfileContains("gicv2"))
        {
            // GICv2 has no ITS, so there is no LPI/MSI path: the kernel must
            // report it absent and fall back to polled rather than claim MSI.
            TR.Run("Its_Lpi_BroughtUp", TestItsAbsentOnGicv2);
            TR.Run("Msi_RoutingAvailable", TestMsiRoutingUnavailableOnGicv2);
        }
        else
        {
            // Bare cell: gic-version is whatever the machine defaults to, so the
            // ITS/MSI state is not determinate. The gicv2/gicv3 cells assert it.
            TR.Skip("Its_Lpi_BroughtUp", "gic-version not pinned by this cell");
            TR.Skip("Msi_RoutingAvailable", "gic-version not pinned by this cell");
        }

        // The IRQ-exit reschedule path is cross-arch, but the on-demand
        // ISR-context trigger it needs (LAPIC self-IPI) only exists on x64.
        TR.Skip("IrqExit_ReschedulesSignaledWaiter", "self-IPI harness is x64-only");
        TR.Skip("Msi_AddressTargetsRunningLapic", "LAPIC MSI address contract is x64-only");
#endif

        TR.Finish();

        Serial.WriteString("\n[Tests Complete - System Halting]\n");
    }

    protected override void Run() => Stop();

    protected override void AfterRun()
    {
        TR.Complete();
        Cosmos.Kernel.System.Power.Halt();
    }

    // ==================== Cross-arch ====================

    private static void TestInterruptManagerEnabled()
    {
        Assert.True(InterruptManager.IsEnabled, "InterruptManager should report enabled");
    }

    // A registered timer device is the periodic interrupt source that drives
    // the scheduler; without one nothing would generate IRQs to dispatch.
    private static void TestTimerSourceRegistered()
    {
        Assert.True(TimerManager.IsInitialized, "TimerManager should be initialized");
        Assert.True(TimerManager.Timer != null, "A timer device should be registered");
    }

    // Exercises the dynamic-vector allocator MSI/MSI-X programmers depend on:
    // every allocation must fall in the [0x40, 0xFE] dynamic window and be
    // unique, so two devices never collide on the same vector.
    private static void TestVectorAllocatorDistinct()
    {
        byte v1 = InterruptManager.AllocateVector(NoopHandler);
        byte v2 = InterruptManager.AllocateVector(NoopHandler);
        byte v3 = InterruptManager.AllocateVector(NoopHandler);

        Assert.True(v1 >= 0x40 && v1 <= 0xFE, "vector 1 should be in the dynamic range");
        Assert.True(v2 >= 0x40 && v2 <= 0xFE, "vector 2 should be in the dynamic range");
        Assert.True(v3 >= 0x40 && v3 <= 0xFE, "vector 3 should be in the dynamic range");
        Assert.True(v1 != v2 && v2 != v3 && v1 != v3, "allocated vectors must be distinct");
    }

    // End-to-end proof the interrupt path is live: a scheduler-blocking sleep
    // can only resume once the periodic timer IRQ fires, is dispatched through
    // the controller, and the handler wakes the blocked thread. The elapsed
    // time is read from the free-running Stopwatch (TSC / CNTPCT), which does
    // not depend on interrupts, so a sane duration isolates the IRQ path. If
    // interrupts were dead the sleep would never return and the suite would
    // time out.
    private static void TestTimerInterruptWakesSleepingThread()
    {
        long start = Stopwatch.GetTimestamp();
        SysThread.Sleep(200);
        long end = Stopwatch.GetTimestamp();

        long elapsedMs = (end - start) * 1000 / Stopwatch.Frequency;
        Serial.WriteString("[Interrupts] Sleep(200ms) measured ms: ");
        Serial.WriteNumber((ulong)elapsedMs);
        Serial.WriteString("\n");

        Assert.True(elapsedMs >= 100 && elapsedMs <= 800,
            "a 200ms scheduler sleep should resume in roughly 200ms via the timer IRQ");
    }

    // Fills the dynamic range to exhaustion, frees one slot, and proves the
    // allocator's wrap pass hands the freed slot back out — the behavior the
    // wrap-scan comment always promised but that FreeVector only now makes
    // possible. Frees everything it allocated so later cells see a clean
    // allocator.
    private static void TestVectorAllocatorReusesFreedSlots()
    {
        byte[] allocated = new byte[256];
        int count = 0;
        while (count < allocated.Length)
        {
            byte v = TryAllocateVector();
            if (v == 0)
            {
                break;
            }
            allocated[count++] = v;
        }

        Assert.True(count > 0, "the dynamic range should not already be exhausted");

        byte freed = allocated[count / 2];
        InterruptManager.FreeVector(freed);
        byte reused = TryAllocateVector();

        // Free every vector this cell allocated before asserting, so a
        // failure doesn't leave the allocator exhausted for later cells.
        for (int i = 0; i < count; i++)
        {
            InterruptManager.FreeVector(allocated[i]);
        }
        InterruptManager.FreeVector(reused);

        Assert.True(reused == freed, "after exhaustion, the allocator must reuse the freed slot");
    }

    // Single try/catch in its own helper (arm64 EH dispatch mismatches the
    // clause when try/catch blocks share a frame with other locals). Returns
    // 0 (an out-of-range vector) on exhaustion.
    private static byte TryAllocateVector()
    {
        try
        {
            return InterruptManager.AllocateVector(NoopHandler);
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static void NoopHandler(ref IRQContext context)
    {
    }

#if ARCH_X64

    private static void TestLapicInitialized()
    {
        Assert.True(LocalApic.IsInitialized, "LAPIC should be initialized");
    }

    private static void TestLapicTimerCalibrated()
    {
        Assert.True(LocalApic.IsTimerCalibrated, "LAPIC timer should be calibrated");
        Assert.True(LocalApic.TicksPerMs > 0, "LAPIC ticks-per-ms should be non-zero");
    }

    // On x64 the LAPIC is the MSI routing backend and its binder is registered
    // at boot, so MsiRouting must report available.
    private static void TestMsiRoutingAvailableX64()
    {
        Assert.True(MsiRouting.IsAvailable, "x64 MSI routing (LAPIC binder) should be available");
    }

    // ==================== IRQ-exit reschedule latency ====================
    // An ISR-side InterruptEvent.Signal only readies the waiter; if nothing
    // reschedules at device-IRQ exit, the woken thread sits in the run queue
    // until the next timer tick — up to a full 10ms quantum of added latency
    // per I/O. This measures signal-to-wake latency with a LAPIC self-IPI as
    // the "device" interrupt: the waiter parks on the event, the main thread
    // fires the IPI whose handler Signals from ISR context, and the waiter
    // timestamps its wake-up.
    private const int IrqLatencyRounds = 6;
    private static Cosmos.Kernel.Core.Scheduler.InterruptEvent? _wakeEvent;
    private static volatile bool _waiterReady;
    private static volatile bool _waiterDone;
    private static long _wakeTimestamp;

    private static void IpiSignalHandler(ref IRQContext context)
    {
        _wakeEvent?.Signal();
    }

    private static void TestIrqExitReschedulesSignaledWaiter()
    {
        _wakeEvent = new Cosmos.Kernel.Core.Scheduler.InterruptEvent();
        _waiterReady = false;
        _waiterDone = false;
        byte vector = InterruptManager.AllocateVector(IpiSignalHandler);

        var waiter = new SysThread(IrqLatencyWaiter);
        waiter.Start();

        long worstTicks = 0;
        for (int round = 0; round < IrqLatencyRounds; round++)
        {
            while (!_waiterReady)
            {
                // waiter not yet at Wait()
            }
            _waiterReady = false;
            _waiterDone = false;

            // Give the waiter a few full quanta to actually park (Blocked):
            // a signal latched before it blocks would measure ~0 and mask
            // the very latency this cell pins down.
            TimerManager.Wait(30);

            long signalTicks = Stopwatch.GetTimestamp();
            LocalApic.SendSelfIpi(vector);

            while (!_waiterDone)
            {
                // the parked waiter records its wake timestamp
            }

            long latency = _wakeTimestamp - signalTicks;
            if (latency > worstTicks)
            {
                worstTicks = latency;
            }
        }

        long worstUs = worstTicks * 1_000_000 / Stopwatch.Frequency;
        Serial.WriteString("[Interrupts] worst signal-to-wake latency us: ");
        Serial.WriteNumber((ulong)worstUs);
        Serial.WriteString("\n");

        // Rescheduled at IRQ exit the wake costs microseconds; parked until
        // the next timer tick it costs up to 10_000us. 2_000us splits the
        // two regimes with wide margin on a loaded CI host.
        Assert.True(worstUs < 2_000,
            "an ISR-side Signal must wake the parked waiter at IRQ exit, not at the next timer tick");
    }

    // Pins the MSI address contract: the destination field must carry the
    // running CPU's ACTUAL APIC ID (LocalApic.GetId()), not its scheduler
    // index. QEMU's BSP APIC ID is 0, so this cell cannot fail there today —
    // it exists to catch the index-as-ID shortcut on any machine (or future
    // emulator config) whose BSP APIC ID is nonzero, where the mistake makes
    // device MSIs silently target a nonexistent LAPIC.
    private static void TestMsiAddressTargetsRunningLapic()
    {
        MsiRouting.BindEntry(null, 0, NoopHandler, 0, out ulong address, out uint data);

        Assert.True((address & 0xFFF00000UL) == 0xFEE00000UL, "MSI doorbell must sit in the LAPIC address window");
        byte destination = (byte)((address >> 12) & 0xFF);
        Assert.True(destination == LocalApic.GetId(), "MSI destination must be the running CPU's actual APIC ID, not its index");
        Assert.True(data != 0, "MSI data must carry the allocated vector");
    }

    private static void IrqLatencyWaiter()
    {
        for (int round = 0; round < IrqLatencyRounds; round++)
        {
            _waiterReady = true;
            _wakeEvent!.Wait();
            _wakeTimestamp = Stopwatch.GetTimestamp();
            _waiterDone = true;
        }
    }

#else

    private static void TestGicInitialized()
    {
        Assert.True(GIC.IsInitialized, "GIC should be initialized");
    }

    // gicv3 cell: the ITS and its LPI configuration/pending tables are the
    // ARM64 MSI delivery path and must both be up.
    private static void TestItsLpiUp()
    {
        Assert.True(GICv3Its.IsInitialized, "GICv3 ITS should be initialized on a gicv3 machine");
        Assert.True(GICv3Lpi.IsInitialized, "GICv3 LPI tables should be initialized on a gicv3 machine");
    }

    private static void TestMsiRoutingAvailableArm64()
    {
        Assert.True(MsiRouting.IsAvailable, "arm64 MSI routing (ITS binder) should be available on a gicv3 machine");
    }

    // gicv2 cell: no ITS exists, so the LPI/MSI path must report absent. This
    // is the path that forces drivers to the polled fallback.
    private static void TestItsAbsentOnGicv2()
    {
        Assert.False(GICv3Its.IsInitialized, "GICv2 exposes no ITS");
    }

    private static void TestMsiRoutingUnavailableOnGicv2()
    {
        Assert.False(MsiRouting.IsAvailable, "GICv2 has no ITS, so MSI routing must be unavailable");
    }

#endif
}
