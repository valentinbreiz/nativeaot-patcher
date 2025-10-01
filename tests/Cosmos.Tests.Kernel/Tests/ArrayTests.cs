using Cosmos.Tests.Kernel.Framework;

namespace Cosmos.Tests.Kernel.Tests;

/// <summary>
/// Tests for array operations and edge cases.
/// </summary>
public static class ArrayTests
{
    [Test(Description = "Tests empty array allocation")]
    public static void Test_EmptyArray()
    {
        int[] empty = new int[0];
        Assert.IsNotNull(empty);
        Assert.AreEqual(0, empty.Length);
    }

    [Test(Description = "Tests single element array")]
    public static void Test_SingleElementArray()
    {
        int[] single = new int[1];
        single[0] = 42;

        Assert.AreEqual(1, single.Length);
        Assert.AreEqual(42, single[0]);
    }

    [Test(Description = "Tests array copy operations")]
    public static void Test_ArrayCopy()
    {
        int[] source = new int[] { 1, 2, 3, 4, 5 };
        int[] dest = new int[5];

        for (int i = 0; i < source.Length; i++)
        {
            dest[i] = source[i];
        }

        for (int i = 0; i < source.Length; i++)
        {
            Assert.AreEqual(source[i], dest[i]);
        }
    }

    // NOTE: Multi-dimensional arrays (int[,]) and jagged arrays (int[][]) are not supported
    // due to current heap implementation limitations. These cause kernel crashes.

    [Test(Description = "Tests array of different types")]
    public static void Test_TypedArrays()
    {
        byte[] bytes = new byte[] { 1, 2, 3 };
        short[] shorts = new short[] { 100, 200 };
        long[] longs = new long[] { 1000L };

        Assert.AreEqual(3, bytes.Length);
        Assert.AreEqual(2, shorts.Length);
        Assert.AreEqual(1, longs.Length);

        Assert.AreEqual((byte)2, bytes[1]);
        Assert.AreEqual(200, shorts[1]);
        Assert.AreEqual(1000L, longs[0]);
    }

    [Test(Description = "Tests array length boundary")]
    public static void Test_ArrayBoundary()
    {
        int[] arr = new int[10];

        // Test first and last valid indices
        arr[0] = 111;
        arr[9] = 999;

        Assert.AreEqual(111, arr[0]);
        Assert.AreEqual(999, arr[9]);
    }

    [Test(Description = "Tests array fill pattern")]
    public static void Test_ArrayFillPattern()
    {
        int[] arr = new int[20];

        // Fill with pattern
        for (int i = 0; i < arr.Length; i++)
        {
            arr[i] = i * 2;
        }

        // Verify pattern
        for (int i = 0; i < arr.Length; i++)
        {
            Assert.AreEqual(i * 2, arr[i]);
        }
    }
}
