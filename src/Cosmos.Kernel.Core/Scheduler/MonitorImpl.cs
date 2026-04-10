using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Provides Monitor.Enter/Exit semantics using a hash table keyed by object address.
/// Supports reentrancy (same thread can acquire the same lock multiple times).
/// Used as the backing implementation for the C# lock keyword.
/// </summary>
public static unsafe class MonitorImpl
{
    private const int TableSize = 256;
    private const int TableMask = TableSize - 1;
    private const nint Tombstone = -1;

    private struct MonitorEntry
    {
        public nint ObjectAddress; // 0 = empty, -1 = tombstone
        public uint OwnerThreadId;
        public int RecursionCount;
    }

    private static MonitorEntry[] s_table = null!;
    private static SpinLock s_tableLock;
    private static bool s_initialized;

    private static void EnsureInitialized()
    {
        if (s_initialized)
        {
            return;
        }

        s_table = new MonitorEntry[TableSize];
        s_initialized = true;
    }

    /// <summary>
    /// Acquires the monitor for the specified object. Blocks until acquired.
    /// Supports reentrancy.
    /// </summary>
    public static void Enter(object obj)
    {
        if (!CosmosFeatures.SchedulerEnabled || !SchedulerManager.Enabled)
        {
            return;
        }

        EnsureInitialized();

        nint addr = GetObjectAddress(obj);
        uint threadId = GetCurrentThreadId();

        while (true)
        {
            s_tableLock.Acquire();

            int slot = FindOrCreateSlot(addr);
            if (slot < 0)
            {
                s_tableLock.Release();
                Serial.WriteString("[MonitorImpl] PANIC: lock table full\n");
                return;
            }

            ref MonitorEntry entry = ref s_table[slot];

            if (entry.ObjectAddress == 0 || entry.ObjectAddress == Tombstone)
            {
                // Empty or tombstone slot: claim it
                entry.ObjectAddress = addr;
                entry.OwnerThreadId = threadId;
                entry.RecursionCount = 1;
                s_tableLock.Release();
                return;
            }

            if (entry.OwnerThreadId == threadId)
            {
                // Reentrant acquisition
                entry.RecursionCount++;
                s_tableLock.Release();
                return;
            }

            // Owned by another thread: release table lock, spin
            s_tableLock.Release();

            // Brief spin to let the scheduler preempt
            for (int i = 0; i < 100; i++) { }
        }
    }

    /// <summary>
    /// Releases the monitor for the specified object.
    /// </summary>
    public static void Exit(object obj)
    {
        if (!CosmosFeatures.SchedulerEnabled || !SchedulerManager.Enabled)
        {
            return;
        }

        if (!s_initialized)
        {
            return;
        }

        nint addr = GetObjectAddress(obj);

        s_tableLock.Acquire();

        int slot = FindSlot(addr);
        if (slot < 0)
        {
            s_tableLock.Release();
            return;
        }

        ref MonitorEntry entry = ref s_table[slot];

        entry.RecursionCount--;
        if (entry.RecursionCount == 0)
        {
            entry.ObjectAddress = Tombstone;
            entry.OwnerThreadId = 0;
        }

        s_tableLock.Release();
    }

    /// <summary>
    /// Attempts to acquire the monitor without blocking.
    /// </summary>
    public static bool TryEnter(object obj)
    {
        if (!CosmosFeatures.SchedulerEnabled || !SchedulerManager.Enabled)
        {
            return true;
        }

        EnsureInitialized();

        nint addr = GetObjectAddress(obj);
        uint threadId = GetCurrentThreadId();

        s_tableLock.Acquire();

        int slot = FindOrCreateSlot(addr);
        if (slot < 0)
        {
            s_tableLock.Release();
            return false;
        }

        ref MonitorEntry entry = ref s_table[slot];

        if (entry.ObjectAddress == 0 || entry.ObjectAddress == Tombstone)
        {
            entry.ObjectAddress = addr;
            entry.OwnerThreadId = threadId;
            entry.RecursionCount = 1;
            s_tableLock.Release();
            return true;
        }

        if (entry.OwnerThreadId == threadId)
        {
            entry.RecursionCount++;
            s_tableLock.Release();
            return true;
        }

        s_tableLock.Release();
        return false;
    }

    /// <summary>
    /// Checks if the current thread holds the monitor for the specified object.
    /// </summary>
    public static bool IsEntered(object obj)
    {
        if (!CosmosFeatures.SchedulerEnabled || !SchedulerManager.Enabled)
        {
            return false;
        }

        if (!s_initialized)
        {
            return false;
        }

        nint addr = GetObjectAddress(obj);
        uint threadId = GetCurrentThreadId();

        s_tableLock.Acquire();
        int slot = FindSlot(addr);
        bool result = slot >= 0 && s_table[slot].OwnerThreadId == threadId;
        s_tableLock.Release();

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nint GetObjectAddress(object obj)
    {
        return *(nint*)Unsafe.AsPointer(ref obj);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetCurrentThreadId()
    {
        PerCpuState cpuState = SchedulerManager.GetCpuState(0);
        return cpuState.CurrentThread?.Id ?? 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HashAddress(nint address)
    {
        return (int)((uint)address * 2654435761u) & TableMask;
    }

    /// <summary>
    /// Finds the slot for an existing entry or the first available slot.
    /// Returns -1 if the table is full and the address is not found.
    /// </summary>
    private static int FindOrCreateSlot(nint address)
    {
        int start = HashAddress(address);
        int firstAvailable = -1;

        for (int i = 0; i < TableSize; i++)
        {
            int idx = (start + i) & TableMask;
            nint slotAddr = s_table[idx].ObjectAddress;

            if (slotAddr == address)
            {
                return idx;
            }

            if (slotAddr == Tombstone && firstAvailable < 0)
            {
                firstAvailable = idx;
            }

            if (slotAddr == 0)
            {
                return firstAvailable >= 0 ? firstAvailable : idx;
            }
        }

        return firstAvailable;
    }

    /// <summary>
    /// Finds the slot for an existing entry. Returns -1 if not found.
    /// </summary>
    private static int FindSlot(nint address)
    {
        int start = HashAddress(address);

        for (int i = 0; i < TableSize; i++)
        {
            int idx = (start + i) & TableMask;
            nint slotAddr = s_table[idx].ObjectAddress;

            if (slotAddr == address)
            {
                return idx;
            }

            if (slotAddr == 0)
            {
                return -1;
            }

            // Tombstone: continue probing
        }

        return -1;
    }
}
