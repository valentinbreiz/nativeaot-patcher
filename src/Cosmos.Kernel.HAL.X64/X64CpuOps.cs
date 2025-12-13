using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.HAL.Interfaces;

namespace Cosmos.Kernel.HAL.X64;

public partial class X64CpuOps : ICpuOps
{
    [LibraryImport("*", EntryPoint = "_native_cpu_halt")]
    [SuppressGCTransition]
    private static partial void NativeHalt();

    [LibraryImport("*", EntryPoint = "_native_cpu_memory_barrier")]
    [SuppressGCTransition]
    private static partial void NativeMemoryBarrier();

    [LibraryImport("*", EntryPoint = "_native_cpu_rdtsc")]
    [SuppressGCTransition]
    private static partial ulong NativeReadTSC();

    /// <summary>
    /// TSC (Time Stamp Counter) frequency in Hz.
    /// Default is 1 GHz as a reasonable estimate for modern CPUs.
    /// Calibrated during kernel initialization using PIT as reference.
    /// </summary>
    public static long TscFrequency { get; private set; } = 1_000_000_000;

    /// <summary>
    /// Gets whether the TSC frequency has been calibrated.
    /// </summary>
    public static bool IsTscCalibrated { get; private set; }

    public void Halt() => NativeHalt();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Nop()
    {
        // NOP instruction will be inlined by compiler
    }

    public void MemoryBarrier() => NativeMemoryBarrier();

    /// <summary>
    /// Reads the Time Stamp Counter (TSC).
    /// Returns a 64-bit value representing CPU cycles since reset.
    /// </summary>
    public static ulong ReadTSC() => NativeReadTSC();

    /// <summary>
    /// Calibrates the TSC frequency using PIT as a reference timer.
    /// Uses PIT channel 0 in one-shot mode for ~10ms measurement.
    /// Must be called before any code accesses Stopwatch.Frequency.
    /// </summary>
    public static void CalibrateTsc()
    {
        // PIT frequency is 1193180 Hz, so 11932 ticks = ~10ms
        const ushort pitCount = 11932;
        const uint calibrationMs = 10;

        // PIT command: channel 0, lobyte/hibyte, one-shot mode, binary
        Native.IO.Write8(0x43, 0x30);
        Native.IO.Write8(0x40, (byte)(pitCount & 0xFF));
        Native.IO.Write8(0x40, (byte)(pitCount >> 8));

        // Read TSC before
        ulong tscStart = NativeReadTSC();

        // Wait for PIT to count down by polling
        ushort lastCount = pitCount;
        while (true)
        {
            // Latch count for channel 0
            Native.IO.Write8(0x43, 0x00);
            byte lo = Native.IO.Read8(0x40);
            byte hi = Native.IO.Read8(0x40);
            ushort currentCount = (ushort)(lo | (hi << 8));

            // PIT counts down, check if we've wrapped or reached near zero
            if (currentCount > lastCount || currentCount == 0)
                break;
            lastCount = currentCount;
        }

        // Read TSC after
        ulong tscEnd = NativeReadTSC();

        ulong tscElapsed = tscEnd - tscStart;
        ulong ticksPerMs = tscElapsed / calibrationMs;

        TscFrequency = (long)(ticksPerMs * 1000);
        IsTscCalibrated = true;
    }
}
