using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Cosmos.Build.API.Attributes;
using Cosmos.Build.API.Enum;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.Runtime;
using Cosmos.Kernel.System.IO;
using Cosmos.Kernel.System.Graphics;
using PlatformArchitecture = Cosmos.Build.API.Enum.PlatformArchitecture;

internal unsafe static partial class Program
{


    [LibraryImport("test", EntryPoint = "testGCC")]
    [return: MarshalUsing(typeof(SimpleStringMarshaler))]
    public static unsafe partial string testGCC();

    [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
    private static void KernelMain() => Main();

    private static void Main()
    {
        Serial.WriteString("[Main] Starting Main function\n");

        // Test memory allocator with various allocations
        Serial.WriteString("[Main] Testing memory allocator...\n");

        // Test 1: Small allocation (char array)
        Serial.WriteString("[Main] Test 1: Allocating char array...\n");
        char[] testChars = new char[] { 'R', 'h', 'p' };
        Serial.WriteString("[Main] Test 1: SUCCESS - char array allocated\n");

        // Test 2: String allocation
        Serial.WriteString("[Main] Test 2: Allocating string...\n");
        string testString = new string(testChars);
        Serial.WriteString("[Main] Test 2: SUCCESS - string allocated: ");
        Serial.WriteString(testString);
        Serial.WriteString("\n");

        // Test 3: Larger allocation (int array)
        Serial.WriteString("[Main] Test 3: Allocating int array (100 elements)...\n");
        int[] intArray = new int[100];
        for (int i = 0; i < 10; i++)
        {
            intArray[i] = i * 10;
        }
        Serial.WriteString("[Main] Test 3: SUCCESS - int array allocated and populated\n");
        Serial.WriteString("[Main] Test 3: First 3 values: ");
        Serial.WriteNumber((uint)intArray[0], false);
        Serial.WriteString(", ");
        Serial.WriteNumber((uint)intArray[1], false);
        Serial.WriteString(", ");
        Serial.WriteNumber((uint)intArray[2], false);
        Serial.WriteString("\n");

        // Test 4: Multiple string allocations
        Serial.WriteString("[Main] Test 4: Multiple string allocations...\n");
        string str1 = "Hello";
        string str2 = "World";
        string str3 = str1 + " " + str2;
        Serial.WriteString("[Main] Test 4: SUCCESS - concatenated string: ");
        Serial.WriteString(str3);
        Serial.WriteString("\n");

        // Test 5: GCC interop
        Serial.WriteString("[Main] Test 5: Testing GCC interop...\n");
        var gccString = testGCC();
        Serial.WriteString("[Main] Test 5: SUCCESS - GCC string: ");
        Serial.WriteString(gccString);
        Serial.WriteString("\n");

        Serial.WriteString("[Main] All memory allocator tests PASSED!\n");
        Serial.WriteString("DevKernel: Changes to src/ will be reflected here!\n");

        while (true) ;
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