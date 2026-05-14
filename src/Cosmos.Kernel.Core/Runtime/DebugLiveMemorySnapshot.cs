using System.Runtime;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.Memory;

namespace Cosmos.Kernel.Core.Runtime;

/// <summary>
/// Heap-allocated memory-manager snapshot buffer the kernel updates from
/// the timer tick. Mirrors <see cref="DebugLiveGCSnapshot"/> but reports
/// the Cosmos page allocator / RAT state — heap range, free-page count
/// and per-PageType page counts — so the VS Code extension can show a
/// live "Memory Manager" view via QMP without pausing the guest.
///
/// Layout (little-endian, packed):
///   0:   u32 magic   = 0xC05D0003
///   4:   u32 version = 1
///   8:   u32 flags   (bit0 = page allocator initialized)
///   12:  u32 pageSize
///   16:  u64 seq     (seqlock; even = stable, odd = writing)
///   24:  u64 ramStart
///   32:  u64 ramSize
///   40:  u64 totalPageCount
///   48:  u64 freePageCount
///   56:  u64 ratAddress
///   64:  u64 heapEnd
///   72:  u64 pages_empty
///   80:  u64 pages_gcheap
///   88:  u64 pages_heapsmall
///   96:  u64 pages_heapmedium
///   104: u64 pages_heaplarge
///   112: u64 pages_unmanaged
///   120: u64 pages_pagedirectory
///   128: u64 pages_pageallocator
///   136: u64 pages_smt
///   144: u64 pages_extension
///   152: u64 pages_unknown
///   = 160 bytes
/// </summary>
internal static unsafe class DebugLiveMemorySnapshot
{
    private const uint Magic = 0xC05D0003u;
    private const uint Version = 1u;
    private const int BufferSize = 192;

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
        *(uint*)(s_buffer + 12) = (uint)PageAllocator.PageSize;
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

        bool initialized = PageAllocator.TotalPageCount > 0 && PageAllocator.RamStart != null;
        *(uint*)(buf + 8) = initialized ? 1u : 0u;
        *(uint*)(buf + 12) = (uint)PageAllocator.PageSize;

        ulong ramStart = (ulong)PageAllocator.RamStart;
        ulong ramSize = PageAllocator.RamSize;

        *(ulong*)(buf + 24) = ramStart;
        *(ulong*)(buf + 32) = ramSize;
        *(ulong*)(buf + 40) = PageAllocator.TotalPageCount;
        *(ulong*)(buf + 48) = PageAllocator.FreePageCount;
        *(ulong*)(buf + 56) = PageAllocator.RatAddress;
        *(ulong*)(buf + 64) = ramStart + ramSize;

        PageAllocator.GetPageCountsByType(
            out ulong empty,
            out ulong gcHeap,
            out ulong heapSmall,
            out ulong heapMedium,
            out ulong heapLarge,
            out ulong unmanaged,
            out ulong pageDirectory,
            out ulong pageAllocator,
            out ulong smt,
            out ulong extension,
            out ulong unknown);

        *(ulong*)(buf + 72) = empty;
        *(ulong*)(buf + 80) = gcHeap;
        *(ulong*)(buf + 88) = heapSmall;
        *(ulong*)(buf + 96) = heapMedium;
        *(ulong*)(buf + 104) = heapLarge;
        *(ulong*)(buf + 112) = unmanaged;
        *(ulong*)(buf + 120) = pageDirectory;
        *(ulong*)(buf + 128) = pageAllocator;
        *(ulong*)(buf + 136) = smt;
        *(ulong*)(buf + 144) = extension;
        *(ulong*)(buf + 152) = unknown;

        // Publish: make seq even again.
        *seqPtr = seq + 2;
    }

    [RuntimeExport("CosmosDbg_GetMemorySnapshotAddr")]
    internal static ulong GetSnapshotAddr()
    {
        return (ulong)s_buffer;
    }
}
