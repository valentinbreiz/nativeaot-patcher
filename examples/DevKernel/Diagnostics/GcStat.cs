using System;
using System.Drawing;
using System.Threading;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using DevKernel.Diagnostics.Charting;
using DevKernel.Graphics;
using KernelGc = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;
using KernelHeap = Cosmos.Kernel.Core.Memory.Heap.Heap;

namespace DevKernel.Diagnostics;

// Full-screen GC/memory monitor: counters grouped in labelled sections, a chart of
// committed/heap/fragmented bytes over time, and a marker on every frame during which
// the collector ran.
//
// The two upper sections deliberately come from different sources, because they answer
// different questions:
//
//   LIVE HEAP        walks the heap right now, through the GarbageCollector getters.
//   LAST COLLECTION  reads GCMemoryInfo, which describes the heap as of the last GC.
//
// Watching them side by side is instructive: fragmentation falls back to almost nothing
// between two collections, because refilling a TLAB drains the very free lists the live
// reading sums, while the recorded value stays put until the next collection.
//
// A collection is forced every CollectEveryFrames frames so generation 0 has something to
// report; collections triggered by allocation pressure are marked just the same.
//
// Exits on ESC.
internal static class GcStat
{
    // The kernel collector's own counters, read in one go so a frame stays coherent.
    private readonly struct CollectorStats
    {
        private CollectorStats(int percentTimeInGc, int collections, int objectsFreed)
        {
            PercentTimeInGc = percentTimeInGc;
            Collections = collections;
            ObjectsFreed = objectsFreed;
        }

        public int PercentTimeInGc { get; }

        public int Collections { get; }

        public int ObjectsFreed { get; }

        public static CollectorStats Read()
        {
            int percentTimeInGc = KernelGc.GetLastGCPercentTimeInGC();
            KernelGc.GetStats(out int collections, out int objectsFreed);

            return new CollectorStats(percentTimeInGc, collections, objectsFreed);
        }
    }

    private const int FrameDelayMs = 250;
    private const int CollectEveryFrames = 50;

    private const int HistorySize = 120;
    private const int ChartHeightCapPx = 180;

    private const int Gen0 = 0;

    private const int LabelChars = 12;
    private const int ValueChars = 12;
    private const int IndentChars = 2;
    private const int ColumnGapChars = 2;

    // Distance between the start of the left column and the start of the right one.
    private const int ColumnChars = LabelChars + ValueChars + ColumnGapChars;

    private static readonly Color s_sectionColor = Color.DarkGray;
    private static readonly Color s_liveColor = Color.White;
    private static readonly Color s_collectionColor = Color.Yellow;
    private static readonly Color s_collectorColor = Color.LimeGreen;

    private static readonly ChartSeries s_committedSeries =
        new ChartSeries("committed", Color.LimeGreen, HistorySize);

    private static readonly ChartSeries s_heapSeries =
        new ChartSeries("heap", Color.Cyan, HistorySize);

    private static readonly ChartSeries s_fragmentedSeries =
        new ChartSeries("fragmented", Color.Orange, HistorySize);

    private static readonly ChartSeries[] s_series = [s_committedSeries, s_heapSeries, s_fragmentedSeries];

    private static readonly ChartMarkers s_collectionMarkers =
        new ChartMarkers("GC", Color.Magenta, HistorySize);

    private static readonly ChartMarkers[] s_markers = [s_collectionMarkers];

