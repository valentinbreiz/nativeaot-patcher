using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.HAL.Devices.Network;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Network.IPv4;
using Cosmos.Kernel.System.Network.IPv4.UDP;
using Cosmos.Kernel.System.Network.IPv4.UDP.DHCP;
using Cosmos.Kernel.System.Network.IPv4.UDP.DNS;
using Cosmos.Kernel.System.Timer;
using Sys = Cosmos.Kernel.System;

namespace DevKernel;

/// <summary>
/// DevKernel - Test kernel for Cosmos gen3 development.
/// </summary>
public class Kernel : Sys.Kernel
{
    private string _prompt = "cosmos";

    protected override void BeforeRun()
    {
        Serial.WriteString("[DevKernel] BeforeRun() called\n");

        Console.Clear();
        Console.WriteLine("========================================");
        Console.WriteLine("         CosmosOS 3.0.33 Shell       ");
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

#endif


                case "meminfo":
                    ShowMemoryInfo();
                    break;

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
        PrintCommand("halt", "Halt the system");
#if ARCH_X64
        PrintCommand("netconfig", "Configure network stack");
        PrintCommand("netinfo", "Show network device info");
        PrintCommand("netsend", "Send UDP test packet");
        PrintCommand("netlisten", "Listen for UDP packets");
        PrintCommand("dhcp", "Auto-configure network via DHCP");
        PrintCommand("dns <domain>", "Resolve domain name to IP");
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

        PrintInfoLine("OS", "CosmosOS v3.0.33 (gen3)");
        PrintInfoLine("Runtime", "NativeAOT");
#if ARCH_X64
        PrintInfoLine("Architecture", "x86-64");
#elif ARCH_ARM64
        PrintInfoLine("Architecture", "ARM64");
#endif
        PrintInfoLine("Console", KernelConsole.Cols + "x" + KernelConsole.Rows + " chars");
        if (KernelConsole.IsAvailable)
        {
             var mode = KernelConsole.Canvas.Mode;
             PrintInfoLine("Framebuffer", mode.Width + "x" + mode.Height + "x" + (int)mode.ColorDepth + " (" + KernelConsole.Canvas.Name() + ")");
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
        if (KernelConsole.Canvas.Mode.Width == 0 || KernelConsole.Canvas.Mode.Height == 0)
            return;

        const int squareSize = 80;
        const int margin = 20;

        int x = KernelConsole.Canvas.Mode.Width >= (uint)(squareSize + margin * 2)
            ? (int)KernelConsole.Canvas.Mode.Width - squareSize - margin
            : margin;
        int y = KernelConsole.Canvas.Mode.Height >= (uint)(squareSize + margin * 2)
            ? (int)KernelConsole.Canvas.Mode.Height - squareSize - margin
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

                    KernelConsole.Canvas.DrawPoint(pixelColor, x + dx, y + dy);
                }
            }

            frame++;
            KernelConsole.Canvas.Display();
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
