using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using SchedThread = Cosmos.Kernel.Core.Scheduler.Thread;
using SysThread = System.Threading.Thread;

namespace DevKernel;

internal static class CpuStat
{
    private const int MaxStressThreads = 8;
    private const int BurnMs = 30;
    private const int SleepMs = 400;
    private const int FrameSleepMs = 100;
    private const int StepMs = 500;
    private const int HistorySize = 600;
    private const int DrainBudgetMs = 2000;

    private static int s_live;
    private static int s_dropRequest;
    private static int s_stop;

    private static readonly float[] s_pctHistory = new float[HistorySize];
    private static readonly int[] s_threadHistory = new int[HistorySize];
    private static int s_historyHead;
    private static int s_historyFilled;

    public static void Run()
    {
        if (!SchedulerManager.IsEnabled)
        {
            Console.WriteLine("cpustat: scheduler disabled (set CosmosEnableScheduler=true).");
            return;
        }

        Canvas canvas = Canvas.GetFullScreen();
        PCScreenFont font = PCScreenFont.DefaultFont;

        ResetState();

        long freq = Stopwatch.Frequency;
        long stepTicks = freq * StepMs / 1000;

        long lastWall = Stopwatch.GetTimestamp();
        long lastStepWall = lastWall;
        ulong lastBusy = SchedulerManager.GetBusyCpuTimeNs();

        int target = 0;
        int direction = +1;
        double currentPct = 0;
        double peakPct = 0;

        while (true)
        {
            if (Console.KeyAvailable)
            {
                ConsoleKeyInfo k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Escape)
                {
                    break;
                }
            }

            long nowWall = Stopwatch.GetTimestamp();

            if (nowWall - lastStepWall >= stepTicks)
            {
                lastStepWall = nowWall;
                target += direction;
                if (target >= MaxStressThreads)
                {
                    target = MaxStressThreads;
                    direction = -1;
                }
                else if (target <= 0)
                {
                    target = 0;
                    direction = +1;
                }
            }

            int liveSnap = Volatile.Read(ref s_live);
            int dropSnap = Volatile.Read(ref s_dropRequest);
            int effective = liveSnap - dropSnap;
            if (effective < 0)
            {
                effective = 0;
            }

            if (effective < target)
            {
                int spawn = target - effective;
                for (int i = 0; i < spawn; i++)
                {
                    Interlocked.Increment(ref s_live);
                    try
                    {
                        SysThread worker = new SysThread(StressWorker);
                        worker.Start();
                    }
                    catch
                    {
                        Interlocked.Decrement(ref s_live);
                        break;
                    }
                }
            }
            else if (effective > target)
            {
                int delta = effective - target;
                for (int i = 0; i < delta; i++)
                {
                    Interlocked.Increment(ref s_dropRequest);
                }
            }

            ulong busyNow = SchedulerManager.GetBusyCpuTimeNs();
            long wallDelta = nowWall - lastWall;
            uint cpuCount = SchedulerManager.CpuCount;
            if (wallDelta > 0 && cpuCount > 0)
            {
                ulong busyDelta = busyNow >= lastBusy ? busyNow - lastBusy : 0UL;
                double availableNs = (double)wallDelta * 1_000_000_000.0 / (double)freq * (double)cpuCount;
                if (availableNs > 0)
                {
                    double pct = (double)busyDelta * 100.0 / availableNs;
                    if (pct < 0)
                    {
                        pct = 0;
                    }
                    if (pct > 100)
                    {
                        pct = 100;
                    }
                    currentPct = pct;
                    if (pct > peakPct)
                    {
                        peakPct = pct;
                    }
                }
            }
            lastBusy = busyNow;
            lastWall = nowWall;

            liveSnap = Volatile.Read(ref s_live);
            dropSnap = Volatile.Read(ref s_dropRequest);
            effective = liveSnap - dropSnap;
            if (effective < 0)
            {
                effective = 0;
            }

            s_pctHistory[s_historyHead] = (float)currentPct;
            s_threadHistory[s_historyHead] = effective;
            s_historyHead++;
            if (s_historyHead >= HistorySize)
            {
                s_historyHead = 0;
            }
            if (s_historyFilled < HistorySize)
            {
                s_historyFilled++;
            }

            Render(canvas, font, currentPct, peakPct, target, liveSnap, dropSnap, effective, direction);

            canvas.Display();
            SysThread.Sleep(FrameSleepMs);
        }

        Volatile.Write(ref s_stop, 1);
        long deadline = Stopwatch.GetTimestamp() + freq * DrainBudgetMs / 1000;
        while (Volatile.Read(ref s_live) > 0 && Stopwatch.GetTimestamp() < deadline)
        {
            SysThread.Sleep(50);
        }

