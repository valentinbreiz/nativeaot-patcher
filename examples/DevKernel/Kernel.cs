using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.Core.Scheduler.Stride;
using Cosmos.Kernel.Graphics;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.Devices.Network;
using Cosmos.Kernel.Services.Network;
using Cosmos.Kernel.Services.Network.IPv4;
using Cosmos.Kernel.Services.Network.IPv4.UDP;
using Cosmos.Kernel.Services.Timer;
#if ARCH_X64
using Cosmos.Kernel.HAL.X64.Devices.Network;
#endif

internal static partial class Program
{
    [LibraryImport("test", EntryPoint = "testGCC")]
    [return: MarshalUsing(typeof(SimpleStringMarshaler))]
    public static unsafe partial string testGCC();

    private static void Main()
    {
        Serial.WriteString("[Main] Starting Main function\n");

        // GCC interop test (DevKernel-specific)
        Serial.WriteString("[Main] Testing GCC interop...\n");
        var gccString = testGCC();
        Serial.WriteString("[Main] SUCCESS - GCC string: ");
        Serial.WriteString(gccString);
        Serial.WriteString("\n");

        PrintSuccess("GCC interop: ");
        Console.WriteLine(gccString);

        DebugInfo.Print();

        Serial.WriteString("DevKernel: Changes to src/ will be reflected here!\n");

        // Start simple shell
        RunShell();
    }

    private static void RunShell()
    {
        Serial.WriteString("[Shell] Starting shell...\n");
        Console.Clear();

        // Print banner
        Console.WriteLine("========================================");
        Console.WriteLine("         CosmosOS 3.0.0 Shell       ");
        Console.WriteLine("========================================");
        Console.WriteLine();
        PrintInfo("Type 'help' for available commands.");
        Console.WriteLine();

        while (true)
        {
            // Print prompt
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("cosmos");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(":");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write("~");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("$ ");
            Console.ResetColor();

            string? input = Console.ReadLine();

            if (string.IsNullOrEmpty(input))
                continue;

            string trimmed = input.Trim();
            string command = trimmed.ToLower();

            // Handle commands with arguments
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

                case "colors":
                    ShowColors();
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
#endif

                case "halt":
                case "shutdown":
                    PrintWarning("Halting system...");
                    Cosmos.Kernel.Kernel.Halt();
                    break;

                default:
                    PrintError("Unknown command: " + cmd);
                    PrintInfo("Type 'help' for available commands.");
                    break;
            }
        }
    }

    private static void PrintHelp()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Available Commands:");
        Console.ResetColor();

        PrintCommand("help", "Show this help message");
        PrintCommand("clear", "Clear the screen");
        PrintCommand("echo <text>", "Echo back text");
        PrintCommand("info", "Show system information");
        PrintCommand("timer", "Test 10 second countdown timer");
        PrintCommand("colors", "Display color palette");
        PrintCommand("schedinfo", "Show scheduler status and threads");
        PrintCommand("thread", "Test System.Threading.Thread");
        PrintCommand("gfx", "Start graphics thread (draws square)");
        PrintCommand("kill <id>", "Kill a thread by ID");
#if ARCH_X64
        PrintCommand("netconfig", "Configure network stack");
        PrintCommand("netinfo", "Show network device info");
        PrintCommand("netsend", "Send UDP test packet");
        PrintCommand("netlisten", "Listen for UDP packets");
