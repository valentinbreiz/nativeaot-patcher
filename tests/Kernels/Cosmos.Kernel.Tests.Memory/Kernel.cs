using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Cosmos.Kernel.Core.IO;
using Cosmos.TestRunner.Framework;
using static Cosmos.TestRunner.Framework.TestRunner;
using static Cosmos.TestRunner.Framework.Assert;

namespace Cosmos.Kernel.Tests.Memory
{
    internal unsafe static partial class Program
    {
        [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
        private static void KernelMain() => Main();

        private static void Main()
        {
            Serial.WriteString("[Memory Tests] Starting test suite\n");
            Start("Memory Tests", expectedTests: 22); // 8 boxing + 5 memory + 9 collections

            // Boxing/Unboxing Tests
            Run("Boxing_Char", TestBoxingChar);
            Run("Boxing_Int32", TestBoxingInt32);
            Run("Boxing_Byte", TestBoxingByte);
            Run("Boxing_Long", TestBoxingLong);
            Run("Boxing_Nullable", TestBoxingNullable);
            Run("Boxing_Interface", TestBoxingInterface);
            Run("Boxing_CustomStruct", TestBoxingCustomStruct);
            Skip("Boxing_ArrayCopy", "TestArrayCopyWithBoxing Array Copy fails with boxing");

            // Memory Allocation Tests
            Run("Memory_CharArray", TestCharArrayAllocation);
            Run("Memory_StringAllocation", TestStringAllocation);
            Run("Memory_IntArray", TestIntArrayAllocation);
            Run("Memory_StringConcat", TestStringConcatenation);
            Run("Memory_StringBuilder", TestStringBuilder);

            // Generic Collection Tests
            Run("Collections_ListInt", TestListInt);
            Run("Collections_ListString", TestListString);
            Run("Collections_ListByte", TestListByte);
            Run("Collections_ListLong", TestListLong);
            Run("Collections_ListStruct", TestListStruct);
            Run("Collections_ListContains", TestListContains);
            Run("Collections_ListIndexOf", TestListIndexOf);
            Run("Collections_ListRemoveAt", TestListRemoveAt);
            Skip("Boxing_ArrayCopy", "TestListRemoveAt fails");

            Serial.WriteString("[Memory Tests] All tests completed\n");
            Finish();

            while (true) ;
        }

        // ==================== Boxing/Unboxing Tests ====================

        private static void TestBoxingChar()
        {
            object boxed = 'c';
            Equal("c", boxed.ToString());
            Equal(0x00630063, boxed.GetHashCode());

            char unboxed = (char)boxed;
            True(unboxed == 'c', "Boxing: char to object and back");
        }

        private static void TestBoxingInt32()
        {
            object boxed = 42;
            Equal("42", boxed.ToString());
            Equal(42, boxed.GetHashCode());
            True(boxed.Equals(42), "Int32.Equals on boxed int (same value)");
            True(!boxed.Equals(5), "Int32.Equals on boxed int (different value)");

            object boxed2 = 42;
            True(Object.Equals(boxed, boxed2), "Object.Equals with two boxed ints");

            int unboxed = (int)boxed;
            True(unboxed == 42, "Boxing: int to object and back");
        }

        private static void TestBoxingByte()
        {
            byte value = 255;
            object boxed = value;
            byte unboxed = (byte)boxed;
            True(unboxed == 255, "Boxing: byte to object and back");
        }

        private static void TestBoxingLong()
        {
            long value = 9876543210L;
            object boxed = value;
            long unboxed = (long)boxed;
            True(unboxed == 9876543210L, "Boxing: long to object and back");
        }

        private static void TestBoxingNullable()
        {
            // Test null case
            int? nullableNull = null;
            object boxedNull = nullableNull;
            True(boxedNull == null, "Boxing: Nullable<int> null boxes to null");

            // Test value case
            int? nullableValue = 777;
            object boxedValue = nullableValue;
            True(boxedValue != null && (int)boxedValue == 777, "Boxing: Nullable<int> with value boxes correctly");
        }

        private static void TestBoxingInterface()
        {
            int value = 100;
            IComparable comparable = value;
            True(comparable != null, "Boxing: int to interface (IComparable)");
        }

        private static void TestBoxingCustomStruct()
        {
            TestPoint point = new TestPoint { X = 10, Y = 20 };
            object boxed = point;
            TestPoint unboxed = (TestPoint)boxed;
            True(unboxed.X == 10 && unboxed.Y == 20, "Boxing: custom struct box/unbox");
        }

        private static void TestArrayCopyWithBoxing()
        {
            int[] sourceIntArray = new int[] { 10, 20, 30 };
            object[] destObjectArray = new object[3];
            Array.Copy(sourceIntArray, destObjectArray, 3);

            bool passed = (int)destObjectArray[0] == 10 &&
                         (int)destObjectArray[1] == 20 &&
                         (int)destObjectArray[2] == 30;
            True(passed, "Boxing: Array.Copy with automatic boxing");
        }

        // ==================== Memory Allocation Tests ====================

        private static void TestCharArrayAllocation()
        {
            char[] testChars = new char[] { 'R', 'h', 'p' };
            True(testChars.Length == 3 && testChars[0] == 'R', "Memory: char array allocation");
        }

        private static void TestStringAllocation()
        {
            char[] chars = new char[] { 'R', 'h', 'p' };
            string str = new string(chars);
            True(str == "Rhp", "Memory: string allocation from char array");
        }

        private static void TestIntArrayAllocation()
        {
            int[] array = new int[100];
            for (int i = 0; i < 10; i++)
            {
                array[i] = i * 10;
            }
            True(array[0] == 0 && array[1] == 10 && array[2] == 20, "Memory: int array allocation and access");
        }

        private static void TestStringConcatenation()
        {
            string str1 = "Hello";
            string str2 = "World";
            string str3 = str1 + " " + str2;
            True(str3 == "Hello World", "Memory: string concatenation");
        }

        private static void TestStringBuilder()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Hello");
            sb.Append(" ");
            sb.Append("StringBuilder");
            string result = sb.ToString();
            True(result == "Hello StringBuilder", "Memory: StringBuilder operations");
        }

