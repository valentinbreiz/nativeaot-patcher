using System;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL;
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

        var gccString = testGCC();
        Console.WriteLine(gccString);

        // Uncomment to use hard breakpoint (must use Continue, not Step Over)
        // Console.WriteLine("Hard breakpoint (use Continue to resume)...");
        // Native.Debug.Breakpoint();  // INT3 - stops execution until Continue
        // Console.WriteLine("Hard breakpoint passed.");

        char[] testChars = new char[] { 'R', 'h', 'p' };
        string testString = new string(testChars);
        Console.WriteLine(testString);
        Serial.WriteString(testString + "\n");

        while (true) ;
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
