using Cosmos.Tests.Kernel.Framework;

namespace Cosmos.Tests.Kernel.Tests;

/// <summary>
/// Tests for numeric operations and edge cases.
/// </summary>
public static class NumericTests
{
    [Test(Description = "Tests basic integer arithmetic")]
    public static void Test_IntegerArithmetic()
    {
        int a = 10;
        int b = 5;

        Assert.AreEqual(15, a + b);
        Assert.AreEqual(5, a - b);
        Assert.AreEqual(50, a * b);
        Assert.AreEqual(2, a / b);
        Assert.AreEqual(0, a % b);
    }

    [Test(Description = "Tests integer overflow behavior")]
    public static void Test_IntegerOverflow()
    {
        int max = int.MaxValue;
        int min = int.MinValue;

        Assert.AreEqual(2147483647, max);
        Assert.AreEqual(-2147483648, min);

        // Verify max value
        Assert.AreEqual(max, 2147483647);
    }

    [Test(Description = "Tests unsigned integer operations")]
    public static void Test_UnsignedIntegers()
    {
        uint a = 100;
        uint b = 50;

        Assert.AreEqual(150u, a + b);
        Assert.AreEqual(50u, a - b);
        Assert.AreEqual(5000u, a * b);
        Assert.AreEqual(2u, a / b);
    }

    [Test(Description = "Tests long integer operations")]
    public static void Test_LongIntegers()
    {
        long a = 1000000000L;
        long b = 2000000000L;

        Assert.AreEqual(3000000000L, a + b);
        Assert.AreEqual(-1000000000L, a - b);
    }

    [Test(Description = "Tests byte operations")]
    public static void Test_ByteOperations()
    {
        byte a = 255;
        byte b = 128;

        Assert.AreEqual((byte)255, a);
        Assert.AreEqual((byte)128, b);

        // Bitwise operations
        byte result = (byte)(a & b);
        Assert.AreEqual((byte)128, result);
    }

    [Test(Description = "Tests bitwise operations")]
    public static void Test_BitwiseOperations()
    {
        int a = 0b1100;  // 12
        int b = 0b1010;  // 10

        Assert.AreEqual(0b1000, a & b);  // AND = 8
        Assert.AreEqual(0b1110, a | b);  // OR = 14
        Assert.AreEqual(0b0110, a ^ b);  // XOR = 6
    }

    [Test(Description = "Tests shift operations")]
    public static void Test_ShiftOperations()
    {
        int value = 8;

        Assert.AreEqual(16, value << 1);  // Left shift
        Assert.AreEqual(4, value >> 1);   // Right shift
        Assert.AreEqual(32, value << 2);  // Left shift by 2
    }

    [Test(Description = "Tests negative number operations")]
    public static void Test_NegativeNumbers()
    {
        int a = -10;
        int b = 5;

        Assert.AreEqual(-5, a + b);
        Assert.AreEqual(-15, a - b);
        Assert.AreEqual(-50, a * b);
        Assert.AreEqual(-2, a / b);
    }

    [Test(Description = "Tests zero operations")]
    public static void Test_ZeroOperations()
    {
        int zero = 0;
        int value = 42;

        Assert.AreEqual(42, value + zero);
        Assert.AreEqual(42, value - zero);
        Assert.AreEqual(0, value * zero);
        Assert.AreEqual(0, zero * value);
    }

    [Test(Description = "Tests comparison operations")]
    public static void Test_ComparisonOperations()
    {
        int a = 10;
        int b = 20;

        Assert.IsTrue(a < b);
        Assert.IsTrue(b > a);
        Assert.IsTrue(a <= b);
        Assert.IsTrue(b >= a);
        Assert.IsFalse(a == b);
        Assert.IsTrue(a != b);
    }
}
