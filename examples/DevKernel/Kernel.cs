using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.Graphics;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.Services.Timer;

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
                    Console.WriteLine("  help   - Show this help");
                    Console.WriteLine("  clear  - Clear the screen");
                    Console.WriteLine("  echo   - Echo back input");
                    Console.WriteLine("  info   - Show system info");
                    Console.WriteLine("  timer  - Test 10 second timer");
                    Console.WriteLine("  halt   - Halt the system");
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
