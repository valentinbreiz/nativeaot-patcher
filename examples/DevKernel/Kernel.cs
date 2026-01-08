using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.Graphics;
using Cosmos.Kernel.HAL.Devices.Network;
using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Network.IPv4;
using Cosmos.Kernel.System.Network.IPv4.UDP;
using Cosmos.Kernel.System.Network.IPv4.UDP.DHCP;
using Cosmos.Kernel.System.Network.IPv4.UDP.DNS;
using Cosmos.Kernel.System.Timer;
using Cosmos.Kernel.System.VFS;
using Cosmos.Kernel.System.VFS.FAT;
using Cosmos.Kernel.System.VFS.Enums;
#if ARCH_X64
using Cosmos.Kernel.HAL.X64.Devices.Storage;
#endif
using Sys = Cosmos.Kernel.System;

namespace DevKernel;

/// <summary>
/// DevKernel - Test kernel for Cosmos gen3 development.
/// </summary>
public class Kernel : Sys.Kernel
{
    private string _prompt = "cosmos";
    private int _currentDrive = -1; // -1 = no drive selected

    // Pre-allocated drive paths to avoid string concatenation issues
    private static readonly string[] _drivePaths = new string[]
    {
        "0:/", "1:/", "2:/", "3:/", "4:/", "5:/", "6:/", "7:/", "8:/", "9:/"
    };

