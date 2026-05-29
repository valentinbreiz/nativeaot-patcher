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

// Exercises the interrupt subsystem itself (no devices): the controller is up,
// the dynamic-vector allocator behaves, and an interrupt actually fires and is
// dispatched end-to-end. Per-arch tests cover the backend that delivers MSIs
// (x64 LAPIC, arm64 GICv3 ITS/LPI). The NVMe MSI-X delivery path is covered
// separately by the Storage suite.
public class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
        Serial.WriteString("[Interrupts] BeforeRun() reached!\n");

        // 4 cross-arch + 3 arch-specific = 7 tests per architecture.
        TR.Start("Interrupt System Tests", expectedTests: 7);

        // ==================== Cross-arch ====================
        TR.Run("InterruptManager_Enabled", TestInterruptManagerEnabled);
        TR.Run("TimerSource_Registered", TestTimerSourceRegistered);
        TR.Run("VectorAllocator_ReturnsDistinctDynamicVectors", TestVectorAllocatorDistinct);
        TR.Run("TimerInterrupt_WakesSleepingThread", TestTimerInterruptWakesSleepingThread);

#if ARCH_X64
        // ==================== x64 (LAPIC + MSI) ====================
        TR.Run("Lapic_Initialized", TestLapicInitialized);
        TR.Run("Lapic_TimerCalibrated", TestLapicTimerCalibrated);
        TR.Run("Msi_RoutingAvailable", TestMsiRoutingAvailableX64);
#else
        // ==================== arm64 (GIC + ITS/LPI) ====================
        TR.Run("Gic_Initialized", TestGicInitialized);
        // The ITS/LPI path only exists when the machine instantiates a GICv3
        // ITS; on an ITS-less machine these report Skipped instead of Failed.
        TR.RunIf(GICv3Its.IsInitialized, "Its_Lpi_BroughtUp", TestItsLpiBroughtUp, NoItsReason);
        TR.RunIf(GICv3Its.IsInitialized, "Msi_RoutingAvailable", TestMsiRoutingAvailableArm64, NoItsReason);
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

#else

    private const string NoItsReason = "no GIC ITS on this machine (MSI routing needs ITS + LPI)";

    private static void TestGicInitialized()
    {
        Assert.True(GIC.IsInitialized, "GIC should be initialized");
    }

    // Runs only when the machine exposes a GICv3 ITS (gated by the caller).
    // With the ITS up, the LPI configuration/pending tables must also be
    // initialized; together they are the ARM64 MSI delivery path.
    private static void TestItsLpiBroughtUp()
    {
        Assert.True(GICv3Its.IsInitialized, "GICv3 ITS should be initialized");
        Assert.True(GICv3Lpi.IsInitialized, "GICv3 LPI tables should be initialized");
    }

    private static void TestMsiRoutingAvailableArm64()
    {
        Assert.True(MsiRouting.IsAvailable, "arm64 MSI routing (ITS binder) should be available");
    }

#endif
}
