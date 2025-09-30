using System;

namespace Cosmos.Tests.Kernel.Framework;

/// <summary>
/// Provides assertion methods for kernel tests.
/// </summary>
public static class Assert
{
    /// <summary>
    /// Asserts that a condition is true.
    /// </summary>
    public static void IsTrue(bool condition, string? message = null)
    {
        if (!condition)
        {
            Fail(message ?? "Expected: true, Actual: false");
        }
    }

    /// <summary>
    /// Asserts that a condition is false.
    /// </summary>
    public static void IsFalse(bool condition, string? message = null)
    {
        if (condition)
        {
            Fail(message ?? "Expected: false, Actual: true");
        }
    }

    /// <summary>
    /// Asserts that a value is not null.
    /// </summary>
    public static void IsNotNull(object? value, string? message = null)
    {
        if (value == null)
        {
            Fail(message ?? "Expected: not null, Actual: null");
        }
    }

    /// <summary>
    /// Asserts that a value is null.
    /// </summary>
    public static void IsNull(object? value, string? message = null)
    {
        if (value != null)
        {
            Fail(message ?? "Expected: null, Actual: not null");
        }
    }

    /// <summary>
    /// Asserts that two values are equal.
    /// </summary>
    public static void AreEqual(int expected, int actual, string? message = null)
    {
        if (expected != actual)
        {
            Fail(message ?? $"Expected: {expected}, Actual: {actual}");
        }
    }

    /// <summary>
    /// Asserts that two values are equal.
    /// </summary>
    public static void AreEqual(uint expected, uint actual, string? message = null)
    {
        if (expected != actual)
        {
            Fail(message ?? $"Expected: {expected}, Actual: {actual}");
        }
    }

    /// <summary>
    /// Asserts that two values are equal.
    /// </summary>
    public static void AreEqual(long expected, long actual, string? message = null)
    {
        if (expected != actual)
        {
            Fail(message ?? $"Expected: {expected}, Actual: {actual}");
        }
    }

    /// <summary>
    /// Asserts that two strings are equal.
    /// </summary>
    public static void AreEqual(string? expected, string? actual, string? message = null)
    {
        if (expected != actual)
        {
            Fail(message ?? $"Expected: {expected}, Actual: {actual}");
        }
    }

    /// <summary>
    /// Fails the test with the specified message.
    /// </summary>
    public static void Fail(string message)
    {
        throw new AssertionException(message);
    }
}

/// <summary>
/// Exception thrown when an assertion fails.
/// </summary>
public class AssertionException : Exception
{
    public AssertionException(string message) : base(message)
    {
    }
}