    protected override void BeforeRun()
    {
        Serial.WriteString("[DevKernel] BeforeRun() called\n");

        Console.Clear();
        Console.WriteLine("========================================");
        Console.WriteLine("         CosmosOS 3.0.0 Shell       ");
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
        Console.Write("~");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("$ ");
        Console.ResetColor();

        string? input = Console.ReadLine();

        if (string.IsNullOrEmpty(input))
            return;

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
                    Console.WriteLine(trimmed.Substring(5));
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
                    KillThread(killId);
                else
                    PrintError("Usage: kill <thread_id>");
                break;

            case "halt":
            case "shutdown":
                PrintWarning("Halting system...");
                Stop();
                break;

#if ARCH_X64
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
                    ResolveDNS(parts[1]);
                else
                    PrintError("Usage: dns <domain>");
                break;

            case "partitions":
            case "lspart":
                ListPartitions();
                break;

            case "disks":
            case "listdisks":
                ListDisks();
                break;

            case "creatembr":
                if (parts.Length > 1 && int.TryParse(parts[1], out int mbrDiskNum))
                    CreateMBRPartition(mbrDiskNum);
                else
                    PrintError("Usage: creatembr <disk_number>");
                break;

            case "creategpt":
                if (parts.Length > 1 && int.TryParse(parts[1], out int gptDiskNum))
                    CreateGPTPartition(gptDiskNum);
                else
                    PrintError("Usage: creategpt <disk_number>");
                break;

            case "mkpart":
                if (parts.Length >= 3 && int.TryParse(parts[1], out int mkDiskNum) && int.TryParse(parts[2], out int mkSizeMB))
                    CreatePartitionEntry(mkDiskNum, mkSizeMB);
                else
                    PrintError("Usage: mkpart <disk_number> <size_mb>");
                break;

            case "format":
                if (parts.Length >= 2 && int.TryParse(parts[1], out int fmtPartNum))
                    FormatPartition(fmtPartNum);
                else
                    PrintError("Usage: format <partition_number>");
                break;

            case "mount":
                if (parts.Length >= 3 && int.TryParse(parts[1], out int mountPartNum) && int.TryParse(parts[2], out int driveNum))
                    MountPartition(mountPartNum, driveNum);
                else
                    PrintError("Usage: mount <partition_number> <drive_number>");
                break;

            case "ls":
            case "dir":
                ListDirectory(parts.Length >= 2 ? parts[1] : "/");
                break;

            case "cat":
            case "type":
                if (parts.Length >= 2)
                    DisplayFileContents(parts[1]);
                else
                    PrintError("Usage: cat <path>");
                break;

            case "mkdir":
                if (parts.Length >= 2)
                    CreateDirectory(parts[1]);
                else
                    PrintError("Usage: mkdir <path>");
                break;

            case "touch":
                if (parts.Length >= 2)
                    CreateFile(parts[1]);
                else
                    PrintError("Usage: touch <path>");
                break;

            case "rm":
            case "del":
                if (parts.Length >= 2)
                    DeleteEntry(parts[1]);
                else
                    PrintError("Usage: rm <path>");
                break;

            case "write":
                if (parts.Length >= 3)
                {
                    // Join parts[2..] manually without LINQ
                    string text = "";
                    for (int i = 2; i < parts.Length; i++)
                    {
                        if (i > 2) text += " ";
                        text += parts[i];
                    }
                    WriteToFile(parts[1], text);
                }
                else
                    PrintError("Usage: write <path> <text>");
                break;

            case "mounts":
                ShowMountPoints();
                break;

#endif


            case "meminfo":
                ShowMemoryInfo();
                break;

            default:
#if ARCH_X64
                // Check for drive switch command like "0:" or "1:"
                if (cmd.Length >= 2 && cmd.EndsWith(":") && int.TryParse(cmd[..^1], out int switchDrive))
                {
                    SwitchDrive(switchDrive);
                    break;
                }
#endif
                PrintError($"\"{cmd}\" is not a command");
                Console.WriteLine("Type 'help' for available commands.");
                break;
        }
    }

    protected override void AfterRun()
    {
        Serial.WriteString("[DevKernel] AfterRun() called\n");
        Console.WriteLine("Goodbye!");
        Cosmos.Kernel.Kernel.Halt();
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
        PrintCommand("halt", "Halt the system");
#if ARCH_X64
        PrintCommand("netconfig", "Configure network stack");
        PrintCommand("netinfo", "Show network device info");
        PrintCommand("netsend", "Send UDP test packet");
        PrintCommand("netlisten", "Listen for UDP packets");
        PrintCommand("dhcp", "Auto-configure network via DHCP");
        PrintCommand("dns <domain>", "Resolve domain name to IP");
        PrintCommand("disks", "List storage devices");
        PrintCommand("partitions", "List disk partitions");
        PrintCommand("creatembr <n>", "Create MBR on disk n");
        PrintCommand("creategpt <n>", "Create GPT on disk n");
        PrintCommand("mkpart <n> <mb>", "Create partition on disk n");
        PrintCommand("format <n>", "Format partition n as FAT32");
        PrintCommand("mount <p> <d>", "Mount partition p as drive d");
        PrintCommand("<d>:", "Switch to drive d (e.g. 0:)");
        PrintCommand("mounts", "Show mounted filesystems");
        PrintCommand("ls [path]", "List directory contents");
        PrintCommand("cat <path>", "Display file contents");
        PrintCommand("mkdir <path>", "Create directory");
        PrintCommand("touch <path>", "Create empty file");
        PrintCommand("rm <path>", "Delete file or directory");
        PrintCommand("write <path> <txt>", "Write text to file");
#endif
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

        PrintInfoLine("OS", "CosmosOS v3.0.0 (gen3)");
        PrintInfoLine("Runtime", "NativeAOT");
#if ARCH_X64
        PrintInfoLine("Architecture", "x86-64");
#elif ARCH_ARM64
        PrintInfoLine("Architecture", "ARM64");
#endif
        PrintInfoLine("Console", KernelConsole.Cols + "x" + KernelConsole.Rows + " chars");
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
            Console.ForegroundColor = ConsoleColor.Green;
        else if (usagePercent < 80)
            Console.ForegroundColor = ConsoleColor.Yellow;
        else
            Console.ForegroundColor = ConsoleColor.Red;

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
        if (Canvas.Width == 0 || Canvas.Height == 0)
            return;

        const int squareSize = 80;
        const int margin = 20;

        int x = Canvas.Width >= (uint)(squareSize + margin * 2)
            ? (int)Canvas.Width - squareSize - margin
            : margin;
        int y = Canvas.Height >= (uint)(squareSize + margin * 2)
            ? (int)Canvas.Height - squareSize - margin
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
                    if (dist > 255) dist = 255;

                    int factor = 255 - dist / 2;
                    byte pr = (byte)((r * factor) / 255);
                    byte pg = (byte)((g * factor) / 255);
                    byte pb = (byte)((b * factor) / 255);
                    uint pixelColor = (uint)((pr << 16) | (pg << 8) | pb);

                    Canvas.DrawPixel(pixelColor, x + dx, y + dy);
                }
            }

            frame++;
            System.Threading.Thread.Sleep(100);
        }
    }

