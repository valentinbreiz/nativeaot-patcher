using Cosmos.Tests.Kernel.Framework;

namespace Cosmos.Tests.Kernel.Tests;

/// <summary>
/// Tests to validate the test framework itself.
/// These tests intentionally contain failures to verify the assertion framework works.
/// </summary>
public static class ValidationTests
{
    [Test(Description = "Validates that assertion framework detects failures")]
    public static void Test_FrameworkValidation()
    {
        // These should PASS
        Assert.IsTrue(true, "IsTrue with true should pass");
        Assert.IsFalse(false, "IsFalse with false should pass");
        Assert.AreEqual(1, 1, "Equal integers should pass");
        Assert.AreEqual(5u, 5u, "Equal uints should pass");

        // These should FAIL (intentionally)
        Assert.IsTrue(false, "IsTrue with false should fail");
        Assert.IsFalse(true, "IsFalse with true should fail");
        Assert.AreEqual(1, 2, "Different integers should fail");
        Assert.AreEqual(100, 200, "Different values should fail");

        // More passing assertions
        Assert.IsNotNull("test", "Non-null string should pass");

        // This should work - we expect 9 assertions, 4 failures
    }
}
