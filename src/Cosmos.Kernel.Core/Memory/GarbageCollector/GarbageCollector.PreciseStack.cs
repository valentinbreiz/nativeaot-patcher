// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.Bridge;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.Core.Runtime.GcInfo;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

#pragma warning disable CS8500

/// <summary>
/// Precise GC stack scanning for the GC-triggering thread, driven by NativeAOT GCInfo
/// (issue #346, epic #348 phases 2–3). Walks the thread's managed frames one at a time using the
/// CFI unwinder from <see cref="ExceptionHelper"/>, decoding each method's GCInfo at the matching
/// instruction pointer — including exception-funclet frames (catch / filter / finally bodies),
/// which share their main method's slot table — and reporting only the slots it names. CFI-described
/// asm trampolines (the funclet-invoke stubs) carry no GCInfo of their own; the walk steps through
/// them without reporting anything (their frame contents are reported by the managed frames on
/// either side, via register save locations, and the in-flight exception object by the funclet's
/// <c>ex</c> slot / the dispatcher's locals). Falls back to a conservative scan of the rest of the
/// stack only when the walk leaves everything it can describe (asm entry stubs / native imports /
/// IRQ entry — no CFI at all) or a frame's slot table is too large to decode.
/// </summary>
public static unsafe partial class GarbageCollector
{
    private const int MaxPreciseFrames = 256;

    private enum FrameResult
    {
        Continue,
        Stop,
    }

    /// <summary>
    /// Precisely scans the GC-triggering (current) thread. Captures this frame's register context
    /// via <see cref="ContextSwitchNative.CaptureRegDisplay"/> and walks up from there.
    /// <paramref name="threadStackEnd"/> bounds the conservative fallback tail (top-of-stack address).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void PreciseScanCurrentThread(nuint threadStackEnd)
    {
        REGDISPLAY rd = default;
        nuint ip = ContextSwitchNative.CaptureRegDisplay(&rd);
        PreciseScanFrameChain(&rd, ip, threadStackEnd);
    }

