using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.Graphics;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.Cpu;
using Cosmos.Kernel.HAL.Cpu.Data;

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

#if ARCH_X64
        // Register a handler for INT 32 to test interrupt handling
        Serial.WriteString("[Main] Registering INT 32 handler...\n");
        InterruptManager.SetHandler(32, TestInt32Handler);
        Serial.WriteString("[Main] INT 32 handler registered!\n");

        // Test triggering INT 32
        Serial.WriteString("[Main] Triggering INT 32...\n");
        TriggerInt32Test();
        Serial.WriteString("[Main] INT 32 test complete!\n");
#else
        Serial.WriteString("[Main] Interrupt test skipped (ARM64 platform)\n");
#endif

        // Test exception handling
        TestExceptionHandling();

        while (true) ;
    }

    private static void TestExceptionHandling()
    {
        Console.WriteLine("Testing throw statement...");
        Serial.WriteString("[Main] Testing throw statement...\n");

        // This will throw an exception
        // Expected: System will print exception info and halt gracefully
        throw new InvalidOperationException("This is a test exception!");

        // Should not reach here
        Console.WriteLine("  ✗ ERROR: Should not reach here\n");
    }

#if ARCH_X64
    [LibraryImport("*", EntryPoint = "__test_int32")]
    private static partial void TriggerInt32Test();

    // Handler for INT 32 - this will be called when the interrupt fires
    private static void TestInt32Handler(ref IRQContext context)
    {
        Serial.WriteString("[INT 32 Handler] Interrupt 32 received!\n");
        Serial.WriteString("[INT 32 Handler] RIP: 0x");
        Serial.WriteString(context.rax.ToString("X16"));
        Serial.WriteString("\n");
        Serial.WriteString("[INT 32 Handler] Interrupt number: ");
        Serial.WriteString(context.interrupt.ToString());
        Serial.WriteString("\n");
        Serial.WriteString("[INT 32 Handler] CPU Flags: 0x");
        Serial.WriteString(context.cpu_flags.ToString("X16"));
        Serial.WriteString("\n");
        Serial.WriteString("[INT 32 Handler] Handler execution complete\n");
    }
#endif

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
