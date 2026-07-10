using System;
using System.Drawing;
using System.Threading;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using DevKernel.Graphics;
using KernelGc = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;
using KernelHeap = Cosmos.Kernel.Core.Memory.Heap.Heap;

namespace DevKernel.Diagnostics;

/// <summary>
/// Full-screen overlay behind <c>gc</c>: the runtime's <see cref="GCMemoryInfo"/>
/// counters refreshed every frame, plus gen0 before/after deltas sampled around
/// a forced collection. Exits on ESC.
/// </summary>
internal static class GcMonitor
{
    /// <summary>Field alignment (chars) for byte-count values.</summary>
    private const int ValueAlignment = 15;

    /// <summary>Field alignment (chars) for the GC time percentage.</summary>
    private const int PercentAlignment = 3;

    /// <summary>Field alignment (chars) for collection/object counters.</summary>
    private const int CountAlignment = 6;

    /// <summary>Delay (ms) between refreshes of the overlay.</summary>
    private const int FrameDelayMs = 250;

    /// <summary>Frame interval at which the overlay collects and samples gen0 stats.</summary>
    private const int CollectFrameInterval = 50;

    /// <summary>Index of generation 0 in <see cref="GCMemoryInfo.GenerationInfo"/>, the generation sampled here.</summary>
    private const int Gen0Index = 0;

    /// <summary>Runs the overlay loop until ESC is pressed, then clears the console.</summary>
    public static void Run()
    {
        PCScreenFont font = PCScreenFont.DefaultFont;
        Canvas canvas = Canvas.GetFullScreen();

        uint frames = 0;
        long sizeBefore = 0;
        long sizeAfter = 0;
        long sizeDelta = 0;
        long maxDeltaSize = 0;
        long fragBefore = 0;
        long fragAfter = 0;
        long commitedMax = 0;

        int x = OverlayLayout.TextMarginPx;
        int lineHeight = OverlayLayout.LineHeight(font);

        while (!Console.KeyAvailable || Console.ReadKey(true).Key != ConsoleKey.Escape)
        {
            unchecked
            {
                frames++;
            }

            canvas.Clear(Color.Black);

            GCMemoryInfo info = GC.GetGCMemoryInfo();
            commitedMax = Math.Max(commitedMax, info.TotalCommittedBytes);

            int rowY = OverlayLayout.TextMarginPx;
            canvas.DrawString($"GC Info ({frames})", font, Color.Cyan, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Size values are in bytes, ESC to exit;", font, Color.Cyan, x, rowY);
            rowY += lineHeight;

            canvas.DrawString($"RamSize         : {PageAllocator.RamSize,ValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"HeapSize        : {info.HeapSizeBytes,ValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Fragmented      : {info.FragmentedBytes,ValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Committed       : {info.TotalCommittedBytes,ValueAlignment}; max size  : {commitedMax,ValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Promoted        : {info.PromotedBytes,ValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Pinned          : {info.PinnedObjectsCount,ValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Collections     : {info.Index,ValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Condemned gen   : {info.Generation,ValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;

            // Last sampled generation, before/after the forced collection below.
            canvas.DrawString($"Gen0 size before: {sizeBefore,ValueAlignment}; size after: {sizeAfter,ValueAlignment}", font, Color.Yellow, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Gen0 size delta : {sizeDelta,ValueAlignment}; max size  : {maxDeltaSize,ValueAlignment}", font, Color.Yellow, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Frag size before: {fragBefore,ValueAlignment}; size after: {fragAfter,ValueAlignment}", font, Color.Yellow, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Frag size delta : {fragAfter - fragBefore,ValueAlignment}", font, Color.Yellow, x, rowY);
            rowY += lineHeight;

            int pct = KernelGc.GetLastGCPercentTimeInGC();
            KernelGc.GetStats(out int totalCollections, out int totalObjectsFreed);
            canvas.DrawString(
                $"Last GC % time in GC: {pct,PercentAlignment}%, Collections: {totalCollections,CountAlignment}, Objects Freed: {totalObjectsFreed,CountAlignment}",
                font,
                Color.Green,
                x,
                rowY);

            if (frames % CollectFrameInterval == 0)
            {
                KernelHeap.Collect();
                info = GC.GetGCMemoryInfo();
                sizeBefore = info.GenerationInfo[Gen0Index].SizeBeforeBytes;
                sizeAfter = info.GenerationInfo[Gen0Index].SizeAfterBytes;
                fragBefore = info.GenerationInfo[Gen0Index].FragmentationBeforeBytes;
                fragAfter = info.GenerationInfo[Gen0Index].FragmentationAfterBytes;

                sizeDelta = sizeBefore - sizeAfter;
                maxDeltaSize = Math.Max(maxDeltaSize, sizeDelta);
            }

            canvas.Display();
            Thread.Sleep(FrameDelayMs);
        }

        Console.Clear();
    }
}
