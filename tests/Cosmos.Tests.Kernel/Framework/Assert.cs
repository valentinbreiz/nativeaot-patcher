using Cosmos.Kernel.System.IO;

namespace Cosmos.Tests.Kernel.Framework;

/// <summary>
/// Provides assertion methods for kernel tests using UART protocol.
/// Each assertion outputs [ASSERT_PASS] or [ASSERT_FAIL: message] to enable external validation.
/// </summary>
public static class Assert
{
    private static int _assertCount = 0;
    private static int _failCount = 0;

    /// <summary>
    /// Asserts that a condition is true.
    /// </summary>
    public static void IsTrue(bool condition, string? message = null)
    {
        _assertCount++;
        if (!condition)
        {
            Fail(message ?? "Expected: true, Actual: false");
        }
        else
        {
            Pass();
        }
    }

    /// <summary>
    /// Asserts that a condition is false.
    /// </summary>
    public static void IsFalse(bool condition, string? message = null)
    {
        _assertCount++;
        if (condition)
        {
            Fail(message ?? "Expected: false, Actual: true");
        }
        else
        {
            Pass();
        }
    }

    /// <summary>
    /// Asserts that a value is not null.
    /// </summary>
    public static void IsNotNull(object? value, string? message = null)
    {
        _assertCount++;
        if (value == null)
        {
            Fail(message ?? "Expected: not null, Actual: null");
        }
        else
        {
            Pass();
        }
    }

    /// <summary>
    /// Asserts that a value is null.
    /// </summary>
    public static void IsNull(object? value, string? message = null)
    {
        _assertCount++;
        if (value != null)
        {
            Fail(message ?? "Expected: null, Actual: not null");
        }
        else
        {
            Pass();
        }
    }

    /// <summary>
    /// Asserts that two values are equal.
    /// </summary>
    public static void AreEqual(int expected, int actual, string? message = null)
    {
        _assertCount++;
        if (expected != actual)
        {
            Fail(message ?? $"Expected: {expected}, Actual: {actual}");
        }
        else
        {
            Pass();
        }
    }

    /// <summary>
    /// Asserts that two values are equal.
    /// </summary>
    public static void AreEqual(uint expected, uint actual, string? message = null)
    {
        _assertCount++;
        if (expected != actual)
        {
            Fail(message ?? $"Expected: {expected}, Actual: {actual}");
        }
        else
        {
            Pass();
        }
    }

    /// <summary>
    /// Asserts that two values are equal.
    /// </summary>
    public static void AreEqual(long expected, long actual, string? message = null)
    {
        _assertCount++;
        if (expected != actual)
        {
            Fail(message ?? $"Expected: {expected}, Actual: {actual}");
        }
        else
        {
            Pass();
        }
    }

    /// <summary>
    /// Asserts that two values are equal.
    /// </summary>
    public static void AreEqual(byte expected, byte actual, string? message = null)
    {
        _assertCount++;
        if (expected != actual)
        {
            Fail(message ?? $"Expected: {expected}, Actual: {actual}");
        }
        else
        {
            Pass();
        }
    }

    /// <summary>
    /// Asserts that two values are equal.
    /// </summary>
    public static void AreEqual(char expected, char actual, string? message = null)
    {
        _assertCount++;
        if (expected != actual)
        {
            Fail(message ?? $"Expected: {expected}, Actual: {actual}");
        }
        else
        {
            Pass();
        }
    }

    /// <summary>
    /// Asserts that two strings are equal.
    /// </summary>
    public static void AreEqual(string? expected, string? actual, string? message = null)
    {
        _assertCount++;
        if (expected != actual)
        {
            Fail(message ?? $"Expected: {expected}, Actual: {actual}");
        }
        else
        {
            Pass();
        }
    }

    /// <summary>
    /// Gets the total number of assertions executed.
    /// </summary>
    public static int AssertCount => _assertCount;

    /// <summary>
    /// Gets the number of failed assertions.
    /// </summary>
    public static int FailCount => _failCount;

    /// <summary>
    /// Resets assertion counters.
    /// </summary>
    public static void Reset()
    {
        _assertCount = 0;
        _failCount = 0;
    }

    private static void Pass()
    {
        // Silent pass - only failures are logged
    }

    private static void Fail(string message)
    {
        _failCount++;
        Serial.WriteString("[ASSERT_FAIL: ");
        Serial.WriteString(message);
        Serial.WriteString("]\n");
    }
}
