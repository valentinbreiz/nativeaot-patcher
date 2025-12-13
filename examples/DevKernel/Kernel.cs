using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Runtime;
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

    [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
    private static void KernelMain() => Main();

    private static void Main()
    {
        Serial.WriteString("[Main] Starting Main function\n");

        // GCC interop test (DevKernel-specific)
        Serial.WriteString("[Main] Testing GCC interop...\n");
        var gccString = testGCC();
        Serial.WriteString("[Main] SUCCESS - GCC string: ");
        Serial.WriteString(gccString);
        Serial.WriteString("\n");
        KernelConsole.Write("GCC interop: PASS - ");
        KernelConsole.WriteLine(gccString);

        DebugInfo.Print();

        Serial.WriteString("DevKernel: Changes to src/ will be reflected here!\n");

        // Start simple shell
        RunShell();
    }

    private static void RunShell()
    {
        Serial.WriteString("[Shell] Starting shell...\n");
        Console.WriteLine();
        Console.WriteLine("=== CosmosOS Shell ===");
        Console.WriteLine("Type 'help' for available commands.");
        Console.WriteLine();

        while (true)
        {
            Console.Write("> ");
            string? input = Console.ReadLine();

            if (string.IsNullOrEmpty(input))
                continue;

            string command = input.Trim().ToLower();

            switch (command)
            {
                case "help":
                    Console.WriteLine("Available commands:");
                    Console.WriteLine("  help      - Show this help");
                    Console.WriteLine("  clear     - Clear the screen");
                    Console.WriteLine("  echo      - Echo back input");
                    Console.WriteLine("  info      - Show system info");
                    Console.WriteLine("  timer     - Test 10 second timer");
                    Console.WriteLine("  netconfig - Configure network stack");
                    Console.WriteLine("  netinfo   - Show network info");
                    Console.WriteLine("  netsend   - Send UDP test packet");
                    Console.WriteLine("  netlisten - Listen for packets");
                    Console.WriteLine("  halt      - Halt the system");
                    break;

                case "clear":
                    //KernelConsole.Clear();
                    break;

                case "info":
                    Console.WriteLine("CosmosOS v3.0.0 (gen3)");
                    Console.WriteLine("Architecture: x86-64");
                    Console.WriteLine("Runtime: NativeAOT");
                    break;

                case "timer":
                    Console.WriteLine("Testing 10 second timer...");
                    Console.WriteLine("Waiting 10 seconds...");
                    for (int i = 10; i > 0; i--)
                    {
                        Console.WriteLine(i + "...");
                        TimerManager.Wait(1000);
                    }
                    Console.WriteLine("Timer test complete!");
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
                    Console.WriteLine("Halting system...");
                    Cosmos.Kernel.Kernel.Halt();
                    break;

                default:
                    if (command.StartsWith("echo "))
                    {
                        Console.WriteLine(input.Substring(5));
                    }
                    else
                    {
                        Console.WriteLine("Unknown command: " + command);
                        Console.WriteLine("Type 'help' for available commands.");
                    }
                    break;
            }
        }
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
            Console.WriteLine("No network device found");
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
        Console.WriteLine("Network configured:");
        Console.WriteLine("  IP: " + _localIP.ToString());
        Console.WriteLine("  Gateway: " + _gatewayIP.ToString());
    }

    private static void ShowNetworkInfo()
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null)
        {
            Console.WriteLine("No network device found");
            return;
        }

        Console.WriteLine("Network Device: " + device.Name);
        Console.Write("MAC Address: ");
        var mac = device.MacAddress;
        for (int i = 0; i < 6; i++)
        {
            if (i > 0) Console.Write(":");
            Console.Write(mac[i].ToString("X2"));
        }
        Console.WriteLine();
        Console.WriteLine("Link: " + (device.LinkUp ? "UP" : "DOWN"));
        Console.WriteLine("Ready: " + (device.Ready ? "YES" : "NO"));
        Console.WriteLine("IP Configured: " + (_networkConfigured ? "YES" : "NO"));
        if (_networkConfigured && _localIP != null)
        {
            Console.WriteLine("IP Address: " + _localIP.ToString());
        }
    }

    private static void SendTestPacket()
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null)
        {
            Console.WriteLine("No network device found");
            return;
        }

        if (!device.Ready)
        {
            Console.WriteLine("Network device not ready");
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

        Console.WriteLine("Sending UDP packet to " + _gatewayIP!.ToString() + ":5555...");
        bool sent = device.Send(udpPacket.RawData, udpPacket.RawData.Length);
        Console.WriteLine(sent ? "Packet sent!" : "Failed to send packet");
    }

    private static void StartListening()
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null)
        {
            Console.WriteLine("No network device found");
            return;
        }

        // Ensure network is configured
        if (!_networkConfigured)
        {
            ConfigureNetwork();
        }

        Console.WriteLine("Listening for UDP packets on port 5555...");
        Console.WriteLine("Send from host with: echo 'test' | nc -u localhost 5555");
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

        // Also print to console
        Console.Write("UDP from ");
        Console.Write(packet.SourceIP.ToString());
        Console.Write(":");
        Console.Write(packet.SourcePort.ToString());
        Console.Write(" - ");
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
