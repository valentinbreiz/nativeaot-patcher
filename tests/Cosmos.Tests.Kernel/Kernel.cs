using System;
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

        // === Validation Tests ===
        Serial.WriteString("CATEGORY: Validation Tests\n");
        RunTest_FrameworkValidation(ref passCount, ref failCount);
        RunTest_IntentionalFailure(ref passCount, ref failCount);

        // === Serial Tests ===
        Serial.WriteString("CATEGORY: Serial Tests\n");
        RunTest_SerialWrite(ref passCount, ref failCount);
        RunTest_SerialWriteNumber(ref passCount, ref failCount);

        // === Memory Tests ===
        Serial.WriteString("CATEGORY: Memory Tests\n");
        RunTest_SmallAllocation(ref passCount, ref failCount);
        RunTest_LargeAllocation(ref passCount, ref failCount);
        RunTest_ArrayReadWrite(ref passCount, ref failCount);
        RunTest_MultipleAllocations(ref passCount, ref failCount);
        RunTest_ByteArrayAllocation(ref passCount, ref failCount);

        // === String Tests ===
        Serial.WriteString("CATEGORY: String Tests\n");
        RunTest_StringFromChars(ref passCount, ref failCount);
        RunTest_StringConcat(ref passCount, ref failCount);
        RunTest_StringLength(ref passCount, ref failCount);
        RunTest_StringAllocation(ref passCount, ref failCount);

        // === GC/Heap Tests ===
        Serial.WriteString("CATEGORY: GC/Heap Tests\n");
        RunTest_AllocationPattern(ref passCount, ref failCount);
        RunTest_LargeObjectAllocation(ref passCount, ref failCount);
        RunTest_MixedSizeAllocations(ref passCount, ref failCount);
        RunTest_HeapFragmentation(ref passCount, ref failCount);
        RunTest_ZeroInitialization(ref passCount, ref failCount);

        // === Array Tests ===
        Serial.WriteString("CATEGORY: Array Tests\n");
        RunTest_EmptyArray(ref passCount, ref failCount);
        RunTest_SingleElementArray(ref passCount, ref failCount);
        RunTest_ArrayCopy(ref passCount, ref failCount);
        RunTest_TypedArrays(ref passCount, ref failCount);
        RunTest_ArrayBoundary(ref passCount, ref failCount);
        RunTest_ArrayFillPattern(ref passCount, ref failCount);

        // === Numeric Tests ===
        Serial.WriteString("CATEGORY: Numeric Tests\n");
        RunTest_IntegerArithmetic(ref passCount, ref failCount);
        RunTest_IntegerOverflow(ref passCount, ref failCount);
        RunTest_UnsignedIntegers(ref passCount, ref failCount);
        RunTest_LongIntegers(ref passCount, ref failCount);
        RunTest_ByteOperations(ref passCount, ref failCount);
        RunTest_BitwiseOperations(ref passCount, ref failCount);
        RunTest_ShiftOperations(ref passCount, ref failCount);
        RunTest_NegativeNumbers(ref passCount, ref failCount);
        RunTest_ZeroOperations(ref passCount, ref failCount);
        RunTest_ComparisonOperations(ref passCount, ref failCount);

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

    // ============================================================================
    // Validation Tests
    // ============================================================================

    private static void RunTest_FrameworkValidation(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_framework_validation: ");
        Assert.Reset();
        ValidationTests.Test_FrameworkValidation();
        // This test SHOULD have failures - that's how we validate the framework works
        if (Assert.FailCount > 0)
        {
            Serial.WriteString("PASS (");
            Serial.WriteNumber((uint)Assert.AssertCount, false);
            Serial.WriteString(" assertions, ");
            Serial.WriteNumber((uint)Assert.FailCount, false);
            Serial.WriteString(" expected failures)\n");
            passCount++;
        }
        else
        {
            Serial.WriteString("FAIL (framework not detecting failures)\n");
            failCount++;
        }
    }

    private static void RunTest_IntentionalFailure(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_intentional_failure: ");
        Assert.Reset();
        FailingTest.Test_IntentionalFailure();
        CheckAssertions(ref passCount, ref failCount);
    }

    // ============================================================================
    // Serial Tests
    // ============================================================================
    private static void RunTest_SerialWrite(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_serial_write: ");
        Assert.Reset();
        SerialTests.Test_SerialWrite();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_SerialWriteNumber(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_serial_write_number: ");
        Assert.Reset();
        SerialTests.Test_SerialWriteNumber();
        CheckAssertions(ref passCount, ref failCount);
    }

    // ============================================================================
    // Memory Tests
    // ============================================================================

    private static void RunTest_SmallAllocation(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_small_allocation: ");
        Assert.Reset();
        MemoryTests.Test_SmallAllocation();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_LargeAllocation(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_large_allocation: ");
        Assert.Reset();
        MemoryTests.Test_LargeAllocation();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_ArrayReadWrite(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_array_read_write: ");
        Assert.Reset();
        MemoryTests.Test_ArrayReadWrite();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_MultipleAllocations(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_multiple_allocations: ");
        Assert.Reset();
        MemoryTests.Test_MultipleAllocations();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_ByteArrayAllocation(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_byte_array_allocation: ");
        Assert.Reset();
        MemoryTests.Test_ByteArrayAllocation();
        CheckAssertions(ref passCount, ref failCount);
    }

    // ============================================================================
    // String Tests
    // ============================================================================
    private static void RunTest_StringFromChars(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_string_from_chars: ");
        Assert.Reset();
        StringTests.Test_StringFromChars();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_StringConcat(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_string_concat: ");
        Assert.Reset();
        StringTests.Test_StringConcat();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_StringLength(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_string_length: ");
        Assert.Reset();
        StringTests.Test_StringLength();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_StringAllocation(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_string_allocation: ");
        Assert.Reset();
        StringTests.Test_StringAllocation();
        CheckAssertions(ref passCount, ref failCount);
    }

    // ============================================================================
    // GC/Heap Tests
    // ============================================================================
    private static void RunTest_AllocationPattern(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_allocation_pattern: ");
        Assert.Reset();
        GCTests.Test_AllocationPattern();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_LargeObjectAllocation(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_large_object_allocation: ");
        Assert.Reset();
        GCTests.Test_LargeObjectAllocation();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_MixedSizeAllocations(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_mixed_size_allocations: ");
        Assert.Reset();
        GCTests.Test_MixedSizeAllocations();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_HeapFragmentation(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_heap_fragmentation: ");
        Assert.Reset();
        GCTests.Test_HeapFragmentation();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_ZeroInitialization(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_zero_initialization: ");
        Assert.Reset();
        GCTests.Test_ZeroInitialization();
        CheckAssertions(ref passCount, ref failCount);
    }

    // ============================================================================
    // Array Tests
    // ============================================================================
    private static void RunTest_EmptyArray(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_empty_array: ");
        Assert.Reset();
        ArrayTests.Test_EmptyArray();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_SingleElementArray(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_single_element_array: ");
        Assert.Reset();
        ArrayTests.Test_SingleElementArray();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_ArrayCopy(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_array_copy: ");
        Assert.Reset();
        ArrayTests.Test_ArrayCopy();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_TypedArrays(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_typed_arrays: ");
        Assert.Reset();
        ArrayTests.Test_TypedArrays();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_ArrayBoundary(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_array_boundary: ");
        Assert.Reset();
        ArrayTests.Test_ArrayBoundary();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_ArrayFillPattern(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_array_fill_pattern: ");
        Assert.Reset();
        ArrayTests.Test_ArrayFillPattern();
        CheckAssertions(ref passCount, ref failCount);
    }

    // ============================================================================
    // Numeric Tests
    // ============================================================================
    private static void RunTest_IntegerArithmetic(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_integer_arithmetic: ");
        Assert.Reset();
        NumericTests.Test_IntegerArithmetic();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_IntegerOverflow(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_integer_overflow: ");
        Assert.Reset();
        NumericTests.Test_IntegerOverflow();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_UnsignedIntegers(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_unsigned_integers: ");
        Assert.Reset();
        NumericTests.Test_UnsignedIntegers();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_LongIntegers(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_long_integers: ");
        Assert.Reset();
        NumericTests.Test_LongIntegers();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_ByteOperations(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_byte_operations: ");
        Assert.Reset();
        NumericTests.Test_ByteOperations();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_BitwiseOperations(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_bitwise_operations: ");
        Assert.Reset();
        NumericTests.Test_BitwiseOperations();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_ShiftOperations(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_shift_operations: ");
        Assert.Reset();
        NumericTests.Test_ShiftOperations();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_NegativeNumbers(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_negative_numbers: ");
        Assert.Reset();
        NumericTests.Test_NegativeNumbers();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_ZeroOperations(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_zero_operations: ");
        Assert.Reset();
        NumericTests.Test_ZeroOperations();
        CheckAssertions(ref passCount, ref failCount);
    }

    private static void RunTest_ComparisonOperations(ref int passCount, ref int failCount)
    {
        Serial.WriteString("test_comparison_operations: ");
        Assert.Reset();
        NumericTests.Test_ComparisonOperations();
        CheckAssertions(ref passCount, ref failCount);
    }

    // Helper to check assertion results
    private static void CheckAssertions(ref int passCount, ref int failCount)
    {
        if (Assert.FailCount == 0)
        {
            Serial.WriteString("PASS (");
            Serial.WriteNumber((uint)Assert.AssertCount, false);
            Serial.WriteString(" assertions)\n");
            passCount++;
        }
        else
        {
            Serial.WriteString("FAIL (");
            Serial.WriteNumber((uint)Assert.FailCount, false);
            Serial.WriteString("/");
            Serial.WriteNumber((uint)Assert.AssertCount, false);
            Serial.WriteString(" assertions failed)\n");
            failCount++;
        }
    }
}