#endif
        PrintCommand("halt", "Halt the system");
    }

    private static void PrintCommand(string cmd, string description)
    {
        Console.Write("  ");
        Console.Write(cmd.PadRight(14));
        Console.WriteLine(description);
    }

    private static void PrintSystemInfo()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
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

    private static void PrintInfoLine(string label, string value)
    {
        Console.Write("  ");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(label.PadRight(14));
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(value);
        Console.ResetColor();
    }

    private static void RunTimerTest()
    {
        PrintInfo("Starting 10 second countdown...");
        for (int i = 10; i > 0; i--)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(i.ToString());
            Console.ResetColor();
            Console.WriteLine("...");
            TimerManager.Wait(1000);
        }
        PrintSuccess("Timer test complete!\n");
    }

    private static void ShowColors()
    {
        Console.WriteLine("Color palette:");
        for (int i = 0; i < 16; i++)
        {
            Console.ForegroundColor = (ConsoleColor)i;
            Console.Write("  " + ((ConsoleColor)i).ToString().PadRight(14));
            if (i == 7)
                Console.WriteLine();
        }
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void ShowSchedulerInfo()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Scheduler Information:");
        Console.ResetColor();

        // Check if scheduler is initialized
        var scheduler = SchedulerManager.Current;
        if (scheduler == null)
        {
            PrintInfoLine("Status", "Not initialized");
            PrintInfo("Run 'sched' or 'thread' first to initialize the scheduler.");
            return;
        }

        // Basic scheduler info
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

        // Per-CPU information
        for (uint cpuId = 0; cpuId < SchedulerManager.CpuCount; cpuId++)
        {
            var cpuState = SchedulerManager.GetCpuState(cpuId);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  CPU " + cpuId + ":");
            Console.ResetColor();

            // Current thread (running)
            var currentThread = cpuState.CurrentThread;
            if (currentThread != null)
            {
                PrintThreadInfo(scheduler, currentThread);
            }

            // Run queue threads (waiting)
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

    private static void PrintThreadInfo(IScheduler scheduler, Cosmos.Kernel.Core.Scheduler.Thread thread)
    {
        Console.Write("    ");

        // Thread ID
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("Thread " + thread.Id);

        // State with color coding - avoid ToString() on enum (not AOT friendly)
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
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("Blocked");
                break;
            case Cosmos.Kernel.Core.Scheduler.ThreadState.Sleeping:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("Sleeping");
                break;
            case Cosmos.Kernel.Core.Scheduler.ThreadState.Dead:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("Dead");
                break;
            case Cosmos.Kernel.Core.Scheduler.ThreadState.Created:
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("Created");
                break;
            default:
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("Unknown");
                break;
        }

        // Priority (generic via IScheduler.GetPriority) - only if scheduler data is set
        if (thread.SchedulerData != null)
        {
            long priority = scheduler.GetPriority(thread);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" Pri=" + priority);
        }

        // Runtime
        ulong runtimeMs = thread.TotalRuntime / 1_000_000;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(" Run=" + runtimeMs + "ms");

        Console.ResetColor();
        Console.WriteLine();
    }

    private static void TestThread()
    {
        Serial.WriteString("[Thread] Testing System.Threading.Thread API\n");

        // Check scheduler state
        Serial.WriteString("[Thread] Scheduler enabled: ");
        Serial.WriteString(SchedulerManager.Enabled ? "true" : "false");
        Serial.WriteString("\n");

        PrintInfo("Creating and starting a thread...");

        var thread = new System.Threading.Thread(() =>
        {
            Serial.WriteString("[Thread] Hello from thread delegate!\n");
            Console.WriteLine("Hello from thread!");
        });

        thread.Start();

        PrintSuccess("Thread started!\n");

        // Wait for a bit to allow scheduler ticks and context switch
        Serial.WriteString("[Thread] Waiting 2 seconds for context switch...\n");
        TimerManager.Wait(2000);

        Serial.WriteString("[Thread] Test complete\n");
    }

    private static void StartGraphicsThread()
    {
        Serial.WriteString("[GfxThread] Starting graphics thread\n");
        PrintInfo("Starting graphics thread (draws color-cycling square)...");

        var thread = new System.Threading.Thread(GraphicsWorker);
        thread.Start();

        PrintSuccess("Graphics thread started!\n");
        PrintInfo("Watch the bottom-right corner of the screen.");
    }

    private static void KillThread(uint threadId)
    {
        var scheduler = SchedulerManager.Current;
        if (scheduler == null)
        {
            PrintError("Scheduler not initialized");
            return;
        }

        // Don't allow killing thread 0 (idle/main thread)
        if (threadId == 0)
        {
            PrintError("Cannot kill idle thread (ID 0)");
            return;
        }

        // Search for the thread across all CPUs
        for (uint cpuId = 0; cpuId < SchedulerManager.CpuCount; cpuId++)
        {
            var cpuState = SchedulerManager.GetCpuState(cpuId);

            // Check if it's the current thread
            if (cpuState.CurrentThread?.Id == threadId)
            {
                PrintWarning("Cannot kill currently running thread");
                PrintInfo("Thread will be terminated when it yields");
                cpuState.CurrentThread.State = Cosmos.Kernel.Core.Scheduler.ThreadState.Dead;
                return;
            }

            // Search in run queue
            int count = scheduler.GetRunQueueCount(cpuState);
            for (int i = 0; i < count; i++)
            {
                var thread = scheduler.GetRunQueueThread(cpuState, i);
                if (thread?.Id == threadId)
                {
                    Serial.WriteString("[Kill] Killing thread ");
                    Serial.WriteNumber(threadId);
                    Serial.WriteString("\n");

                    SchedulerManager.ExitThread(cpuId, thread);
                    PrintSuccess("Thread " + threadId + " killed\n");
                    return;
                }
            }
        }

        PrintError("Thread " + threadId + " not found");
    }

    private static void GraphicsWorker()
    {
        Serial.WriteString("[GfxWorker] Graphics thread started!\n");

        const int squareSize = 80;
        const int margin = 20;

        // Position in bottom-right corner
        int x = (int)Canvas.Width - squareSize - margin;
        int y = (int)Canvas.Height - squareSize - margin;

        int frame = 0;

        // Run forever drawing color-changing gradient squares
        while (true)
        {
            // Create gradient color based on frame
            int phase = frame % 60;
            byte r, g, b;

            if (phase < 10)
            {
                // Red to Yellow
                r = 255;
                g = (byte)(phase * 25);
                b = 0;
            }
            else if (phase < 20)
            {
                // Yellow to Green
                r = (byte)(255 - (phase - 10) * 25);
                g = 255;
                b = 0;
            }
            else if (phase < 30)
            {
                // Green to Cyan
                r = 0;
                g = 255;
                b = (byte)((phase - 20) * 25);
            }
            else if (phase < 40)
            {
                // Cyan to Blue
                r = 0;
                g = (byte)(255 - (phase - 30) * 25);
                b = 255;
            }
            else if (phase < 50)
            {
                // Blue to Magenta
                r = (byte)((phase - 40) * 25);
                g = 0;
                b = 255;
            }
            else
            {
                // Magenta to Red
                r = 255;
                g = 0;
                b = (byte)(255 - (phase - 50) * 25);
            }

            // Draw gradient square - lighter in center, darker at edges
            for (int dy = 0; dy < squareSize; dy++)
            {
                for (int dx = 0; dx < squareSize; dx++)
                {
                    // Calculate distance from center for gradient
                    int cx = dx - squareSize / 2;
                    int cy = dy - squareSize / 2;
                    int dist = (cx * cx + cy * cy) * 255 / (squareSize * squareSize / 2);
                    if (dist > 255) dist = 255;

                    // Blend color with gradient (brighter in center)
                    int factor = 255 - dist / 2;
                    byte pr = (byte)((r * factor) / 255);
                    byte pg = (byte)((g * factor) / 255);
                    byte pb = (byte)((b * factor) / 255);
                    uint pixelColor = (uint)((pr << 16) | (pg << 8) | pb);

                    Canvas.DrawPixel(pixelColor, x + dx, y + dy);
                }
            }

            frame++;

            // Sleep to slow down animation (allows preemption)
            System.Threading.Thread.Sleep(100);
        }
    }

    // Helper methods for colored output
    private static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(message);
        Console.ResetColor();
    }

    private static void PrintWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static void PrintInfo(string message)
    {
        Console.WriteLine(message);
    }

    [ModuleInitializer]
    public static void Init()
    {
        Serial.WriteString("Kernel Init\n");
    }

#if ARCH_X64
    // Network configuration
    private static Address? _localIP;
    private static Address? _gatewayIP;
    private static bool _networkConfigured = false;

    private static void ConfigureNetwork()
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

    private static void ShowNetworkInfo()
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null)
        {
            PrintError("No network device found");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
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

    private static void SendTestPacket()
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

    private static void StartListening()
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

    private static void OnUDPDataReceived(UDPPacket packet)
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
#endif
}

[CustomMarshaller(typeof(string), MarshalMode.Default, typeof(SimpleStringMarshaler))]
internal static unsafe class SimpleStringMarshaler
{
    public static string ConvertToManaged(char* unmanaged)
    {
        // Count the length of the null-terminated UTF-16 string
        int length = 0;
        char* p = unmanaged;
        while (*p != '\0')
        {
            length++;
            p++;
        }

        // Create a new string from the character span
        return new string(unmanaged, 0, length);
    }

    public static char* ConvertToUnmanaged(string managed)
    {
        fixed (char* p = managed)
        {
            return p;
        }
    }
}
