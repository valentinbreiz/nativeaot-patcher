using Cosmos.Tests.Kernel.Framework;

namespace Cosmos.Tests.Kernel.Tests;

/// <summary>
/// A test that intentionally fails to demonstrate failure detection.
/// This test should be commented out in Kernel.cs for normal runs.
/// </summary>
public static class FailingTest
{
    [Test(Description = "Intentionally fails to test failure detection")]
    public static void Test_IntentionalFailure()
    {
        // This should pass
        Assert.AreEqual(1, 1, "1 equals 1");

        // This should FAIL
        Assert.AreEqual(1, 2, "1 does not equal 2 - this should fail");

        // This should pass
        Assert.IsTrue(true, "true is true");

        // This should FAIL
        Assert.IsTrue(false, "false is not true - this should fail");
    }
}
