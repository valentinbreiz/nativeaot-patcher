// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.Diagnostics;
using Cosmos.Build.API.Attributes;

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

}