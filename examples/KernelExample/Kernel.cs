using System;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Cosmos.Kernel;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Runtime;
using Cosmos.Kernel.System.IO;

internal unsafe static partial class Program
{
    [LibraryImport("test", EntryPoint = "testGCC")]
    [return: MarshalUsing(typeof(SimpleStringMarshaler))]
    public static unsafe partial string testGCC();

    [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
    private static void KernelMain() => Main();

    private static void Main()
    {
        Native.Debug.BreakpointSoft();

        // Native.Debug.BreakpointSoft(); // Commented out for ARM64 debugging
        // Test GCC integration
        var gccString = testGCC();
        Console.WriteLine(gccString);

        // Test string operations
        char[] testChars = new char[] { 'R', 'h', 'p' };
        string testString = new string(testChars);
        Console.WriteLine(testString);
        Serial.WriteString(testString + "\n");

        // Main loop - use Cosmos.Kernel's Halt method
        while (true)
        {
            Kernel.Halt();
        }
    }
}

[CustomMarshaller(typeof(string), MarshalMode.Default, typeof(SimpleStringMarshaler))]
internal static unsafe class SimpleStringMarshaler
{
    public static string ConvertToManaged(char* unmanaged)
    {
        string result = new(unmanaged);
        return result;
    }

    public static char* ConvertToUnmanaged(string managed)
    {
        fixed (char* p = managed)
        {
            return p;
        }
    }
}
