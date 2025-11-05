using System;

namespace Cosmos.TestRunner.Framework
{
    /// <summary>
    /// Assertion methods for kernel tests
    /// </summary>
    public static class Assert
    {
        /// <summary>
        /// Assert that two values are equal
        /// </summary>
        public static void Equal<T>(T expected, T actual) where T : IEquatable<T>
        {
            if (expected == null && actual == null)
                return;

            if (expected == null || actual == null || !expected.Equals(actual))
            {
                throw new AssertionException($"Expected {expected}, got {actual}");
            }
        }

        /// <summary>
        /// Assert that two integers are equal
        /// </summary>
        public static void Equal(int expected, int actual)
        {
            if (expected != actual)
            {
                throw new AssertionException($"Expected {expected}, got {actual}");
            }
        }

        /// <summary>
        /// Assert that two unsigned integers are equal
        /// </summary>
        public static void Equal(uint expected, uint actual)
        {
            if (expected != actual)
            {
                throw new AssertionException($"Expected {expected}, got {actual}");
            }
        }

        /// <summary>
        /// Assert that two longs are equal
        /// </summary>
        public static void Equal(long expected, long actual)
        {
            if (expected != actual)
            {
                throw new AssertionException($"Expected {expected}, got {actual}");
            }
        }

        /// <summary>
        /// Assert that two bytes are equal
        /// </summary>
        public static void Equal(byte expected, byte actual)
        {
            if (expected != actual)
            {
                throw new AssertionException($"Expected {expected}, got {actual}");
            }
        }

        /// <summary>
        /// Assert that two booleans are equal
        /// </summary>
        public static void Equal(bool expected, bool actual)
        {
            if (expected != actual)
            {
                throw new AssertionException($"Expected {expected}, got {actual}");
            }
        }

        /// <summary>
        /// Assert that a value is not null
        /// </summary>
        public static void NotNull(object? obj)
        {
            if (obj == null)
            {
                throw new AssertionException("Expected non-null, got null");
            }
        }

        /// <summary>
        /// Assert that a value is null
        /// </summary>
        public static void Null(object? obj)
        {
            if (obj != null)
            {
                throw new AssertionException($"Expected null, got {obj}");
            }
        }

        /// <summary>
        /// Assert that a condition is true
        /// </summary>
        public static void True(bool condition, string? message = null)
        {
            if (!condition)
            {
                var msg = message != null ? $"Expected true: {message}" : "Expected true, got false";
                throw new AssertionException(msg);
            }
        }

        /// <summary>
        /// Assert that a condition is false
        /// </summary>
        public static void False(bool condition, string? message = null)
        {
            if (condition)
            {
                var msg = message != null ? $"Expected false: {message}" : "Expected false, got true";
                throw new AssertionException(msg);
            }
        }

        /// <summary>
        /// Assert that an action throws a specific exception type
        /// </summary>
        public static void Throws<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
                throw new AssertionException($"Expected {typeof(TException).Name}, but no exception was thrown");
            }
            catch (TException)
            {
                // Expected exception caught
                return;
            }
            catch (Exception ex)
            {
                throw new AssertionException($"Expected {typeof(TException).Name}, but got {ex.GetType().Name}");
            }
        }

        /// <summary>
        /// Unconditional failure
        /// </summary>
        public static void Fail(string message)
        {
            throw new AssertionException(message);
        }
    }
}
