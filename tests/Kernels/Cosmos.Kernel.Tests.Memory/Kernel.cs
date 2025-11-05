using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Cosmos.Kernel.Core.IO;
using Cosmos.TestRunner.Framework;

namespace Cosmos.Kernel.Tests.Memory
{
    internal unsafe static partial class Program
    {
        [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
        private static void KernelMain() => Main();

        private static void Main()
        {
            Serial.WriteString("[Memory Tests] Starting test suite\n");
            TestRunner.Start();

            // Boxing/Unboxing Tests
            TestBoxingChar();
            TestBoxingInt32();
            TestBoxingByte();
            TestBoxingLong();
            TestBoxingNullable();
            TestBoxingInterface();
            TestBoxingCustomStruct();
            TestArrayCopyWithBoxing();

            // Memory Allocation Tests
            TestCharArrayAllocation();
            TestStringAllocation();
            TestIntArrayAllocation();
            TestStringConcatenation();
            TestStringBuilder();

            // Generic Collection Tests
            TestListInt();
            TestListString();
            TestListByte();
            TestListLong();
            TestListStruct();
            TestListContains();
            TestListIndexOf();
            TestListRemoveAt();

            Serial.WriteString("[Memory Tests] All tests completed\n");
            TestRunner.Finish();
        }

        // ==================== Boxing/Unboxing Tests ====================

        private static void TestBoxingChar()
        {
            object boxed = 'c';
            Assert.Equal("c", boxed.ToString(), "Char.ToString on boxed char should work");
            Assert.Equal(0x00630063, boxed.GetHashCode(), "Char.GetHashCode on boxed char should work");

            char unboxed = (char)boxed;
            TestRunner.Run("Boxing: char to object and back", unboxed == 'c');
        }

        private static void TestBoxingInt32()
        {
            object boxed = 42;
            Assert.Equal("42", boxed.ToString(), "Int32.ToString on boxed int");
            Assert.Equal(42, boxed.GetHashCode(), "Int32.GetHashCode on boxed int");
            Assert.True(boxed.Equals(42), "Int32.Equals on boxed int (same value)");
            Assert.True(!boxed.Equals(5), "Int32.Equals on boxed int (different value)");

            object boxed2 = 42;
            Assert.True(Object.Equals(boxed, boxed2), "Object.Equals with two boxed ints");

            int unboxed = (int)boxed;
            TestRunner.Run("Boxing: int to object and back", unboxed == 42);
        }

        private static void TestBoxingByte()
        {
            byte value = 255;
            object boxed = value;
            byte unboxed = (byte)boxed;
            TestRunner.Run("Boxing: byte to object and back", unboxed == 255);
        }

        private static void TestBoxingLong()
        {
            long value = 9876543210L;
            object boxed = value;
            long unboxed = (long)boxed;
            TestRunner.Run("Boxing: long to object and back", unboxed == 9876543210L);
        }

        private static void TestBoxingNullable()
        {
            // Test null case
            int? nullableNull = null;
            object boxedNull = nullableNull;
            TestRunner.Run("Boxing: Nullable<int> null boxes to null", boxedNull == null);

            // Test value case
            int? nullableValue = 777;
            object boxedValue = nullableValue;
            TestRunner.Run("Boxing: Nullable<int> with value boxes correctly",
                boxedValue != null && (int)boxedValue == 777);
        }

        private static void TestBoxingInterface()
        {
            int value = 100;
            IComparable comparable = value;
            TestRunner.Run("Boxing: int to interface (IComparable)", comparable != null);
        }

        private static void TestBoxingCustomStruct()
        {
            TestPoint point = new TestPoint { X = 10, Y = 20 };
            object boxed = point;
            TestPoint unboxed = (TestPoint)boxed;
            TestRunner.Run("Boxing: custom struct box/unbox", unboxed.X == 10 && unboxed.Y == 20);
        }

        private static void TestArrayCopyWithBoxing()
        {
            int[] sourceIntArray = new int[] { 10, 20, 30 };
            object[] destObjectArray = new object[3];
            Array.Copy(sourceIntArray, destObjectArray, 3);

            bool passed = (int)destObjectArray[0] == 10 &&
                         (int)destObjectArray[1] == 20 &&
                         (int)destObjectArray[2] == 30;
            TestRunner.Run("Boxing: Array.Copy with automatic boxing", passed);
        }

        // ==================== Memory Allocation Tests ====================

        private static void TestCharArrayAllocation()
        {
            char[] testChars = new char[] { 'R', 'h', 'p' };
            TestRunner.Run("Memory: char array allocation", testChars.Length == 3 && testChars[0] == 'R');
        }

        private static void TestStringAllocation()
        {
            char[] chars = new char[] { 'R', 'h', 'p' };
            string str = new string(chars);
            TestRunner.Run("Memory: string allocation from char array", str == "Rhp");
        }

        private static void TestIntArrayAllocation()
        {
            int[] array = new int[100];
            for (int i = 0; i < 10; i++)
            {
                array[i] = i * 10;
            }
            TestRunner.Run("Memory: int array allocation and access",
                array[0] == 0 && array[1] == 10 && array[2] == 20);
        }

        private static void TestStringConcatenation()
        {
            string str1 = "Hello";
            string str2 = "World";
            string str3 = str1 + " " + str2;
            TestRunner.Run("Memory: string concatenation", str3 == "Hello World");
        }

        private static void TestStringBuilder()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Hello");
            sb.Append(" ");
            sb.Append("StringBuilder");
            string result = sb.ToString();
            TestRunner.Run("Memory: StringBuilder operations", result == "Hello StringBuilder");
        }

