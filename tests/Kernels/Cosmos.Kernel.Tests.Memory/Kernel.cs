using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.TestRunner.Framework;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;

namespace Cosmos.Kernel.Tests.Memory;

public unsafe class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
        TR.Start("Memory Tests", expectedTests: 81);

        // Boxing/Unboxing Tests
        TR.Run("Boxing_Char", TestBoxingChar);
        TR.Run("Boxing_Int32", TestBoxingInt32);
        TR.Run("Boxing_Byte", TestBoxingByte);
        TR.Run("Boxing_Long", TestBoxingLong);
        TR.Run("Boxing_Nullable", TestBoxingNullable);
        TR.Run("Boxing_Interface", TestBoxingInterface);
        TR.Run("Boxing_CustomStruct", TestBoxingCustomStruct);
        TR.Run("Boxing_ArrayCopy", TestArrayCopyWithBoxing);
        TR.Run("Boxing_Enum", TestBoxingEnum);
        TR.Run("Boxing_ValueTuple", TestBoxingValueTuple);
        TR.Run("Boxing_NullInterface", TestBoxingNullInterface);

        // Memory Allocation Tests
        TR.Run("Memory_CharArray", TestCharArrayAllocation);
        TR.Run("Memory_StringAllocation", TestStringAllocation);
        TR.Run("Memory_IntArray", TestIntArrayAllocation);
        TR.Run("Memory_StringConcat", TestStringConcatenation);
        TR.Run("Memory_StringBuilder", TestStringBuilder);
        TR.Run("Memory_ZeroLengthArray", TestZeroLengthArray);
        TR.Run("Memory_EmptyString", TestEmptyString);
        TR.Run("Memory_LargeAllocation", TestLargeAllocation);

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
        TR.Run("Collections_ListEmpty", TestListEmpty);

        // Dictionary
        TR.Run("Collections_DictCustomComparer", TestDictionaryCustomComparer);
        TR.Run("Collections_DictAddGet", TestDictionaryAddGet);
        TR.Run("Collections_DictIndexer", TestDictionaryIndexer);
        TR.Run("Collections_DictContains", TestDictionaryContains);
        TR.Run("Collections_DictRemove", TestDictionaryRemove);
        TR.Run("Collections_DictClear", TestDictionaryClear);
        TR.Run("Collections_DictTryGetValue", TestDictionaryTryGetValue);
        TR.Run("Collections_DictKeysValues", TestDictionaryKeysValues);
        TR.Run("Collections_DictEmpty", TestDictionaryEmpty);

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
        TR.Run("MemCopy_0Bytes", TestMemCopy0Bytes);
        TR.Run("MemCopy_1Byte", TestMemCopy1Byte);
        TR.Run("MemMove_Overlap_DestBeforeSrc", TestMemMoveOverlapDestBeforeSrc);

        // Array.Copy Tests (uses SIMD via memmove/RhBulkMoveWithWriteBarrier)
        TR.Run("ArrayCopy_IntArray", TestArrayCopyIntArray);
        TR.Run("ArrayCopy_ByteArray", TestArrayCopyByteArray);
        TR.Run("ArrayCopy_LargeArray", TestArrayCopyLargeArray);
        TR.Run("ArrayCopy_ZeroLength", TestArrayCopyZeroLength);
        TR.Run("ArrayCopy_Overlap", TestArrayCopyOverlap);

        // Garbage Collection Tests
        TR.Run("GC_IsEnabled", TestGCIsEnabled);
        TR.Run("GC_GetStats", TestGCGetStats);
        TR.Run("GC_CollectBasic", TestGCCollectBasic);
        TR.Run("GC_StatsIncrement", TestGCStatsIncrement);
        TR.Run("GC_ExactCollectionCount", TestGCExactCollectionCount);
        TR.Run("GC_ObjectSurvival", TestGCObjectSurvival);
        TR.Run("GC_StringSurvival", TestGCStringSurvival);
        TR.Run("GC_ArraySurvival", TestGCArraySurvival);
        TR.Run("GC_ListSurvival", TestGCListSurvival);
        TR.Run("GC_UnreachableExactCount", TestGCUnreachableExactCount);
        TR.Run("GC_ObjectGraphSurvival", TestGCObjectGraphSurvival);
        TR.Run("GC_MixedTypeSurvival", TestGCMixedTypeSurvival);
        TR.Run("GC_AllocAfterCollect", TestGCAllocAfterCollect);
        TR.Run("GC_WeakReference", TestGCWeakReference);
        TR.Run("GC_LargeAllocCollect", TestGCLargeAllocationAndCollect);
        TR.Run("GC_StructArraySurvival", TestGCStructArraySurvival);
        TR.Run("GC_DictSurvival", TestGCDictionarySurvival);
        TR.Run("GC_PageAccounting", TestGCPageAccounting);

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

    private static void TestBoxingEnum()
    {
        TestEnum val = TestEnum.ValueB;
        object boxed = val;
        Assert.True(boxed is TestEnum, "Boxing: enum is TestEnum");
        Assert.True((TestEnum)boxed == TestEnum.ValueB, "Boxing: enum value preserved");
    }

    private static void TestBoxingValueTuple()
    {
        var tuple = (1, "test");
        object boxed = tuple;
        var unboxed = ((int, string))boxed;
        Assert.True(unboxed.Item1 == 1 && unboxed.Item2 == "test", "Boxing: ValueTuple box/unbox");
    }

    private static void TestBoxingNullInterface()
    {
        IComparable comparable = null;
        object boxed = comparable;
        Assert.True(boxed == null, "Boxing: null interface is null object");
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

    private static void TestZeroLengthArray()
    {
        int[] arr = new int[0];
        Assert.True(arr != null && arr.Length == 0, "Memory: zero-length array allocation");
    }

    private static void TestEmptyString()
    {
        string s = string.Empty;
        Assert.True(s != null && s.Length == 0, "Memory: empty string access");
    }

    private static void TestLargeAllocation()
    {
        int size = 1024 * 1024; // 1MB
        byte[] large = new byte[size];
        large[0] = 0xAA;
        large[size - 1] = 0x55;
        Assert.True(large.Length == size && large[0] == 0xAA && large[size - 1] == 0x55, "Memory: 1MB allocation");
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

    private static void TestListEmpty()
    {
        List<int> list = new List<int>();
        Assert.True(list.Count == 0, "Collections: empty list count");
        Assert.True(!list.Contains(1), "Collections: empty list contains");
        Assert.True(list.ToArray().Length == 0, "Collections: empty list ToArray");
    }

    // ==================== Dictionary Tests ====================

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
        dict.Add("KeyA", 1);
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

    private static void TestDictionaryEmpty()
    {
        Dictionary<int, int> dict = new Dictionary<int, int>();
        Assert.True(dict.Count == 0, "Dictionary: empty count");
        Assert.True(!dict.ContainsKey(1), "Dictionary: empty ContainsKey");
        int val;
        Assert.True(!dict.TryGetValue(1, out val), "Dictionary: empty TryGetValue");
    }

    // ==================== IEnumerable Tests ====================

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

    private static void TestMemCopy0Bytes()
    {
        byte* src = stackalloc byte[1];
        byte* dest = stackalloc byte[1];
        src[0] = 0xAA;
        dest[0] = 0xBB;
        MemoryOp.MemCopy(dest, src, 0);
        Assert.True(dest[0] == 0xBB, "MemCopy: 0 bytes is no-op");
    }

    private static void TestMemCopy1Byte()
    {
        byte* src = stackalloc byte[1];
        byte* dest = stackalloc byte[1];
        src[0] = 0xAA;
        dest[0] = 0xBB;
        MemoryOp.MemCopy(dest, src, 1);
        Assert.True(dest[0] == 0xAA, "MemCopy: 1 byte copy");
    }

    private static void TestMemMoveOverlapDestBeforeSrc()
    {
        byte* buffer = stackalloc byte[32];
        for (int i = 0; i < 32; i++) buffer[i] = (byte)i;

        // Move 16 bytes from offset 8 to offset 0 (overlapping, dest < src)
        MemoryOp.MemMove(buffer, buffer + 8, 16);

        bool passed = true;
        for (int i = 0; i < 16; i++)
        {
            if (buffer[i] != (byte)(i + 8)) passed = false;
        }
        Assert.True(passed, "MemMove: overlapping regions (dest < src)");
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

    private static void TestArrayCopyZeroLength()
    {
        int[] src = new int[] { 1 };
        int[] dest = new int[] { 2 };
        Array.Copy(src, dest, 0);
        Assert.True(dest[0] == 2, "Array.Copy: 0 length is no-op");
    }

    private static void TestArrayCopyOverlap()
    {
        int[] arr = new int[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        // Copy {0, 1, 2, 3} to indices 2, 3, 4, 5
        Array.Copy(arr, 0, arr, 2, 4);

        bool passed = arr[0] == 0 && arr[1] == 1 &&
                     arr[2] == 0 && arr[3] == 1 &&
                     arr[4] == 2 && arr[5] == 3 &&
                     arr[6] == 6 && arr[7] == 7;
        Assert.True(passed, "Array.Copy: overlapping regions");
    }

    // ==================== Garbage Collection Tests ====================

    private static void TestGCIsEnabled()
    {
        Assert.True(GarbageCollector.IsEnabled, "GC: IsEnabled should be true");
    }

    private static void TestGCGetStats()
    {
        GarbageCollector.GetStats(out int totalCollections, out int totalObjectsFreed);
        // After running all previous tests, some collections may have already happened
        // (from allocation pressure). Both counters must be non-negative.
        Assert.True(totalCollections >= 0, "GC: totalCollections must be non-negative");
        Assert.True(totalObjectsFreed >= 0, "GC: totalObjectsFreed must be non-negative");
    }

    private static void TestGCCollectBasic()
    {
        // Snapshot before
        GarbageCollector.GetStats(out int collsBefore, out int freedBefore);

        int freed = GarbageCollector.Collect();
        Assert.True(freed >= 0, "GC: Collect must return non-negative freed count");

        // Verify stats incremented by exactly 1 collection
        GarbageCollector.GetStats(out int collsAfter, out int freedAfter);
        Assert.Equal(collsBefore + 1, collsAfter, "GC: Collect must increment collection count by exactly 1");
        Assert.Equal(freedBefore + freed, freedAfter, "GC: totalObjectsFreed must increase by exact freed count");
    }

    private static void TestGCStatsIncrement()
    {
        // Run 3 collections and verify exact increments
        GarbageCollector.GetStats(out int collsBefore, out int freedBefore);

        int freed1 = GarbageCollector.Collect();
        int freed2 = GarbageCollector.Collect();
        int freed3 = GarbageCollector.Collect();

        GarbageCollector.GetStats(out int collsAfter, out int freedAfter);

        // Exactly 3 collections must have been recorded
        Assert.Equal(collsBefore + 3, collsAfter, "GC: 3 successive Collects must increment count by exactly 3");

        // Total freed must be exact sum
        int expectedFreed = freedBefore + freed1 + freed2 + freed3;
        Assert.Equal(expectedFreed, freedAfter, "GC: totalObjectsFreed must equal sum of all Collect return values");
    }

    private static void TestGCExactCollectionCount()
    {
        // Verify that even a collection that frees nothing still increments the counter
        // First collect to clean up any existing garbage
        GarbageCollector.Collect();

        GarbageCollector.GetStats(out int collsBefore, out int freedBefore);

        // Second collect on a clean heap — likely frees 0 objects
        int freed = GarbageCollector.Collect();

        GarbageCollector.GetStats(out int collsAfter, out int freedAfter);

        // Collection count must always increment by 1, even if freed == 0
        Assert.Equal(collsBefore + 1, collsAfter, "GC: collection count increments even when freed == 0");
        Assert.Equal(freedBefore + freed, freedAfter, "GC: freed accounting exact even for zero-freed collection");
    }

    private static void TestGCObjectSurvival()
    {
        // Reachable boxed values must survive collection
        object boxed = 42;
        GarbageCollector.Collect();
        Assert.True((int)boxed == 42, "GC: boxed int survives collection");
    }

    private static void TestGCStringSurvival()
    {
        // Dynamically created strings must survive when reachable
        string s1 = "Hello";
        string s2 = "World";
        string concat = s1 + " " + s2;
        GarbageCollector.Collect();
        Assert.True(concat == "Hello World", "GC: concatenated string survives collection");
    }

    private static void TestGCArraySurvival()
    {
        // Arrays must survive collection when reachable
        int[] arr = new int[10];
        for (int i = 0; i < 10; i++) arr[i] = i * 10;
        GarbageCollector.Collect();
        Assert.Equal(10, arr.Length, "GC: array length survives collection");
        Assert.True(arr[0] == 0 && arr[5] == 50 && arr[9] == 90, "GC: array contents survive collection");
    }

    private static void TestGCListSurvival()
    {
        // List<T> with internal array must survive collection
        List<int> list = new List<int>();
        list.Add(100);
        list.Add(200);
        list.Add(300);
        GarbageCollector.Collect();
        Assert.Equal(3, list.Count, "GC: list count survives collection");
        Assert.True(list[0] == 100 && list[1] == 200 && list[2] == 300, "GC: list contents survive collection");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateExactUnreachable(int count, int arraySize)
    {
        // Each iteration creates exactly 1 unreachable byte[] object
        for (int i = 0; i < count; i++)
        {
            object obj = new byte[arraySize];
            // Prevent optimizer from removing the allocation
            if (obj == null) break;
        }
    }

    private static void TestGCUnreachableExactCount()
    {
        // Collect twice to stabilize — clear any prior garbage
        GarbageCollector.Collect();
        GarbageCollector.Collect();

        GarbageCollector.GetStats(out int _, out int freedBefore);

        // Allocate exactly 50 unreachable byte[64] arrays
        // Each byte[64]: BaseSize(24) + 64*1 = 88, Align(88) = 88
        const int objectCount = 50;
        AllocateExactUnreachable(objectCount, 64);

        // Collect — must free exactly those 50 objects (plus possibly some
        // internal allocations from the loop itself, e.g., enumerator objects)
        int freed = GarbageCollector.Collect();

        GarbageCollector.GetStats(out int _2, out int freedAfter);

        // freed must be at least objectCount - 2 (conservative scanning may
        // falsely retain a few objects when stack values coincidentally look like
        // GC heap pointers)
        Assert.True(freed >= objectCount - 2,
            "GC: must free at least " + (objectCount - 2) + " unreachable byte[64] objects, freed: " + freed);

        // freedAfter - freedBefore must match the Collect return value exactly
        Assert.Equal(freed, freedAfter - freedBefore,
            "GC: totalObjectsFreed delta must match Collect return value exactly");
    }

    private static void TestGCObjectGraphSurvival()
    {
        // Object graph: list holding strings must keep everything alive
        List<string> strings = new List<string>();
        strings.Add("Alpha");
        strings.Add("Beta");
        strings.Add("Gamma");

        GarbageCollector.Collect();

        Assert.Equal(3, strings.Count, "GC: object graph list count survives");
        Assert.True(strings[0] == "Alpha", "GC: object graph first element survives");
        Assert.True(strings[2] == "Gamma", "GC: object graph last element survives");
    }

    private static void TestGCMixedTypeSurvival()
    {
        // Various types allocated and kept alive across GC
        byte[] byteArr = new byte[] { 0xAA, 0xBB, 0xCC };
        int[] intArr = new int[] { 1, 2, 3 };
        string str = "MixedTest";
        object boxedLong = 9876543210L;
        TestPoint point = new TestPoint { X = 42, Y = 99 };
        object boxedPoint = point;

        GarbageCollector.Collect();

        Assert.True(byteArr[0] == 0xAA && byteArr[2] == 0xCC, "GC: byte array survives mixed collection");
        Assert.Equal(2, intArr[1], "GC: int array survives mixed collection");
        Assert.True(str == "MixedTest", "GC: string survives mixed collection");
        Assert.True((long)boxedLong == 9876543210L, "GC: boxed long survives mixed collection");
        TestPoint unboxed = (TestPoint)boxedPoint;
        Assert.True(unboxed.X == 42 && unboxed.Y == 99, "GC: boxed struct survives mixed collection");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateGarbage(int count, int size)
    {
        for (int i = 0; i < count; i++)
        {
            byte[] temp = new byte[size];
            if (temp == null) break;
        }
    }

    private static void TestGCAllocAfterCollect()
    {
        // Generate garbage then collect, then allocate again
        AllocateGarbage(50, 256);

        GarbageCollector.GetStats(out int collsBefore, out int _);
        int freed = GarbageCollector.Collect();
        GarbageCollector.GetStats(out int collsAfter, out int _2);

        // Exactly 1 collection must have been recorded
        Assert.Equal(collsBefore + 1, collsAfter, "GC: exactly 1 collection after AllocateGarbage");
        // Must have freed at least 50 objects
        Assert.True(freed >= 50, "GC: must free at least 50 garbage byte[256] arrays, freed: " + freed);

        // Must be able to allocate after GC reclaims memory
        int[] newArr = new int[100];
        for (int i = 0; i < 100; i++) newArr[i] = i;
        Assert.Equal(99, newArr[99], "GC: allocation works after collection");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateWeakRef()
    {
        object target = new byte[128];
        return new WeakReference(target);
    }

    private static void TestGCWeakReference()
    {
        // Create a weak reference to an object that becomes unreachable
        WeakReference weakRef = CreateWeakRef();

        // After collection, the weak handle's target must be cleared
        GarbageCollector.Collect();
        GarbageCollector.Collect(); // Second pass to ensure weak handles are freed

        Assert.True(!weakRef.IsAlive, "GC: weak reference cleared after collection");
        Assert.True(weakRef.Target == null, "GC: weak reference target is null after collection");
    }

    private static void TestGCLargeAllocationAndCollect()
    {
        // Allocate 20 × byte[4096], let them become garbage, then collect
        const int count = 20;
        AllocateGarbage(count, 4096);

        GarbageCollector.GetStats(out int _, out int freedBefore);
        int freed = GarbageCollector.Collect();
        GarbageCollector.GetStats(out int _2, out int freedAfter);

        // Must free at least the 20 large arrays
        Assert.True(freed >= count,
            "GC: must free at least " + count + " large byte[4096] arrays, freed: " + freed);
        Assert.Equal(freed, freedAfter - freedBefore,
            "GC: freedAfter - freedBefore must match freed count exactly");

        // Must be able to allocate again after large garbage is collected
        byte[] postGC = new byte[8192];
        postGC[0] = 0xDE;
        postGC[8191] = 0xAD;
        Assert.Equal((byte)0xDE, postGC[0], "GC: post-GC large alloc byte 0");
        Assert.Equal((byte)0xAD, postGC[8191], "GC: post-GC large alloc last byte");
    }

    private static void TestGCStructArraySurvival()
    {
        // Array of structs with value-type fields must survive GC
        TestPoint[] points = new TestPoint[5];
        for (int i = 0; i < 5; i++)
        {
            points[i] = new TestPoint { X = i * 10, Y = i * 20 };
        }

        GarbageCollector.Collect();

        Assert.Equal(5, points.Length, "GC: struct array length survives");
        Assert.Equal(0, points[0].X, "GC: struct array [0].X survives");
        Assert.Equal(0, points[0].Y, "GC: struct array [0].Y survives");
        Assert.Equal(40, points[4].X, "GC: struct array [4].X survives");
        Assert.Equal(80, points[4].Y, "GC: struct array [4].Y survives");
    }

    private static void TestGCDictionarySurvival()
    {
        // Dictionary with string keys and int values must survive GC
        Dictionary<string, int> dict = new Dictionary<string, int>();
        dict.Add("One", 1);
        dict.Add("Two", 2);
        dict.Add("Three", 3);

        GarbageCollector.Collect();

        Assert.Equal(3, dict.Count, "GC: dictionary count survives collection");
        Assert.Equal(1, dict["One"], "GC: dictionary value 'One' survives");
        Assert.Equal(3, dict["Three"], "GC: dictionary value 'Three' survives");
        Assert.True(dict.ContainsKey("Two"), "GC: dictionary ContainsKey after collection");
    }

    private static void TestGCPageAccounting()
    {
        // Verify PageAllocator accounting is consistent before and after GC
        ulong totalPages = PageAllocator.TotalPageCount;
        ulong freePagesBefore = PageAllocator.FreePageCount;
        ulong usedPagesBefore = totalPages - freePagesBefore;

        // Total pages must be positive
        Assert.True(totalPages > 0, "GC: TotalPageCount must be > 0");

        // Used pages must not exceed total
        Assert.True(usedPagesBefore <= totalPages,
            "GC: used pages (" + usedPagesBefore + ") must be <= total (" + totalPages + ")");

        // Allocate garbage to consume pages, then collect to reclaim
        AllocateGarbage(30, 4096);
        GarbageCollector.Collect();

        ulong freePagesAfter = PageAllocator.FreePageCount;

        // Total pages must stay constant (physical memory doesn't change)
        Assert.Equal((int)totalPages, (int)PageAllocator.TotalPageCount,
            "GC: TotalPageCount must not change after GC");

        // Free pages should stay the same or increase after GC (segments released)
        Assert.True(freePagesAfter >= freePagesBefore,
            "GC: free pages after collect (" + freePagesAfter + ") must be >= before (" + freePagesBefore + ")");
    }

    private class SimpleStringComparer : IEqualityComparer<string>
    {
        public bool Equals(string x, string y) => x == y || (x != null && y != null && x.Equals(y));
        public int GetHashCode(string obj) => obj?.GetHashCode() ?? 0;
    }
}

internal enum TestEnum
{
    ValueA,
    ValueB,
    ValueC
}

// Test struct for boxing and collection tests
internal struct TestPoint
{
    public int X;
    public int Y;
}
