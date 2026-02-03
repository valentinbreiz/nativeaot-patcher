using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;

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

    /// <summary>
    /// Initialize the debug buffer.
    /// </summary>
    public static void Initialize()
    {
        if (s_initialized)
            return;

        // Get pointer to buffer in .cosmos_debug section
        s_debugBuffer = (DebugBuffer*)GetDebugBufferPointer();

        if (s_debugBuffer == null)
        {
            // Buffer not available - C file may not be linked
            return;
        }

        // Initialize header
        s_debugBuffer->Magic = 0x434F534D4F53; // "COSMOS"
        s_debugBuffer->Version = 1;
        s_debugBuffer->Timestamp = 0;
        s_debugBuffer->LimineEntryCount = 0;
        s_debugBuffer->RatSampleCount = 0;

        s_initialized = true;
    }

    /// <summary>
    /// Update the debug buffer with current memory state.
    /// This can be called anytime - the buffer is continuously readable via QEMU QMP.
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
        if (!s_initialized || s_debugBuffer == null || rat == null)
            return;

        s_debugBuffer->Timestamp++;

        // Update Limine memory map
        var limineResponse = Limine.MemoryMap.Response;
        if (limineResponse != null)
        {
            var entryCount = limineResponse->EntryCount;
            if (entryCount > MAX_LIMINE_ENTRIES)
                entryCount = MAX_LIMINE_ENTRIES;

            s_debugBuffer->LimineEntryCount = (uint)entryCount;

            for (ulong i = 0; i < entryCount; i++)
            {
                var entry = limineResponse->Entries[i];
                var idx = i * 3;
                s_debugBuffer->LimineEntries[idx + 0] = (ulong)entry->Base;
                s_debugBuffer->LimineEntries[idx + 1] = entry->Length;
                s_debugBuffer->LimineEntries[idx + 2] = (ulong)entry->Type;
            }
        }
        else
        {
            s_debugBuffer->LimineEntryCount = 0;
        }

        // Update page allocator state
        s_debugBuffer->RamStart = (ulong)ramStart;
        s_debugBuffer->HeapEnd = (ulong)heapEnd;
        s_debugBuffer->RatLocation = (ulong)ratLocation;
        s_debugBuffer->RamSize = ramSize;
        s_debugBuffer->TotalPageCount = totalPageCount;
        s_debugBuffer->FreePageCount = freePageCount;

        // Update RAT sample
        if (ratSampleCount > MAX_RAT_SAMPLE)
            ratSampleCount = MAX_RAT_SAMPLE;
        if (ratSampleCount > totalPageCount)
            ratSampleCount = (uint)totalPageCount;

        s_debugBuffer->RatSampleCount = ratSampleCount;

        for (uint i = 0; i < ratSampleCount; i++)
        {
            s_debugBuffer->RatData[i] = rat[i];
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
