using Cosmos.Tests.Kernel.Framework;

namespace Cosmos.Tests.Kernel.Tests;

/// <summary>
/// Tests for memory allocation and management.
/// </summary>
public static class MemoryTests
{
    [Test(Description = "Tests allocation of small char array")]
    public static void Test_SmallAllocation()
    {
        char[] testChars = new char[] { 'T', 'e', 's', 't' };
        Assert.IsNotNull(testChars);
        Assert.AreEqual(4, testChars.Length);
        Assert.AreEqual('T', testChars[0]);
        Assert.AreEqual('t', testChars[3]);
    }

    [Test(Description = "Tests allocation of larger int array")]
    public static void Test_LargeAllocation()
    {
        int[] intArray = new int[100];
        Assert.IsNotNull(intArray);
        Assert.AreEqual(100, intArray.Length);
    }

    [Test(Description = "Tests writing to and reading from allocated array")]
    public static void Test_ArrayReadWrite()
    {
        int[] array = new int[10];
        for (int i = 0; i < 10; i++)
        {
            array[i] = i * 10;
        }

        Assert.AreEqual(0, array[0]);
        Assert.AreEqual(50, array[5]);
        Assert.AreEqual(90, array[9]);
    }

    [Test(Description = "Tests multiple sequential allocations")]
    public static void Test_MultipleAllocations()
    {
        int[] array1 = new int[10];
        int[] array2 = new int[20];
        int[] array3 = new int[30];

        Assert.IsNotNull(array1);
        Assert.IsNotNull(array2);
        Assert.IsNotNull(array3);
        Assert.AreEqual(10, array1.Length);
        Assert.AreEqual(20, array2.Length);
        Assert.AreEqual(30, array3.Length);
    }

    [Test(Description = "Tests allocation of byte array")]
    public static void Test_ByteArrayAllocation()
    {
        byte[] bytes = new byte[256];
        Assert.IsNotNull(bytes);
        Assert.AreEqual(256, bytes.Length);

        bytes[0] = 0xFF;
        bytes[255] = 0xAA;

        Assert.AreEqual(0xFF, bytes[0]);
        Assert.AreEqual(0xAA, bytes[255]);
    }
}
