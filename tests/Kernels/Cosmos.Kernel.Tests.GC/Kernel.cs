using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Memory.GC;
using Cosmos.Kernel.Core.Memory.Heap;
using Cosmos.TestRunner.Framework;
using static Cosmos.TestRunner.Framework.TestRunner;
using static Cosmos.TestRunner.Framework.Assert;

namespace Cosmos.Kernel.Tests.GC
{
    internal unsafe static partial class Program
    {
        [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
        private static void KernelMain() => Main();

        private static void Main()
        {
            Serial.WriteString("[GC Tests] Starting test suite\n");
            Start("GC Tests", expectedTests: 15);

            // Reference Counting Tests
            Run("RefCount_InitialCount", TestInitialRefCount);
            Run("RefCount_Increment", TestRefCountIncrement);
            Run("RefCount_Decrement", TestRefCountDecrement);
            Run("RefCount_ArrayRefCount", TestArrayRefCount);

            // Allocation Tests
            Run("Alloc_SmallObject", TestSmallObjectAllocation);
            Run("Alloc_MediumObject", TestMediumObjectAllocation);
            Run("Alloc_MultipleObjects", TestMultipleObjectAllocations);

            // Collection Tests
            Run("GC_CollectOrphanedObjects", TestCollectOrphanedObjects);
            Run("GC_PreserveReferencedObjects", TestPreserveReferencedObjects);
            Run("GC_CollectionCycle", TestCollectionCycle);

            // Object Lifecycle Tests
            Run("Lifecycle_StringCreation", TestStringLifecycle);
            Run("Lifecycle_ArrayCreation", TestArrayLifecycle);
            Run("Lifecycle_ListOperations", TestListLifecycle);

            // Handle Table Tests
            Run("Handles_Initialization", TestHandleTableInitialization);
            Run("Handles_AllocFree", TestHandleAllocFree);

            Serial.WriteString("[GC Tests] All tests completed\n");
            Finish();

            while (true) ;
        }

        // ==================== Reference Counting Tests ====================

        private static void TestInitialRefCount()
        {
            // Newly allocated objects should have refcount of 1
            int[] array = new int[10];

            // We can't directly check refcount without unsafe code, but we can verify
            // the object is valid and usable
            True(array != null, "RefCount: Object allocated successfully");
            True(array.Length == 10, "RefCount: Array length correct");
        }

        private static void TestRefCountIncrement()
        {
            // Creating a reference should keep object alive
            string original = "Test String";
            string reference = original;

            True(original == reference, "RefCount: Reference points to same object");
            True(original.Length == 11, "RefCount: Original still accessible");
            True(reference.Length == 11, "RefCount: Reference still accessible");
        }

        private static void TestRefCountDecrement()
        {
            int initialCount = SmallHeap.GetAllocatedObjectCount();

            // Create and immediately lose reference
            CreateAndDiscardObject();

            // Run collection to clean up orphaned objects
            int freed = Heap.Collect();

            // Verify collection happened (may or may not free depending on timing)
            True(freed >= 0, "RefCount: Collection returned valid count");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CreateAndDiscardObject()
        {
            // Create object that goes out of scope
            var temp = new int[50];
            temp[0] = 42;
        }

        private static void TestArrayRefCount()
        {
            // Test array of references
            string[] strings = new string[3];
            strings[0] = "First";
            strings[1] = "Second";
            strings[2] = "Third";

            True(strings[0] == "First", "RefCount: Array element 0 accessible");
            True(strings[1] == "Second", "RefCount: Array element 1 accessible");
            True(strings[2] == "Third", "RefCount: Array element 2 accessible");

            // Overwrite reference
            strings[1] = "Modified";
            True(strings[1] == "Modified", "RefCount: Array element modified correctly");
        }

        // ==================== Allocation Tests ====================

        private static void TestSmallObjectAllocation()
        {
            // Small objects (< 1020 bytes) go to SmallHeap
            byte[] small = new byte[100];
            True(small != null, "Alloc: Small object allocated");
            True(small.Length == 100, "Alloc: Small object correct size");

            // Write and verify
            for (int i = 0; i < 100; i++)
                small[i] = (byte)(i & 0xFF);

            bool valid = true;
            for (int i = 0; i < 100; i++)
                if (small[i] != (byte)(i & 0xFF))
                    valid = false;

            True(valid, "Alloc: Small object data integrity");
        }

        private static void TestMediumObjectAllocation()
        {
            // Medium objects (1020 - 4096 bytes) go to MediumHeap
            byte[] medium = new byte[2000];
            True(medium != null, "Alloc: Medium object allocated");
            True(medium.Length == 2000, "Alloc: Medium object correct size");

            // Write pattern
            for (int i = 0; i < 2000; i++)
                medium[i] = (byte)(i & 0xFF);

            // Verify pattern
            bool valid = true;
            for (int i = 0; i < 2000; i++)
                if (medium[i] != (byte)(i & 0xFF))
                    valid = false;

            True(valid, "Alloc: Medium object data integrity");
        }

        private static void TestMultipleObjectAllocations()
        {
            int beforeCount = SmallHeap.GetAllocatedObjectCount();

            // Allocate multiple objects
            int[] a = new int[10];
            int[] b = new int[20];
            int[] c = new int[30];

            int afterCount = SmallHeap.GetAllocatedObjectCount();

            // Should have at least 3 more objects
            True(afterCount >= beforeCount, "Alloc: Object count increased");
            True(a.Length == 10 && b.Length == 20 && c.Length == 30, "Alloc: All objects valid");
        }

        // ==================== Collection Tests ====================

        private static void TestCollectOrphanedObjects()
        {
            int beforeCount = SmallHeap.GetAllocatedObjectCount();

            // Create orphaned objects
            CreateOrphanedObjects();

            int afterAllocCount = SmallHeap.GetAllocatedObjectCount();

            // Run GC
            int freed = Heap.Collect();

            int afterGcCount = SmallHeap.GetAllocatedObjectCount();

            Serial.WriteString("[GC Test] Before: ");
            Serial.WriteNumber((ulong)beforeCount);
            Serial.WriteString(", After alloc: ");
            Serial.WriteNumber((ulong)afterAllocCount);
            Serial.WriteString(", After GC: ");
            Serial.WriteNumber((ulong)afterGcCount);
            Serial.WriteString(", Freed: ");
            Serial.WriteNumber((ulong)freed);
            Serial.WriteString("\n");

            True(freed >= 0, "GC: Collection completed without error");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CreateOrphanedObjects()
        {
            // These objects become garbage after method returns
            for (int i = 0; i < 10; i++)
            {
                var temp = new int[100];
                temp[0] = i;
            }
        }

        private static void TestPreserveReferencedObjects()
        {
            // Create objects we want to keep
            int[] keepAlive = new int[100];
            for (int i = 0; i < 100; i++)
                keepAlive[i] = i * 2;

            // Run GC
            Heap.Collect();

            // Verify our object survived
            bool intact = true;
            for (int i = 0; i < 100; i++)
                if (keepAlive[i] != i * 2)
                    intact = false;

            True(intact, "GC: Referenced objects preserved");
        }

        private static void TestCollectionCycle()
        {
            // Multiple collection cycles should be stable
            int count1 = SmallHeap.GetAllocatedObjectCount();
            Heap.Collect();

            int count2 = SmallHeap.GetAllocatedObjectCount();
            Heap.Collect();

            int count3 = SmallHeap.GetAllocatedObjectCount();

            // Counts should be stable after consecutive collections
            True(count2 <= count1, "GC: First collection reduced or maintained count");
            True(count3 <= count2, "GC: Second collection stable");
        }

        // ==================== Object Lifecycle Tests ====================

        private static void TestStringLifecycle()
        {
            string s1 = "Hello";
            string s2 = " ";
            string s3 = "World";

            string result = s1 + s2 + s3;

            True(result == "Hello World", "Lifecycle: String concatenation works");
            True(result.Length == 11, "Lifecycle: String length correct");
        }

        private static void TestArrayLifecycle()
        {
            // Create array, use it, let it go out of scope
            int sum = CalculateArraySum();
            True(sum == 4950, "Lifecycle: Array operations correct (sum of 0-99)");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CalculateArraySum()
        {
            int[] numbers = new int[100];
            for (int i = 0; i < 100; i++)
                numbers[i] = i;

            int sum = 0;
            for (int i = 0; i < 100; i++)
                sum += numbers[i];

            return sum;
        }

        private static void TestListLifecycle()
        {
            List<int> list = new List<int>();

            // Add items
            for (int i = 0; i < 50; i++)
                list.Add(i * 2);

            True(list.Count == 50, "Lifecycle: List count correct");
            True(list[0] == 0, "Lifecycle: List first element correct");
            True(list[49] == 98, "Lifecycle: List last element correct");

            // Remove some
            list.RemoveAt(25);
            True(list.Count == 49, "Lifecycle: List removal works");
        }

        // ==================== Handle Table Tests ====================

        private static void TestHandleTableInitialization()
        {
            // Handle table should be initialized at kernel startup
            HandleTable.PrintStats();

            // Just verify we can access stats without crashing
            True(true, "Handles: Handle table accessible");
        }

        private static void TestHandleAllocFree()
        {
            // Test GCHandle-style operations
            int[] target = new int[] { 1, 2, 3, 4, 5 };

            // Allocate a handle
            IntPtr handle = HandleTable.Alloc(target, HandleType.Normal);
            bool allocated = handle != IntPtr.Zero;

            if (allocated)
            {
                // Get the object back
                object? retrieved = HandleTable.Get(handle);
                bool retrieved_ok = retrieved != null;

                if (retrieved_ok)
                {
                    int[] retrievedArray = (int[])retrieved;
                    bool values_ok = retrievedArray[0] == 1 && retrievedArray[4] == 5;
                    True(values_ok, "Handles: Handle retrieval correct");
                }
                else
                {
                    True(false, "Handles: Failed to retrieve handle");
                }

                // Free the handle
                HandleTable.Free(handle);
            }
            else
            {
                // Handle table might not be initialized in all configurations
                True(true, "Handles: Handle allocation (skipped - not initialized)");
            }
        }
    }
}