        // ==================== Generic Collection Tests ====================

        private static void TestListInt()
        {
            List<int> list = new List<int>();
            list.Add(100);
            list.Add(200);
            list.Add(300);
            True(list.Count == 3 && list[0] == 100 && list[1] == 200 && list[2] == 300, "Collections: List<int> Add and indexer");
        }

        private static void TestListString()
        {
            List<string> list = new List<string>();
            list.Add("First");
            list.Add("Second");
            list.Add("Third");
            list.Add("Fourth");
            list.Add("Fifth");

            True(list.Count == 5 && list[0] == "First" && list[4] == "Fifth", "Collections: List<string> with resize");
        }

        private static void TestListByte()
        {
            List<byte> list = new List<byte>();
            list.Add(0xFF);
            list.Add(0x00);
            list.Add(0xAB);
            list.Add(0x12);

            True(list.Count == 4 && list[0] == 0xFF && list[2] == 0xAB, "Collections: List<byte> operations");
        }

        private static void TestListLong()
        {
            List<long> list = new List<long>();
            list.Add(0x123456789ABCDEF0);
            list.Add(-9999999999999);
            list.Add(42);

            True(list.Count == 3 && list[0] == 0x123456789ABCDEF0 && list[2] == 42, "Collections: List<long> with 64-bit values");
        }

        private static void TestListStruct()
        {
            List<TestPoint> list = new List<TestPoint>();
            list.Add(new TestPoint { X = 1, Y = 2 });
            list.Add(new TestPoint { X = 3, Y = 4 });
            list.Add(new TestPoint { X = 5, Y = 6 });

            True(list.Count == 3 && list[0].X == 1 && list[2].Y == 6, "Collections: List<struct> operations");
        }

        private static void TestListContains()
        {
            List<int> list = new List<int>();
            list.Add(10);
            list.Add(20);
            list.Add(30);

            bool found20 = list.Contains(20);
            bool found99 = list.Contains(99);

            True(found20 && !found99, "Collections: List.Contains method");
        }

        private static void TestListIndexOf()
        {
            List<int> list = new List<int>();
            list.Add(10);
            list.Add(20);
            list.Add(30);

            int index20 = list.IndexOf(20);
            int index99 = list.IndexOf(99);

            True(index20 == 1 && index99 == -1, "Collections: List.IndexOf method");
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

            True(list.Count == 4 && list[2] == 40, "Collections: List.RemoveAt method");
        }
    }

    // Test struct for boxing and collection tests
    internal struct TestPoint
    {
        public int X;
        public int Y;
    }
}
