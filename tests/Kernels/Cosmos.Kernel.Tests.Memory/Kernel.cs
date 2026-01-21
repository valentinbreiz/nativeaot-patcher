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
        TR.Start("Memory Tests", expectedTests: 51); // 8 boxing + 5 memory + 8 collections + 12 SIMD + 3 Array.Copy + 15 exhaustive collections

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
        TR.Run("Collections_ListInsert", TestListInsert);
        TR.Run("Collections_ListRemove", TestListRemove);
        TR.Run("Collections_ListClear", TestListClear);
        TR.Run("Collections_ListToArray", TestListToArray);
        TR.Run("Collections_ListForeach", TestListForeach);

        // Dictionary
        TR.Run("Collections_StringEquals", TestStringEquals);
        TR.Run("Collections_StringComparer", TestStringEqualityComparer);
        TR.Run("Collections_DictCustomComparer", TestDictionaryCustomComparer);
        TR.Run("Collections_DictAddGet", TestDictionaryAddGet);
        TR.Run("Collections_DictIndexer", TestDictionaryIndexer);
        TR.Run("Collections_DictContains", TestDictionaryContains);
        TR.Run("Collections_DictRemove", TestDictionaryRemove);
        TR.Run("Collections_DictClear", TestDictionaryClear);
        TR.Run("Collections_DictTryGetValue", TestDictionaryTryGetValue);
        TR.Run("Collections_DictKeysValues", TestDictionaryKeysValues);

        // IEnumerable
        TR.Run("Collections_IEnumerable", TestIEnumerable);

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

    private static void TestDictionaryAddGet()
    {
        Dictionary<int, string> dict = new Dictionary<int, string>();
        dict.Add(1, "One");
        dict.Add(2, "Two");

        Assert.True(dict.Count == 2, "Dictionary.Add count");
        Assert.True(dict[1] == "One" && dict[2] == "Two", "Dictionary.Add get values");
    }

    private static void TestStringEqualityComparer()
    {
        var comparer = EqualityComparer<string>.Default;
        Assert.True(comparer != null, "EqualityComparer<string>.Default is not null");
        Assert.True(!comparer.Equals("KeyA", "KeyB"), "Comparer KeyA != KeyB");
        Assert.True(comparer.Equals("KeyA", "KeyA"), "Comparer KeyA == KeyA");
        Assert.True(comparer.GetHashCode("KeyA") != comparer.GetHashCode("KeyB"), "Comparer HashCode KeyA != KeyB");
    }

    private static void TestStringEquals()
    {
        string s1 = "KeyA";
        string s2 = "KeyB";
        bool eq = s1.Equals(s2);
        
        Serial.WriteString("TestStringEquals: 'KeyA'.Equals('KeyB') = ");
        Serial.WriteString(eq.ToString());
        Serial.WriteString("\n");
        
        Assert.True(!eq, "String.Equals('KeyA', 'KeyB') should be false");
        
        string s3 = "KeyA";
        bool eq2 = s1.Equals(s3);
        Assert.True(eq2, "String.Equals('KeyA', 'KeyA') should be true");
    }

    private static void TestDictionaryIndexer()
    {
        Dictionary<string, int> dict = new Dictionary<string, int>();
        
        string keyA = "KeyA";
        string keyB = "KeyB";
        string keyC = "KeyC";

        // 1. Test Add and Get with string keys
        dict.Add(keyA, 10);
        dict.Add(keyB, 20);
        Assert.True(dict[keyA] == 10 && dict[keyB] == 20, "Dictionary string key Add/Get");

        // 2. Test Update via Indexer
        dict[keyA] = 30; 
        Assert.True(dict[keyA] == 30, "Dictionary string key Update");
        
        // 3. Test Insert via Indexer
        dict[keyC] = 40;
        Assert.True(dict[keyC] == 40, "Dictionary string key Insert via Indexer");
    }

    private static void TestDictionaryCustomComparer()
    {
        Dictionary<string, int> dict = new Dictionary<string, int>(new SimpleStringComparer());
        
        Serial.WriteString("CustomComparer: Adding KeyA\n");
        dict.Add("KeyA", 1);
        
        Serial.WriteString("CustomComparer: Adding KeyB\n");
        dict.Add("KeyB", 2);
        
        Assert.True(dict["KeyA"] == 1 && dict["KeyB"] == 2, "Dictionary with custom comparer");
    }

    private static void TestDictionaryContains()
    {
        Dictionary<int, string> dict = new Dictionary<int, string>();
        dict.Add(1, "One");

        Assert.True(dict.ContainsKey(1), "Dictionary.ContainsKey found");
        Assert.True(!dict.ContainsKey(2), "Dictionary.ContainsKey not found");
        Assert.True(dict.ContainsValue("One"), "Dictionary.ContainsValue found");
        Assert.True(!dict.ContainsValue("Two"), "Dictionary.ContainsValue not found");
    }

    private static void TestDictionaryRemove()
    {
        Dictionary<int, string> dict = new Dictionary<int, string>();
        dict.Add(1, "One");
        dict.Add(2, "Two");

        bool removed = dict.Remove(1);
        Assert.True(removed && dict.Count == 1 && !dict.ContainsKey(1), "Dictionary.Remove existing");

        bool removed2 = dict.Remove(3);
        Assert.True(!removed2 && dict.Count == 1, "Dictionary.Remove non-existing");
    }

    private static void TestDictionaryClear()
    {
        Dictionary<int, string> dict = new Dictionary<int, string>();
        dict.Add(1, "One");
        dict.Add(2, "Two");

        dict.Clear();
        Assert.True(dict.Count == 0 && !dict.ContainsKey(1), "Dictionary.Clear");
    }

    private static void TestDictionaryTryGetValue()
    {
        Dictionary<int, string> dict = new Dictionary<int, string>();
        dict.Add(1, "One");

        string val;
        bool found = dict.TryGetValue(1, out val);
        Assert.True(found && val == "One", "Dictionary.TryGetValue found");

        bool found2 = dict.TryGetValue(2, out val);
        Assert.True(!found2 && val == null, "Dictionary.TryGetValue not found");
    }

    private static void TestDictionaryKeysValues()
    {
        Dictionary<int, string> dict = new Dictionary<int, string>();
        dict.Add(1, "One");
        dict.Add(2, "Two");

        int keySum = 0;
        foreach (var k in dict.Keys) keySum += k;

        Assert.True(keySum == 3, "Dictionary.Keys iteration");

        Assert.True(dict.Values != null && dict.Values.Count == 2, "Dictionary.Values access");
    }

    private static void TestListInsert()
    {
        List<int> list = new List<int>();
        list.Add(1);
        list.Add(3);
        list.Insert(1, 2);

        Assert.True(list.Count == 3 && list[1] == 2 && list[2] == 3, "List.Insert");
    }

    private static void TestListRemove()
    {
        List<string> list = new List<string>();
        list.Add("A");
        list.Add("B");
        list.Add("A");

        bool removed = list.Remove("A"); // Removes first "A"
        Assert.True(removed && list.Count == 2 && list[0] == "B" && list[1] == "A", "List.Remove");
    }

    private static void TestListClear()
    {
        List<int> list = new List<int>();
        list.Add(1);
        list.Clear();
        Assert.True(list.Count == 0, "List.Clear");
    }

    private static void TestListToArray()
    {
        List<int> list = new List<int>();
        list.Add(1);
        list.Add(2);
        int[] arr = list.ToArray();

        Assert.True(arr.Length == 2 && arr[0] == 1 && arr[1] == 2, "List.ToArray");
    }

    private static void TestListForeach()
    {
        List<int> list = new List<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        int sum = 0;
        foreach (int i in list) sum += i;

        Assert.True(sum == 6, "List foreach iteration");
    }

    private static void TestIEnumerable()
    {
        int[] arr = new int[] { 1, 2, 3 };
        IEnumerable<int> enumerable = arr;

        int sum = 0;
        foreach (int i in enumerable) sum += i;

        Assert.True(sum == 6, "IEnumerable foreach on array");
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

    private class SimpleStringComparer : IEqualityComparer<string>
    {
        public bool Equals(string x, string y)
        {
            bool result = false;
            if (x == y) result = true;
            else if (x == null || y == null) result = false;
            else result = x.Equals(y);

            Serial.WriteString("Comparer.Equals: ");
            Serial.WriteString(x ?? "null");
            Serial.WriteString(" vs ");
            Serial.WriteString(y ?? "null");
            Serial.WriteString(" -> ");
            Serial.WriteString(result ? "True" : "False");
            Serial.WriteString("\n");

            return result;
        }

        public int GetHashCode(string obj)
        {
            int h = obj.GetHashCode();
            Serial.WriteString("Comparer.GetHashCode: ");
            Serial.WriteString(obj ?? "null");
            Serial.WriteString(" -> ");
            Serial.WriteString(h.ToString());
            Serial.WriteString("\n");
            return h;
        }
    }
}

// Test struct for boxing and collection tests
internal struct TestPoint
{
    public int X;
    public int Y;
}
