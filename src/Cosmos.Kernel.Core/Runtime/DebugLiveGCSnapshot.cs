using System.Runtime;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Memory.GarbageCollector;

namespace Cosmos.Kernel.Core.Runtime;

/// <summary>
/// Heap-allocated GC stats snapshot buffer the kernel updates from the timer
/// tick. Mirrors <see cref="DebugLiveSnapshot"/> but for garbage collector
/// state. The Cosmos VS Code extension reads it over QEMU's QMP socket while
/// the guest is running, so the GC live view stays current without pausing.
///
/// Layout (little-endian, packed):
///   0:   u32 magic   = 0xC05D0002
///   4:   u32 version = 1
///   8:   u32 flags   (bit0 = GC initialized)
///   12:  u32 reserved
///   16:  u64 seq     (seqlock; even = stable, odd = writing)
///   24:  u64 heapSizeBytes
///   32:  u64 fragmentedBytes
///   40:  u64 totalCommittedBytes
///   48:  u64 totalAllocatedBytes
///   56:  u64 pinnedObjectsCount
///   64:  u32 collectionCount
///   68:  u32 totalObjectsFreed
///   72:  u64 memoryLoadBytes
///   80:  u64 gcSegmentSize
///   88:  u64 lastGCDurationTicks
///   96:  u32 lastGCPercentTimeInGC
///   100: u32 reserved2
///   104: u64 lastGen0SizeBefore
///   112: u64 lastGen0FragBefore
///   120: u64 lastGen0SizeAfter
///   128: u64 lastGen0FragAfter
///   = 136 bytes
/// </summary>
internal static unsafe class DebugLiveGCSnapshot
{
    private const uint Magic = 0xC05D0002u;
    private const uint Version = 1u;
    private const int BufferSize = 160;

    private static byte* s_buffer;

    internal static void Initialize()
    {
        if (s_buffer != null)
        {
            return;
        }
        s_buffer = (byte*)MemoryOp.Alloc(BufferSize);
        if (s_buffer == null)
        {
            return;
        }
        for (int i = 0; i < BufferSize; i++)
        {
            s_buffer[i] = 0;
        }
        *(uint*)(s_buffer + 0) = Magic;
        *(uint*)(s_buffer + 4) = Version;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Update()
    {
        byte* buf = s_buffer;
        if (buf == null)
        {
            return;
        }

        // Seqlock: bump to odd before writing, even after.
        ulong* seqPtr = (ulong*)(buf + 16);
        ulong seq = *seqPtr;
        *seqPtr = seq + 1;

        *(uint*)(buf + 8) = GarbageCollector.IsEnabled ? 1u : 0u;

        *(ulong*)(buf + 24) = GarbageCollector.GetHeapSizeBytes();
        *(ulong*)(buf + 32) = GarbageCollector.GetFragmentedBytes();
        *(ulong*)(buf + 40) = GarbageCollector.GetTotalCommittedBytes();
        *(ulong*)(buf + 48) = GarbageCollector.GetTotalAllocatedBytes();
        *(ulong*)(buf + 56) = GarbageCollector.GetPinnedObjectsCount();

        GarbageCollector.GetStats(out int totalCollections, out int totalObjectsFreed);
        *(uint*)(buf + 64) = (uint)totalCollections;
        *(uint*)(buf + 68) = (uint)totalObjectsFreed;

        *(ulong*)(buf + 72) = GarbageCollector.GetMemoryLoadBytes();
        *(ulong*)(buf + 80) = GarbageCollector.GetGCSegmentSizeBytes();

        *(ulong*)(buf + 104) = GarbageCollector.GetLastGenSizeBefore(0);
        *(ulong*)(buf + 112) = GarbageCollector.GetLastGenFragmentationBefore(0);
        *(ulong*)(buf + 120) = GarbageCollector.GetLastGenSizeAfter(0);
        *(ulong*)(buf + 128) = GarbageCollector.GetLastGenFragmentationAfter(0);

        // last-GC duration + %time-in-GC live behind internal fields; reuse
        // the public accessor for percent and skip duration ticks (no
        // accessor) — the host shows the percentage instead.
        *(uint*)(buf + 96) = (uint)GarbageCollector.GetLastGCPercentTimeInGC();

        // Publish: make seq even again.
        *seqPtr = seq + 2;
    }

    [RuntimeExport("CosmosDbg_GetGCSnapshotAddr")]
    internal static ulong GetSnapshotAddr()
    {
        return (ulong)s_buffer;
    }
}
