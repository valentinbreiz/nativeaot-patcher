using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.Debug;

/// <summary>
/// Debug instrumentation buffer for memory/page allocator state.
/// This buffer is placed in the .cosmos_debug linker section via C code
/// and read by debugging tools via QEMU QMP without pausing execution.
/// </summary>
public static unsafe partial class MemoryDebug
{
    private const int MAX_LIMINE_ENTRIES = 64;
    private const int MAX_RAT_SAMPLE = 1000;

    /// <summary>
    /// Debug buffer structure - matches TypeScript parsing in VS Code extension.
    /// Layout must match exactly with what the extension expects.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DebugBuffer
    {
        // Magic number for validation (0x434F534D4F53 = "COSMOS" in hex)
        public ulong Magic;

        // Version for compatibility
        public uint Version;

        // Timestamp (incremented each update)
        public ulong Timestamp;

        // Limine Memory Map
        public uint LimineEntryCount;
        public fixed ulong LimineEntries[MAX_LIMINE_ENTRIES * 3]; // base, length, type for each entry

        // Page Allocator State
        public ulong RamStart;
        public ulong HeapEnd;
        public ulong RatLocation;
        public ulong RamSize;
        public ulong TotalPageCount;
        public ulong FreePageCount;

        // RAT Sample
        public uint RatSampleCount;
        public fixed byte RatData[MAX_RAT_SAMPLE];
    }

    // Import C functions that access the buffer in .cosmos_debug section
    [LibraryImport("*", EntryPoint = "__cosmos_get_debug_buffer")]
    private static partial void* GetDebugBufferPointer();

    [LibraryImport("*", EntryPoint = "__cosmos_get_debug_buffer_size")]
    private static partial ulong GetDebugBufferSize();

    private static DebugBuffer* s_debugBuffer;
    private static bool s_initialized = false;
    private static DebugBuffer* s_ivshmemBuffer;
    private static ISharedMemoryDevice? s_sharedMemory;

    /// <summary>
    /// Initialize the debug buffer using ELF section.
    /// Call SetSharedMemory() later if a shared memory device becomes available.
    /// </summary>
    public static void Initialize()
    {
        if (s_initialized)
            return;

        // Initialize ELF section buffer
        s_debugBuffer = (DebugBuffer*)GetDebugBufferPointer();

        if (s_debugBuffer != null)
        {
            // Initialize ELF buffer
            s_debugBuffer->Magic = 0x434F534D4F53; // "COSMOS"
            s_debugBuffer->Version = 1;
            s_debugBuffer->Timestamp = 0;
            s_debugBuffer->LimineEntryCount = 0;
            s_debugBuffer->RatSampleCount = 0;
        }

        s_initialized = true;
    }

    /// <summary>
    /// Set the shared memory device for zero-pause streaming.
    /// Called by the kernel after HAL initialization if a shared memory device is available.
    /// </summary>
    public static void SetSharedMemory(ISharedMemoryDevice device)
    {
        s_sharedMemory = device;

        var shmem = device.GetSharedMemory();
        var size = device.GetSharedMemorySize();

        if (shmem != 0 && size >= (ulong)sizeof(DebugBuffer))
        {
            s_ivshmemBuffer = (DebugBuffer*)(void*)shmem;

            // Initialize shared memory buffer
            s_ivshmemBuffer->Magic = 0x434F534D4F53; // "COSMOS"
            s_ivshmemBuffer->Version = 1;
            s_ivshmemBuffer->Timestamp = 0;
            s_ivshmemBuffer->LimineEntryCount = 0;
            s_ivshmemBuffer->RatSampleCount = 0;
        }
    }

    /// <summary>
    /// Update the debug buffer with current memory state.
    /// Writes to ivshmem for zero-pause streaming (preferred).
    /// Also updates ELF section buffer if available (fallback).
    /// </summary>
    public static void UpdateMemoryState(
        byte* ramStart,
        byte* heapEnd,
        byte* ratLocation,
        ulong ramSize,
        ulong totalPageCount,
        ulong freePageCount,
        byte* rat,
        uint ratSampleCount = MAX_RAT_SAMPLE)
    {
        if (!s_initialized || rat == null)
            return;

        // Choose which buffer to update (prefer ivshmem)
        DebugBuffer* targetBuffer = s_ivshmemBuffer != null ? s_ivshmemBuffer : s_debugBuffer;

        if (targetBuffer == null)
            return;

        targetBuffer->Timestamp++;

        // Update Limine memory map
        var limineResponse = Limine.MemoryMap.Response;
        if (limineResponse != null)
        {
            var entryCount = limineResponse->EntryCount;
            if (entryCount > MAX_LIMINE_ENTRIES)
                entryCount = MAX_LIMINE_ENTRIES;

            targetBuffer->LimineEntryCount = (uint)entryCount;

            for (ulong i = 0; i < entryCount; i++)
            {
                var entry = limineResponse->Entries[i];
                var idx = i * 3;
                targetBuffer->LimineEntries[idx + 0] = (ulong)entry->Base;
                targetBuffer->LimineEntries[idx + 1] = entry->Length;
                targetBuffer->LimineEntries[idx + 2] = (ulong)entry->Type;
            }
        }
        else
        {
            targetBuffer->LimineEntryCount = 0;
        }

        // Update page allocator state
        targetBuffer->RamStart = (ulong)ramStart;
        targetBuffer->HeapEnd = (ulong)heapEnd;
        targetBuffer->RatLocation = (ulong)ratLocation;
        targetBuffer->RamSize = ramSize;
        targetBuffer->TotalPageCount = totalPageCount;
        targetBuffer->FreePageCount = freePageCount;

        // Update RAT sample
        if (ratSampleCount > MAX_RAT_SAMPLE)
            ratSampleCount = MAX_RAT_SAMPLE;
        if (ratSampleCount > totalPageCount)
            ratSampleCount = (uint)totalPageCount;

        targetBuffer->RatSampleCount = ratSampleCount;

        for (uint i = 0; i < ratSampleCount; i++)
        {
            targetBuffer->RatData[i] = rat[i];
        }
    }

    /// <summary>
    /// Get the address of the debug buffer.
    /// This is used by the extension to locate the buffer in QEMU memory.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void* GetDebugBufferAddress()
    {
        return s_debugBuffer;
    }
}
