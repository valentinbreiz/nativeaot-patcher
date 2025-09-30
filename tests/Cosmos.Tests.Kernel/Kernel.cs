using System.Runtime.InteropServices;
using Cosmos.Tests.Kernel.Framework;
using Cosmos.Tests.Kernel.Tests;
using Cosmos.Kernel.System.IO;

internal unsafe static partial class Program
{
    [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
    private static void KernelMain() => Main();

    private static void Main()
    {
        Serial.WriteString("Cosmos Kernel Runtime Tests\n");
        Serial.WriteString("===========================\n\n");

        Serial.WriteString("TEST_START\n");

        int passCount = 0;
        int failCount = 0;

        // Serial Tests
        Serial.WriteString("test_serial_write: ");
        SerialTests.Test_SerialWrite();
        Serial.WriteString("PASS\n");
        passCount++;

        Serial.WriteString("test_serial_write_number: ");
        SerialTests.Test_SerialWriteNumber();
        Serial.WriteString("PASS\n");
        passCount++;

        // Memory Tests
        Serial.WriteString("test_small_allocation: ");
        MemoryTests.Test_SmallAllocation();
        Serial.WriteString("PASS\n");
        passCount++;

        Serial.WriteString("test_large_allocation: ");
        MemoryTests.Test_LargeAllocation();
        Serial.WriteString("PASS\n");
        passCount++;

        Serial.WriteString("test_array_read_write: ");
        MemoryTests.Test_ArrayReadWrite();
        Serial.WriteString("PASS\n");
        passCount++;

        Serial.WriteString("test_multiple_allocations: ");
        MemoryTests.Test_MultipleAllocations();
        Serial.WriteString("PASS\n");
        passCount++;

        Serial.WriteString("test_byte_array_allocation: ");
        MemoryTests.Test_ByteArrayAllocation();
        Serial.WriteString("PASS\n");
        passCount++;

        // String Tests
        Serial.WriteString("test_string_from_chars: ");
        StringTests.Test_StringFromChars();
        Serial.WriteString("PASS\n");
        passCount++;

        Serial.WriteString("test_string_concat: ");
        StringTests.Test_StringConcat();
        Serial.WriteString("PASS\n");
        passCount++;

        Serial.WriteString("test_string_length: ");
        StringTests.Test_StringLength();
        Serial.WriteString("PASS\n");
        passCount++;

        Serial.WriteString("test_string_allocation: ");
        StringTests.Test_StringAllocation();
        Serial.WriteString("PASS\n");
        passCount++;

        // Output summary
        int total = passCount + failCount;
        Serial.WriteString("TEST_END: ");
        Serial.WriteNumber((uint)passCount, false);
        Serial.WriteString("/");
        Serial.WriteNumber((uint)total, false);
        Serial.WriteString(" passed");

        if (failCount > 0)
        {
            Serial.WriteString(", ");
            Serial.WriteNumber((uint)failCount, false);
            Serial.WriteString(" failed");
        }

        Serial.WriteString("\n\n");

        // Final status
        if (failCount == 0 && passCount > 0)
        {
            Serial.WriteString("[SUCCESS] All tests passed!\n");
        }
        else
        {
            Serial.WriteString("[FAILURE] Some tests failed!\n");
        }

        // Halt
        Serial.WriteString("[DEBUG] Halting...\n");
        while (true) ;
    }
}