    public static void Run()
    {
        Canvas canvas = Canvas.GetFullScreen();
        PCScreenFont font = PCScreenFont.DefaultFont;

        ClearHistory();

        ulong peakCommitted = 0;
        int lastCollectionCount = -1;
        uint frames = 0;

        while (!EscapePressed())
        {
            if (frames % CollectEveryFrames == 0)
            {
                KernelHeap.Collect();
            }

            KernelGc.SimpleMemoryInfo live = KernelGc.GetSimpleMemoryInfo();
            GCMemoryInfo recorded = GC.GetGCMemoryInfo();
            CollectorStats collector = CollectorStats.Read();

            if (live.TotalCommittedBytes > peakCommitted)
            {
                peakCommitted = live.TotalCommittedBytes;
            }

            s_committedSeries.Add(live.TotalCommittedBytes);
            s_heapSeries.Add(live.HeapSizeBytes);
            s_fragmentedSeries.Add(live.FragmentedBytes);

            s_collectionMarkers.Add(lastCollectionCount >= 0 && collector.Collections != lastCollectionCount);
            lastCollectionCount = collector.Collections;

            Render(canvas, font, live, recorded, collector, peakCommitted);

            canvas.Display();
            frames++;
            Thread.Sleep(FrameDelayMs);
        }

        Console.Clear();
    }

    private static void ClearHistory()
    {
        foreach (ChartSeries series in s_series)
        {
            series.Clear();
        }

        s_collectionMarkers.Clear();
    }

    private static bool EscapePressed()
    {
        return Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape;
    }

    private static void Render(
        Canvas canvas,
        PCScreenFont font,
        KernelGc.SimpleMemoryInfo live,
        GCMemoryInfo recorded,
        CollectorStats collector,
        ulong peakCommitted)
    {
        int x = OverlayLayout.TextMarginPx;
        int row = OverlayLayout.TextMarginPx;
        int lineHeight = OverlayLayout.LineHeight(font);

        canvas.Clear(Color.Black);

        TextRenderer.DrawTruncated(
            canvas, font, "GC / Memory Monitor - ESC to exit", Color.LightGray, x, row, canvas.Width - x * 2);
        row += lineHeight * OverlayLayout.SectionBreakRowCount;

        row = DrawLiveSection(canvas, font, x, row, live, peakCommitted);
        row = DrawCollectionSection(canvas, font, x, row, recorded);
        row = DrawCollectorSection(canvas, font, x, row, collector);

        DrawMemoryChart(canvas, font, x, row, canvas.Width - x);
    }

    // Heap as it stands right now, recomputed on every frame.
    private static int DrawLiveSection(
        Canvas canvas,
        PCScreenFont font,
        int x,
        int y,
        KernelGc.SimpleMemoryInfo live,
        ulong peakCommitted)
    {
        y = DrawSectionTitle(canvas, font, x, y, "LIVE HEAP");

        y = DrawRow(canvas, font, x, y, s_liveColor,
            "used", ByteFormat.Short((long)live.HeapSizeBytes),
            "fragmented", ByteFormat.Short((long)live.FragmentedBytes));

        y = DrawRow(canvas, font, x, y, s_liveColor,
            "committed", ByteFormat.Short((long)live.TotalCommittedBytes),
            "peak", ByteFormat.Short((long)peakCommitted));

        y = DrawRow(canvas, font, x, y, s_liveColor,
            "memory load", ByteFormat.Short((long)live.MemoryLoadBytes),
            "installed", ByteFormat.Short((long)PageAllocator.RamSize));

        y = DrawRow(canvas, font, x, y, s_liveColor,
            "pinned", live.PinnedObjectsCount.ToString(),
            "collections", live.CollectionIndex.ToString());

        return y + OverlayLayout.LineHeight(font);
    }