#if ARCH_X64
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

        // Initialize network stack and configure IP
        NetworkStack.Initialize();
        NetworkStack.ConfigIP(device, _localIP);

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
            payload[i] = (byte)message[i];

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
            PrintSuccess("Packet sent!\n");
        else
            PrintError("Failed to send packet\n");
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
                Serial.Write(c.ToString());
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
                Console.Write(c.ToString());
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
        Address resolvedIP = dnsClient.Receive(5000);

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

    private void ListPartitions()
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Disk Partitions:");
        Console.ResetColor();

        var partitions = Partition.Partitions;
        if (partitions.Count == 0)
        {
            PrintWarning("No partitions found.");
            Console.WriteLine();
            return;
        }

        for (int i = 0; i < partitions.Count; i++)
        {
            var part = partitions[i];

            Console.Write("  ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("[" + i + "] ");
            Console.ResetColor();

            // Name
            if (!string.IsNullOrEmpty(part.Name))
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(part.Name);
                Console.ResetColor();
            }
            else
            {
                Console.Write("Partition " + i);
            }

            Console.WriteLine();

            // Details
            Console.Write("      ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("Start: ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(part.StartingSector.ToString());
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("  Sectors: ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(part.BlockCount.ToString());
            Console.ResetColor();

            // Size in MB
            ulong sizeBytes = part.BlockCount * part.BlockSize;
            ulong sizeMB = sizeBytes / 1024 / 1024;
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("  Size: ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(sizeMB.ToString() + " MB");
            Console.ResetColor();
            Console.WriteLine();

            // Filesystem type
            Console.Write("      ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("FS: ");
            var fsChecker = VfsManager.GetFileSystem(part);
            if (fsChecker != null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                var fs = fsChecker.GetFileSystem(part, "/");
                Console.Write(fs.Type);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("Unknown/Unformatted");
            }
            Console.ResetColor();
            Console.WriteLine();
        }
        Console.WriteLine();
    }

    private void ListDisks()
    {
        Serial.WriteString("[ListDisks] Listing storage devices...\n");

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Storage Devices:");
        Console.ResetColor();

        var ports = AHCI.Ports;
        Serial.WriteString("[ListDisks] AHCI.Ports.Count = ");
        Serial.WriteNumber((uint)ports.Count);
        Serial.WriteString("\n");

        if (ports.Count == 0)
        {
            PrintWarning("No storage devices found.");
            Console.WriteLine();
            return;
        }

        for (int i = 0; i < ports.Count; i++)
        {
            var port = ports[i];

            Serial.WriteString("[ListDisks] Disk ");
            Serial.WriteNumber((uint)i);
            Serial.WriteString(": ");
            Serial.WriteString(port.PortName);
            Serial.WriteString(" Port=");
            Serial.WriteNumber(port.PortNumber);
            Serial.WriteString(" Sectors=");
            Serial.WriteNumber(port.BlockCount);
            Serial.WriteString("\n");

            Console.Write("  ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("[" + i + "] ");
            Console.ResetColor();

            // Port type and name
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(port.PortName);
            Console.ResetColor();
            Console.Write(" (Port " + port.PortNumber + ")");
            Console.WriteLine();

            // Size info
            Console.Write("      ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("Sectors: ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(port.BlockCount.ToString());

            ulong sizeBytes = port.BlockCount * port.BlockSize;
            ulong sizeMB = sizeBytes / 1024 / 1024;
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("  Size: ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(sizeMB.ToString() + " MB");
            Console.ResetColor();
            Console.WriteLine();

            // Partition table type
            Console.Write("      ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("Table: ");

            Serial.WriteString("[ListDisks] Checking partition table type...\n");
            bool isGPT = GPT.IsGPTPartition(port);
            Serial.WriteString("[ListDisks] IsGPT = ");
            Serial.WriteString(isGPT ? "true" : "false");
            Serial.WriteString("\n");

            if (isGPT)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("GPT");
            }
            else
            {
                bool isMBR = MBR.IsMBR(port);
                Serial.WriteString("[ListDisks] IsMBR = ");
                Serial.WriteString(isMBR ? "true" : "false");
                Serial.WriteString("\n");

                if (isMBR)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("MBR");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("None");
                }
            }
            Console.ResetColor();
            Console.WriteLine();
        }
        Console.WriteLine();
        Serial.WriteString("[ListDisks] Done.\n");
    }

    private void CreateMBRPartition(int diskNum)
    {
        Serial.WriteString("[CreateMBR] Creating MBR on disk ");
        Serial.WriteNumber((uint)diskNum);
        Serial.WriteString("\n");

        var ports = AHCI.Ports;
        if (diskNum < 0 || diskNum >= ports.Count)
        {
            Serial.WriteString("[CreateMBR] ERROR: Invalid disk number\n");
            PrintError("Invalid disk number. Use 'disks' to list available disks.");
            return;
        }

        var port = ports[diskNum];
        Serial.WriteString("[CreateMBR] Selected port: ");
        Serial.WriteString(port.PortName);
        Serial.WriteString(" BlockCount=");
        Serial.WriteNumber(port.BlockCount);
        Serial.WriteString("\n");

        PrintInfo("Creating MBR partition table on disk " + diskNum + "...");

        Serial.WriteString("[CreateMBR] Reading existing MBR...\n");
        var mbr = new MBR(port);

        Serial.WriteString("[CreateMBR] Writing new MBR...\n");
        mbr.CreateMBR(port);

        Serial.WriteString("[CreateMBR] MBR created successfully\n");
        PrintSuccess("MBR partition table created!");

        // Re-scan for partitions
        Serial.WriteString("[CreateMBR] Re-scanning partitions...\n");
        Partition.Partitions.Clear();
        foreach (var p in AHCI.Ports)
        {
            StorageManager.ScanAndInitPartitions(p);
        }

        Serial.WriteString("[CreateMBR] Found ");
        Serial.WriteNumber((uint)Partition.Partitions.Count);
        Serial.WriteString(" partition(s)\n");

        PrintInfo("Found " + Partition.Partitions.Count + " partition(s).");
        Console.WriteLine();
    }

    private void CreateGPTPartition(int diskNum)
    {
        Serial.WriteString("[CreateGPT] Creating GPT on disk ");
        Serial.WriteNumber((uint)diskNum);
        Serial.WriteString("\n");

        var ports = AHCI.Ports;
        if (diskNum < 0 || diskNum >= ports.Count)
        {
            Serial.WriteString("[CreateGPT] ERROR: Invalid disk number\n");
            PrintError("Invalid disk number. Use 'disks' to list available disks.");
            return;
        }

        var port = ports[diskNum];
        Serial.WriteString("[CreateGPT] Selected port: ");
        Serial.WriteString(port.PortName);
        Serial.WriteString(" BlockCount=");
        Serial.WriteNumber(port.BlockCount);
        Serial.WriteString("\n");

        PrintInfo("Creating GPT partition table on disk " + diskNum + "...");

        Serial.WriteString("[CreateGPT] Writing protective MBR...\n");
        Serial.WriteString("[CreateGPT] Writing GPT header at LBA 1...\n");
        Serial.WriteString("[CreateGPT] Clearing partition entries (LBA 2-33)...\n");
        GPT.CreateGPT(port);

        Serial.WriteString("[CreateGPT] GPT created successfully\n");
        PrintSuccess("GPT partition table created!");

        // Re-scan for partitions
        Serial.WriteString("[CreateGPT] Re-scanning partitions...\n");
        Partition.Partitions.Clear();
        foreach (var p in AHCI.Ports)
        {
            StorageManager.ScanAndInitPartitions(p);
        }

        Serial.WriteString("[CreateGPT] Found ");
        Serial.WriteNumber((uint)Partition.Partitions.Count);
        Serial.WriteString(" partition(s)\n");

        PrintInfo("Found " + Partition.Partitions.Count + " partition(s).");
        Console.WriteLine();
    }

    private void CreatePartitionEntry(int diskNum, int sizeMB)
    {
        Serial.WriteString("[mkpart] Creating partition on disk ");
        Serial.WriteNumber((uint)diskNum);
        Serial.WriteString(" size ");
        Serial.WriteNumber((uint)sizeMB);
        Serial.WriteString(" MB\n");

        if (sizeMB <= 0)
        {
            PrintError("Partition size must be greater than 0 MB.");
            return;
        }

        var ports = AHCI.Ports;
        if (diskNum < 0 || diskNum >= ports.Count)
        {
            PrintError("Invalid disk number. Use 'disks' to list available disks.");
            return;
        }

        var port = ports[diskNum];

        // Calculate sectors from MB
        ulong sectorsPerMB = 1024 * 1024 / 512;
        ulong sectorCount = (ulong)sizeMB * sectorsPerMB;

        // Start after first MB (2048 sectors) for alignment, or 34 for GPT
        ulong startSector;

        // Check if disk has GPT
        if (GPT.IsGPTPartition(port))
        {
            Serial.WriteString("[mkpart] GPT disk detected\n");

            // GPT: first usable LBA is 34
            startSector = 34;

            // Check if partition fits
            if (startSector + sectorCount > port.BlockCount - 34)
            {
                PrintError("Partition too large for disk.");
                return;
            }

            Serial.WriteString("[mkpart] Start: ");
            Serial.WriteNumber(startSector);
            Serial.WriteString(" Sectors: ");
            Serial.WriteNumber(sectorCount);
            Serial.WriteString("\n");

            PrintInfo("Creating GPT partition: start=" + startSector + ", sectors=" + sectorCount);

            if (!GPT.AddPartition(port, startSector, sectorCount))
            {
                PrintError("Failed to create GPT partition.");
                return;
            }

            PrintSuccess("GPT partition created!");
        }
        else if (MBR.IsMBR(port))
        {
            Serial.WriteString("[mkpart] MBR disk detected\n");

            startSector = 2048;

            // Check if partition fits
            if (startSector + sectorCount > port.BlockCount)
            {
                PrintError("Partition too large for disk.");
                return;
            }

            Serial.WriteString("[mkpart] Start: ");
            Serial.WriteNumber(startSector);
            Serial.WriteString(" Sectors: ");
            Serial.WriteNumber(sectorCount);
            Serial.WriteString("\n");

            PrintInfo("Creating MBR partition: start=" + startSector + ", sectors=" + sectorCount);

            // Read MBR
            Span<byte> mbr = new byte[512];
            port.ReadBlock(0, 1, mbr);

            // Find free partition slot
            int slot = -1;
            for (int i = 0; i < 4; i++)
            {
                int offset = 446 + i * 16;
                if (mbr[offset + 4] == 0) // System ID = 0 means empty
                {
                    slot = i;
                    break;
                }
            }

            if (slot == -1)
            {
                PrintError("No free partition slot in MBR.");
                return;
            }

            Serial.WriteString("[mkpart] Using slot ");
            Serial.WriteNumber((uint)slot);
            Serial.WriteString("\n");

            int slotOffset = 446 + slot * 16;

            // Write partition entry
            mbr[slotOffset + 0] = 0x00;  // Boot indicator
            mbr[slotOffset + 1] = 0xFE;  // CHS start head
            mbr[slotOffset + 2] = 0xFF;  // CHS start sector/cylinder
            mbr[slotOffset + 3] = 0xFF;  // CHS start cylinder
            mbr[slotOffset + 4] = 0x0B;  // System ID: FAT32
            mbr[slotOffset + 5] = 0xFE;  // CHS end head
            mbr[slotOffset + 6] = 0xFF;  // CHS end sector/cylinder
            mbr[slotOffset + 7] = 0xFF;  // CHS end cylinder

            BitConverter.TryWriteBytes(mbr.Slice(slotOffset + 8, 4), (uint)startSector);
            BitConverter.TryWriteBytes(mbr.Slice(slotOffset + 12, 4), (uint)sectorCount);

            Serial.WriteString("[mkpart] Writing MBR...\n");
            port.WriteBlock(0, 1, mbr);

            PrintSuccess("MBR partition created in slot " + slot);
        }
        else
        {
            PrintError("Disk has no partition table. Run 'creatembr " + diskNum + "' or 'creategpt " + diskNum + "' first.");
            return;
        }

        // Re-scan partitions
        Serial.WriteString("[mkpart] Re-scanning partitions...\n");
        Partition.Partitions.Clear();
        foreach (var p in AHCI.Ports)
        {
            StorageManager.ScanAndInitPartitions(p);
        }

        PrintInfo("Found " + Partition.Partitions.Count + " partition(s).");
        Console.WriteLine();
    }

    private void FormatPartition(int partNum)
    {
        Serial.WriteString("[format] Formatting partition ");
        Serial.WriteNumber((uint)partNum);
        Serial.WriteString("\n");

        var partitions = Partition.Partitions;
        if (partNum < 0 || partNum >= partitions.Count)
        {
            PrintError("Invalid partition number. Use 'partitions' to list.");
            return;
        }

        var partition = partitions[partNum];

        Serial.WriteString("[format] Partition start: ");
        Serial.WriteNumber(partition.StartingSector);
        Serial.WriteString(" blocks: ");
        Serial.WriteNumber(partition.BlockCount);
        Serial.WriteString("\n");

        PrintInfo("Formatting partition " + partNum + " as FAT32...");

        try
        {
            FatFileSystem.CreateFormatted(partition, "/" + partNum + ":/", true);
            PrintSuccess("Partition formatted as FAT32!");
        }
        catch (Exception ex)
        {
            Serial.WriteString("[format] ERROR: ");
            Serial.WriteString(ex.Message);
            Serial.WriteString("\n");
            PrintError("Format failed: " + ex.Message);
        }

        Console.WriteLine();
    }

    private void MountPartition(int partNum, int driveNum)
    {
        if (driveNum < 0 || driveNum >= _drivePaths.Length)
        {
            PrintError("Drive number must be 0-9");
            Console.WriteLine();
            return;
        }

        string mountPath = _drivePaths[driveNum];

        Serial.WriteString("[mount] Mounting partition ");
        Serial.WriteNumber((uint)partNum);
        Serial.WriteString(" as drive ");
        Serial.WriteNumber((uint)driveNum);
        Serial.WriteString(" at ");
        Serial.WriteString(mountPath);
        Serial.WriteString("\n");

        var partitions = Partition.Partitions;
        if (partNum < 0 || partNum >= partitions.Count)
        {
            PrintError("Invalid partition number. Use 'partitions' to list.");
            return;
        }

        var partition = partitions[partNum];

        // Check if partition has a recognized filesystem
        var fs = VfsManager.GetFileSystem(partition);
        if (fs == null)
        {
            PrintError("No filesystem detected on partition. Run 'format " + partNum + "' first.");
            return;
        }

        try
        {
            Serial.WriteString("[mount] Calling VfsManager.Mount...\n");
            VfsManager.Mount(partition, mountPath);
            Serial.WriteString("[mount] Mount succeeded\n");

            Serial.WriteString("[mount] Printing success message...\n");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("Partition ");
            Console.Write(partNum.ToString());
            Console.Write(" mounted as drive ");
            Console.WriteLine(driveNum.ToString());
            Console.ResetColor();
            Serial.WriteString("[mount] Success message printed\n");

            // Auto-switch to new drive if no drive selected
            if (_currentDrive < 0)
            {
                _currentDrive = driveNum;
                Serial.WriteString("[mount] Auto-switching to drive\n");
                Console.Write("Switched to drive ");
                Console.WriteLine(driveNum.ToString());
            }
            Serial.WriteString("[mount] Done\n");
        }
        catch (Exception ex)
        {
            Serial.WriteString("[mount] ERROR: ");
            Serial.WriteString(ex.Message);
            Serial.WriteString("\n");
            PrintError("Mount failed: " + ex.Message);
        }

        Console.WriteLine();
    }

    private void SwitchDrive(int driveNum)
    {
        if (driveNum < 0 || driveNum >= _drivePaths.Length)
        {
            PrintError("Drive number must be 0-9");
            return;
        }

        string mountPath = _drivePaths[driveNum];

        if (!VfsManager.IsMounted(mountPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("Drive ");
            Console.Write(driveNum.ToString());
            Console.WriteLine(" is not mounted.");
            Console.ResetColor();
            return;
        }

        _currentDrive = driveNum;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("Switched to drive ");
        Console.WriteLine(driveNum.ToString());
        Console.ResetColor();
        Console.WriteLine();
    }

    private string GetFullPath(string path)
    {
        if (_currentDrive < 0 || _currentDrive >= _drivePaths.Length)
        {
            return path;
        }

        string drivePath = _drivePaths[_currentDrive];

        // drivePath is "0:/" and path is "/" -> want "0:/"
        // drivePath is "0:/" and path is "/test" -> want "0:/test"
        if (path.StartsWith("/"))
        {
            return drivePath + path.Substring(1);
        }

        return drivePath + path;
    }

    private void ShowMountPoints()
    {
        Serial.WriteString("[mounts] ShowMountPoints called\n");

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Mounted Filesystems:");
        Console.ResetColor();

        int count = VfsManager.GetMountCount();
        Serial.WriteString("[mounts] Count: ");
        Serial.WriteNumber((ulong)count);
        Serial.WriteString("\n");

        if (count == 0)
        {
            PrintWarning("No filesystems mounted.");
            Console.WriteLine();
            return;
        }

        Serial.WriteString("[mounts] Iterating...\n");
        for (int i = 0; i < count; i++)
        {
            var mount = VfsManager.GetMountAt(i);
            if (mount == null) continue;

            Serial.WriteString("[mounts] Path: ");
            Serial.WriteString(mount.Value.Path);
            Serial.WriteString("\n");

            Console.Write("  ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(mount.Value.Path);
            Console.ResetColor();
            Console.Write(" -> ");

            string fsType = mount.Value.FileSystem.Type;
            Serial.WriteString("[mounts] Type: ");
            Serial.WriteString(fsType);
            Serial.WriteString("\n");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(fsType);

            ulong size = mount.Value.FileSystem.Size;
            Serial.WriteString("[mounts] Size: ");
            Serial.WriteNumber(size);
            Serial.WriteString("\n");

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(" (");
            Console.Write((size / 1024 / 1024).ToString());
            Console.Write(" MB)");
            Console.ResetColor();
            Console.WriteLine();
        }
        Serial.WriteString("[mounts] Done\n");
        Console.WriteLine();
    }

    private void ListDirectory(string path)
    {
        if (_currentDrive < 0)
        {
            PrintError("No drive mounted. Use 'mount <partition> <drive>' first.");
            Console.WriteLine();
            return;
        }

        string fullPath = GetFullPath(path);
        Serial.WriteString("[ls] Listing: ");
        Serial.WriteString(fullPath);
        Serial.WriteString("\n");

        try
        {
            var entries = VfsManager.GetDirectoryListing(fullPath);

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("Contents of " + path + ":");
            Console.ResetColor();

            if (entries.Count == 0)
            {
                PrintWarning("  (empty)");
            }
            else
            {
                foreach (var entry in entries)
                {
                    Console.Write("  ");
                    if (entry.Type == DirectoryEntryType.Directory)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write("[DIR]  ");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write("[FILE] ");
                    }

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write(entry.Name.PadRight(20));
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write(entry.Size.ToString().PadLeft(10));
                    Console.Write(" bytes");
                    Console.ResetColor();
                    Console.WriteLine();
                }
            }
        }
        catch (Exception ex)
        {
            Serial.WriteString("[ls] ERROR: ");
            Serial.WriteString(ex.Message);
            Serial.WriteString("\n");
            PrintError("Error: " + ex.Message);
        }

        Console.WriteLine();
    }

    private void DisplayFileContents(string path)
    {
        if (_currentDrive < 0)
        {
            PrintError("No drive mounted. Use 'mount <partition> <drive>' first.");
            Console.WriteLine();
            return;
        }

        string fullPath = GetFullPath(path);
        Serial.WriteString("[cat] Reading: ");
        Serial.WriteString(fullPath);
        Serial.WriteString("\n");

        try
        {
            string? contents = VfsManager.ReadAllText(fullPath);
            if (contents == null)
            {
                PrintError("File not found: " + path);
                return;
            }

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("Contents of " + path + ":");
            Console.ResetColor();
            Console.WriteLine(contents);
        }
        catch (Exception ex)
        {
            Serial.WriteString("[cat] ERROR: ");
            Serial.WriteString(ex.Message);
            Serial.WriteString("\n");
            PrintError("Error: " + ex.Message);
        }

        Console.WriteLine();
    }

    private void CreateDirectory(string path)
    {
        if (_currentDrive < 0)
        {
            PrintError("No drive mounted. Use 'mount <partition> <drive>' first.");
            Console.WriteLine();
            return;
        }

        string fullPath = GetFullPath(path);
        Serial.WriteString("[mkdir] Creating: ");
        Serial.WriteString(fullPath);
        Serial.WriteString("\n");

        try
        {
            VfsManager.CreateDirectory(fullPath);
            PrintSuccess("Directory created: " + path);
        }
        catch (Exception ex)
        {
            Serial.WriteString("[mkdir] ERROR: ");
            Serial.WriteString(ex.Message);
            Serial.WriteString("\n");
            PrintError("Error: " + ex.Message);
        }

        Console.WriteLine();
    }

    private void CreateFile(string path)
    {
        if (_currentDrive < 0)
        {
            PrintError("No drive mounted. Use 'mount <partition> <drive>' first.");
            Console.WriteLine();
            return;
        }

        string fullPath = GetFullPath(path);
        Serial.WriteString("[touch] Creating: ");
        Serial.WriteString(fullPath);
        Serial.WriteString("\n");

        try
        {
            VfsManager.CreateFile(fullPath);
            PrintSuccess("File created: " + path);
        }
        catch (Exception ex)
        {
            Serial.WriteString("[touch] ERROR: ");
            Serial.WriteString(ex.Message);
            Serial.WriteString("\n");
            PrintError("Error: " + ex.Message);
        }

        Console.WriteLine();
    }

    private void DeleteEntry(string path)
    {
        if (_currentDrive < 0)
        {
            PrintError("No drive mounted. Use 'mount <partition> <drive>' first.");
            Console.WriteLine();
            return;
        }

        string fullPath = GetFullPath(path);
        Serial.WriteString("[rm] Deleting: ");
        Serial.WriteString(fullPath);
        Serial.WriteString("\n");

        try
        {
            if (VfsManager.DirectoryExists(fullPath))
            {
                VfsManager.DeleteDirectory(fullPath, false);
                PrintSuccess("Directory deleted: " + path);
            }
            else if (VfsManager.FileExists(fullPath))
            {
                VfsManager.DeleteFile(fullPath);
                PrintSuccess("File deleted: " + path);
            }
            else
            {
                PrintError("Path not found: " + path);
            }
        }
        catch (Exception ex)
        {
            Serial.WriteString("[rm] ERROR: ");
            Serial.WriteString(ex.Message);
            Serial.WriteString("\n");
            PrintError("Error: " + ex.Message);
        }

        Console.WriteLine();
    }

    private void WriteToFile(string path, string text)
    {
        if (_currentDrive < 0)
        {
            PrintError("No drive mounted. Use 'mount <partition> <drive>' first.");
            Console.WriteLine();
            return;
        }

        string fullPath = GetFullPath(path);
        Serial.WriteString("[write] Writing to: ");
        Serial.WriteString(fullPath);
        Serial.WriteString("\n");

        try
        {
            VfsManager.WriteAllText(fullPath, text);
            PrintSuccess("Written " + text.Length + " bytes to " + path);
        }
        catch (Exception ex)
        {
            Serial.WriteString("[write] ERROR: ");
            Serial.WriteString(ex.Message);
            Serial.WriteString("\n");
            PrintError("Error: " + ex.Message);
        }

        Console.WriteLine();
    }
#endif

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
}
