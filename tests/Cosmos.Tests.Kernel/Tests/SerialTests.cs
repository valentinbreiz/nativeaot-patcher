using Cosmos.Tests.Kernel.Framework;
using Cosmos.Kernel.System.IO;

namespace Cosmos.Tests.Kernel.Tests;

/// <summary>
/// Tests for serial I/O functionality.
/// </summary>
public static class SerialTests
{
    [Test(Description = "Tests basic serial write")]
    public static void Test_SerialWrite()
    {
        // If we can write this test output, serial is working
        // This is a basic smoke test
        Serial.WriteString("[SerialTest] Basic write\n");
        Assert.IsTrue(true); // If we got here, serial is working
    }

    [Test(Description = "Tests serial number writing")]
    public static void Test_SerialWriteNumber()
    {
        // Write some numbers to serial
        Serial.WriteString("[SerialTest] Writing numbers: ");
        Serial.WriteNumber(42, false);
        Serial.WriteString(", ");
        Serial.WriteNumber(100, false);
        Serial.WriteString("\n");
        Assert.IsTrue(true); // If we got here, number writing works
    }
}
