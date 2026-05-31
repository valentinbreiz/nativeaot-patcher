using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.TestingFramework.Attributes;
using Cosmos.TestingFramework.Framework;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestingFramework.Framework.TestRunner;

namespace Cosmos.Kernel.Tests.HelloWorld;

[TestClass]
public class Tests
{    
    [TestMethod]
    public static void Test_BasicArithmetic()
    {
        int result = 2 + 2;
        Assert.Equal(4, result);
    }

    [TestMethod]
    public static void Test_BooleanLogic()
    {
        bool isTrue = true;
        Assert.True(isTrue);
        Assert.False(!isTrue);
    }

    [TestMethod]
    public static void Test_IntegerComparison()
    {
        int a = 10;
        int b = 10;
        int c = 20;

        Assert.Equal(a, b);
        Assert.True(a < c);
        Assert.False(a > c);
    }
}
