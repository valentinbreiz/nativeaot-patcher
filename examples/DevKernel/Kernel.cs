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

                case "sched":
                    TestScheduler();
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
        PrintCommand("sched", "Test scheduler API");
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

    private static void TestScheduler()
    {
        Serial.WriteString("[Sched] TestScheduler start\n");

        Serial.WriteString("[Sched] SchedulerManager.Initialize(1)\n");
        SchedulerManager.Initialize(1);

        Serial.WriteString("[Sched] Creating StrideScheduler\n");
        var strideScheduler = new StrideScheduler();

        Serial.WriteString("[Sched] SetScheduler\n");
        SchedulerManager.SetScheduler(strideScheduler);

        Serial.WriteString("[Sched] Creating thread1\n");
        var thread1 = new Cosmos.Kernel.Core.Scheduler.Thread { Id = 1, State = Cosmos.Kernel.Core.Scheduler.ThreadState.Created };

        Serial.WriteString("[Sched] Creating thread2\n");
        var thread2 = new Cosmos.Kernel.Core.Scheduler.Thread { Id = 2, State = Cosmos.Kernel.Core.Scheduler.ThreadState.Created };

        Serial.WriteString("[Sched] Creating thread3\n");
        var thread3 = new Cosmos.Kernel.Core.Scheduler.Thread { Id = 3, State = Cosmos.Kernel.Core.Scheduler.ThreadState.Created };

        Serial.WriteString("[Sched] CreateThread 1\n");
        SchedulerManager.CreateThread(0, thread1);

        Serial.WriteString("[Sched] CreateThread 2\n");
        SchedulerManager.CreateThread(0, thread2);

        Serial.WriteString("[Sched] CreateThread 3\n");
        SchedulerManager.CreateThread(0, thread3);

        Serial.WriteString("[Sched] SetPriority 1\n");
        SchedulerManager.SetPriority(0, thread1, 100);

        Serial.WriteString("[Sched] SetPriority 2\n");
        SchedulerManager.SetPriority(0, thread2, 200);

        Serial.WriteString("[Sched] SetPriority 3\n");
        SchedulerManager.SetPriority(0, thread3, 50);

        Serial.WriteString("[Sched] ReadyThread 1\n");
        SchedulerManager.ReadyThread(0, thread1);

        Serial.WriteString("[Sched] ReadyThread 2\n");
        SchedulerManager.ReadyThread(0, thread2);

        Serial.WriteString("[Sched] ReadyThread 3\n");
        SchedulerManager.ReadyThread(0, thread3);

        Serial.WriteString("[Sched] GetCpuState\n");
        var cpuState = SchedulerManager.GetCpuState(0);

        Serial.WriteString("[Sched] GetSchedulerData\n");
        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();

        Serial.WriteString("[Sched] RunQueue.Count = ");
        Serial.WriteNumber((ulong)cpuData.RunQueue.Count);
        Serial.WriteString("\n");

        Serial.WriteString("[Sched] Starting pick loop\n");
        int[] pickCount = new int[4];

        for (int round = 0; round < 6; round++)
        {
            Serial.WriteString("[Sched] Round ");
            Serial.WriteNumber((ulong)round);
            Serial.WriteString("\n");

            cpuState.Lock.Acquire();
            var picked = strideScheduler.PickNext(cpuState);
            cpuState.Lock.Release();

            if (picked != null)
            {
                Serial.WriteString("[Sched] Picked thread ");
                Serial.WriteNumber(picked.Id);
                Serial.WriteString("\n");

                pickCount[picked.Id]++;

                var td = picked.GetSchedulerData<StrideThreadData>();
                picked.TotalRuntime += SchedulerManager.DefaultQuantumNs;
                td.Pass += (long)td.Stride;

                cpuState.Lock.Acquire();
                strideScheduler.OnThreadYield(cpuState, picked);
                cpuState.Lock.Release();
            }
        }

        Serial.WriteString("[Sched] Pick results:\n");
        Serial.WriteString("[Sched] Thread 1: ");
        Serial.WriteNumber((ulong)pickCount[1]);
        Serial.WriteString(" picks\n");
        Serial.WriteString("[Sched] Thread 2: ");
        Serial.WriteNumber((ulong)pickCount[2]);
        Serial.WriteString(" picks\n");
        Serial.WriteString("[Sched] Thread 3: ");
        Serial.WriteNumber((ulong)pickCount[3]);
        Serial.WriteString(" picks\n");

        Serial.WriteString("[Sched] Test complete\n");
        PrintSuccess("Scheduler tests complete!\n");
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