    // Generation 0 around the last collection, as reported by GCMemoryInfo.
    private static int DrawCollectionSection(
        Canvas canvas,
        PCScreenFont font,
        int x,
        int y,
        GCMemoryInfo recorded)
    {
        y = DrawSectionTitle(canvas, font, x, y, "LAST COLLECTION (GEN 0)");

        if (recorded.GenerationInfo.Length <= Gen0)
        {
            return y + OverlayLayout.LineHeight(font);
        }

        GCGenerationInfo gen0 = recorded.GenerationInfo[Gen0];

        // One value per cell: a "before -> after" pair does not fit in ValueChars, and an
        // oversized value would push the right-hand column out of line with the other rows.
        y = DrawRow(canvas, font, x, y, s_collectionColor,
            "size before", ByteFormat.Short(gen0.SizeBeforeBytes),
            "size after", ByteFormat.Short(gen0.SizeAfterBytes));

        y = DrawRow(canvas, font, x, y, s_collectionColor,
            "frag before", ByteFormat.Short(gen0.FragmentationBeforeBytes),
            "frag after", ByteFormat.Short(gen0.FragmentationAfterBytes));

        y = DrawRow(canvas, font, x, y, s_collectionColor,
            "freed", ByteFormat.Short(gen0.SizeBeforeBytes - gen0.SizeAfterBytes),
            "frag delta", ByteFormat.Short(gen0.FragmentationAfterBytes - gen0.FragmentationBeforeBytes));

        y = DrawRow(canvas, font, x, y, s_collectionColor,
            "heap-wide", ByteFormat.Short(recorded.FragmentedBytes),
            "index", recorded.Index.ToString());

        return y + OverlayLayout.LineHeight(font);
    }

    private static int DrawCollectorSection(
        Canvas canvas,
        PCScreenFont font,
        int x,
        int y,
        CollectorStats collector)
    {
        y = DrawSectionTitle(canvas, font, x, y, "COLLECTOR");

        y = DrawRow(canvas, font, x, y, s_collectorColor,
            "time in GC", collector.PercentTimeInGc + " %",
            "collections", collector.Collections.ToString());

        y = DrawRow(canvas, font, x, y, s_collectorColor,
            "freed obj", collector.ObjectsFreed.ToString());

        return y + OverlayLayout.LineHeight(font);
    }

    private static int DrawSectionTitle(Canvas canvas, PCScreenFont font, int x, int y, string title)
    {
        canvas.DrawString(title, font, s_sectionColor, x, y);
        return y + OverlayLayout.LineHeight(font);
    }

    // One indented "label value" pair, optionally followed by a second one in a
    // right-hand column. Each cell is drawn at its own pixel offset and clipped to its
    // own width, so a value longer than ValueChars can never shift the next column and
    // put this row out of line with the ones above it.
    private static int DrawRow(
        Canvas canvas,
        PCScreenFont font,
        int x,
        int y,
        Color color,
        string label,
        string value,
        string secondLabel = "",
        string secondValue = "")
    {
        int cellX = x + IndentChars * font.Width;

        DrawCell(canvas, font, cellX, y, color, label, value);

        if (secondLabel.Length > 0)
        {
            DrawCell(canvas, font, cellX + ColumnChars * font.Width, y, color, secondLabel, secondValue);
        }

        return y + OverlayLayout.LineHeight(font);
    }

    // Label left-aligned, value right-aligned, the whole cell clipped to its column.
    private static void DrawCell(
        Canvas canvas,
        PCScreenFont font,
        int x,
        int y,
        Color color,
        string label,
        string value)
    {
        string text = label.PadRight(LabelChars) + value.PadLeft(ValueChars);
        int maxWidth = (LabelChars + ValueChars) * font.Width;

        if (x + maxWidth > canvas.Width)
        {
            maxWidth = canvas.Width - x;
        }

        TextRenderer.DrawTruncated(canvas, font, text, color, x, y, maxWidth);
    }

    private static void DrawMemoryChart(Canvas canvas, PCScreenFont font, int x, int y, int width)
    {
        int footerHeight = OverlayLayout.LineHeight(font) * OverlayLayout.SectionBreakRowCount;
        int available = canvas.Height - y - footerHeight;
        int chartHeight = available > ChartHeightCapPx ? ChartHeightCapPx : available;

        double axisMax = s_committedSeries.Peak();

        LineChart.Draw(
            canvas,
            font,
            s_series,
            new Rectangle(x, y, width, chartHeight),
            axisMax,
            ByteFormat.Short(axisMax),
            "window " + s_committedSeries.Count * FrameDelayMs / 1000 + "s",
            s_markers);
    }
}
