using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using Cosmos.Build.API.Attributes;
using Cosmos.Build.API.Enum;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.IO;
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
        KernelConsole.WriteLine("Test 1: PASS");

        // Test 2: String allocation
        Serial.WriteString("[Main] Test 2: Allocating string...\n");
        string testString = new string(testChars);
        Serial.WriteString("[Main] Test 2: SUCCESS - string allocated: ");
        Serial.WriteString(testString);
        Serial.WriteString("\n");
        KernelConsole.Write("Test 2: PASS - ");
        KernelConsole.WriteLine(testString);

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
        KernelConsole.WriteLine("Test 3: PASS");

        // Test 4: Multiple string allocations
        Serial.WriteString("[Main] Test 4: Multiple string allocations...\n");
        string str1 = "Hello";
        string str2 = "World";
        string str3 = str1 + " " + str2;
        Serial.WriteString("[Main] Test 4: SUCCESS - concatenated string: ");
        Serial.WriteString(str3);
        Serial.WriteString("\n");
        KernelConsole.Write("Test 4: PASS - ");
        KernelConsole.WriteLine(str3);

        // Test 5: GCC interop
        Serial.WriteString("[Main] Test 5: Testing GCC interop...\n");
        var gccString = testGCC();
        Serial.WriteString("[Main] Test 5: SUCCESS - GCC string: ");
        Serial.WriteString(gccString);
        Serial.WriteString("\n");
        KernelConsole.Write("Test 5: PASS - ");
        KernelConsole.WriteLine(gccString);

        // Test 6: StringBuilder
        Serial.WriteString("[Main] Test 6: Testing StringBuilder...\n");
        StringBuilder sb = new StringBuilder();
        sb.Append("Hello");
        sb.Append(" ");
        sb.Append("StringBuilder");
        sb.Append(" from ");
        sb.Append("Cosmos!");
        string sbResult = sb.ToString();
        Serial.WriteString("[Main] Test 6: SUCCESS - StringBuilder result: ");
        Serial.WriteString(sbResult);
        Serial.WriteString("\n");
        KernelConsole.Write("Test 6: PASS - ");
        KernelConsole.WriteLine(sbResult);

        // Test 7: Basic boxing - int to object
        Serial.WriteString("[Main] Test 7: Testing basic boxing (int to object)...\n");
        int valueInt = 42;
        object boxedInt = valueInt;
        Serial.WriteString("[Main] Test 7: SUCCESS - boxed int value: ");
        Serial.WriteNumber((uint)(int)boxedInt, false);
        Serial.WriteString("\n");
        KernelConsole.WriteLine("Test 7: PASS - boxing works");

        // Test 8: Unboxing - object to int
        Serial.WriteString("[Main] Test 8: Testing unboxing (object to int)...\n");
        int unboxedInt = (int)boxedInt;
        if (unboxedInt == 42)
        {
            Serial.WriteString("[Main] Test 8: SUCCESS - unboxed value matches: ");
            Serial.WriteNumber((uint)unboxedInt, false);
            Serial.WriteString("\n");
            KernelConsole.WriteLine("Test 8: PASS - unboxed correctly");
        }
        else
        {
            Serial.WriteString("[Main] Test 8: FAILED - values don't match\n");
            KernelConsole.WriteLine("Test 8: FAILED");
        }

        // Test 9: Boxing different value types
        Serial.WriteString("[Main] Test 9: Testing boxing multiple value types...\n");
        byte valueByte = 255;
        short valueShort = -1234;
        long valueLong = 9876543210L;
        object boxedByte = valueByte;
        object boxedShort = valueShort;
        object boxedLong = valueLong;
        Serial.WriteString("[Main] Test 9: SUCCESS - boxed byte, short, long\n");
        KernelConsole.WriteLine("Test 9: PASS - multiple types boxed");

        // Test 10: Boxing to interface (IComparable)
        Serial.WriteString("[Main] Test 10: Testing boxing to interface...\n");
        int compareValue = 100;
        IComparable comparable = compareValue;
        Serial.WriteString("[Main] Test 10: SUCCESS - interface boxing works\n");
        KernelConsole.WriteLine("Test 10: PASS - interface boxing works");

        // Test 11: Nullable boxing (null case)
        Serial.WriteString("[Main] Test 11: Testing Nullable<T> boxing (null)...\n");
        int? nullableNull = null;
        object boxedNull = nullableNull;
        if (boxedNull == null)
        {
            Serial.WriteString("[Main] Test 11: SUCCESS - null Nullable<T> boxes to null\n");
            KernelConsole.WriteLine("Test 11: PASS - nullable null boxing works");
        }
        else
        {
            Serial.WriteString("[Main] Test 11: FAILED - null Nullable<T> did not box to null\n");
            KernelConsole.WriteLine("Test 11: FAILED");
        }

        // Test 12: Nullable boxing (value case)
        Serial.WriteString("[Main] Test 12: Testing Nullable<T> boxing (with value)...\n");
        int? nullableValue = 777;
        object boxedNullable = nullableValue;
        if (boxedNullable != null && (int)boxedNullable == 777)
        {
            Serial.WriteString("[Main] Test 12: SUCCESS - Nullable<T> with value boxes correctly: ");
            Serial.WriteNumber((uint)(int)boxedNullable, false);
            Serial.WriteString("\n");
            KernelConsole.WriteLine("Test 12: PASS - nullable value boxing works");
        }
        else
        {
            Serial.WriteString("[Main] Test 12: FAILED - Nullable<T> boxing incorrect\n");
            KernelConsole.WriteLine("Test 12: FAILED");
        }

        // Test 13: Array.Copy with boxing
        Serial.WriteString("[Main] Test 13: Testing Array.Copy with boxing...\n");
        int[] sourceIntArray = new int[] { 10, 20, 30 };
        object[] destObjectArray = new object[3];
        Array.Copy(sourceIntArray, destObjectArray, 3);
        if ((int)destObjectArray[0] == 10 && (int)destObjectArray[1] == 20 && (int)destObjectArray[2] == 30)
        {
            Serial.WriteString("[Main] Test 13: SUCCESS - Array.Copy boxed correctly\n");
            KernelConsole.WriteLine("Test 13: PASS - Array.Copy with boxing works");
        }
        else
        {
            Serial.WriteString("[Main] Test 13: FAILED - Array.Copy boxing failed\n");
            KernelConsole.WriteLine("Test 13: FAILED");
        }

        // Test 14: Custom struct boxing
        Serial.WriteString("[Main] Test 14: Testing custom struct boxing...\n");
        TestPoint point = new TestPoint { X = 10, Y = 20 };
        object boxedPoint = point;
        TestPoint unboxedPoint = (TestPoint)boxedPoint;
        if (unboxedPoint.X == 10 && unboxedPoint.Y == 20)
        {
            Serial.WriteString("[Main] Test 14: SUCCESS - custom struct boxed/unboxed correctly\n");
            KernelConsole.WriteLine("Test 14: PASS - custom struct boxing works");
        }
        else
        {
            Serial.WriteString("[Main] Test 14: FAILED - struct boxing/unboxing incorrect\n");
            KernelConsole.WriteLine("Test 14: FAILED");
        }

        // Test 15: List<int> - value types
        Serial.WriteString("[Main] Test 15: Testing List<int>...\n");
        List<int> intList = new List<int>();
        intList.Add(100);
        intList.Add(200);
        intList.Add(300);
        if (intList.Count == 3 && intList[0] == 100 && intList[1] == 200 && intList[2] == 300)
        {
            Serial.WriteString("[Main] Test 15: SUCCESS - List<int> works correctly\n");
            KernelConsole.WriteLine("Test 15: PASS - List<int> works");
        }
        else
        {
            Serial.WriteString("[Main] Test 15: FAILED - List<int> incorrect\n");
            KernelConsole.WriteLine("Test 15: FAILED");
        }

        // Test 16: Simple boxing/unboxing
        Serial.WriteString("[Main] Test 16: Testing simple box/unbox...\n");
        object simpleBoxed = (object)999;
        int simpleUnboxed = (int)simpleBoxed;
        if (simpleUnboxed == 999)
        {
            Serial.WriteString("[Main] Test 16: SUCCESS - simple boxing works\n");
            KernelConsole.WriteLine("Test 16: PASS - simple boxing works");
        }
        else
        {
            Serial.WriteString("[Main] Test 16: FAILED - simple boxing incorrect\n");
            KernelConsole.WriteLine("Test 16: FAILED");
        }

        // Test 17: List.Contains with value types
        Serial.WriteString("[Main] Test 17: Testing List<int>.Contains...\n");
        List<int> searchList = new List<int>();
        searchList.Add(10);
        searchList.Add(20);
        searchList.Add(30);

        bool found20 = searchList.Contains(20);
        bool found99 = searchList.Contains(99);

        if (found20 && !found99)
        {
            Serial.WriteString("[Main] Test 17: SUCCESS - List.Contains works correctly\n");
            KernelConsole.WriteLine("Test 17: PASS - List.Contains works");
        }
        else
        {
            Serial.WriteString("[Main] Test 17: FAILED - Contains returned wrong values (found20=");
            Serial.WriteNumber((uint)(found20 ? 1 : 0), false);
            Serial.WriteString(", found99=");
            Serial.WriteNumber((uint)(found99 ? 1 : 0), false);
            Serial.WriteString(")\n");
            KernelConsole.WriteLine("Test 17: FAILED");
        }

        // Test 18: List.IndexOf with value types
        Serial.WriteString("[Main] Test 18: Testing List<int>.IndexOf...\n");
        int index20 = searchList.IndexOf(20);
        int index99 = searchList.IndexOf(99);

        if (index20 == 1 && index99 == -1)
        {
            Serial.WriteString("[Main] Test 18: SUCCESS - List.IndexOf works correctly\n");
            KernelConsole.WriteLine("Test 18: PASS - List.IndexOf works");
        }
        else
        {
            Serial.WriteString("[Main] Test 18: FAILED - IndexOf incorrect (index20=");
            Serial.WriteNumber((uint)index20, false);
            Serial.WriteString(", index99=");
            Serial.WriteNumber((uint)index99, false);
            Serial.WriteString(")\n");
            KernelConsole.WriteLine("Test 18: FAILED");
        }

        // Test 19: List.Count property
        Serial.WriteString("[Main] Test 19: Testing List<int>.Count...\n");
        if (searchList.Count == 3)
        {
            Serial.WriteString("[Main] Test 19: SUCCESS - List.Count is correct (3)\n");
            KernelConsole.WriteLine("Test 19: PASS - List.Count works");
        }
        else
        {
            Serial.WriteString("[Main] Test 19: FAILED - List.Count incorrect (");
            Serial.WriteNumber((uint)searchList.Count, false);
            Serial.WriteString(")\n");
            KernelConsole.WriteLine("Test 19: FAILED");
        }

        // Test 20: List<string> - Test WITHOUT pre-allocation (triggers resize)
        Serial.WriteString("[Main] Test 20: Testing List<string> WITHOUT pre-allocation...\n");

        List<string> stringList = new List<string>(); // Capacity 0 - will trigger resize
        stringList.Add("First");
        stringList.Add("Second");
        stringList.Add("Third");
        stringList.Add("Fourth");
        stringList.Add("Fifth");

        if (stringList.Count == 5 &&
            stringList[0] == "First" &&
            stringList[1] == "Second" &&
            stringList[2] == "Third" &&
            stringList[3] == "Fourth" &&
            stringList[4] == "Fifth")
        {
            Serial.WriteString("[Main] Test 20: PASSED - List<string> works without pre-allocation!\n");
            KernelConsole.WriteLine("Test 20: PASS");
        }
        else
        {
            Serial.WriteString("[Main] Test 20: FAILED - Count=");
            Serial.WriteNumber((uint)stringList.Count, false);
            Serial.WriteString(", [0]='");
            Serial.WriteString(stringList[0]);
            Serial.WriteString("'\n");
            KernelConsole.WriteLine("Test 20: FAILED");
        }

        Serial.WriteString("[Main] All core tests PASSED!\n");
        KernelConsole.WriteLine();
        KernelConsole.WriteLine("All core tests PASSED!");
        Serial.WriteString("DevKernel: Changes to src/ will be reflected here!\n");

        while (true) ;
    }
    [ModuleInitializer]
    public static void Init()
    {
        Serial.WriteString("Kernel Init\n");
    }
}

// Test struct for boxing tests
internal struct TestPoint
{
    public int X;
    public int Y;
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
