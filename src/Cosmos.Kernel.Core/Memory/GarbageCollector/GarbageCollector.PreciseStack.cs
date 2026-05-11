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
/// which share their main method's slot table — and reporting only the slots it names. Falls back
/// to a conservative scan of the rest of the stack when the walk leaves managed code (asm entry
/// stubs / native imports / IRQ entry — including any funclet trampoline without CFI) or a frame's
/// slot table is too large to decode.
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
            // Walked off the managed range (asm entry stub, native import, IRQ entry) — the rest of
            // this thread's stack has no GCInfo; scan it conservatively and stop.
            ScanMemoryRange((nint*)sp, (nint*)threadStackEnd);
            return FrameResult.Stop;
        }

        // Exception funclet frames (catch / filter / finally bodies) share the main method's slot
        // table; MethodGcInfoLookup already resolved mi.CodeOffset against the main method start
        // (issue #227, phase 3). A filter runs mid-throw, so the main method's *untracked* slots
        // are stale and must not be reported — NoReportUntracked. Catch / finally funclets keep the
        // default: they re-report untracked slots, which the parent method's frame will report
        // again later, but mark is idempotent so the duplicate is harmless. The slot offsets resolve
        // against the funclet's own frame, so the REGDISPLAY from this frame's CFI walk is correct.
        CodeManagerFlags flags = CodeManagerFlags.None;
        if (mi.IsFunclet && mi.IsFilter)
        {
            flags = CodeManagerFlags.NoReportUntracked;
        }

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
