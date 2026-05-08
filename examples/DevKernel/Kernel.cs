using System;
using System.Drawing;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.HAL.Devices.Network;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System.Filesystems.Fat;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Network.IPv4;
using Cosmos.Kernel.System.Network.IPv4.UDP;
using Cosmos.Kernel.System.Network.IPv4.UDP.DHCP;
using Cosmos.Kernel.System.Network.IPv4.UDP.DNS;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Timer;
using Cosmos.Kernel.System.Vfs;
using Sys = Cosmos.Kernel.System;

namespace DevKernel;

/// <summary>
/// DevKernel - Test kernel for Cosmos gen3 development.
/// </summary>
public class Kernel : Sys.Kernel
{
    private string _prompt = "cosmos";
    private string _cwd = "/";

    protected override void BeforeRun()
    {
        Serial.WriteString("[DevKernel] BeforeRun() called\n");

        TryRegisterFat();

        Console.Clear();
        Console.WriteLine("========================================");
        Console.WriteLine($"         CosmosOS {Sys.Kernel.VersionString} Shell       ");
        Console.WriteLine("========================================");
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Cosmos booted successfully!");
        Console.ResetColor();
        Console.WriteLine("Type 'help' for available commands.");
        Console.WriteLine();
    }

    protected override void Run()
    {
        // Print prompt
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(_prompt);
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(":");
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.Write(_cwd);
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("$ ");
        Console.ResetColor();

        try
        {
            string? input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                throw new Exception("No input provided");
            }

            string trimmed = input.Trim();
            string[] parts = trimmed.Split(' ');
            string cmd = parts[0].ToLower();

            switch (cmd)
            {
                case "help":
                    PrintHelp();
                    break;

                case "clear":
                case "cls":
                    Console.Clear();
                    break;

                case "echo":
                    if (parts.Length > 1)
                    {
                        Console.WriteLine(trimmed.Substring(5));
                    }

                    break;

                case "info":
                case "sysinfo":
                    PrintSystemInfo();
                    break;

                case "timer":
                    RunTimerTest();
                    break;

                case "schedinfo":
                    ShowSchedulerInfo();
                    break;

                case "thread":
                    TestThread();
                    break;

                case "gfx":
                    StartGraphicsThread();
                    break;

                case "kill":
                    if (parts.Length > 1 && uint.TryParse(parts[1], out uint killId))
                    {
                        KillThread(killId);
                    }
                    else
                    {
                        PrintError("Usage: kill <thread_id>");
                    }

                    break;

                case "halt":
                    PrintWarning("Halting CPU...");
                    Sys.Power.Halt();
                    break;

                case "reboot":
                    PrintWarning("Rebooting...");
                    Sys.Power.Reboot();
                    break;

                case "shutdown":
                    PrintWarning("Shutting down...");
                    Sys.Power.Shutdown();
                    break;

                case "netconfig":
                    ConfigureNetwork();
                    break;

                case "netinfo":
                    ShowNetworkInfo();
                    break;

                case "netsend":
                    SendTestPacket();
                    break;

                case "netlisten":
                    StartListening();
                    break;

                case "dhcp":
                    RunDHCP();
                    break;

                case "dns":
                    if (parts.Length > 1)
                    {
                        ResolveDNS(parts[1]);
                    }
                    else
                    {
                        PrintError("Usage: dns <domain>");
                    }

                    break;

                case "meminfo":
                    ShowMemoryInfo();
                    break;

                case "diskinfo":
                    ShowDiskInfo();
                    break;

                case "partitions":
                case "lspart":
                    ListPartitions();
                    break;

                case "mkmbr":
                    if (parts.Length > 1 && int.TryParse(parts[1], out int mbrDiskNum))
                    {
                        CreateMbrTable(mbrDiskNum);
                    }
                    else
                    {
                        PrintError("Usage: mkmbr <disk_number>");
                    }
                    break;

                case "mkgpt":
                    if (parts.Length > 1 && int.TryParse(parts[1], out int gptDiskNum))
                    {
                        CreateGptTable(gptDiskNum);
                    }
                    else
                    {
                        PrintError("Usage: mkgpt <disk_number>");
                    }
                    break;

                case "mkpart":
                    if (parts.Length == 3
                        && int.TryParse(parts[1], out int cpDiskAuto)
                        && int.TryParse(parts[2], out int cpSizeMbAuto))
                    {
                        CreatePartitionEntry(cpDiskAuto, startSectorOrAuto: null, sizeMB: cpSizeMbAuto);
                    }
                    else if (parts.Length >= 4
                        && int.TryParse(parts[1], out int cpDisk)
                        && ulong.TryParse(parts[2], out ulong cpStart)
                        && int.TryParse(parts[3], out int cpSizeMb))
                    {
                        CreatePartitionEntry(cpDisk, cpStart, cpSizeMb);
                    }
                    else
                    {
                        PrintError("Usage: mkpart <disk> [start_lba] <size_mb>");
                    }
                    break;

                case "rmpart":
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int rmPartNum))
                    {
                        DeletePartitionEntry(rmPartNum);
                    }
                    else
                    {
                        PrintError("Usage: rmpart <partition_number>");
                    }
                    break;

