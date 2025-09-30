using Cosmos.Tests.Kernel.Framework;

namespace Cosmos.Tests.Kernel.Tests;

/// <summary>
/// Tests for string operations.
/// </summary>
public static class StringTests
{
    [Test(Description = "Tests string creation from char array")]
    public static void Test_StringFromChars()
    {
        char[] chars = new char[] { 'T', 'e', 's', 't' };
        string str = new string(chars);

        Assert.IsNotNull(str);
        Assert.AreEqual(4, str.Length);
    }

    [Test(Description = "Tests string concatenation")]
    public static void Test_StringConcat()
    {
        string str1 = "Hello";
        string str2 = "World";
        string result = str1 + " " + str2;

        Assert.IsNotNull(result);
        Assert.AreEqual(11, result.Length); // "Hello World"
    }

    [Test(Description = "Tests string length property")]
    public static void Test_StringLength()
    {
        string empty = "";
        string single = "A";
        string longer = "CosmosOS";

        Assert.AreEqual(0, empty.Length);
        Assert.AreEqual(1, single.Length);
        Assert.AreEqual(8, longer.Length);
    }

    [Test(Description = "Tests string allocation and assignment")]
    public static void Test_StringAllocation()
    {
        string test = "Kernel Test";
        Assert.IsNotNull(test);
        Assert.AreEqual(11, test.Length);
    }
}
