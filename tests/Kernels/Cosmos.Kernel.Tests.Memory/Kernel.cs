using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.TestRunner.Framework;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;

namespace Cosmos.Kernel.Tests.Memory;

public unsafe class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
        Serial.WriteString("[Memory Tests] Starting test suite\n");
        TR.Start("Memory Tests", expectedTests: 36); // 8 boxing + 5 memory + 8 collections + 12 SIMD + 3 Array.Copy

        // Boxing/Unboxing Tests
        TR.Run("Boxing_Char", TestBoxingChar);
        TR.Run("Boxing_Int32", TestBoxingInt32);
        TR.Run("Boxing_Byte", TestBoxingByte);
        TR.Run("Boxing_Long", TestBoxingLong);
        TR.Run("Boxing_Nullable", TestBoxingNullable);
        TR.Run("Boxing_Interface", TestBoxingInterface);
        TR.Run("Boxing_CustomStruct", TestBoxingCustomStruct);
        TR.Run("Boxing_ArrayCopy", TestArrayCopyWithBoxing);

        // Memory Allocation Tests
        TR.Run("Memory_CharArray", TestCharArrayAllocation);
        TR.Run("Memory_StringAllocation", TestStringAllocation);
        TR.Run("Memory_IntArray", TestIntArrayAllocation);
        TR.Run("Memory_StringConcat", TestStringConcatenation);
        TR.Run("Memory_StringBuilder", TestStringBuilder);

        // Generic Collection Tests
        TR.Run("Collections_ListInt", TestListInt);
        TR.Run("Collections_ListString", TestListString);
        TR.Run("Collections_ListByte", TestListByte);
        TR.Run("Collections_ListLong", TestListLong);
        TR.Run("Collections_ListStruct", TestListStruct);
        TR.Run("Collections_ListContains", TestListContains);
        TR.Run("Collections_ListIndexOf", TestListIndexOf);
        TR.Run("Collections_ListRemoveAt", TestListRemoveAt);

        // Memory Copy Tests (SIMD enabled for 16+ bytes)
        TR.Run("MemCopy_8Bytes", TestMemCopy8Bytes);
        TR.Run("MemCopy_16Bytes", TestMemCopy16Bytes);
        TR.Run("MemCopy_24Bytes", TestMemCopy24Bytes);
        TR.Run("MemCopy_32Bytes", TestMemCopy32Bytes);
        TR.Run("MemCopy_48Bytes", TestMemCopy48Bytes);
        TR.Run("MemCopy_64Bytes", TestMemCopy64Bytes);
        TR.Run("MemCopy_80Bytes", TestMemCopy80Bytes);
        TR.Run("MemCopy_128Bytes", TestMemCopy128Bytes);
        TR.Run("MemCopy_256Bytes", TestMemCopy256Bytes);
        TR.Run("MemCopy_264Bytes", TestMemCopy264Bytes);
        TR.Run("MemSet_64Bytes", TestMemSet64Bytes);
        TR.Run("MemMove_Overlap", TestMemMoveOverlap);

        // Array.Copy Tests (uses SIMD via memmove/RhBulkMoveWithWriteBarrier)
        TR.Run("ArrayCopy_IntArray", TestArrayCopyIntArray);
        TR.Run("ArrayCopy_ByteArray", TestArrayCopyByteArray);
        TR.Run("ArrayCopy_LargeArray", TestArrayCopyLargeArray);

        Serial.WriteString("[Memory Tests] All tests completed\n");
        TR.Finish();

        Stop();
    }

    protected override void Run()
    {
        // Tests completed in BeforeRun, nothing to do here
    }

    protected override void AfterRun()
    {
        Cosmos.Kernel.Kernel.Halt();
    }

    // ==================== Boxing/Unboxing Tests ====================

    private static void TestBoxingChar()
    {
        object boxed = 'c';
        Assert.Equal("c", boxed.ToString());
        Assert.Equal(0x00630063, boxed.GetHashCode());

        char unboxed = (char)boxed;
        Assert.True(unboxed == 'c', "Boxing: char to object and back");
    }

    private static void TestBoxingInt32()
    {
        object boxed = 42;
        Assert.Equal("42", boxed.ToString());
        Assert.Equal(42, boxed.GetHashCode());
        Assert.True(boxed.Equals(42), "Int32.Equals on boxed int (same value)");
        Assert.True(!boxed.Equals(5), "Int32.Equals on boxed int (different value)");

        object boxed2 = 42;
        Assert.True(Object.Equals(boxed, boxed2), "Object.Equals with two boxed ints");

        int unboxed = (int)boxed;
        Assert.True(unboxed == 42, "Boxing: int to object and back");
    }

    private static void TestBoxingByte()
    {
        byte value = 255;
        object boxed = value;
        byte unboxed = (byte)boxed;
        Assert.True(unboxed == 255, "Boxing: byte to object and back");
    }

    private static void TestBoxingLong()
    {
        long value = 9876543210L;
        object boxed = value;
        long unboxed = (long)boxed;
        Assert.True(unboxed == 9876543210L, "Boxing: long to object and back");
    }

    private static void TestBoxingNullable()
    {
        // Test null case
        int? nullableNull = null;
        object boxedNull = nullableNull;
        Assert.True(boxedNull == null, "Boxing: Nullable<int> null boxes to null");

        // Test value case
        int? nullableValue = 777;
        object boxedValue = nullableValue;
        Assert.True(boxedValue != null && (int)boxedValue == 777, "Boxing: Nullable<int> with value boxes correctly");
    }

    private static void TestBoxingInterface()
    {
        int value = 100;
        IComparable comparable = value;
        Assert.True(comparable != null, "Boxing: int to interface (IComparable)");
    }

    private static void TestBoxingCustomStruct()
    {
        TestPoint point = new TestPoint { X = 10, Y = 20 };
        object boxed = point;
        TestPoint unboxed = (TestPoint)boxed;
        Assert.True(unboxed.X == 10 && unboxed.Y == 20, "Boxing: custom struct box/unbox");
    }

    private static void TestArrayCopyWithBoxing()
    {
        int[] sourceIntArray = new int[] { 10, 20, 30 };
        object[] destObjectArray = new object[3];

        Array.Copy(sourceIntArray, destObjectArray, 3);

        bool passed = (int)destObjectArray[0] == 10 &&
                     (int)destObjectArray[1] == 20 &&
                     (int)destObjectArray[2] == 30;
        Assert.True(passed, "Boxing: Array.Copy with automatic boxing");
    }

    // ==================== Memory Allocation Tests ====================

    private static void TestCharArrayAllocation()
    {
        char[] testChars = new char[] { 'R', 'h', 'p' };
        Assert.True(testChars.Length == 3 && testChars[0] == 'R', "Memory: char array allocation");
    }

    private static void TestStringAllocation()
    {
        char[] chars = new char[] { 'R', 'h', 'p' };
        string str = new string(chars);
        Assert.True(str == "Rhp", "Memory: string allocation from char array");
    }

    private static void TestIntArrayAllocation()
    {
        int[] array = new int[100];
        for (int i = 0; i < 10; i++)
        {
            array[i] = i * 10;
        }
        Assert.True(array[0] == 0 && array[1] == 10 && array[2] == 20, "Memory: int array allocation and access");
    }

    private static void TestStringConcatenation()
    {
        string str1 = "Hello";
        string str2 = "World";
        string str3 = str1 + " " + str2;
        Assert.True(str3 == "Hello World", "Memory: string concatenation");
    }

    private static void TestStringBuilder()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Hello");
        sb.Append(" ");
        sb.Append("StringBuilder");
        string result = sb.ToString();
        Assert.True(result == "Hello StringBuilder", "Memory: StringBuilder operations");
    }

    // ==================== Generic Collection Tests ====================

    private static void TestListInt()
    {
        List<int> list = new List<int>();
        list.Add(100);
        list.Add(200);
        list.Add(300);
        Assert.True(list.Count == 3 && list[0] == 100 && list[1] == 200 && list[2] == 300, "Collections: List<int> Add and indexer");
    }

    private static void TestListString()
    {
        List<string> list = new List<string>();
        list.Add("First");
        list.Add("Second");
        list.Add("Third");
        list.Add("Fourth");
        list.Add("Fifth");

        Assert.True(list.Count == 5 && list[0] == "First" && list[4] == "Fifth", "Collections: List<string> with resize");
    }

    private static void TestListByte()
    {
        List<byte> list = new List<byte>();
        list.Add(0xFF);
        list.Add(0x00);
        list.Add(0xAB);
        list.Add(0x12);

        Assert.True(list.Count == 4 && list[0] == 0xFF && list[2] == 0xAB, "Collections: List<byte> operations");
    }

    private static void TestListLong()
    {
        List<long> list = new List<long>();
        list.Add(0x123456789ABCDEF0);
        list.Add(-9999999999999);
        list.Add(42);

        Assert.True(list.Count == 3 && list[0] == 0x123456789ABCDEF0 && list[2] == 42, "Collections: List<long> with 64-bit values");
    }

    private static void TestListStruct()
    {
        List<TestPoint> list = new List<TestPoint>();
        list.Add(new TestPoint { X = 1, Y = 2 });
        list.Add(new TestPoint { X = 3, Y = 4 });
        list.Add(new TestPoint { X = 5, Y = 6 });

        Assert.True(list.Count == 3 && list[0].X == 1 && list[2].Y == 6, "Collections: List<struct> operations");
    }

    private static void TestListContains()
    {
        List<int> list = new List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);

        bool found20 = list.Contains(20);
        bool found99 = list.Contains(99);

        Assert.True(found20 && !found99, "Collections: List.Contains method");
    }

    private static void TestListIndexOf()
    {
        List<int> list = new List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);

        int index20 = list.IndexOf(20);
        int index99 = list.IndexOf(99);

        Assert.True(index20 == 1 && index99 == -1, "Collections: List.IndexOf method");
    }

    private static void TestListRemoveAt()
    {
        List<int> list = new List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        list.Add(40);
        list.Add(50);

        int idx = list.IndexOf(30);
        list.RemoveAt(idx);

        Assert.True(list.Count == 4 && list[2] == 40, "Collections: List.RemoveAt method");
    }

    // ==================== Memory Copy Tests ====================

    private static void TestMemCopy8Bytes()
    {
        byte* src = stackalloc byte[8];
        byte* dest = stackalloc byte[8];

        for (int i = 0; i < 8; i++) src[i] = (byte)(i + 1);
        for (int i = 0; i < 8; i++) dest[i] = 0;

        MemoryOp.MemCopy(dest, src, 8);

        bool passed = true;
        for (int i = 0; i < 8; i++)
        {
            if (dest[i] != (byte)(i + 1)) passed = false;
        }
        Assert.True(passed, "MemCopy: 8-byte copy");
    }

    private static void TestMemCopy16Bytes()
    {
        byte* src = stackalloc byte[16];
        byte* dest = stackalloc byte[16];

        for (int i = 0; i < 16; i++) src[i] = (byte)(i + 1);
        for (int i = 0; i < 16; i++) dest[i] = 0;

        MemoryOp.MemCopy(dest, src, 16);

        bool passed = true;
        for (int i = 0; i < 16; i++)
        {
            if (dest[i] != (byte)(i + 1)) passed = false;
        }
        Assert.True(passed, "MemCopy: 16-byte copy");
    }

    private static void TestMemCopy24Bytes()
    {
        byte* src = stackalloc byte[24];
        byte* dest = stackalloc byte[24];

        for (int i = 0; i < 24; i++) src[i] = (byte)(i + 1);
        for (int i = 0; i < 24; i++) dest[i] = 0;

        MemoryOp.MemCopy(dest, src, 24);

        bool passed = true;
        for (int i = 0; i < 24; i++)
        {
            if (dest[i] != (byte)(i + 1)) passed = false;
        }
        Assert.True(passed, "MemCopy: 24-byte copy");
    }

    private static void TestMemCopy32Bytes()
    {
        byte* src = stackalloc byte[32];
        byte* dest = stackalloc byte[32];

        for (int i = 0; i < 32; i++) src[i] = (byte)(i + 1);
        for (int i = 0; i < 32; i++) dest[i] = 0;

        MemoryOp.MemCopy(dest, src, 32);

        bool passed = true;
        for (int i = 0; i < 32; i++)
        {
            if (dest[i] != (byte)(i + 1)) passed = false;
        }
        Assert.True(passed, "MemCopy: 32-byte copy");
    }

    private static void TestMemCopy48Bytes()
    {
        byte* src = stackalloc byte[48];
        byte* dest = stackalloc byte[48];

        for (int i = 0; i < 48; i++) src[i] = (byte)((i + 1) & 0xFF);
        for (int i = 0; i < 48; i++) dest[i] = 0;

        MemoryOp.MemCopy(dest, src, 48);

        bool passed = true;
        for (int i = 0; i < 48; i++)
        {
            if (dest[i] != (byte)((i + 1) & 0xFF)) passed = false;
        }
        Assert.True(passed, "MemCopy: 48-byte copy");
    }

    private static void TestMemCopy64Bytes()
    {
        byte* src = stackalloc byte[64];
        byte* dest = stackalloc byte[64];

        for (int i = 0; i < 64; i++) src[i] = (byte)((i + 1) & 0xFF);
        for (int i = 0; i < 64; i++) dest[i] = 0;

        MemoryOp.MemCopy(dest, src, 64);

        bool passed = true;
        for (int i = 0; i < 64; i++)
        {
            if (dest[i] != (byte)((i + 1) & 0xFF)) passed = false;
        }
        Assert.True(passed, "MemCopy: 64-byte copy");
    }

    private static void TestMemCopy80Bytes()
    {
        byte* src = stackalloc byte[80];
        byte* dest = stackalloc byte[80];

        for (int i = 0; i < 80; i++) src[i] = (byte)((i + 1) & 0xFF);
        for (int i = 0; i < 80; i++) dest[i] = 0;

        MemoryOp.MemCopy(dest, src, 80);

        bool passed = true;
        for (int i = 0; i < 80; i++)
        {
            if (dest[i] != (byte)((i + 1) & 0xFF)) passed = false;
        }
        Assert.True(passed, "MemCopy: 80-byte copy");
    }

    private static void TestMemCopy128Bytes()
    {
        byte* src = stackalloc byte[128];
        byte* dest = stackalloc byte[128];

        for (int i = 0; i < 128; i++) src[i] = (byte)((i + 1) & 0xFF);
        for (int i = 0; i < 128; i++) dest[i] = 0;

        MemoryOp.MemCopy(dest, src, 128);

        bool passed = true;
        for (int i = 0; i < 128; i++)
        {
            if (dest[i] != (byte)((i + 1) & 0xFF)) passed = false;
        }
        Assert.True(passed, "MemCopy: 128-byte copy");
    }

    private static void TestMemCopy256Bytes()
    {
        byte* src = stackalloc byte[256];
        byte* dest = stackalloc byte[256];

        for (int i = 0; i < 256; i++) src[i] = (byte)(i & 0xFF);
        for (int i = 0; i < 256; i++) dest[i] = 0;

        MemoryOp.MemCopy(dest, src, 256);

        bool passed = true;
        for (int i = 0; i < 256; i++)
        {
            if (dest[i] != (byte)(i & 0xFF)) passed = false;
        }
        Assert.True(passed, "MemCopy: 256-byte copy");
    }

    private static void TestMemCopy264Bytes()
    {
        byte* src = stackalloc byte[264];
        byte* dest = stackalloc byte[264];

        for (int i = 0; i < 264; i++) src[i] = (byte)(i & 0xFF);
        for (int i = 0; i < 264; i++) dest[i] = 0;

        MemoryOp.MemCopy(dest, src, 264);

        bool passed = true;
        for (int i = 0; i < 264; i++)
        {
            if (dest[i] != (byte)(i & 0xFF)) passed = false;
        }
        Assert.True(passed, "MemCopy: 264-byte copy");
    }

    private static void TestMemSet64Bytes()
    {
        byte* dest = stackalloc byte[64];

        // Clear first
        for (int i = 0; i < 64; i++) dest[i] = 0;

        // Fill with value 0xAB
        MemoryOp.MemSet(dest, 0xAB, 64);

        bool passed = true;
        for (int i = 0; i < 64; i++)
        {
            if (dest[i] != 0xAB) passed = false;
        }
        Assert.True(passed, "MemSet: 64 bytes with 0xAB");
    }

    private static void TestMemMoveOverlap()
    {
        // Test overlapping copy (dest > src)
        byte* buffer = stackalloc byte[32];

        for (int i = 0; i < 16; i++) buffer[i] = (byte)(i + 1);
        for (int i = 16; i < 32; i++) buffer[i] = 0;

        // Move 16 bytes from offset 0 to offset 8 (overlapping)
        MemoryOp.MemMove(buffer + 8, buffer, 16);

        bool passed = true;
        // First 8 bytes should be unchanged
        for (int i = 0; i < 8; i++)
        {
            if (buffer[i] != (byte)(i + 1)) passed = false;
        }
        // Bytes 8-23 should be copies of original 0-15
        for (int i = 8; i < 24; i++)
        {
            if (buffer[i] != (byte)(i - 8 + 1)) passed = false;
        }
        Assert.True(passed, "MemMove: overlapping regions");
    }

    // ==================== Array.Copy Tests ====================

    private static void TestArrayCopyIntArray()
    {
        int[] source = new int[] { 1, 2, 3, 4, 5 };
        int[] dest = new int[5];

        Array.Copy(source, dest, 5);

        bool passed = dest[0] == 1 && dest[1] == 2 && dest[2] == 3 && dest[3] == 4 && dest[4] == 5;
        Assert.True(passed, "Array.Copy: int[] copy");
    }

    private static void TestArrayCopyByteArray()
    {
        byte[] source = new byte[64];
        byte[] dest = new byte[64];

        for (int i = 0; i < 64; i++) source[i] = (byte)(i + 1);

        Array.Copy(source, dest, 64);

        bool passed = true;
        for (int i = 0; i < 64; i++)
        {
            if (dest[i] != (byte)(i + 1)) passed = false;
        }
        Assert.True(passed, "Array.Copy: byte[] 64 bytes");
    }

    private static void TestArrayCopyLargeArray()
    {
        byte[] source = new byte[256];
        byte[] dest = new byte[256];

        for (int i = 0; i < 256; i++) source[i] = (byte)(i & 0xFF);

        Array.Copy(source, dest, 256);

        bool passed = true;
        for (int i = 0; i < 256; i++)
        {
            if (dest[i] != (byte)(i & 0xFF)) passed = false;
        }
        Assert.True(passed, "Array.Copy: byte[] 256 bytes (large SIMD)");
    }
}

// Test struct for boxing and collection tests
internal struct TestPoint
{
    public int X;
    public int Y;
}
