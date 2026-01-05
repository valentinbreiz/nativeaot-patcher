// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.HAL.ARM64.Cpu;
using Cosmos.Kernel.HAL.Cpu;
using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.HAL.Devices.Timer;

namespace Cosmos.Kernel.HAL.ARM64.Devices.Timer;

/// <summary>
/// ARM64 Generic Timer (Architected Timer) implementation.
/// Uses the physical timer (CNTP_*) for scheduling interrupts.
/// </summary>
public partial class GenericTimer : TimerDevice
{
    /// <summary>
    /// Singleton instance of the Generic Timer.
    /// </summary>
    public static GenericTimer? Instance { get; private set; }

    /// <summary>
    /// Timer frequency in Hz (read from CNTFRQ_EL0).
    /// Typically 62.5 MHz on QEMU virt.
    /// </summary>
    private ulong _timerFrequency;

    /// <summary>
    /// Configured timer period in nanoseconds.
    /// </summary>
    private ulong _periodNs;

    /// <summary>
    /// Timer ticks per period.
    /// </summary>
    private ulong _ticksPerPeriod;

    /// <summary>
    /// Physical timer interrupt number on GIC.
    /// For QEMU virt machine: INTID 30 (non-secure physical timer).
    /// </summary>
    public const uint PhysicalTimerIrq = 30;

    /// <summary>
    /// Default timer period: 10ms (100 Hz) for scheduling.
    /// </summary>
    public const ulong DefaultPeriodNs = 10_000_000;

    // Native functions for timer register access
    [LibraryImport("*", EntryPoint = "_native_arm64_timer_get_frequency")]
    [SuppressGCTransition]
    private static partial ulong GetTimerFrequency();

    [LibraryImport("*", EntryPoint = "_native_arm64_timer_get_counter")]
    [SuppressGCTransition]
    private static partial ulong GetCounter();

    [LibraryImport("*", EntryPoint = "_native_arm64_timer_set_compare")]
    [SuppressGCTransition]
    private static partial void SetCompare(ulong value);

    [LibraryImport("*", EntryPoint = "_native_arm64_timer_enable")]
    [SuppressGCTransition]
    private static partial void EnableTimer();

    [LibraryImport("*", EntryPoint = "_native_arm64_timer_disable")]
    [SuppressGCTransition]
    private static partial void DisableTimer();

    [LibraryImport("*", EntryPoint = "_native_arm64_timer_set_tval")]
    [SuppressGCTransition]
    private static partial void SetTimerValue(uint ticks);

    [LibraryImport("*", EntryPoint = "_native_arm64_timer_get_ctl")]
    [SuppressGCTransition]
    private static partial uint GetTimerControl();

    public GenericTimer()
    {
    }

    /// <inheritdoc/>
    public override uint Frequency => (uint)(1_000_000_000UL / _periodNs);

    /// <summary>
    /// Initialize the Generic Timer.
    /// </summary>
    public override void Initialize()
    {
        Serial.Write("[GenericTimer] Initializing ARM64 Generic Timer...\n");

        Instance = this;

        // Read timer frequency from CNTFRQ_EL0
        _timerFrequency = GetTimerFrequency();
        Serial.Write("[GenericTimer] Timer frequency: ");
        Serial.WriteNumber(_timerFrequency);
        Serial.Write(" Hz\n");

        // Set default period (10ms)
        SetPeriod(DefaultPeriodNs);

        Serial.Write("[GenericTimer] Initialized\n");
    }

    /// <summary>
    /// Sets the timer period in nanoseconds.
    /// </summary>
    /// <param name="periodNs">Period in nanoseconds.</param>
    public void SetPeriod(ulong periodNs)
    {
        _periodNs = periodNs;

        // Calculate ticks per period
        // ticks = (frequency * period_ns) / 1_000_000_000
        _ticksPerPeriod = (_timerFrequency * periodNs) / 1_000_000_000UL;

        Serial.Write("[GenericTimer] Period: ");
        Serial.WriteNumber(periodNs / 1_000_000);
        Serial.Write(" ms, ticks per period: ");
        Serial.WriteNumber(_ticksPerPeriod);
        Serial.Write("\n");
    }

