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
#if !ARCH_X64
    // The active profile-matrix cell name (bare, bare+gicv2, bare+gicv3),
    // injected by the engine on the Limine cmdline. Drives the per-GIC-version
    // assertions below.
    private static string s_profile = string.Empty;
#endif

    protected override void BeforeRun()
    {
        Serial.WriteString("[Interrupts] BeforeRun() reached!\n");

        // 4 cross-arch + 3 arch-specific = 7 tests per cell.
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
        // ==================== arm64 (GIC, per cell's gic-version) ====================
        s_profile = TR.GetProfileName();
        TR.Run("Gic_Initialized", TestGicInitialized);

        if (ProfileContains("gicv3"))
        {
            // GICv3 + ITS: the MSI delivery path must be fully up.
            TR.Run("Its_Lpi_BroughtUp", TestItsLpiUp);
            TR.Run("Msi_RoutingAvailable", TestMsiRoutingAvailableArm64);
        }
        else if (ProfileContains("gicv2"))
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

    // Substring match over the cell name (the kernel runtime does not plug
    // string.Contains). Detects the composed modifier, e.g. the "gicv2" in
    // "bare+gicv2".
    private static bool ProfileContains(string needle)
    {
        int limit = s_profile.Length - needle.Length;
        for (int i = 0; i <= limit; i++)
        {
            int j = 0;
            while (j < needle.Length && s_profile[i + j] == needle[j])
            {
                j++;
            }
            if (j == needle.Length)
            {
                return true;
            }
        }
        return false;
    }

#endif
}
