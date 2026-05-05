// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Scheduler;
using SchedulerThread = Cosmos.Kernel.Core.Scheduler.Thread;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

/// <summary>
/// Thread-Local Allocation Buffer (TLAB) management: refill, return, and per-thread context access.
/// </summary>
public static unsafe partial class GarbageCollector
{
    /// <summary>
    /// Default TLAB size in bytes (8KB).
    /// </summary>
    private const uint TlabSize = 8192;

    /// <summary>
    /// Returns a reference to the current thread's allocation context.
    /// Uses the scheduler's current thread when enabled, otherwise falls back to the static context.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref AllocContext GetCurrentAllocContext()
    {
        // SchedulerManager.Enabled is false during early boot (before scheduler init),
        // so we safely fall back to the static context. CosmosFeatures.SchedulerEnabled
        // alone is a compile-time flag that doesn't guarantee _cpuStates is allocated.
        if (CosmosFeatures.SchedulerEnabled && SchedulerManager.Enabled)
        {
            PerCpuState cpuState = SchedulerManager.GetCpuState(SchedulerManager.GetCurrentCpuId());
            if (cpuState?.CurrentThread != null)
            {
                return ref cpuState.CurrentThread.AllocContext;
            }
        }

        return ref s_fallbackAllocContext;
    }

    /// <summary>
    /// Refills a thread's TLAB from the GC heap (free list, then segment bump, then new segment).
    /// Returns the unused gap from the old TLAB to the free list before acquiring a new buffer.
    /// </summary>
    /// <param name="ac">The allocation context to refill.</param>
    /// <param name="size">Minimum allocation size that must fit in the new TLAB.</param>
    /// <returns><c>true</c> if the TLAB was successfully refilled; otherwise, <c>false</c>.</returns>
    internal static bool RefillAllocContext(ref AllocContext ac, uint size)
    {
        using (InternalCpu.DisableInterruptsScope())
        {
            // Return unused portion of old TLAB
            StampUnusedTlab(ref ac);

            // Determine TLAB request size: at least the requested size, ideally TlabSize
            uint requestSize = size > TlabSize ? size : TlabSize;

            // Try free list first (raw variant — already zeroes memory)
            void* buffer = AllocFromFreeListRaw(requestSize);
            if (buffer != null)
            {
                return SetupTlab(ref ac, buffer, requestSize);
            }

            // Try bump allocation from segments (raw variant — must zero)
            buffer = BumpAllocInSegmentRaw(s_lastSegment, requestSize);
            if (buffer != null)
            {
                MemoryOp.MemSet((byte*)buffer, 0, (int)requestSize);
                return SetupTlab(ref ac, buffer, requestSize);
            }

            // Try slow path: walk segments, allocate new segment if needed
            buffer = AllocateObjectSlowRaw(requestSize);
            if (buffer != null)
            {
                MemoryOp.MemSet((byte*)buffer, 0, (int)requestSize);
                return SetupTlab(ref ac, buffer, requestSize);
            }

            // If full TlabSize failed but we only need `size`, try exact size
            if (requestSize > size)
            {
                buffer = AllocFromFreeListRaw(size);
                if (buffer != null)
                {
                    return SetupTlab(ref ac, buffer, size);
                }

                buffer = AllocateObjectSlowRaw(size);
                if (buffer != null)
                {
                    MemoryOp.MemSet((byte*)buffer, 0, (int)size);
                    return SetupTlab(ref ac, buffer, size);
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Sets up a TLAB from an allocated buffer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SetupTlab(ref AllocContext ac, void* buffer, uint bufferSize)
    {
        ac.AllocPtr = (byte*)buffer;
        ac.AllocLimit = (byte*)buffer + bufferSize;
        return true;
    }

    /// <summary>
    /// Returns a thread's TLAB to the GC heap. Stamps the unused gap as a FreeBlock
    /// and resets the allocation pointers.
    /// </summary>
    /// <param name="ac">The allocation context to return.</param>
    public static void ReturnAllocContext(ref AllocContext ac)
    {
        StampUnusedTlab(ref ac);
        ac.AllocPtr = null;
        ac.AllocLimit = null;
    }

    /// <summary>
    /// Returns all live threads' TLABs to the GC heap. Called at the start of GC collection.
    /// After this call, all threads have AllocPtr == AllocLimit == null and will refill on next alloc.
    /// </summary>
    internal static void ReturnAllAllocContexts()
    {
        if (CosmosFeatures.SchedulerEnabled)
        {
            SchedulerThread?[]? threads = SchedulerManager.Threads;
            if (threads != null)
            {
                int count = SchedulerManager.ThreadCount;
                for (int i = 0; i < threads.Length && count > 0; i++)
                {
                    SchedulerThread? thread = threads[i];
                    if (thread != null)
                    {
                        ReturnAllocContext(ref thread.AllocContext);
                        count--;
                    }
                }
            }
        }

        ReturnAllocContext(ref s_fallbackAllocContext);
    }

    /// <summary>
    /// Stamps the unused portion of a TLAB [AllocPtr, AllocLimit) as a FreeBlock
    /// and adds it to the free list so it can be reused.
    /// </summary>
    private static void StampUnusedTlab(ref AllocContext ac)
    {
        if (ac.AllocPtr == null || ac.AllocLimit == null)
        {
            return;
        }

        uint gap = (uint)(ac.AllocLimit - ac.AllocPtr);
        if (gap >= MinBlockSize)
        {
            FreeBlock* freeBlock = (FreeBlock*)ac.AllocPtr;
            freeBlock->MethodTable = s_freeMethodTable;
            freeBlock->Size = (int)gap;
            freeBlock->Next = null;
            AddToFreeList(freeBlock);
        }
        else if (gap > 0)
        {
            // Gap too small for a FreeBlock — zero it so sweep doesn't see
            // stale MethodTable pointers and break early.
            MemoryOp.MemSet(ac.AllocPtr, 0, (int)gap);
        }
    }
}