    /// <summary>
    /// Walks managed frames starting from <paramref name="rd0"/> / <paramref name="startIp"/>,
    /// precisely reporting GC slots per frame and CFI-unwinding to the caller until the walk leaves
    /// managed code or hits a bail-out condition.
    /// </summary>
    private static void PreciseScanFrameChain(REGDISPLAY* rd0, nuint startIp, nuint threadStackEnd)
    {
        nuint ip = startIp;

        // Frame 0 — the captured frame; its REGDISPLAY came straight from the capture stub.
        if (RunFrame(rd0, ip, threadStackEnd) == FrameResult.Stop)
        {
            return;
        }

        UnwindState st = default;
        ExceptionHelper.SeedUnwindStateFromRegDisplay(ref st, rd0, ip);

        nuint prevSp = rd0->SP;
        for (int i = 0; i < MaxPreciseFrames; i++)
        {
            if (!ExceptionHelper.UnwindOneFrameWithCFI(ref st, ip))
            {
                // No CFI for this IP — treat the rest of the stack conservatively and stop.
                if (prevSp != 0 && prevSp < threadStackEnd)
                {
                    ScanMemoryRange((nint*)prevSp, (nint*)threadStackEnd);
                }
                return;
            }

#if ARCH_ARM64
            nuint sp = st.SP;
#else
            nuint sp = st.RSP;
#endif
            ip = st.ReturnAddress;

            // Stack grows down: each older frame has a strictly higher SP. Anything else is a sign
            // the unwind produced garbage — bail rather than chase it.
            if (ip == 0 || sp == 0 || sp <= prevSp || sp >= threadStackEnd)
            {
                return;
            }
            prevSp = sp;

            REGDISPLAY rd = default;
            ExceptionHelper.ProjectRegDisplay(ref st, &rd);
            if (RunFrame(&rd, ip, threadStackEnd) == FrameResult.Stop)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Reports the GC roots live in one frame. Returns <see cref="FrameResult.Stop"/> (after a
    /// conservative scan of the remaining stack) when the frame has no usable GCInfo.
    /// </summary>
    private static FrameResult RunFrame(REGDISPLAY* rd, nuint ip, nuint threadStackEnd)
    {
        nuint sp = rd->SP;
        if (sp == 0 || sp >= threadStackEnd)
        {
            return FrameResult.Stop;
        }

        if (!MethodGcInfoLookup.TryGetMethodGcInfo(ip, out MethodGcInfoLookup.MethodGcInfo mi) || mi.GcInfo == null)
        {
            // No GCInfo for this IP. A hand-written asm trampoline (RhpCallCatchFunclet /
            // RhpCallFilterFunclet) still carries .cfi directives — it just has no .dotnet_eh_table
            // LSDA. Its own frame holds no managed references the managed frames on either side
            // don't already report via their register save locations (and the in-flight exception
            // object is reported by the funclet's `ex` slot above it and the dispatcher's locals
            // below it), so step through it and keep the precise walk going. Only when there is no
            // CFI at all (IRQ entry, the bootloader) do we conservatively scan the rest and stop.
            if (MethodGcInfoLookup.TryGetMethodCFI(ip, out _))
            {
                return FrameResult.Continue;
            }
            ScanMemoryRange((nint*)sp, (nint*)threadStackEnd);
            return FrameResult.Stop;
        }

        // Exception funclet frames (catch / filter / finally bodies) carry no GCInfo of their own —
        // they share the main method's slot table, indexed at a synthetic code offset past the main
        // body (MethodGcInfoLookup already resolved mi.CodeOffset against the main method start;
        // issue #227). The funclet runs on the establisher (main) frame's RBP — RhpCallCatchFunclet
        // restores it and the funclet sets up no RBP frame of its own — so the main method's
        // RBP-relative slots (every stack slot, for an EH-bearing method, which always keeps a frame
        // register) resolve correctly against this frame's REGDISPLAY. A filter runs mid-throw, so
        // the main method's *untracked* slots may be stale and must not be reported
        // (NoReportUntracked); catch / finally funclets re-report untracked slots — the conservative
        // tail (and, later, the parent frame's own precise scan) reports them again, but mark is
        // idempotent so the duplicate is harmless. This mirrors UnixNativeCodeManager::EnumGcRefs.
        CodeManagerFlags flags = (mi.IsFunclet && mi.IsFilter) ? CodeManagerFlags.NoReportUntracked : CodeManagerFlags.None;

        GcInfoDecoder decoder = new GcInfoDecoder(mi.GcInfo, GcInfoEncoding.GCINFO_VERSION, GcInfoDecoderFlags.DECODE_GC_LIFETIMES, mi.CodeOffset);
        bool fit = decoder.EnumerateLiveSlots(rd, reportScratchSlots: false, flags, &PreciseRootTrampoline, null);
        if (!fit)
        {
            // Slot table overflowed the decoder's fixed buffer — fall back conservatively for the rest.
            ScanMemoryRange((nint*)sp, (nint*)threadStackEnd);
            return FrameResult.Stop;
        }

        return FrameResult.Continue;
    }

    /// <summary>
    /// <see cref="GcInfoDecoder.EnumerateLiveSlots"/> callback: marks each reported root via
    /// <see cref="TryMarkRoot"/>. <paramref name="pObjRef"/> is null for scratch registers Cosmos's
    /// REGDISPLAY does not track — skipped. Interior pointers (GC_CALL_INTERIOR) are passed through
    /// as-is in phase 2: <see cref="TryMarkRoot"/>'s MethodTable check drops a non-header byref, the
    /// same hole the conservative scanner has today. Pinned (GC_CALL_PINNED) is a no-op for the
    /// non-moving mark phase.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoOptimization)]
    private static void PreciseRootTrampoline(void* ctx, nuint* pObjRef, uint gcRefFlags)
    {
        if (pObjRef == null)
        {
            return;
        }
        TryMarkRoot((nint)(*pObjRef));
    }
}