        // ==================== Generic Collection Tests ====================

        private static void TestListInt()
        {
            List<int> list = new List<int>();
            list.Add(100);
            list.Add(200);
            list.Add(300);
            TestRunner.Run("Collections: List<int> Add and indexer",
                list.Count == 3 && list[0] == 100 && list[1] == 200 && list[2] == 300);
        }

        private static void TestListString()
        {
            List<string> list = new List<string>();
            list.Add("First");
            list.Add("Second");
            list.Add("Third");
            list.Add("Fourth");
            list.Add("Fifth");

            TestRunner.Run("Collections: List<string> with resize",
                list.Count == 5 && list[0] == "First" && list[4] == "Fifth");
        }

        private static void TestListByte()
        {
            List<byte> list = new List<byte>();
            list.Add(0xFF);
            list.Add(0x00);
            list.Add(0xAB);
            list.Add(0x12);

            TestRunner.Run("Collections: List<byte> operations",
                list.Count == 4 && list[0] == 0xFF && list[2] == 0xAB);
        }

        private static void TestListLong()
        {
            List<long> list = new List<long>();
            list.Add(0x123456789ABCDEF0);
            list.Add(-9999999999999);
            list.Add(42);

            TestRunner.Run("Collections: List<long> with 64-bit values",
                list.Count == 3 && list[0] == 0x123456789ABCDEF0 && list[2] == 42);
        }

        private static void TestListStruct()
        {
            List<TestPoint> list = new List<TestPoint>();
            list.Add(new TestPoint { X = 1, Y = 2 });
            list.Add(new TestPoint { X = 3, Y = 4 });
            list.Add(new TestPoint { X = 5, Y = 6 });

            TestRunner.Run("Collections: List<struct> operations",
                list.Count == 3 && list[0].X == 1 && list[2].Y == 6);
        }

        private static void TestListContains()
        {
            List<int> list = new List<int>();
            list.Add(10);
            list.Add(20);
            list.Add(30);

            bool found20 = list.Contains(20);
            bool found99 = list.Contains(99);

            TestRunner.Run("Collections: List.Contains method", found20 && !found99);
        }

        private static void TestListIndexOf()
        {
            List<int> list = new List<int>();
            list.Add(10);
            list.Add(20);
            list.Add(30);

            int index20 = list.IndexOf(20);
            int index99 = list.IndexOf(99);

            TestRunner.Run("Collections: List.IndexOf method", index20 == 1 && index99 == -1);
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

            TestRunner.Run("Collections: List.RemoveAt method",
                list.Count == 4 && list[2] == 40);
        }
    }

    // Test struct for boxing and collection tests
    internal struct TestPoint
    {
        public int X;
        public int Y;
    }
}
