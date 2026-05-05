// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.Diagnostics;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core;
using Monitor = Cosmos.Kernel.Core.Scheduler.Monitor;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
#if ARCH_X64
using Cosmos.Kernel.HAL.X64.Devices.Clock;
#elif ARCH_ARM64
using Cosmos.Kernel.HAL.ARM64.Devices.Clock;
#endif


namespace Cosmos.Kernel.Plugs.System;

/// <summary>
/// Plug for Interop+Sys class to provide OS interop functions for bare-metal kernel.
/// </summary>
[Plug("Interop/Sys")]
public static class InteropSysPlug
{
    // Simple LFSR-based pseudo-random number generator state
    private static ulong s_randomState = 0x853c49e6748fea9bUL;

    /// <summary>
    /// Provides non-cryptographic random bytes for Random.Shared seeding.
    /// Uses a simple XorShift algorithm since we don't have OS randomness.
    /// </summary>
    [PlugMember]
    public static unsafe void GetNonCryptographicallySecureRandomBytes(byte* buffer, int length)
    {
        // Mix in some entropy from the timer/tick count
        ulong state = s_randomState;
        state ^= (ulong)Stopwatch.GetTimestamp();

        for (int i = 0; i < length; i++)
        {
            // XorShift64 algorithm
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            buffer[i] = (byte)(state & 0xFF);
        }

        s_randomState = state;
    }

    /// <summary>
    /// Provides cryptographically secure random bytes.
    /// In a real kernel, this would use hardware RNG (RDRAND) if available.
    /// </summary>
    [PlugMember]
    public static unsafe void GetCryptographicallySecureRandomBytes(byte* buffer, int length)
    {
        // For now, use the same non-crypto implementation
        // TODO: Use RDRAND instruction if available
        GetNonCryptographicallySecureRandomBytes(buffer, length);
    }

    [PlugMember]
    public static long GetLowResolutionTimestamp()
    {

        if (CosmosFeatures.TimerEnabled)
        {
            if (RTC.Instance == null)
            {
                return 0;
            }

            return RTC.Instance.GetElapsedTicks() / TimeSpan.TicksPerMillisecond;
        }
        else
        {
            return 0;
        }
    }

    [PlugMember]
    public static int GetErrNo()
    {
        // Always return 0 (no error) for now
        return 0;
    }

    [PlugMember]
    public static void SetErrNo(int value)
    {
        // No-op for now
    }

    [PlugMember]
    internal static IntPtr LowLevelMonitor_Create()
    {
        Monitor monitor = new();
        var gchandle = new GCHandle<Monitor>(monitor);

        return GCHandle<Monitor>.ToIntPtr(gchandle);
    }

    [PlugMember]
    internal static void LowLevelMonitor_Destroy(IntPtr monitor)
    {
        var gchandle = GCHandle<Monitor>.FromIntPtr(monitor);
        gchandle.Target.Dispose();
        gchandle.Dispose();
    }

    [PlugMember]
    internal static void LowLevelMonitor_Acquire(IntPtr monitor)
    {
        var gchandle = GCHandle<Monitor>.FromIntPtr(monitor);
        gchandle.Target.Acquire();
    }

    [PlugMember]
    internal static void LowLevelMonitor_Release(IntPtr monitor)
    {
        var gchandle = GCHandle<Monitor>.FromIntPtr(monitor);
        gchandle.Target.Release();
    }

    [PlugMember]
    internal static void LowLevelMonitor_Wait(IntPtr monitor)
    {
        var gchandle = GCHandle<Monitor>.FromIntPtr(monitor);
        gchandle.Target.Wait();
    }

    [PlugMember]
    internal static bool LowLevelMonitor_TimedWait(IntPtr monitor, int timeoutMilliseconds)
    {
        Serial.Write("[LowLevelMonitor] LowLevelMonitor_TimedWait: BEGIN, timeout=");
        Serial.Write(timeoutMilliseconds);
        Serial.Write("ms\n");
        var mon = GCHandle<Monitor>.FromIntPtr(monitor).Target;

        if (timeoutMilliseconds < 0)
        {
            mon.Wait();
            return true;
        }

        mon.Wait(timeoutMilliseconds);

        return true;
    }

    [PlugMember]
    internal static void LowLevelMonitor_Signal_Release(IntPtr monitor)
    {
        var gchandle = GCHandle<Monitor>.FromIntPtr(monitor);
        gchandle.Target.Signal();
    }

}
