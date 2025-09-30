using Cosmos.Tests.Kernel.Framework;

namespace Cosmos.Tests.Kernel.Tests;

/// <summary>
/// Tests for garbage collection and heap management.
/// </summary>
public static class GCTests
{
    [Test(Description = "Tests allocation and deallocation pattern")]
    public static void Test_AllocationPattern()
    {
        // Allocate and release multiple objects to test heap reuse
        for (int i = 0; i < 10; i++)
        {
            int[] temp = new int[50];
            temp[0] = i;
            Assert.AreEqual(i, temp[0]);
        }
    }

    [Test(Description = "Tests large object allocation")]
    public static void Test_LargeObjectAllocation()
    {
        // Allocate a large array (should go to medium/large heap)
        int[] largeArray = new int[1000];
        Assert.IsNotNull(largeArray);
        Assert.AreEqual(1000, largeArray.Length);

        // Write to start, middle, and end
        largeArray[0] = 111;
        largeArray[500] = 222;
        largeArray[999] = 333;

        Assert.AreEqual(111, largeArray[0]);
        Assert.AreEqual(222, largeArray[500]);
        Assert.AreEqual(333, largeArray[999]);
    }

    [Test(Description = "Tests mixed size allocations")]
    public static void Test_MixedSizeAllocations()
    {
        // Small allocation
        byte[] small = new byte[16];
        small[0] = 0xAA;

        // Medium allocation
        int[] medium = new int[100];
        medium[0] = 12345;

        // Another small allocation
        char[] small2 = new char[8];
        small2[0] = 'X';

        // Verify all allocations are intact
        Assert.AreEqual((byte)0xAA, small[0]);
        Assert.AreEqual(12345, medium[0]);
        Assert.AreEqual('X', small2[0]);
    }

    [Test(Description = "Tests heap fragmentation handling")]
    public static void Test_HeapFragmentation()
    {
        // Create several objects of different sizes
        int[] arr1 = new int[10];
        byte[] arr2 = new byte[50];
        int[] arr3 = new int[20];
        byte[] arr4 = new byte[100];

        // Write distinct values
        arr1[5] = 100;
        arr2[25] = 200;
        arr3[15] = 300;
        arr4[75] = 250;

        // All should still be valid
        Assert.AreEqual(100, arr1[5]);
        Assert.AreEqual((byte)200, arr2[25]);
        Assert.AreEqual(300, arr3[15]);
        Assert.AreEqual((byte)250, arr4[75]);
    }

    [Test(Description = "Tests zero-initialized memory")]
    public static void Test_ZeroInitialization()
    {
        int[] arr = new int[50];

        // All elements should be zero-initialized
        for (int i = 0; i < arr.Length; i++)
        {
            Assert.AreEqual(0, (int)arr[i]);
        }

        byte[] bytes = new byte[25];
        for (int i = 0; i < bytes.Length; i++)
        {
            Assert.AreEqual((byte)0, bytes[i]);
        }
    }
}