    /// <inheritdoc/>
    public override void SetFrequency(uint frequency)
    {
        if (frequency == 0)
            return;

        ulong periodNs = 1_000_000_000UL / frequency;
        SetPeriod(periodNs);
    }

    /// <summary>
    /// Starts the timer and arms it for the first interrupt.
    /// </summary>
    public void Start()
    {
        Serial.Write("[GenericTimer] Starting timer...\n");

        // Set TVAL to trigger after one period
        if (_ticksPerPeriod > uint.MaxValue)
        {
            Serial.Write("[GenericTimer] WARNING: ticks per period exceeds 32-bit, clamping\n");
            SetTimerValue(uint.MaxValue);
        }
        else
        {
            SetTimerValue((uint)_ticksPerPeriod);
        }

        // Enable the timer (ENABLE=1, IMASK=0)
        EnableTimer();

        Serial.Write("[GenericTimer] Timer started, CTL=0x");
        Serial.WriteHex(GetTimerControl());
        Serial.Write("\n");
    }

    /// <summary>
    /// Stops the timer.
    /// </summary>
    public void Stop()
    {
        DisableTimer();
        Serial.Write("[GenericTimer] Timer stopped\n");
    }

    /// <summary>
    /// Registers the IRQ handler for the timer.
    /// Should be called after GIC is initialized.
    /// </summary>
    public void RegisterIRQHandler()
    {
        Serial.Write("[GenericTimer] Registering timer IRQ handler for INTID ");
        Serial.WriteNumber(PhysicalTimerIrq);
        Serial.Write("\n");

        // Register handler for GIC interrupt
        // The vector will be PhysicalTimerIrq (30) mapped through GIC
        InterruptManager.SetHandler((byte)PhysicalTimerIrq, HandleIRQ);

        Serial.Write("[GenericTimer] Timer IRQ handler registered\n");
    }

    /// <summary>
    /// Timer tick counter for debugging.
    /// </summary>
    private static uint _timerTickCount;

    /// <summary>
    /// Handles the timer interrupt.
    /// </summary>
    private static unsafe void HandleIRQ(ref IRQContext ctx)
    {
        if (Instance == null)
            return;

        _timerTickCount++;

        // Re-arm the timer for the next period
        if (Instance._ticksPerPeriod > uint.MaxValue)
        {
            SetTimerValue(uint.MaxValue);
        }
        else
        {
            SetTimerValue((uint)Instance._ticksPerPeriod);
        }

        // Invoke the OnTick handler (for TimerManager)
        Instance.OnTick?.Invoke();

        // Get current CPU ID (for now, always 0 on single CPU ARM64)
        uint cpuId = 0;

        // Calculate SP pointing to saved context for context switching
        // On ARM64, ctx is at offset 512 from start of saved context (after NEON regs)
        nuint contextPtr = (nuint)Unsafe.AsPointer(ref ctx);
        nuint currentSp = contextPtr - 512;  // SP points to start of NEON save area

        // Log first few ticks and then periodically
        if (_timerTickCount <= 5 || _timerTickCount % 100 == 0)
        {
            Serial.Write("[GenericTimer] Tick ");
            Serial.WriteNumber(_timerTickCount);
            Serial.Write(" SP=0x");
            Serial.WriteHex((ulong)currentSp);
            Serial.Write("\n");
        }

        // Call scheduler with elapsed time
        SchedulerManager.OnTimerInterrupt(cpuId, currentSp, Instance._periodNs);

        // EOI is sent by the interrupt handler after we return
    }

    /// <inheritdoc/>
    public override void Wait(uint timeoutMs)
    {
        ulong targetTicks = GetCounter() + ((_timerFrequency * timeoutMs) / 1000);

        while (GetCounter() < targetTicks)
        {
            // Busy wait
        }
    }

    /// <summary>
    /// Gets the current counter value.
    /// </summary>
    public ulong GetCurrentCounter() => GetCounter();

    /// <summary>
    /// Gets the timer period in nanoseconds.
    /// </summary>
    public ulong PeriodNs => _periodNs;
}