        Console.Clear();
    }

    private static void ResetState()
    {
        Volatile.Write(ref s_live, 0);
        Volatile.Write(ref s_dropRequest, 0);
        Volatile.Write(ref s_stop, 0);
        s_historyHead = 0;
        s_historyFilled = 0;
        for (int i = 0; i < HistorySize; i++)
        {
            s_pctHistory[i] = 0;
            s_threadHistory[i] = 0;
        }
    }

    private static void StressWorker()
    {
        long freq = Stopwatch.Frequency;
        long burnTicks = freq * BurnMs / 1000;
        try
        {
            while (Volatile.Read(ref s_stop) == 0)
            {
                int after = Interlocked.Decrement(ref s_dropRequest);
                if (after >= 0)
                {
                    return;
                }
                Interlocked.Increment(ref s_dropRequest);

                long burnEnd = Stopwatch.GetTimestamp() + burnTicks;
                while (Stopwatch.GetTimestamp() < burnEnd)
                {
                }

                SysThread.Sleep(SleepMs);
            }
        }
        finally
        {
            Interlocked.Decrement(ref s_live);
        }
    }

    private static void Render(
        Canvas canvas,
        PCScreenFont font,
        double currentPct,
        double peakPct,
        int target,
        int live,
        int drop,
        int effective,
        int direction)
    {
        int w = canvas.Width;
        int h = canvas.Height;
        int charW = font.Width;
        int lh = font.Height + 2;

        canvas.DrawFilledRectangle(Color.Black, 0, 0, w, h);

        int row = 4;
        int leftPad = 4;

        DrawTruncated(canvas, font, "CPU Utilization Monitor - ESC to exit", Color.LightGray, leftPad, row, w - leftPad * 2);
        row += lh;

        Color cpuColor = currentPct < 50 ? Color.LimeGreen : (currentPct < 80 ? Color.Yellow : Color.OrangeRed);
        string cpuLine = "CPU: " + ((int)currentPct).ToString() + "%";
        canvas.DrawString(cpuLine, font, cpuColor, leftPad, row);

        string peakLine = "Peak: " + ((int)peakPct).ToString() + "%";
        int peakX = leftPad + cpuLine.Length * charW + charW * 2;
        if (peakX + peakLine.Length * charW <= w - leftPad)
        {
            canvas.DrawString(peakLine, font, Color.LightGray, peakX, row);
        }
        row += lh;

        int barH = lh - 2;
        canvas.DrawFilledRectangle(Color.FromArgb(40, 40, 40), 0, row, w, barH);
        int fillW = (int)((double)w * currentPct / 100.0);
        if (fillW > 0)
        {
            canvas.DrawFilledRectangle(cpuColor, 0, row, fillW, barH);
        }
        row += barH + 2;

        string dirGlyph = direction > 0 ? "+" : "-";
        string statsLong = "target=" + target + "  live=" + live + "  drop=" + drop + "  eff=" + effective + "  dir=" + dirGlyph;
        string statsMid = "tgt=" + target + " live=" + live + " eff=" + effective + " " + dirGlyph;
        string statsShort = "t=" + target + " e=" + effective;
        string stats = ChooseFitting(statsLong, statsMid, statsShort, w - leftPad * 2, charW);
        canvas.DrawString(stats, font, Color.LightGray, leftPad, row);
        row += lh;

        int reservedFooter = lh * 2;
        int graphHCap = 180;
        int graphAvail = h - row - reservedFooter;
        int graphH = graphAvail > graphHCap ? graphHCap : graphAvail;

        if (graphH >= 60)
        {
            int graphY = row;

            canvas.DrawFilledRectangle(Color.FromArgb(15, 15, 15), 0, graphY, w, graphH);

            Color grid = Color.FromArgb(45, 45, 45);
            for (int p = 25; p <= 75; p += 25)
            {
                int gy = graphY + graphH - 1 - (graphH - 1) * p / 100;
                canvas.DrawLine(grid, 0, gy, w - 1, gy);
            }

            int filled = s_historyFilled;
            if (filled >= 2)
            {
                int prevX = -1;
                int prevPctY = 0;
                int prevThY = 0;
                for (int i = 0; i < filled; i++)
                {
                    int idx = s_historyFilled < HistorySize
                        ? i
                        : (s_historyHead + i) % HistorySize;

                    int px = (int)((long)i * (w - 1) / (filled - 1));

                    float pct = s_pctHistory[idx];
                    int pctY = graphY + graphH - 1 - (int)(pct * (graphH - 1) / 100f);

                    int t = s_threadHistory[idx];
                    if (t > MaxStressThreads)
                    {
                        t = MaxStressThreads;
                    }
                    int thY = graphY + graphH - 1 - t * (graphH - 1) / MaxStressThreads;

                    if (prevX >= 0)
                    {
                        canvas.DrawLine(Color.LimeGreen, prevX, prevPctY, px, pctY);
                        canvas.DrawLine(Color.Cyan, prevX, prevThY, px, thY);
                    }
                    prevX = px;
                    prevPctY = pctY;
                    prevThY = thY;
                }
            }

            row += graphH + 2;

            DrawLegend(canvas, font, leftPad, row, w - leftPad, filled);
            row += lh;
        }

        DrawRegistry(canvas, font, leftPad, row, w - leftPad, h - row, lh, charW);
    }

    private static void DrawLegend(Canvas canvas, PCScreenFont font, int x, int y, int maxWidth, int filled)
    {
        int charW = font.Width;
        int swatchSize = font.Height - 4;

        string cpuLabel = "CPU %";
        string thLabel = "stress (0.." + MaxStressThreads + ")";
        string winLabel = "window " + (filled / 10) + "s";

        int cursor = x;
        int remaining = maxWidth;

        int cpuW = swatchSize + 4 + cpuLabel.Length * charW + charW * 2;
        int thW = swatchSize + 4 + thLabel.Length * charW + charW * 2;
        int winW = winLabel.Length * charW;

        if (cpuW <= remaining)
        {
            canvas.DrawFilledRectangle(Color.LimeGreen, cursor, y + 2, swatchSize, swatchSize);
            canvas.DrawString(cpuLabel, font, Color.LightGray, cursor + swatchSize + 4, y);
            cursor += cpuW;
            remaining -= cpuW;
        }
        else
        {
            return;
        }

        if (thW <= remaining)
        {
            canvas.DrawFilledRectangle(Color.Cyan, cursor, y + 2, swatchSize, swatchSize);
            canvas.DrawString(thLabel, font, Color.LightGray, cursor + swatchSize + 4, y);
            cursor += thW;
            remaining -= thW;
        }

        if (winW <= remaining)
        {
            canvas.DrawString(winLabel, font, Color.LightGray, cursor, y);
        }
    }

    private static void DrawRegistry(Canvas canvas, PCScreenFont font, int x, int y, int maxWidth, int maxHeight, int lh, int charW)
    {
        SchedThread?[]? threads = SchedulerManager.Threads;
        int regCount = SchedulerManager.ThreadCount;
        if (threads == null || regCount <= 0)
        {
            return;
        }

        if (maxHeight < lh * 2)
        {
            return;
        }

        string header = "Scheduler threads (" + regCount + " live):";
        DrawTruncated(canvas, font, header, Color.LightGray, x, y, maxWidth);

        int gridY = y + lh;
        int gridH = maxHeight - lh;

        int colW = charW * 18;
        if (colW > maxWidth)
        {
            colW = maxWidth;
        }
        int cols = maxWidth / colW;
        int rows = gridH / lh;

        int maxEntries = cols * rows;
        int drawn = 0;
        for (int i = 0; i < threads.Length && drawn < maxEntries; i++)
        {
            SchedThread? t = threads[i];
            if (t == null)
            {
                continue;
            }

            int colIdx = drawn % cols;
            int rowIdx = drawn / cols;
            int ex = x + colIdx * colW;
            int ey = gridY + rowIdx * lh;
            DrawTruncated(canvas, font, FormatThread(t), Color.White, ex, ey, colW - charW);
            drawn++;
        }
    }

    private static string FormatThread(SchedThread t)
    {
        string flag = (t.Flags & ThreadFlags.IdleThread) != 0 ? "idle"
                    : (t.Flags & ThreadFlags.Managed) != 0 ? "mgd"
                    : "krn";
        string state = t.State switch
        {
            Cosmos.Kernel.Core.Scheduler.ThreadState.Running => "RUN",
            Cosmos.Kernel.Core.Scheduler.ThreadState.Ready => "RDY",
            Cosmos.Kernel.Core.Scheduler.ThreadState.Blocked => "BLK",
            Cosmos.Kernel.Core.Scheduler.ThreadState.Sleeping => "SLP",
            Cosmos.Kernel.Core.Scheduler.ThreadState.Dead => "DED",
            Cosmos.Kernel.Core.Scheduler.ThreadState.Created => "NEW",
            _ => "???"
        };
        return "T" + t.Id + " " + flag + " " + state + " " + FormatRuntime(t.TotalRuntime);
    }

    private static string FormatRuntime(ulong ns)
    {
        ulong ms = ns / 1_000_000UL;
        if (ms < 1000)
        {
            return ms.ToString() + "ms";
        }
        ulong sec = ms / 1000UL;
        if (sec < 60)
        {
            ulong tenths = (ms % 1000UL) / 100UL;
            return sec.ToString() + "." + tenths.ToString() + "s";
        }
        ulong min = sec / 60UL;
        if (min < 60)
        {
            return min.ToString() + "m";
        }
        ulong hr = min / 60UL;
        return hr.ToString() + "h";
    }

    private static string ChooseFitting(string longForm, string midForm, string shortForm, int maxWidth, int charW)
    {
        int maxChars = maxWidth / charW;
        if (longForm.Length <= maxChars)
        {
            return longForm;
        }
        if (midForm.Length <= maxChars)
        {
            return midForm;
        }
        return shortForm;
    }

    private static void DrawTruncated(Canvas canvas, PCScreenFont font, string s, Color color, int x, int y, int maxWidth)
    {
        int charW = font.Width;
        int maxChars = maxWidth / charW;
        if (maxChars <= 0)
        {
            return;
        }
        if (s.Length > maxChars)
        {
            s = s.Substring(0, maxChars);
        }
        canvas.DrawString(s, font, color, x, y);
    }
}
