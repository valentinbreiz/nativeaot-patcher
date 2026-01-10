using System;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Thread execution state.
/// </summary>
public enum ThreadState : byte
{
    Created,    // Just created, not yet scheduled
    Ready,      // Can be scheduled
    Running,    // Currently executing on a CPU
    Blocked,    // Waiting for I/O, lock, etc.
    Sleeping,   // Timed wait
    Dead        // Terminated, awaiting cleanup
}

/// <summary>
/// Thread flags.
/// </summary>
[Flags]
public enum ThreadFlags : ushort
{
    None = 0,
    KernelThread = 1 << 0,  // Kernel-mode thread
    IdleThread = 1 << 1,    // Per-CPU idle thread
    Pinned = 1 << 2,        // Cannot migrate to other CPUs
    // Bits 8-15 reserved for scheduler-specific flags
}
