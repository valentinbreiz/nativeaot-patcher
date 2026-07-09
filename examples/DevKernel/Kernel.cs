using System;
using System.Diagnostics;
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
    /// <summary>Length of the "echo " command prefix stripped before echoing the argument text.</summary>
    private const int EchoPrefixLength = 5;
    /// <summary>Column width (chars) used to pad command names and info labels in shell output.</summary>
    private const int LabelColumnWidth = 14;
    /// <summary>Column width (chars) used to pad the labels of the diskinfo listing.</summary>
    private const int DiskLabelColumnWidth = 17;
    /// <summary>Column width (chars) used to left-pad GC configuration variable names in the gcvar listing.</summary>
    private const int GcVarNameColumnWidth = 15;
    /// <summary>Field alignment (chars) for byte-count values in the live GC info overlay.</summary>
    private const int GcInfoValueAlignment = 15;
    /// <summary>Field alignment (chars) for the GC time percentage in the live GC info overlay.</summary>
    private const int GcInfoPercentAlignment = 3;
    /// <summary>Field alignment (chars) for collection/object counters in the live GC info overlay.</summary>
    private const int GcInfoCountAlignment = 6;

    /// <summary>Milliseconds in one second; delay of each countdown step of the timer test.</summary>
    private const uint OneSecondMs = 1000;
    /// <summary>Number of seconds counted down by the timer test.</summary>
    private const int CountdownStartSeconds = 10;
    /// <summary>Delay (ms) after starting the test thread so its output can appear.</summary>
    private const uint ThreadTestWaitMs = 2000;
    /// <summary>Timeout (ms) when waiting for a DNS response.</summary>
    private const int DnsReceiveTimeoutMs = 5000;
    /// <summary>Delay (ms) between refreshes of the live GC info overlay.</summary>
    private const int GcInfoFrameDelayMs = 250;
    /// <summary>Delay (ms) between frames drawn by the graphics worker thread.</summary>
    private const int GraphicsFrameDelayMs = 100;

    /// <summary>Bytes per kibibyte; applied twice for MiB conversions.</summary>
    private const ulong BytesPerKiB = 1024;
    /// <summary>Nanoseconds per millisecond, for converting scheduler times to ms.</summary>
    private const ulong NsPerMs = 1_000_000;
    /// <summary>Scale factor for expressing a ratio as a percentage.</summary>
    private const ulong PercentScale = 100;
    /// <summary>Memory usage percentage below which usage is shown in green.</summary>
    private const ulong MemoryUsageWarnPercent = 50;
    /// <summary>Memory usage percentage below which usage is shown in yellow (red at or above).</summary>
    private const ulong MemoryUsageCriticalPercent = 80;

    /// <summary>Left/top margin (pixels) of text drawn on graphics overlays.</summary>
    private const int TextMarginPx = 10;
    /// <summary>Vertical spacing (pixels) added below the font height for each text row.</summary>
    private const int LineSpacingPx = 2;
    /// <summary>Width (pixels) of the startx info panel.</summary>
    private const int PanelWidthPx = 360;
    /// <summary>Number of text rows in the startx info panel.</summary>
    private const int PanelRowCount = 9;
    /// <summary>Extra vertical padding (pixels) of the startx info panel.</summary>
    private const int PanelPaddingPx = 8;
    /// <summary>Frame interval at which the startx loop triggers a heap collection.</summary>
    private const int GfxGcCollectFrameInterval = 100;
    /// <summary>Frame interval at which the live GC info overlay collects and samples Gen0 stats.</summary>
    private const int GcInfoCollectFrameInterval = 50;

    /// <summary>Number of phases in the graphics worker color cycle (six 10-phase segments).</summary>
    private const int ColorCyclePhaseCount = 60;
    /// <summary>Number of phases per color-cycle segment (one channel ramp).</summary>
    private const int PhaseSegmentLength = 10;
    /// <summary>Color channel increment per phase (~255 / 10) within a segment.</summary>
    private const int PhaseColorStep = 25;
    /// <summary>Maximum value of an 8-bit color channel.</summary>
    private const int ColorChannelMax = 255;
    /// <summary>Bit position of the red channel in a 0x00RRGGBB pixel value.</summary>
    private const int RedShiftBits = 16;
    /// <summary>Bit position of the green channel in a 0x00RRGGBB pixel value.</summary>
    private const int GreenShiftBits = 8;

    /// <summary>First octet of the QEMU user-networking (SLIRP) 10.0.2.0/24 subnet.</summary>
    private const byte QemuNetOctet1 = 10;
    /// <summary>Second octet of the QEMU user-networking (SLIRP) 10.0.2.0/24 subnet.</summary>
    private const byte QemuNetOctet2 = 0;
    /// <summary>Third octet of the QEMU user-networking (SLIRP) 10.0.2.0/24 subnet.</summary>
    private const byte QemuNetOctet3 = 2;
    /// <summary>Host octet of the default QEMU guest IP (10.0.2.15).</summary>
    private const byte QemuGuestHostOctet = 15;
    /// <summary>Host octet of the QEMU user-networking gateway IP (10.0.2.2).</summary>
    private const byte QemuGatewayHostOctet = 2;
    /// <summary>Fully-masked octet of the /24 subnet mask (255.255.255.0).</summary>
    private const byte SubnetMaskFullOctet = 255;
    /// <summary>Unmasked host octet of the /24 subnet mask (255.255.255.0).</summary>
    private const byte SubnetMaskHostOctet = 0;
    /// <summary>Octet value of the Cloudflare public DNS resolver 1.1.1.1.</summary>
    private const byte CloudflareDnsOctet = 1;
    /// <summary>UDP port used for the netsend/netlisten test traffic.</summary>
    private const ushort TestUdpPort = 5555;
    /// <summary>First printable ASCII code (space); lower bound of the payload dump filter.</summary>
    private const int AsciiPrintableMin = 32;
    /// <summary>ASCII DEL code; exclusive upper bound of the payload dump filter.</summary>
    private const int AsciiPrintableLimit = 127;
    /// <summary>Maximum UDP payload bytes echoed to the console per packet.</summary>
    private const int UdpPreviewMaxBytes = 64;

    /// <summary>Number of bytes hex-dumped by the diskread command.</summary>
    private const int HexDumpLengthBytes = 64;
    /// <summary>Number of bytes shown per line of the diskread hex dump.</summary>
    private const int HexDumpBytesPerLine = 16;

    /// <summary>First usable LBA on a GPT disk (past the protective MBR, header, and entry array).</summary>
    private const ulong GptFirstUsableLba = 34;
    /// <summary>Conventional first partition LBA on an MBR disk (1 MiB alignment).</summary>
    private const ulong MbrFirstPartitionLba = 2048;
    /// <summary>MBR system ID byte (FAT32 LBA) stamped on partitions created by mkpart.</summary>
    private const byte MbrFat32LbaSystemId = 0x0B;
    /// <summary>Column width (chars) used to pad entry names in the ls listing.</summary>
    private const int DirEntryNameColumnWidth = 24;
    /// <summary>Column width (chars) used to right-align file sizes in the ls listing.</summary>
    private const int FileSizeColumnWidth = 10;
    /// <summary>Size (bytes) of each read chunk used by the cat command.</summary>
    private const int CatChunkSizeBytes = 512;

    private string _prompt = "cosmos";

    /// <summary>Current working directory: shown in the prompt and used to resolve relative paths.</summary>
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
        Console.WriteLine("Parameters:");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.Gray;
        foreach (var param in Environment.GetCommandLineArgs())
        {
            Console.Write('\t');
            Console.WriteLine(param);
        }
        Console.ResetColor();

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
                        Console.WriteLine(trimmed.Substring(EchoPrefixLength));
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

                case "diskread":
                    if (parts.Length > 1 && ulong.TryParse(parts[1], out ulong readLba))
                    {
                        DiskRead(readLba);
                    }
                    else
                    {
                        PrintError("Usage: diskread <lba>");
                    }
                    break;

                case "diskwrite":
                    if (parts.Length > 2
                        && ulong.TryParse(parts[1], out ulong writeLba)
                        && byte.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out byte writeByte))
                    {
                        DiskWrite(writeLba, writeByte);
                    }
                    else
                    {
                        PrintError("Usage: diskwrite <lba> <hex-byte>   (e.g. diskwrite 100 A5)");
                    }
                    break;

                case "disktest":
                    DiskTest();
                    break;

                case "lsdisk":
                    ListDisks();
                    break;

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
                    if (parts.Length >= 3
                        && int.TryParse(parts[1], out int rmDisk)
                        && int.TryParse(parts[2], out int rmPart))
                    {
                        DeletePartitionEntry(rmDisk, rmPart);
                    }
                    else
                    {
                        PrintError("Usage: rmpart <disk> <part>");
                    }
                    break;

                case "format":
                    if (parts.Length >= 3
                        && int.TryParse(parts[1], out int fmtDisk)
                        && int.TryParse(parts[2], out int fmtPart))
                    {
                        string fmtType = parts.Length >= 4 ? parts[3].ToLower() : "fat";
                        FormatPartition(fmtDisk, fmtPart, fmtType);
                    }
                    else
                    {
                        PrintError("Usage: format <disk> <part> [fs_type]   (default: fat)");
                    }
                    break;

                case "mount":
                    if (parts.Length >= 4
                        && int.TryParse(parts[1], out int mntDisk)
                        && int.TryParse(parts[2], out int mntPart))
                    {
                        MountPartition(mntDisk, mntPart, parts[3]);
                    }
                    else
                    {
                        PrintError("Usage: mount <disk> <part> <mountpoint>   (e.g. mount 0 0 /mnt)");
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
                        Console.WriteLine(varable.Key.PadLeft(GcVarNameColumnWidth) + ":" + varable.Value.ToString());
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

                    int x = TextMarginPx;
                    int y = TextMarginPx;
                    int lineHeight = font.Height + LineSpacingPx;
                    int panelWidth = PanelWidthPx;
                    int panelHeight = lineHeight * PanelRowCount + PanelPaddingPx;

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
                        canvas.DrawString("Total: " + (totalBytes / BytesPerKiB / BytesPerKiB) + " MB", font, Color.White, x, rowY);
                        rowY += lineHeight;
                        canvas.DrawString("Used : " + (usedBytes / BytesPerKiB / BytesPerKiB) + " MB", font, Color.White, x, rowY);
                        rowY += lineHeight;
                        canvas.DrawString("Free : " + (freeBytes / BytesPerKiB / BytesPerKiB) + " MB", font, Color.White, x, rowY);
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

                        if (frames % GfxGcCollectFrameInterval == 0)
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
            PrintError($"Exception: {ex.Message}");
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
        int x = TextMarginPx;
        int lineHeight = font.Height + LineSpacingPx;

        while (!Console.KeyAvailable || Console.ReadKey(true).Key != ConsoleKey.Escape)
        {
            unchecked
            {
                frames++;
            }

            canvas.Clear(Color.Black);

            //GarbageCollector.SimpleMemoryInfo info = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector.GetSimpleMemoryInfo();

            GCMemoryInfo info = GC.GetGCMemoryInfo();

            int rowY = TextMarginPx;
            canvas.DrawString($"GC Info ({frames})", font, Color.Cyan, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Size values are in bytes, ESC to exit;", font, Color.Cyan, x, rowY);
            rowY += lineHeight;

            commitedMax = Math.Max(commitedMax, info.TotalCommittedBytes);

            canvas.DrawString($"RamSize         : {PageAllocator.RamSize,GcInfoValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"HeapSize        : {info.HeapSizeBytes,GcInfoValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Fragmented      : {info.FragmentedBytes,GcInfoValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Committed       : {info.TotalCommittedBytes,GcInfoValueAlignment}; max size  : {commitedMax,GcInfoValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Promoted        : {info.PromotedBytes,GcInfoValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Pinned          : {info.PinnedObjectsCount,GcInfoValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Collections     : {info.Index,GcInfoValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Condemned gen   : {info.Generation,GcInfoValueAlignment}", font, Color.White, x, rowY);
            rowY += lineHeight;

            // last gen before/after
            canvas.DrawString($"Gen0 size before: {sizeBefore,GcInfoValueAlignment}; size after: {sizeAfter,GcInfoValueAlignment}", font, Color.Yellow, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Gen0 size delta : {sizeDelta,GcInfoValueAlignment}; max size  : {maxDeltaSize,GcInfoValueAlignment}", font, Color.Yellow, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Frag size before: {fragBefore,GcInfoValueAlignment}; size after: {fragAfter,GcInfoValueAlignment}", font, Color.Yellow, x, rowY);
            rowY += lineHeight;
            canvas.DrawString($"Frag size delta : {fragAfter - fragBefore,GcInfoValueAlignment}", font, Color.Yellow, x, rowY);
            rowY += lineHeight;

            int pct = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector.GetLastGCPercentTimeInGC();
            Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector.GetStats(out int totalCollections, out int totalObjectsFreed);
            canvas.DrawString($"Last GC % time in GC: {pct,GcInfoPercentAlignment}%, Collections: {totalCollections,GcInfoCountAlignment}, Objects Freed: {totalObjectsFreed,GcInfoCountAlignment}", font, Color.Green, x, rowY); rowY += lineHeight;

            if (frames % GcInfoCollectFrameInterval == 0)
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
            System.Threading.Thread.Sleep(GcInfoFrameDelayMs);
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
        PrintCommand("diskread <lba>", "Read 1 block, hex-dump first 64 bytes");
        PrintCommand("diskwrite <lba> <hex>", "Fill 1 block with byte value (e.g. A5) and write");
        PrintCommand("disktest", "Run a quick write/read roundtrip on the primary disk");
        PrintCommand("lsdisk", "Show storage devices and partition table type");
        PrintCommand("lspart", "List partitions, grouped under each disk");
        PrintCommand("mkmbr <n>", "Write a fresh empty MBR to disk n");
        PrintCommand("mkgpt <n>", "Write a fresh empty GPT to disk n");
        PrintCommand("mkpart <n> [start] <mb>", "Create a partition on disk n (start LBA optional)");
        PrintCommand("rmpart <d> <p>", "Delete partition p on disk d");
        PrintCommand("format <d> <p> [fs]", "Format disk d partition p (fs: fat | fat12 | fat16 | fat32, default fat)");
        PrintCommand("mount <d> <p> <path>", "Mount disk d partition p at <path> (e.g. mount 0 0 /mnt)");
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
        Console.Write(cmd.PadRight(LabelColumnWidth));
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
        Console.Write(label.PadRight(LabelColumnWidth));
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(value);
        Console.ResetColor();
    }

    private void RunTimerTest()
    {
        Console.WriteLine("Starting 10 second countdown...");
        for (int i = CountdownStartSeconds; i > 0; i--)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(i.ToString());
            Console.ResetColor();
            Console.WriteLine("...");
            TimerManager.Wait(OneSecondMs);
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

        PrintInfoLine("Page Size", (pageSize / BytesPerKiB).ToString() + " KB");
        PrintInfoLine("Total Pages", totalPages.ToString());
        PrintInfoLine("Used Pages", usedPages.ToString());
        PrintInfoLine("Free Pages", freePages.ToString());

        Console.WriteLine();

        // Memory in MB
        PrintInfoLine("Total Memory", (totalBytes / BytesPerKiB / BytesPerKiB).ToString() + " MB");
        PrintInfoLine("Used Memory", (usedBytes / BytesPerKiB / BytesPerKiB).ToString() + " MB");
        PrintInfoLine("Free Memory", (freeBytes / BytesPerKiB / BytesPerKiB).ToString() + " MB");

        // Usage percentage
        ulong usagePercent = totalPages > 0 ? (usedPages * PercentScale) / totalPages : 0;

        Console.WriteLine();
        Console.Write("  ");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("Usage".PadRight(LabelColumnWidth));

        // Color based on usage
        if (usagePercent < MemoryUsageWarnPercent)
        {
            Console.ForegroundColor = ConsoleColor.Green;
        }
        else if (usagePercent < MemoryUsageCriticalPercent)
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
        Console.Write("Status".PadRight(LabelColumnWidth));
        Console.ForegroundColor = SchedulerManager.Enabled ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(SchedulerManager.Enabled ? "ENABLED" : "DISABLED");
        Console.ResetColor();

        PrintInfoLine("Scheduler", scheduler.Name);
        PrintInfoLine("CPU Count", SchedulerManager.CpuCount.ToString());
        PrintInfoLine("Quantum", (SchedulerManager.DefaultQuantumNs / NsPerMs).ToString() + " ms");
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

        ulong runtimeMs = thread.TotalRuntime / NsPerMs;
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

        TimerManager.Wait(ThreadTestWaitMs);
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
            int phase = frame % ColorCyclePhaseCount;
            byte r, g, b;

            if (phase < PhaseSegmentLength) { r = ColorChannelMax; g = (byte)(phase * PhaseColorStep); b = 0; }
            else if (phase < PhaseSegmentLength * 2) { r = (byte)(ColorChannelMax - (phase - PhaseSegmentLength) * PhaseColorStep); g = ColorChannelMax; b = 0; }
            else if (phase < PhaseSegmentLength * 3) { r = 0; g = ColorChannelMax; b = (byte)((phase - PhaseSegmentLength * 2) * PhaseColorStep); }
            else if (phase < PhaseSegmentLength * 4) { r = 0; g = (byte)(ColorChannelMax - (phase - PhaseSegmentLength * 3) * PhaseColorStep); b = ColorChannelMax; }
            else if (phase < PhaseSegmentLength * 5) { r = (byte)((phase - PhaseSegmentLength * 4) * PhaseColorStep); g = 0; b = ColorChannelMax; }
            else { r = ColorChannelMax; g = 0; b = (byte)(ColorChannelMax - (phase - PhaseSegmentLength * 5) * PhaseColorStep); }

            for (int dy = 0; dy < squareSize; dy++)
            {
                for (int dx = 0; dx < squareSize; dx++)
                {
                    int cx = dx - squareSize / 2;
                    int cy = dy - squareSize / 2;
                    int dist = (cx * cx + cy * cy) * ColorChannelMax / (squareSize * squareSize / 2);
                    if (dist > ColorChannelMax)
                    {
                        dist = ColorChannelMax;
                    }

                    int factor = ColorChannelMax - dist / 2;
                    byte pr = (byte)((r * factor) / ColorChannelMax);
                    byte pg = (byte)((g * factor) / ColorChannelMax);
                    byte pb = (byte)((b * factor) / ColorChannelMax);
                    uint pixelColor = (uint)((pr << RedShiftBits) | (pg << GreenShiftBits) | pb);

                    KernelConsole.Default.Canvas.DrawPoint(pixelColor, x + dx, y + dy);
                }
            }

            frame++;
            KernelConsole.Default.Canvas.Display();
            System.Threading.Thread.Sleep(GraphicsFrameDelayMs);
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
        _localIP = new Address(QemuNetOctet1, QemuNetOctet2, QemuNetOctet3, QemuGuestHostOctet);
        _gatewayIP = new Address(QemuNetOctet1, QemuNetOctet2, QemuNetOctet3, QemuGatewayHostOctet);
        var subnet = new Address(SubnetMaskFullOctet, SubnetMaskFullOctet, SubnetMaskFullOctet, SubnetMaskHostOctet);

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
        Console.Write("Link".PadRight(LabelColumnWidth));
        Console.ForegroundColor = device.LinkUp ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(device.LinkUp ? "UP" : "DOWN");

        Console.Write("  ");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("Ready".PadRight(LabelColumnWidth));
        Console.ForegroundColor = device.Ready ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(device.Ready ? "YES" : "NO");

        Console.Write("  ");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("Configured".PadRight(LabelColumnWidth));
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
            TestUdpPort,                         // Source port
            TestUdpPort,                         // Destination port
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
            if (c >= AsciiPrintableMin && c < AsciiPrintableLimit)
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

        for (int i = 0; i < data.Length && i < UdpPreviewMaxBytes; i++)
        {
            char c = (char)data[i];
            if (c >= AsciiPrintableMin && c < AsciiPrintableLimit)
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
        var dnsServer = new Address(CloudflareDnsOctet, CloudflareDnsOctet, CloudflareDnsOctet, CloudflareDnsOctet);
        DNSConfig.Add(dnsServer);

        // Create DNS client and connect
        var dnsClient = new DnsClient();
        dnsClient.Connect(dnsServer);

        // Send query
        dnsClient.SendAsk(domain);

        // Wait for response (5 second timeout)
        Address? resolvedIP = dnsClient.Receive(DnsReceiveTimeoutMs);

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

            ulong totalBytes = dev.BlockCount * dev.BlockSize;
            Console.WriteLine();
            PrintInfoLine($"[{i}] Name".PadRight(DiskLabelColumnWidth), dev.Name);
            PrintInfoLine("    Block Size".PadRight(DiskLabelColumnWidth), dev.BlockSize.ToString() + " B");
            PrintInfoLine("    Block Count".PadRight(DiskLabelColumnWidth), dev.BlockCount.ToString());
            PrintInfoLine("    Capacity".PadRight(DiskLabelColumnWidth), (totalBytes / BytesPerKiB / BytesPerKiB).ToString() + " MiB");
            if (i == 0)
            {
                PrintInfoLine("    Primary".PadRight(DiskLabelColumnWidth), "yes");
            }
        }
    }

    private void DiskRead(ulong lba)
    {
        IBlockDevice? dev = StorageManager.PrimaryDevice;
        if (dev == null)
        {
            PrintError("No primary storage device.");
            return;
        }
        if (lba >= dev.BlockCount)
        {
            PrintError($"LBA {lba} out of range (max {dev.BlockCount - 1}).");
            return;
        }

        Span<byte> buf = new byte[dev.BlockSize];
        dev.ReadBlock(lba, 1, buf);

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"Block {lba} (first 64 bytes):");
        Console.ResetColor();

        int show = (int)Math.Min((ulong)HexDumpLengthBytes, dev.BlockSize);
        for (int i = 0; i < show; i++)
        {
            if (i > 0 && i % HexDumpBytesPerLine == 0)
            {
                Console.WriteLine();
            }
            Console.Write(buf[i].ToString("X2"));
            Console.Write(' ');
        }
        Console.WriteLine();
    }

    private void DiskWrite(ulong lba, byte value)
    {
        IBlockDevice? dev = StorageManager.PrimaryDevice;
        if (dev == null)
        {
            PrintError("No primary storage device.");
            return;
        }
        if (lba >= dev.BlockCount)
        {
            PrintError($"LBA {lba} out of range (max {dev.BlockCount - 1}).");
            return;
        }

        Span<byte> buf = new byte[dev.BlockSize];
        buf.Fill(value);
        dev.WriteBlock(lba, 1, buf);

        PrintSuccess($"Wrote {dev.BlockSize} bytes of 0x{value:X2} to LBA {lba}.");
    }

    private void DiskTest()
    {
        IBlockDevice? dev = StorageManager.PrimaryDevice;
        if (dev == null)
        {
            PrintError("No primary storage device.");
            return;
        }

        const ulong lba = 0xCAFE;
        if (lba >= dev.BlockCount)
        {
            PrintError($"Test LBA {lba} out of range (max {dev.BlockCount - 1}).");
            return;
        }

        // Save the original contents so the roundtrip is non-destructive on
        // whatever image is attached.
        Span<byte> original = new byte[dev.BlockSize];
        dev.ReadBlock(lba, 1, original);

        // Tick-derived seed: with a fixed pattern the test false-passes on
        // repeat runs — after one success the pattern persists on the image,
        // so a driver whose writes silently no-op would still read the stale
        // pattern back.
        byte seed = (byte)Stopwatch.GetTimestamp();
        Span<byte> writeBuf = new byte[dev.BlockSize];
        for (int i = 0; i < (int)dev.BlockSize; i++)
        {
            writeBuf[i] = (byte)(i ^ seed);
        }
        dev.WriteBlock(lba, 1, writeBuf);

        Span<byte> readBuf = new byte[dev.BlockSize];
        dev.ReadBlock(lba, 1, readBuf);

        int mismatch = -1;
        for (int i = 0; i < (int)dev.BlockSize; i++)
        {
            if (writeBuf[i] != readBuf[i])
            {
                mismatch = i;
                break;
            }
        }

        // Restore before reporting so even a failed compare leaves the
        // image as we found it.
        dev.WriteBlock(lba, 1, original);

        if (mismatch >= 0)
        {
            PrintError($"Mismatch at byte {mismatch}: wrote 0x{writeBuf[mismatch]:X2}, read 0x{readBuf[mismatch]:X2}.");
            return;
        }
        PrintSuccess($"Disk W/R roundtrip OK at LBA {lba} ({dev.BlockSize} bytes, seed 0x{seed:X2}), original contents restored.");
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

    private void ListDisks()
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
                PrintInfoLine("    Primary".PadRight(DiskLabelColumnWidth), "yes");
            }

            int diskPartCount = 0;
            for (int p = 0; p < partitions.Count; p++)
            {
                Partition part = partitions[p];
                if (!ReferenceEquals(part.Host, dev))
                {
                    continue;
                }

                int localIndex = diskPartCount;
                diskPartCount++;
                ulong sizeBytes = part.BlockCount * part.BlockSize;
                Console.Write("        ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("[" + localIndex + "] ");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(part.Name);
                Console.ResetColor();
                Console.Write("  Start=" + part.StartSector);
                Console.Write("  Sectors=" + part.BlockCount);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("  " + (sizeBytes / BytesPerKiB / BytesPerKiB) + " MiB");
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
        PrintInfoLine($"[{index}] Name".PadRight(DiskLabelColumnWidth), dev.Name);
        if (detailed)
        {
            PrintInfoLine("    Block Size".PadRight(DiskLabelColumnWidth), dev.BlockSize.ToString() + " B");
        }
        PrintInfoLine("    Sectors".PadRight(DiskLabelColumnWidth), dev.BlockCount.ToString());
        PrintInfoLine("    Capacity".PadRight(DiskLabelColumnWidth), (totalBytes / BytesPerKiB / BytesPerKiB).ToString() + " MiB");

        string table;
        if (Gpt.IsGpt(dev))
        {
            table = "GPT";
        }
        else if (Mbr.IsMbr(dev))
        {
            table = "MBR";
        }
        else
        {
            table = "None";
        }
        PrintInfoLine("    Table".PadRight(DiskLabelColumnWidth), table);
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

        Mbr.Create(dev);
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

        Gpt.Create(dev);
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

        bool isGpt = Gpt.IsGpt(dev);
        bool isMbr = !isGpt && Mbr.IsMbr(dev);
        if (!isGpt && !isMbr)
        {
            PrintError("Disk has no partition table. Run 'mkmbr " + diskNum + "' or 'mkgpt " + diskNum + "' first.");
            return;
        }

        ulong firstUsable = isGpt ? GptFirstUsableLba : MbrFirstPartitionLba;
        ulong sectorsPerMB = BytesPerKiB * BytesPerKiB / dev.BlockSize;
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
                ulong pEnd = p.StartSector + p.BlockCount;
                if (startSector < pEnd && startSector + sectorCount > p.StartSector)
                {
                    PrintError("Range overlaps partition [" + p.StartSector + ".." + pEnd + ").");
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
                ulong end = p.StartSector + p.BlockCount;
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

        if (!PartitionManager.Create(dev, startSector, sectorCount, mbrSystemId: MbrFat32LbaSystemId, gptType: Gpt.BasicDataPartitionType))
        {
            PrintError("Failed to create partition (no free slot or bad geometry).");
            return;
        }

        StorageManager.RescanPartitions(dev);
        PrintSuccess("Partition created at LBA " + startSector + " (" + sectorCount + " sectors).");
    }

    /// <summary>
    /// Resolve a (disk, per-disk part) pair to (global index, Partition).
    /// The global index is what VfsManager / PartitionManager expect under
    /// the hood; per-disk numbering is what the user sees.
    /// </summary>
    private static bool TryResolvePartition(int diskNum, int partNum, out int globalIndex, out Partition? partition)
    {
        globalIndex = -1;
        partition = null;

        IBlockDevice? dev = StorageManager.GetDevice(diskNum);
        if (dev == null)
        {
            return false;
        }

        int local = 0;
        IReadOnlyList<Partition> all = StorageManager.Partitions;
        for (int i = 0; i < all.Count; i++)
        {
            Partition p = all[i];
            if (!ReferenceEquals(p.Host, dev))
            {
                continue;
            }
            if (local == partNum)
            {
                globalIndex = i;
                partition = p;
                return true;
            }
            local++;
        }
        return false;
    }

    private void DeletePartitionEntry(int diskNum, int partNum)
    {
        if (!TryResolvePartition(diskNum, partNum, out _, out Partition? part) || part == null)
        {
            PrintError("Invalid disk/partition. Use 'lspart' to list.");
            return;
        }

        IBlockDevice host = part.Host;
        ulong start = part.StartSector;
        ulong count = part.BlockCount;

        if (!PartitionManager.Delete(host, new PartitionManager.PartitionLocation(start, count)))
        {
            PrintError("Failed to delete partition.");
            return;
        }

        StorageManager.RescanPartitions(host);
        PrintSuccess("Disk " + diskNum + " partition " + partNum + " deleted (LBA " + start + ", " + count + " sectors).");
    }

    private void FormatPartition(int diskNum, int partNum, string fsType)
    {
        if (!TryResolvePartition(diskNum, partNum, out int globalIndex, out Partition? target) || target == null)
        {
            PrintError("Invalid disk/partition. Use 'lspart' to list.");
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

        if (!VfsManager.TryFormat(driverName, globalIndex.ToString(), options))
        {
            ulong sizeMiB = target.BlockCount * target.BlockSize / BytesPerKiB / BytesPerKiB;
            PrintError("Format failed: partition is likely too small for " + fsType.ToUpper() +
                " (" + sizeMiB + " MiB). Try 'format " + diskNum + " " + partNum + " fat' to auto-pick a variant.");
            return;
        }

        PrintSuccess("Disk " + diskNum + " partition " + partNum + " formatted as " + fsType.ToUpper() + ".");

        // Warn if any mount likely targets this partition. We can't (yet)
        // ask VfsManager which superblock backs which IBlockDevice, so this
        // fires whenever there's any mount — better a stray warning than a
        // confused user staring at the pre-format files in /mnt.
        if (VfsManager.Mounts.Count > 0)
        {
            PrintWarning("If this partition is currently mounted, its cached state is now stale. Reboot to pick up the fresh layout.");
        }
    }

    private void MountPartition(int diskNum, int partNum, string mountPoint)
    {
        if (!TryResolvePartition(diskNum, partNum, out int globalIndex, out Partition? _))
        {
            PrintError("Invalid disk/partition. Use 'lspart' to list.");
            return;
        }

        if (string.IsNullOrEmpty(mountPoint) || mountPoint[0] != '/')
        {
            PrintError("Mount point must be an absolute path (e.g. /mnt).");
            return;
        }

        if (!VfsManager.TryMount("fat", globalIndex.ToString(), MountFlags.None, mountPoint, out _))
        {
            PrintError("Mount failed (not FAT or unreadable).");
            return;
        }

        PrintSuccess("Disk " + diskNum + " partition " + partNum + " mounted at " + mountPoint);
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

            // For the FAT driver, m.Source is the global partition index in
            // StorageManager.Partitions. Turn that into the per-disk view
            // ('disk D part P  PartitionName  FAT32') the user reasons about.
            if (int.TryParse(m.Source, out int globalIdx)
                && globalIdx >= 0
                && globalIdx < StorageManager.Partitions.Count)
            {
                Partition p = StorageManager.Partitions[globalIdx];
                int diskIdx = -1;
                int localIdx = 0;
                int seen = 0;
                for (int d = 0; d < StorageManager.DeviceCount; d++)
                {
                    IBlockDevice? dev = StorageManager.GetDevice(d);
                    if (dev == null) { continue; }
                    int local = 0;
                    for (int g = 0; g < StorageManager.Partitions.Count; g++)
                    {
                        if (!ReferenceEquals(StorageManager.Partitions[g].Host, dev)) { continue; }
                        if (g == globalIdx)
                        {
                            diskIdx = d;
                            localIdx = local;
                            seen = 1;
                            break;
                        }
                        local++;
                    }
                    if (seen != 0) { break; }
                }

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("disk " + diskIdx + " part " + localIdx);
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("  " + p.Name);
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write("  " + DetectFilesystem(p));
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(m.Name);
                Console.ResetColor();
            }

            if (m.Superblock.SuperOperations.StatFs(m.Superblock, out VfsStatFs sf))
            {
                ulong totalBytes = sf.Blocks * sf.BlockSize;
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write(" (" + (totalBytes / BytesPerKiB / BytesPerKiB) + " MiB)");
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
        Console.WriteLine("  1. lsdisk                 - show attached disks");
        Console.WriteLine("  2. lspart                 - list partitions on each disk");
        Console.WriteLine("  3. mkgpt <d>              - if disk has no partition table");
        Console.WriteLine("  4. mkpart <d> <mb>        - create a partition of <mb> MiB (or 'mkpart <d> <start> <mb>')");
        Console.WriteLine("  5. format <d> <p> [fs]    - format disk <d> partition <p> (fs: fat | fat12 | fat16 | fat32)");
        Console.WriteLine("  6. mount <d> <p> <path>   - mount disk <d> partition <p> at any path (e.g. /mnt)");
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

        if (!dir.TryReadDir(out IReadOnlyList<IVfsInode> entries))
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
            IVfsInode e = entries[i];
            VfsStat stat = default;
            bool haveStat = e.InodeOperations != null && e.InodeOperations.GetAttr(e, out stat);
            bool isDirectory = haveStat && (stat.Mode & ModeEnum.FileTypeMask) == ModeEnum.Directory;

            Console.Write("  ");
            if (isDirectory)
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
            Console.Write(e.Name.PadRight(DirEntryNameColumnWidth));
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(stat.Size.ToString().PadLeft(FileSizeColumnWidth));
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
            Span<byte> buffer = stackalloc byte[CatChunkSizeBytes];
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
                    if (c == '\n' || c == '\r' || (c >= AsciiPrintableMin && c < AsciiPrintableLimit))
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
    /// <summary>Cursor pattern code for a border (black) pixel.</summary>
    private const int CursorPatternBorder = 1;
    /// <summary>Cursor pattern code for a fill (white) pixel.</summary>
    private const int CursorPatternFill = 2;

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
                if (pixel == CursorPatternBorder)
                {
                    // Border (black)
                    canvas.DrawPoint(Color.Black, px, py);
                }
                else if (pixel == CursorPatternFill)
                {
                    // Fill (white)
                    canvas.DrawPoint(Color.White, px, py);
                }
                // pixel == 0: transparent, don't draw
            }
        }
    }
}