                case "format":
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int fmtPartNum))
                    {
                        string fmtType = parts.Length >= 3 ? parts[2].ToLower() : "fat";
                        FormatPartition(fmtPartNum, fmtType);
                    }
                    else
                    {
                        PrintError("Usage: format <partition_number> [fs_type]   (default: fat)");
                    }
                    break;

                case "mount":
                    if (parts.Length >= 3
                        && int.TryParse(parts[1], out int mountPartNum))
                    {
                        MountPartition(mountPartNum, parts[2]);
                    }
                    else
                    {
                        PrintError("Usage: mount <partition_number> <mountpoint>   (e.g. mount 0 /mnt)");
                    }
                    break;

                case "mounts":
                    ShowMountPoints();
                    break;

                case "cd":
                    ChangeDirectory(parts.Length >= 2 ? parts[1] : "/");
                    break;

                case "pwd":
                    Console.WriteLine(_cwd);
                    break;

                case "ls":
                case "dir":
                    ListDirectory(parts.Length >= 2 ? parts[1] : _cwd);
                    break;

                case "cat":
                case "type":
                    if (parts.Length >= 2)
                    {
                        DisplayFileContents(parts[1]);
                    }
                    else
                    {
                        PrintError("Usage: cat <path>");
                    }
                    break;

                case "mkdir":
                    if (parts.Length >= 2)
                    {
                        CreateDirectory(parts[1]);
                    }
                    else
                    {
                        PrintError("Usage: mkdir <path>");
                    }
                    break;

                case "touch":
                    if (parts.Length >= 2)
                    {
                        CreateEmptyFile(parts[1]);
                    }
                    else
                    {
                        PrintError("Usage: touch <path>");
                    }
                    break;

                case "rm":
                case "del":
                    if (parts.Length >= 2)
                    {
                        DeleteEntry(parts[1]);
                    }
                    else
                    {
                        PrintError("Usage: rm <path>");
                    }
                    break;

                case "write":
                    if (parts.Length >= 3)
                    {
                        string text = JoinFrom(parts, 2);
                        WriteToFile(parts[1], text);
                    }
                    else
                    {
                        PrintError("Usage: write <path> <text>");
                    }
                    break;


                case "free":
                    Console.WriteLine(Cosmos.Kernel.Core.Memory.Heap.Heap.Collect() + " objects collected.");
                    break;

                case "gc":
                    GarbadgeColectorLiveInformation();
                    break;

                case "cpustat":
                    CpuStat.Run();
                    break;

                case "gcvar":
                    foreach (KeyValuePair<string, object> varable in GC.GetConfigurationVariables())
                    {
                        Console.WriteLine(varable.Key.PadLeft(15) + ":" + varable.Value.ToString());
                    }
                    break;

                case "startx":

                {
                    /* First test with the DefaultMode */
                    Canvas canvas = Canvas.GetFullScreen();
                    var font = PCScreenFont.DefaultFont;

                    int fps = 0;
                    int frames = 0;
                    int framesSinceFps = 0;
                    long lastFpsTicks = 0;
                    long swFrequency = System.Diagnostics.Stopwatch.Frequency;
                    int refreshRate = canvas.RefreshRate;
                    long frameInterval = swFrequency / refreshRate;
                    long lastFrameStart = System.Diagnostics.Stopwatch.GetTimestamp();

                    Serial.Write("Testing Canvas with mode " + canvas.Mode + " @ " + refreshRate + " Hz\n");

                    // Set up mouse for cursor
                    Cosmos.Kernel.System.Mouse.MouseManager.SetScreenSize((int)canvas.Mode.Width, (int)canvas.Mode.Height);

                    int x = 10;
                    int y = 10;
                    int lineHeight = font.Height + 2;
                    int panelWidth = 360;
                    int panelHeight = lineHeight * 9 + 8;

                    while (true)
                    {
                        canvas.Clear(Color.Black);

                        frames++;
                        framesSinceFps++;

                        {
                            long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                            if (lastFpsTicks == 0)
                            {
                                lastFpsTicks = nowTicks;
                            }
                            else if (nowTicks - lastFpsTicks >= swFrequency)
                            {
                                fps = (int)(framesSinceFps * swFrequency / (nowTicks - lastFpsTicks));
                                framesSinceFps = 0;
                                lastFpsTicks = nowTicks;
                            }
                        }

                        ulong totalPages = PageAllocator.TotalPageCount;
                        ulong freePages = PageAllocator.FreePageCount;
                        ulong usedPages = totalPages - freePages;
                        ulong pageSize = PageAllocator.PageSize;

                        ulong totalBytes = totalPages * pageSize;
                        ulong usedBytes = usedPages * pageSize;
                        ulong freeBytes = freePages * pageSize;

                        Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector.GetStats(out int totalCollections, out int totalObjectsFreed);

                        int rowY = y;

                        canvas.DrawString("Meminfo", font, Color.Cyan, x, rowY);
                        rowY += lineHeight;
                        canvas.DrawString("Total: " + (totalBytes / 1024 / 1024) + " MB", font, Color.White, x, rowY);
                        rowY += lineHeight;
                        canvas.DrawString("Used : " + (usedBytes / 1024 / 1024) + " MB", font, Color.White, x, rowY);
                        rowY += lineHeight;
                        canvas.DrawString("Free : " + (freeBytes / 1024 / 1024) + " MB", font, Color.White, x, rowY);
                        rowY += lineHeight;
                        canvas.DrawString("Pages: " + usedPages + "/" + totalPages, font, Color.White, x, rowY);
                        rowY += lineHeight * 2;

                        canvas.DrawString("GCinfo", font, Color.Cyan, x, rowY);
                        rowY += lineHeight;
                        canvas.DrawString("Collections: " + totalCollections, font, Color.White, x, rowY);
                        rowY += lineHeight;
                        canvas.DrawString("Objects Freed: " + totalObjectsFreed, font, Color.White, x, rowY);
                        rowY += lineHeight * 2;

                        canvas.DrawString("FPS: " + fps + " / " + refreshRate + " Hz", font, Color.Yellow, x, rowY);

                        // Draw mouse cursor
                        DrawMouseCursor(canvas, Cosmos.Kernel.System.Mouse.MouseManager.X, Cosmos.Kernel.System.Mouse.MouseManager.Y);

                        if (frames % 100 == 0)
                        {
                            Cosmos.Kernel.Core.Memory.Heap.Heap.Collect();
                        }

                        canvas.Display();

                        // Frame pacing: spin until next frame deadline
                        lastFrameStart += frameInterval;
                        long now = System.Diagnostics.Stopwatch.GetTimestamp();
                        if (now > lastFrameStart)
                        {
                            lastFrameStart = now; // fell behind, reset to avoid burst catch-up
                        }
                        else
                        {
                            while (System.Diagnostics.Stopwatch.GetTimestamp() < lastFrameStart) { }
                        }
                    }
                    break;
                }

                default:
                    PrintError($"\"{cmd}\" is not a command");
                    Console.WriteLine("Type 'help' for available commands.");
                    break;
            }
        }
        catch (Exception ex)
        {
            // Use Serial instead of Console to avoid OOM from threading initialization
            Serial.WriteString("[CATCH] Exception caught: ");
            Serial.WriteString(ex.Message);
            Serial.WriteString("\n");

            // Call Stop() to signal the main loop to exit
            Serial.WriteString("[CATCH] Calling Stop()...\n");
            Stop();
        }
    }


    public void GarbadgeColectorLiveInformation()
    {
        PCScreenFont font = PCScreenFont.DefaultFont;
        Canvas canvas = Canvas.GetFullScreen();

        uint frames = 0;
        long sizeBefore = 0, sizeAfter = 0, sizeDelta = 0, maxDeltaSize = 0, fragBefore = 0, fragAfter = 0;
        long commitedMax = 0;
        int x = 10;
        int lineHeight = font.Height + 2;

        while (!Console.KeyAvailable || Console.ReadKey(true).Key != ConsoleKey.Escape)
        {
            unchecked
            {
                frames++;
            }

            canvas.Clear(Color.Black);

            //GarbageCollector.SimpleMemoryInfo info = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector.GetSimpleMemoryInfo();

            GCMemoryInfo info = GC.GetGCMemoryInfo();

            int rowY = 10;
            canvas.DrawString($"GC Info ({frames})", font, Color.Cyan, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Size values are in bytes, ESC to exit;", font, Color.Cyan, x, rowY);
            rowY += lineHeight;

            commitedMax = Math.Max(commitedMax, info.TotalCommittedBytes);

            canvas.DrawString($"RamSize         : {PageAllocator.RamSize,15}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"HeapSize        : {info.HeapSizeBytes,15}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Fragmented      : {info.FragmentedBytes,15}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Committed       : {info.TotalCommittedBytes,15}; max size  : {commitedMax,15}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Promoted        : {info.PromotedBytes,15}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Pinned          : {info.PinnedObjectsCount,15}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Collections     : {info.Index,15}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Condemned gen   : {info.Generation,15}", font, Color.White, x, rowY);
            rowY += lineHeight;

            // last gen before/after
            canvas.DrawString($"Gen0 size before: {sizeBefore,15}; size after: {sizeAfter,15}", font, Color.Yellow, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Gen0 size delta : {sizeDelta,15}; max size  : {maxDeltaSize,15}", font, Color.Yellow, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Frag size before: {fragBefore,15}; size after: {fragAfter,15}", font, Color.Yellow, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Frag size delta : {fragAfter - fragBefore,15}", font, Color.Yellow, x, rowY);
            rowY += lineHeight;

            int pct = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector.GetLastGCPercentTimeInGC();
            Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector.GetStats(out int totalCollections, out int totalObjectsFreed);
            canvas.DrawString($"Last GC % time in GC: {pct,3}%, Collections: {totalCollections,6}, Objects Freed: {totalObjectsFreed,6}", font, Color.Green, x, rowY); rowY += lineHeight;

            if (frames % 50 == 0)
            {
                Cosmos.Kernel.Core.Memory.Heap.Heap.Collect();
                info = GC.GetGCMemoryInfo();
                sizeBefore = info.GenerationInfo[0].SizeBeforeBytes;
                sizeAfter = info.GenerationInfo[0].SizeAfterBytes;
                fragBefore = info.GenerationInfo[0].FragmentationBeforeBytes;
                fragAfter = info.GenerationInfo[0].FragmentationAfterBytes;

                sizeDelta = sizeBefore - sizeAfter;
                maxDeltaSize = Math.Max(maxDeltaSize, sizeDelta);
            }

            canvas.Display();

            // simple frame pacing
            System.Threading.Thread.Sleep(250);
        }
        Console.Clear();
    }

    protected override void AfterRun()
    {
        Serial.WriteString("[DevKernel] AfterRun() called\n");
        Console.WriteLine("Goodbye!");
    }

    private void PrintHelp()
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Available Commands:");
        Console.ResetColor();

        PrintCommand("help", "Show this help message");
        PrintCommand("clear", "Clear the screen");
        PrintCommand("echo <text>", "Echo back text");
        PrintCommand("info", "Show system information");
        PrintCommand("timer", "Test 10 second countdown timer");
        PrintCommand("schedinfo", "Show scheduler status and threads");
        PrintCommand("meminfo", "Show memory allocator state");
        PrintCommand("thread", "Test System.Threading.Thread");
        PrintCommand("gfx", "Start graphics thread (draws square)");
        PrintCommand("kill <id>", "Kill a thread by ID");
        PrintCommand("halt", "Halt the CPU (does not power off)");
        PrintCommand("reboot", "Restart the machine");
        PrintCommand("shutdown", "Power off the machine");
        PrintCommand("netconfig", "Configure network stack");
        PrintCommand("netinfo", "Show network device info");
        PrintCommand("netsend", "Send UDP test packet");
        PrintCommand("netlisten", "Listen for UDP packets");
        PrintCommand("dhcp", "Auto-configure network via DHCP");
        PrintCommand("dns <domain>", "Resolve domain name to IP");
        PrintCommand("gc", "Give live information on the GC");
        PrintCommand("cpustat", "Live CPU% + thread monitor with stress wave");
        PrintCommand("diskinfo", "Show storage devices and geometry");
        PrintCommand("partitions", "List partitions, grouped under each disk");
        PrintCommand("mkmbr <n>", "Write a fresh empty MBR to disk n");
        PrintCommand("mkgpt <n>", "Write a fresh empty GPT to disk n");
        PrintCommand("mkpart <n> [start] <mb>", "Create a partition on disk n (start LBA optional)");
        PrintCommand("rmpart <p>", "Delete partition p");
        PrintCommand("format <n> [fs]", "Format partition n (fs: fat | fat12 | fat16 | fat32, default fat)");
        PrintCommand("mount <p> <path>", "Mount partition p at <path> (e.g. mount 0 /mnt)");
        PrintCommand("mounts", "Show mounted filesystems");
        PrintCommand("cd <path>", "Change current directory");
        PrintCommand("pwd", "Print current directory");
        PrintCommand("ls [path]", "List directory contents (defaults to cwd)");
        PrintCommand("cat <path>", "Display file contents");
        PrintCommand("mkdir <path>", "Create directory");
        PrintCommand("touch <path>", "Create empty file");
        PrintCommand("rm <path>", "Delete file or empty directory");
        PrintCommand("write <path> <txt>", "Write text to file (overwrite)");
    }

    private void PrintCommand(string cmd, string description)
    {
        Console.Write("  ");
        Console.Write(cmd.PadRight(14));
        Console.WriteLine(description);
    }

    private void PrintSystemInfo()
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("System Information:");
        Console.ResetColor();

        PrintInfoLine("OS", $"CosmosOS v{Sys.Kernel.VersionString} (gen3)");
        PrintInfoLine("Runtime", "NativeAOT");
#if ARCH_X64
        PrintInfoLine("Architecture", "x86-64");
#elif ARCH_ARM64
        PrintInfoLine("Architecture", "ARM64");
#endif
        PrintInfoLine("Console", KernelConsole.Default.Cols + "x" + KernelConsole.Default.Rows + " chars");
        if (KernelConsole.Default.IsAvailable)
        {
            var mode = KernelConsole.Default.Canvas.Mode;
            PrintInfoLine("Framebuffer", mode.Width + "x" + mode.Height + "x" + (int)mode.ColorDepth + " (" + KernelConsole.Default.Canvas.Name() + ")");
        }
        else
        {
            PrintInfoLine("Framebuffer", "Disabled");
        }
    }

    private void PrintInfoLine(string label, string value)
    {
        Console.Write("  ");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(label.PadRight(14));
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(value);
        Console.ResetColor();
    }

    private void RunTimerTest()
    {
        Console.WriteLine("Starting 10 second countdown...");
        for (int i = 10; i > 0; i--)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(i.ToString());
            Console.ResetColor();
            Console.WriteLine("...");
            TimerManager.Wait(1000);
        }
        PrintSuccess("Timer test complete!");
        Console.WriteLine();
    }

    private void ShowMemoryInfo()
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Memory Information:");
        Console.ResetColor();

        // Page allocator stats
        ulong totalPages = PageAllocator.TotalPageCount;
        ulong freePages = PageAllocator.FreePageCount;
        ulong usedPages = totalPages - freePages;
        ulong pageSize = PageAllocator.PageSize;

        ulong totalBytes = totalPages * pageSize;
        ulong freeBytes = freePages * pageSize;
        ulong usedBytes = usedPages * pageSize;

        PrintInfoLine("Page Size", (pageSize / 1024).ToString() + " KB");
        PrintInfoLine("Total Pages", totalPages.ToString());
        PrintInfoLine("Used Pages", usedPages.ToString());
        PrintInfoLine("Free Pages", freePages.ToString());

        Console.WriteLine();

        // Memory in MB
        PrintInfoLine("Total Memory", (totalBytes / 1024 / 1024).ToString() + " MB");
        PrintInfoLine("Used Memory", (usedBytes / 1024 / 1024).ToString() + " MB");
        PrintInfoLine("Free Memory", (freeBytes / 1024 / 1024).ToString() + " MB");

        // Usage percentage
        ulong usagePercent = totalPages > 0 ? (usedPages * 100) / totalPages : 0;

        Console.WriteLine();
        Console.Write("  ");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("Usage".PadRight(14));

        // Color based on usage
        if (usagePercent < 50)
        {
            Console.ForegroundColor = ConsoleColor.Green;
        }
        else if (usagePercent < 80)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
        }

        Console.WriteLine(usagePercent.ToString() + "%");
        Console.ResetColor();
    }

    private void ShowSchedulerInfo()
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Scheduler Information:");
        Console.ResetColor();

        var scheduler = SchedulerManager.Current;
        if (scheduler == null)
        {
            PrintInfoLine("Status", "Not initialized");
            return;
        }

        Console.Write("  ");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("Status".PadRight(14));
        Console.ForegroundColor = SchedulerManager.Enabled ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(SchedulerManager.Enabled ? "ENABLED" : "DISABLED");
        Console.ResetColor();

        PrintInfoLine("Scheduler", scheduler.Name);
        PrintInfoLine("CPU Count", SchedulerManager.CpuCount.ToString());
        PrintInfoLine("Quantum", (SchedulerManager.DefaultQuantumNs / 1_000_000).ToString() + " ms");
        Console.WriteLine();

        for (uint cpuId = 0; cpuId < SchedulerManager.CpuCount; cpuId++)
        {
            var cpuState = SchedulerManager.GetCpuState(cpuId);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  CPU " + cpuId + ":");
            Console.ResetColor();

            var currentThread = cpuState.CurrentThread;
            if (currentThread != null)
            {
                PrintThreadInfo(scheduler, currentThread);
            }

            int runQueueCount = scheduler.GetRunQueueCount(cpuState);
            for (int i = 0; i < runQueueCount; i++)
            {
                var thread = scheduler.GetRunQueueThread(cpuState, i);
                if (thread != null)
                {
                    PrintThreadInfo(scheduler, thread);
                }
            }
        }
        Console.WriteLine();
    }

    private void PrintThreadInfo(IScheduler scheduler, Cosmos.Kernel.Core.Scheduler.Thread thread)
    {
        Console.Write("    ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("Thread " + thread.Id);

        Console.Write(" ");
        switch (thread.State)
        {
            case Cosmos.Kernel.Core.Scheduler.ThreadState.Running:
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("Running");
                break;
            case Cosmos.Kernel.Core.Scheduler.ThreadState.Ready:
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Ready");
                break;
            case Cosmos.Kernel.Core.Scheduler.ThreadState.Blocked:
            case Cosmos.Kernel.Core.Scheduler.ThreadState.Sleeping:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(thread.State == Cosmos.Kernel.Core.Scheduler.ThreadState.Blocked ? "Blocked" : "Sleeping");
                break;
            case Cosmos.Kernel.Core.Scheduler.ThreadState.Dead:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("Dead");
                break;
            default:
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("Unknown");
                break;
        }

        if (thread.SchedulerData != null)
        {
            long priority = scheduler.GetPriority(thread);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" Pri=" + priority);
        }

        ulong runtimeMs = thread.TotalRuntime / 1_000_000;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(" Run=" + runtimeMs + "ms");

        Console.ResetColor();
        Console.WriteLine();
    }

    private void TestThread()
    {
        Serial.WriteString("[Thread] Testing System.Threading.Thread API\n");
        Console.WriteLine("Creating and starting a thread...");

        var thread = new System.Threading.Thread(() =>
        {
            Serial.WriteString("[Thread] Hello from thread delegate!\n");
            Console.WriteLine("Hello from thread!");
        });

        thread.Start();
        PrintSuccess("Thread started!");
        Console.WriteLine();

        TimerManager.Wait(2000);
    }

    private void StartGraphicsThread()
    {
        Serial.WriteString("[GfxThread] Starting graphics thread\n");
        Console.WriteLine("Starting graphics thread (draws color-cycling square)...");

        var thread = new System.Threading.Thread(GraphicsWorker);
        thread.Start();

        PrintSuccess("Graphics thread started!");
        Console.WriteLine();
    }

    private void KillThread(uint threadId)
    {
        var scheduler = SchedulerManager.Current;
        if (scheduler == null)
        {
            PrintError("Scheduler not initialized");
            return;
        }

        if (threadId == 0)
        {
            PrintError("Cannot kill idle thread (ID 0)");
            return;
        }

        for (uint cpuId = 0; cpuId < SchedulerManager.CpuCount; cpuId++)
        {
            var cpuState = SchedulerManager.GetCpuState(cpuId);

            if (cpuState.CurrentThread?.Id == threadId)
            {
                PrintWarning("Cannot kill currently running thread");
                cpuState.CurrentThread.State = Cosmos.Kernel.Core.Scheduler.ThreadState.Dead;
                return;
            }

            int count = scheduler.GetRunQueueCount(cpuState);
            for (int i = 0; i < count; i++)
            {
                var thread = scheduler.GetRunQueueThread(cpuState, i);
                if (thread?.Id == threadId)
                {
                    SchedulerManager.ExitThread(cpuId, thread);
                    PrintSuccess("Thread " + threadId + " killed");
                    Console.WriteLine();
                    return;
                }
            }
        }

        PrintError("Thread " + threadId + " not found");
    }

    private static void GraphicsWorker()
    {
        if (KernelConsole.Default.Canvas.Mode.Width == 0 || KernelConsole.Default.Canvas.Mode.Height == 0)
        {
            return;
        }

        const int squareSize = 80;
        const int margin = 20;

        int x = KernelConsole.Default.Canvas.Mode.Width >= (uint)(squareSize + margin * 2)
            ? (int)KernelConsole.Default.Canvas.Mode.Width - squareSize - margin
            : margin;
        int y = KernelConsole.Default.Canvas.Mode.Height >= (uint)(squareSize + margin * 2)
            ? (int)KernelConsole.Default.Canvas.Mode.Height - squareSize - margin
            : margin;

        int frame = 0;

        while (true)
        {
            int phase = frame % 60;
            byte r, g, b;

            if (phase < 10) { r = 255; g = (byte)(phase * 25); b = 0; }
            else if (phase < 20) { r = (byte)(255 - (phase - 10) * 25); g = 255; b = 0; }
            else if (phase < 30) { r = 0; g = 255; b = (byte)((phase - 20) * 25); }
            else if (phase < 40) { r = 0; g = (byte)(255 - (phase - 30) * 25); b = 255; }
            else if (phase < 50) { r = (byte)((phase - 40) * 25); g = 0; b = 255; }
            else { r = 255; g = 0; b = (byte)(255 - (phase - 50) * 25); }

            for (int dy = 0; dy < squareSize; dy++)
            {
                for (int dx = 0; dx < squareSize; dx++)
                {
                    int cx = dx - squareSize / 2;
                    int cy = dy - squareSize / 2;
                    int dist = (cx * cx + cy * cy) * 255 / (squareSize * squareSize / 2);
                    if (dist > 255)
                    {
                        dist = 255;
                    }

                    int factor = 255 - dist / 2;
                    byte pr = (byte)((r * factor) / 255);
                    byte pg = (byte)((g * factor) / 255);
                    byte pb = (byte)((b * factor) / 255);
                    uint pixelColor = (uint)((pr << 16) | (pg << 8) | pb);

                    KernelConsole.Default.Canvas.DrawPoint(pixelColor, x + dx, y + dy);
                }
            }

            frame++;
            KernelConsole.Default.Canvas.Display();
            System.Threading.Thread.Sleep(100);
        }
    }

    // Network configuration
    private Address? _localIP;
    private Address? _gatewayIP;
    private bool _networkConfigured = false;

    private void ConfigureNetwork()
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null)
        {
            PrintError("No network device found");
            return;
        }

        // Configure IP address (10.0.2.15 for QEMU user networking)
        _localIP = new Address(10, 0, 2, 15);
        _gatewayIP = new Address(10, 0, 2, 2);
        var subnet = new Address(255, 255, 255, 0);

        // Initialize network stack and configure IP with full config (subnet + gateway)
        // so that IPConfig.FindNetwork() can route outbound packets
        NetworkStack.Initialize();
        IPConfig.Enable(device, _localIP, subnet, _gatewayIP);

        // Register UDP callback
        UDPPacket.OnUDPDataReceived = OnUDPDataReceived;

        _networkConfigured = true;

        PrintSuccess("Network configured!\n");
        PrintInfoLine("IP", _localIP.ToString());
        PrintInfoLine("Gateway", _gatewayIP.ToString());
    }

    private void ShowNetworkInfo()
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null)
        {
            PrintError("No network device found");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Network Information:");
        Console.ResetColor();

        PrintInfoLine("Device", device.Name);
        PrintInfoLine("MAC", device.MacAddress.ToString());

        Console.Write("  ");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("Link".PadRight(14));
        Console.ForegroundColor = device.LinkUp ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(device.LinkUp ? "UP" : "DOWN");

        Console.Write("  ");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("Ready".PadRight(14));
        Console.ForegroundColor = device.Ready ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(device.Ready ? "YES" : "NO");

        Console.Write("  ");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("Configured".PadRight(14));
        Console.ForegroundColor = _networkConfigured ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(_networkConfigured ? "YES" : "NO");
        Console.ResetColor();

        if (_networkConfigured && _localIP != null)
        {
            PrintInfoLine("IP Address", _localIP.ToString());
        }
    }

    private void SendTestPacket()
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null)
        {
            PrintError("No network device found");
            return;
        }

        if (!device.Ready)
        {
            PrintError("Network device not ready");
            return;
        }

        // Ensure network is configured
        if (!_networkConfigured)
        {
            ConfigureNetwork();
        }

        // Create UDP packet using the packet classes
        string message = "Hello from CosmosOS!";
        byte[] payload = new byte[message.Length];
        for (int i = 0; i < message.Length; i++)
        {
            payload[i] = (byte)message[i];
        }

        // Create UDP packet (using broadcast MAC for now since we don't have full ARP)
        var udpPacket = new UDPPacket(
            _localIP!,                           // Source IP
            _gatewayIP!,                         // Destination IP
            5555,                                // Source port
            5555,                                // Destination port
            payload,                             // Data
            MACAddress.Broadcast                 // Destination MAC (broadcast)
        );

        PrintInfo("Sending UDP packet to " + _gatewayIP!.ToString() + ":5555...");
        bool sent = device.Send(udpPacket.RawData, udpPacket.RawData.Length);

        if (sent)
        {
            PrintSuccess("Packet sent!\n");
        }
        else
        {
            PrintError("Failed to send packet\n");
        }
    }

    private void StartListening()
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null)
        {
            PrintError("No network device found");
            return;
        }

        // Ensure network is configured
        if (!_networkConfigured)
        {
            ConfigureNetwork();
        }

        PrintInfo("Listening for UDP packets on port 5555...");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Send from host: echo 'test' | nc -u localhost 5555");
        Console.ResetColor();
    }

    private void OnUDPDataReceived(UDPPacket packet)
    {
        Serial.Write("[UDP] Received packet from ");
        Serial.WriteString(packet.SourceIP.ToString());
        Serial.Write(":");
        Serial.WriteNumber((ulong)packet.SourcePort);
        Serial.Write(" -> port ");
        Serial.WriteNumber((ulong)packet.DestinationPort);
        Serial.Write("\n");

        // Get the UDP payload
        byte[] data = packet.UDPData;
        Serial.Write("[UDP] Payload (");
        Serial.WriteNumber((ulong)data.Length);
        Serial.Write(" bytes): ");

        for (int i = 0; i < data.Length; i++)
        {
            char c = (char)data[i];
            if (c >= 32 && c < 127)
            {
                Serial.Write(c.ToString());
            }
        }
        Serial.Write("\n");

        // Also print to console with colors
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.Write("[UDP] ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(packet.SourceIP.ToString() + ":" + packet.SourcePort.ToString());
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(" -> ");
        Console.ResetColor();

        for (int i = 0; i < data.Length && i < 64; i++)
        {
            char c = (char)data[i];
            if (c >= 32 && c < 127)
            {
                Console.Write(c.ToString());
            }
        }
        Console.WriteLine();
    }

    private void RunDHCP()
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null)
        {
            PrintError("No network device found");
            return;
        }

        if (!device.Ready)
        {
            PrintError("Network device not ready");
            return;
        }

        PrintInfo("Starting DHCP auto-configuration...");

        // Initialize network stack first
        NetworkStack.Initialize();

        // Create DHCP client and send discover
        var dhcpClient = new DHCPClient();
        int result = dhcpClient.SendDiscoverPacket();

        if (result == -1)
        {
            PrintError("DHCP timeout - no response from server");
            return;
        }

        // Get the assigned configuration
        var netConfig = NetworkConfigManager.Get(device);
        if (netConfig == null)
        {
            PrintError("No network configuration after DHCP");
            return;
        }

        _localIP = netConfig.IPAddress;
        _gatewayIP = netConfig.DefaultGateway;
        _networkConfigured = true;

        // Register UDP callback
        UDPPacket.OnUDPDataReceived = OnUDPDataReceived;

        PrintSuccess("DHCP configuration successful!");
        PrintInfoLine("IP Address", _localIP.ToString());
        PrintInfoLine("Subnet", netConfig.SubnetMask.ToString());
        PrintInfoLine("Gateway", _gatewayIP.ToString());
        Console.WriteLine();
    }

    private void ResolveDNS(string domain)
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null)
        {
            PrintError("No network device found");
            return;
        }

        if (!_networkConfigured)
        {
            PrintError("Network not configured. Run 'dhcp' or 'netconfig' first.");
            return;
        }

        PrintInfo("Resolving " + domain + "...");

        // Configure DNS server (Cloudflare)
        var dnsServer = new Address(1, 1, 1, 1);
        DNSConfig.Add(dnsServer);

        // Create DNS client and connect
        var dnsClient = new DnsClient();
        dnsClient.Connect(dnsServer);

        // Send query
        dnsClient.SendAsk(domain);

        // Wait for response (5 second timeout)
        Address? resolvedIP = dnsClient.Receive(5000);

        if (resolvedIP != null && resolvedIP.Hash != 0)
        {
            PrintSuccess(domain + " -> " + resolvedIP.ToString());
        }
        else
        {
            PrintError("DNS resolution failed or timed out");
        }

        dnsClient.Close();
        Console.WriteLine();
    }

    private static void TryRegisterFat()
    {
        if (!CosmosFeatures.FatEnabled)
        {
            return;
        }

        if (!VfsManager.RegisterFilesystem("fat", new FatFilesystemType()))
        {
            Serial.WriteString("[DevKernel] FAT driver already registered or invalid\n");
            return;
        }

        Serial.WriteString("[DevKernel] FAT driver registered\n");

        if (!CosmosFeatures.StorageEnabled || StorageManager.Partitions.Count == 0)
        {
            return;
        }

        if (VfsManager.TryMount("fat", "0", MountFlags.None, "/mnt", out _))
        {
            Serial.WriteString("[DevKernel] FAT mounted on /mnt from partition 0\n");
        }
        else
        {
            Serial.WriteString("[DevKernel] FAT mount on partition 0 skipped (not FAT or unreadable)\n");
        }
    }

    private void ShowDiskInfo()
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Storage Information:");
        Console.ResetColor();

        if (!StorageManager.IsEnabled)
        {
            PrintError("Storage support is disabled (CosmosEnableStorage=false).");
            return;
        }
        if (StorageManager.DeviceCount == 0)
        {
            PrintWarning("No storage devices discovered. Attach a SATA disk to QEMU and reboot.");
            return;
        }

        PrintInfoLine("Device Count", StorageManager.DeviceCount.ToString());

        for (int i = 0; i < StorageManager.DeviceCount; i++)
        {
            IBlockDevice? dev = StorageManager.GetDevice(i);
            if (dev == null)
            {
                continue;
            }

            Console.WriteLine();
            PrintDiskBlock(i, dev, detailed: true);
        }
    }

    private void ListPartitions()
    {
        if (!StorageManager.IsEnabled)
        {
            PrintError("Storage support is disabled.");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Partitions:");
        Console.ResetColor();

        if (StorageManager.DeviceCount == 0)
        {
            PrintWarning("No storage devices discovered.");
            return;
        }

        IReadOnlyList<Partition> partitions = StorageManager.Partitions;
        for (int i = 0; i < StorageManager.DeviceCount; i++)
        {
            IBlockDevice? dev = StorageManager.GetDevice(i);
            if (dev == null)
            {
                continue;
            }

            Console.WriteLine();
            PrintDiskBlock(i, dev, detailed: false);
            if (ReferenceEquals(dev, StorageManager.PrimaryDevice))
            {
                PrintInfoLine("    Primary".PadRight(17), "yes");
            }

            int diskPartCount = 0;
            for (int p = 0; p < partitions.Count; p++)
            {
                Partition part = partitions[p];
                if (!ReferenceEquals(part.Host, dev))
                {
                    continue;
                }

                diskPartCount++;
                ulong sizeBytes = part.BlockCount * part.BlockSize;
                Console.Write("        ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("[" + p + "] ");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(part.Name);
                Console.ResetColor();
                Console.Write("  Start=" + part.StartingSector);
                Console.Write("  Sectors=" + part.BlockCount);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("  " + (sizeBytes / 1024 / 1024) + " MiB");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write("  " + DetectFilesystem(part));
                Console.ResetColor();
                Console.WriteLine();
            }

            if (diskPartCount == 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("        (no partitions)");
                Console.ResetColor();
            }
        }
    }

    private void PrintDiskBlock(int index, IBlockDevice dev, bool detailed)
    {
        ulong totalBytes = dev.BlockCount * dev.BlockSize;
        PrintInfoLine($"[{index}] Name".PadRight(17), dev.Name);
        if (detailed)
        {
            PrintInfoLine("    Block Size".PadRight(17), dev.BlockSize.ToString() + " B");
        }
        PrintInfoLine("    Sectors".PadRight(17), dev.BlockCount.ToString());
        PrintInfoLine("    Capacity".PadRight(17), (totalBytes / 1024 / 1024).ToString() + " MiB");

        string table;
        if (GPT.IsGPT(dev))
        {
            table = "GPT";
        }
        else if (MBR.IsMBR(dev))
        {
            table = "MBR";
        }
        else
        {
            table = "None";
        }
        PrintInfoLine("    Table".PadRight(17), table);
    }

    private static string DetectFilesystem(Partition part)
    {
        Span<byte> boot = new byte[part.BlockSize];
        try
        {
            part.ReadBlock(0, 1, boot);
        }
        catch
        {
            return "unreadable";
        }

        if (FatBootSector.TryParse(boot, out FatBootSector? bs) && bs != null)
        {
            return bs.Type switch
            {
                FatType.Fat12 => "FAT12",
                FatType.Fat16 => "FAT16",
                FatType.Fat32 => "FAT32",
                _ => "FAT"
            };
        }

        return "unknown";
    }

    private void CreateMbrTable(int diskNum)
    {
        IBlockDevice? dev = StorageManager.GetDevice(diskNum);
        if (dev == null)
        {
            PrintError("Invalid disk number.");
            return;
        }

        MBR.Create(dev);
        StorageManager.RescanPartitions(dev);
        PrintSuccess("MBR table written to disk " + diskNum + ".");
    }

    private void CreateGptTable(int diskNum)
    {
        IBlockDevice? dev = StorageManager.GetDevice(diskNum);
        if (dev == null)
        {
            PrintError("Invalid disk number.");
            return;
        }

        GPT.Create(dev);
        StorageManager.RescanPartitions(dev);
        PrintSuccess("GPT table written to disk " + diskNum + ".");
    }

    private void CreatePartitionEntry(int diskNum, ulong? startSectorOrAuto, int sizeMB)
    {
        if (sizeMB <= 0)
        {
            PrintError("Partition size must be greater than 0 MB.");
            return;
        }

        IBlockDevice? dev = StorageManager.GetDevice(diskNum);
        if (dev == null)
        {
            PrintError("Invalid disk number.");
            return;
        }

        bool isGpt = GPT.IsGPT(dev);
        bool isMbr = !isGpt && MBR.IsMBR(dev);
        if (!isGpt && !isMbr)
        {
            PrintError("Disk has no partition table. Run 'mkmbr " + diskNum + "' or 'mkgpt " + diskNum + "' first.");
            return;
        }

        ulong firstUsable = isGpt ? 34UL : 2048UL;
        ulong sectorsPerMB = (1024UL * 1024UL) / dev.BlockSize;
        ulong sectorCount = (ulong)sizeMB * sectorsPerMB;

        ulong startSector;
        if (startSectorOrAuto.HasValue)
        {
            startSector = startSectorOrAuto.Value;
            if (startSector < firstUsable)
            {
                PrintError("start_lba must be >= " + firstUsable + " on " + (isGpt ? "GPT" : "MBR") + " disks.");
                return;
            }
            // Reject overlap with any existing partition on the same disk.
            for (int i = 0; i < StorageManager.Partitions.Count; i++)
            {
                Partition p = StorageManager.Partitions[i];
                if (!ReferenceEquals(p.Host, dev))
                {
                    continue;
                }
                ulong pEnd = p.StartingSector + p.BlockCount;
                if (startSector < pEnd && startSector + sectorCount > p.StartingSector)
                {
                    PrintError("Range overlaps partition [" + p.StartingSector + ".." + pEnd + ").");
                    return;
                }
            }
        }
        else
        {
            startSector = firstUsable;
            for (int i = 0; i < StorageManager.Partitions.Count; i++)
            {
                Partition p = StorageManager.Partitions[i];
                if (!ReferenceEquals(p.Host, dev))
                {
                    continue;
                }
                ulong end = p.StartingSector + p.BlockCount;
                if (end > startSector)
                {
                    startSector = end;
                }
            }
        }

        if (startSector + sectorCount > dev.BlockCount)
        {
            PrintError("Partition does not fit on disk.");
            return;
        }

        if (!PartitionManager.Create(dev, startSector, sectorCount, mbrSystemId: 0x0B, gptType: GPT.BasicDataPartitionType))
        {
            PrintError("Failed to create partition (no free slot or bad geometry).");
            return;
        }

        StorageManager.RescanPartitions(dev);
        PrintSuccess("Partition created at LBA " + startSector + " (" + sectorCount + " sectors).");
    }

    private void DeletePartitionEntry(int partNum)
    {
        IReadOnlyList<Partition> partitions = StorageManager.Partitions;
        if (partNum < 0 || partNum >= partitions.Count)
        {
            PrintError("Invalid partition number. Use 'partitions' to list.");
            return;
        }

        Partition part = partitions[partNum];
        IBlockDevice host = part.Host;
        ulong start = part.StartingSector;
        ulong count = part.BlockCount;

        if (!PartitionManager.Delete(host, new PartitionManager.PartitionLocation(start, count)))
        {
            PrintError("Failed to delete partition.");
            return;
        }

        StorageManager.RescanPartitions(host);
        PrintSuccess("Partition " + partNum + " deleted (LBA " + start + ", " + count + " sectors).");
    }

    private void FormatPartition(int partNum, string fsType)
    {
        IReadOnlyList<Partition> partitions = StorageManager.Partitions;
        if (partNum < 0 || partNum >= partitions.Count)
        {
            PrintError("Invalid partition number. Use 'partitions' to list.");
            return;
        }

        // FAT family: accept fat / fat12 / fat16 / fat32 — all dispatched to
        // the "fat" driver with a FatFormatOptions hint.
        string driverName;
        IVfsFormatOptions? options = null;
        switch (fsType)
        {
            case "fat":
                driverName = "fat";
                break;
            case "fat12":
                driverName = "fat";
                options = new FatFormatOptions { Type = FatType.Fat12 };
                break;
            case "fat16":
                driverName = "fat";
                options = new FatFormatOptions { Type = FatType.Fat16 };
                break;
            case "fat32":
                driverName = "fat";
                options = new FatFormatOptions { Type = FatType.Fat32 };
                break;
            default:
                PrintError("Unknown filesystem: " + fsType + ". Supported: fat, fat12, fat16, fat32.");
                return;
        }

        if (!VfsManager.TryFormat(driverName, partNum.ToString(), options))
        {
            // Driver is registered (fat is wired at boot), so a false return
            // means the formatter rejected the request — almost always because
            // the partition is too small for the requested variant. FAT32
            // needs > 65525 clusters; FAT16 needs > 4084.
            Partition target = partitions[partNum];
            ulong sizeMiB = target.BlockCount * target.BlockSize / 1024 / 1024;
            PrintError("Format failed: partition is likely too small for " + fsType.ToUpper() +
                " (" + sizeMiB + " MiB). Try 'format " + partNum + " fat' to auto-pick a variant.");
            return;
        }

        PrintSuccess("Partition " + partNum + " formatted as " + fsType.ToUpper() + ".");

        // Warn if any mount likely targets this partition. We can't (yet)
        // ask VfsManager which superblock backs which IBlockDevice, so this
        // fires whenever there's any mount — better a stray warning than a
        // confused user staring at the pre-format files in /mnt.
        if (VfsManager.Mounts.Count > 0)
        {
            PrintWarning("If this partition is currently mounted, its cached state is now stale. Reboot to pick up the fresh layout.");
        }
    }

    private void MountPartition(int partNum, string mountPoint)
    {
        IReadOnlyList<Partition> partitions = StorageManager.Partitions;
        if (partNum < 0 || partNum >= partitions.Count)
        {
            PrintError("Invalid partition number.");
            return;
        }

        if (string.IsNullOrEmpty(mountPoint) || mountPoint[0] != '/')
        {
            PrintError("Mount point must be an absolute path (e.g. /mnt).");
            return;
        }

        if (!VfsManager.TryMount("fat", partNum.ToString(), MountFlags.None, mountPoint, out _))
        {
            PrintError("Mount failed (not FAT or unreadable).");
            return;
        }

        PrintSuccess("Partition " + partNum + " mounted at " + mountPoint);
    }


    private void ShowMountPoints()
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Mounted Filesystems:");
        Console.ResetColor();

        IReadOnlyList<VfsManager.VfsMount> mounts = VfsManager.Mounts;
        if (mounts.Count == 0)
        {
            PrintWarning("No filesystems mounted.");
            return;
        }

        for (int i = 0; i < mounts.Count; i++)
        {
            VfsManager.VfsMount m = mounts[i];
            Console.Write("  ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(m.MountPoint);
            Console.ResetColor();
            Console.Write(" -> ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(m.Name);
            Console.ResetColor();

            if (m.Superblock.SuperOperations.StatFs(m.Superblock, out VfsStatFs sf))
            {
                ulong totalBytes = sf.Blocks * sf.BlockSize;
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write(" (" + (totalBytes / 1024 / 1024) + " MiB)");
                Console.ResetColor();
            }
            Console.WriteLine();
        }
    }

    private void PrintNoMountHelp()
    {
        PrintWarning("No filesystem mounted.");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("To mount a filesystem and use 'ls':");
        Console.ResetColor();
        Console.WriteLine("  1. diskinfo               - show attached disks");
        Console.WriteLine("  2. partitions             - list partitions on each disk");
        Console.WriteLine("  3. mkgpt <d>              - if disk has no partition table");
        Console.WriteLine("  4. mkpart <d> <mb>        - create a partition of <mb> MiB (or 'mkpart <d> <start> <mb>')");
        Console.WriteLine("  5. format <p> [fs]        - format partition <p> (fs: fat | fat12 | fat16 | fat32)");
        Console.WriteLine("  6. mount <p> <mountpoint> - mount partition <p> at any path (e.g. /mnt)");
        Console.WriteLine("  7. cd <mountpoint>        - change into it, then 'ls'");
    }

    private void PrintAvailableMounts()
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Available mount points (cd into one):");
        Console.ResetColor();

        IReadOnlyList<VfsManager.VfsMount> mounts = VfsManager.Mounts;
        for (int i = 0; i < mounts.Count; i++)
        {
            VfsManager.VfsMount m = mounts[i];
            Console.Write("  ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(m.MountPoint);
            Console.ResetColor();
            Console.Write(" (");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(m.Name);
            Console.ResetColor();
            Console.WriteLine(")");
        }
    }

    private void ChangeDirectory(string path)
    {
        string target = NormalizePath(ResolvePath(path));

        if (target != "/" && (!VfsManager.TryOpenDirectory(target, out IVfsDirectoryHandle? dir) || dir == null))
        {
            PrintError("No such directory: " + target);
            return;
        }

        _cwd = target;
    }

    private string ResolvePath(string path)
    {
        if (path.Length > 0 && path[0] == '/')
        {
            return path;
        }
        if (_cwd == "/")
        {
            return "/" + path;
        }
        return _cwd + "/" + path;
    }

    private static string NormalizePath(string path)
    {
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        List<string> stack = new();
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            if (p == ".")
            {
                continue;
            }
            if (p == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }
                continue;
            }
            stack.Add(p);
        }
        if (stack.Count == 0)
        {
            return "/";
        }
        return "/" + string.Join("/", stack);
    }

    private static (string parent, string leaf) SplitPath(string fullPath)
    {
        int slash = fullPath.LastIndexOf('/');
        if (slash <= 0)
        {
            return ("/", fullPath.TrimStart('/'));
        }
        return (fullPath.Substring(0, slash), fullPath.Substring(slash + 1));
    }

    private void ListDirectory(string path)
    {
        if (VfsManager.Mounts.Count == 0)
        {
            PrintNoMountHelp();
            return;
        }

        string fullPath = NormalizePath(ResolvePath(path));

        // No mount lives at "/" itself, so plain `ls` would fail even when
        // filesystems are mounted at sub-paths. List the available mount
        // points so the user can `cd` into one rather than see a cryptic
        // "Cannot open directory: /".
        if (fullPath == "/" && !VfsManager.TryGetMount("/", out _))
        {
            PrintAvailableMounts();
            return;
        }

        if (!VfsManager.TryOpenDirectory(fullPath, out IVfsDirectoryHandle? dir) || dir == null)
        {
            PrintError("Cannot open directory: " + fullPath);
            return;
        }

        List<VfsDirectoryEntry> entries = new();
        if (!dir.TryReadDir(entries))
        {
            PrintError("ReadDir failed.");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Contents of " + fullPath + ":");
        Console.ResetColor();

        if (entries.Count == 0)
        {
            PrintWarning("  (empty)");
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            VfsDirectoryEntry e = entries[i];
            Console.Write("  ");
            if (e.IsDirectory)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("[DIR] ");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("[FILE]");
            }
            Console.ResetColor();
            Console.Write(" ");
            Console.Write(e.Name.PadRight(24));
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(e.Size.ToString().PadLeft(10));
            Console.Write(" bytes");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    private void DisplayFileContents(string path)
    {
        if (VfsManager.Mounts.Count == 0)
        {
            PrintNoMountHelp();
            return;
        }
        string fullPath = ResolvePath(path);
        if (!VfsManager.TryOpenFile(fullPath, out IVfsFileHandle? file) || file == null)
        {
            PrintError("File not found: " + fullPath);
            return;
        }

        try
        {
            Span<byte> buffer = stackalloc byte[512];
            while (true)
            {
                long n = file.Read(buffer);
                if (n <= 0)
                {
                    break;
                }
                for (int i = 0; i < n; i++)
                {
                    char c = (char)buffer[i];
                    if (c == '\n' || c == '\r' || (c >= 32 && c < 127))
                    {
                        Console.Write(c.ToString());
                    }
                    else
                    {
                        Console.Write('.');
                    }
                }
            }
            Console.WriteLine();
        }
        finally
        {
            file.Dispose();
        }
    }

    private void CreateDirectory(string path)
    {
        if (VfsManager.Mounts.Count == 0)
        {
            PrintNoMountHelp();
            return;
        }
        string fullPath = ResolvePath(path);
        (string parent, string leaf) = SplitPath(fullPath);
        if (string.IsNullOrEmpty(leaf))
        {
            PrintError("Invalid path.");
            return;
        }

        if (!VfsManager.TryOpenDirectory(parent, out IVfsDirectoryHandle? parentDir) || parentDir == null)
        {
            PrintError("Parent directory not found: " + parent);
            return;
        }

        if (!parentDir.TryCreateDirectory(leaf, ModeEnum.Directory | ModeEnum.OwnerRead | ModeEnum.OwnerWrite | ModeEnum.OwnerExecute, out _))
        {
            PrintError("Mkdir failed.");
            return;
        }

        PrintSuccess("Directory created: " + fullPath);
    }

    private void CreateEmptyFile(string path)
    {
        if (VfsManager.Mounts.Count == 0)
        {
            PrintNoMountHelp();
            return;
        }
        string fullPath = ResolvePath(path);
        (string parent, string leaf) = SplitPath(fullPath);
        if (string.IsNullOrEmpty(leaf))
        {
            PrintError("Invalid path.");
            return;
        }

        if (!VfsManager.TryOpenDirectory(parent, out IVfsDirectoryHandle? parentDir) || parentDir == null)
        {
            PrintError("Parent directory not found: " + parent);
            return;
        }

        if (!parentDir.TryCreateFile(leaf, ModeEnum.RegularFile | ModeEnum.OwnerRead | ModeEnum.OwnerWrite, out _))
        {
            PrintError("Create file failed.");
            return;
        }

        PrintSuccess("File created: " + fullPath);
    }

    private void DeleteEntry(string path)
    {
        if (VfsManager.Mounts.Count == 0)
        {
            PrintNoMountHelp();
            return;
        }
        string fullPath = ResolvePath(path);
        (string parent, string leaf) = SplitPath(fullPath);
        if (string.IsNullOrEmpty(leaf))
        {
            PrintError("Invalid path.");
            return;
        }

        if (!VfsManager.TryOpenDirectory(parent, out IVfsDirectoryHandle? parentDir) || parentDir == null)
        {
            PrintError("Parent directory not found: " + parent);
            return;
        }

        if (parentDir.TryUnlink(leaf))
        {
            PrintSuccess("File deleted: " + fullPath);
            return;
        }

        if (parentDir.TryRemoveDirectory(leaf))
        {
            PrintSuccess("Directory deleted: " + fullPath);
            return;
        }

        PrintError("Path not found or not empty: " + fullPath);
    }

    private void WriteToFile(string path, string text)
    {
        if (VfsManager.Mounts.Count == 0)
        {
            PrintNoMountHelp();
            return;
        }
        string fullPath = ResolvePath(path);

        if (!VfsManager.TryOpenFile(fullPath, out IVfsFileHandle? file) || file == null)
        {
            (string parent, string leaf) = SplitPath(fullPath);
            if (string.IsNullOrEmpty(leaf)
                || !VfsManager.TryOpenDirectory(parent, out IVfsDirectoryHandle? parentDir)
                || parentDir == null
                || !parentDir.TryCreateFile(leaf, ModeEnum.RegularFile | ModeEnum.OwnerRead | ModeEnum.OwnerWrite, out _)
                || !VfsManager.TryOpenFile(fullPath, out file)
                || file == null)
            {
                PrintError("Cannot open or create: " + fullPath);
                return;
            }
        }

        try
        {
            byte[] bytes = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                bytes[i] = (byte)text[i];
            }
            long written = file.Write(bytes);
            file.Flush();
            PrintSuccess("Wrote " + written + " bytes to " + fullPath);
        }
        finally
        {
            file.Dispose();
        }
    }

    private static string JoinFrom(string[] parts, int start)
    {
        if (start >= parts.Length)
        {
            return string.Empty;
        }
        string result = parts[start];
        for (int i = start + 1; i < parts.Length; i++)
        {
            result = result + " " + parts[i];
        }
        return result;
    }

    private void PrintInfo(string message)
    {
        Console.WriteLine(message);
    }

    private void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private void PrintWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    // Static cursor pattern - allocated once, reused every frame
    private static readonly int[] s_cursorPattern = new int[]
    {
        // Row by row pattern for arrow cursor
        // Pattern: 1 = border (black), 2 = fill (white), 0 = transparent
        1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 1, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 2, 1, 0, 0, 0, 0, 0, 0, 0,
        1, 2, 2, 1, 0, 0, 0, 0, 0, 0,
        1, 2, 2, 2, 1, 0, 0, 0, 0, 0,
        1, 2, 2, 2, 2, 1, 0, 0, 0, 0,
        1, 2, 2, 2, 2, 2, 1, 0, 0, 0,
        1, 2, 2, 2, 2, 2, 2, 1, 0, 0,
        1, 2, 2, 2, 2, 2, 2, 2, 1, 0,
        1, 2, 2, 2, 2, 2, 2, 2, 2, 1,
        1, 2, 2, 2, 2, 2, 1, 1, 1, 1,
        1, 2, 2, 1, 2, 2, 1, 0, 0, 0,
        1, 2, 1, 0, 1, 2, 2, 1, 0, 0,
        1, 1, 0, 0, 1, 2, 2, 1, 0, 0,
        1, 0, 0, 0, 0, 1, 2, 2, 1, 0,
        0, 0, 0, 0, 0, 1, 1, 1, 1, 0,
    };

    private const int CursorWidth = 10;
    private const int CursorHeight = 16;

    /// <summary>
    /// Draws a simple arrow mouse cursor.
    /// </summary>
    private static void DrawMouseCursor(Canvas canvas, int x, int y)
    {

        for (int cy = 0; cy < CursorHeight; cy++)
        {
            for (int cx = 0; cx < CursorWidth; cx++)
            {
                int px = x + cx;
                int py = y + cy;

                // Bounds check
                if (px < 0 || px >= canvas.Mode.Width || py < 0 || py >= canvas.Mode.Height)
                {
                    continue;
                }

                int pixel = s_cursorPattern[cy * CursorWidth + cx];
                if (pixel == 1)
                {
                    // Border (black)
                    canvas.DrawPoint(Color.Black, px, py);
                }
                else if (pixel == 2)
                {
                    // Fill (white)
                    canvas.DrawPoint(Color.White, px, py);
                }
                // pixel == 0: transparent, don't draw
            }
        }
    }
}
