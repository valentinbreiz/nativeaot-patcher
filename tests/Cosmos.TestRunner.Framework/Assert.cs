namespace Cosmos.TestRunner.Framework
{
    /// <summary>
    /// Assertion methods for kernel tests.
    /// Uses static failure state instead of exceptions for NativeAOT compatibility.
    /// </summary>
    public static class Assert
    {
        /// <summary>
        /// Whether the current test has failed
        /// </summary>
        public static bool Failed { get; private set; }

        /// <summary>
        /// The failure message if Failed is true
        /// </summary>
        public static string? FailureMessage { get; private set; }

        /// <summary>
        /// Reset the failure state for a new test
        /// </summary>
        public static void Reset()
        {
            Failed = false;
            FailureMessage = null;
        }

        private static void SetFailed(string message)
        {
            if (!Failed)  // Only record first failure
            {
                Failed = true;
                FailureMessage = message;
            }
        }

        /// <summary>
        /// Assert that two values are equal
        /// </summary>
        public static void Equal<T>(T expected, T actual) where T : System.IEquatable<T>
        {
            if (expected == null && actual == null)
                return;

            if (expected == null || actual == null || !expected.Equals(actual))
            {
                SetFailed("Values are not equal");
            }
        }

        /// <summary>
        /// Assert that two integers are equal
        /// </summary>
        public static void Equal(int expected, int actual)
        {
            if (expected != actual)
            {
                SetFailed("Integer values are not equal");
            }
        }

        /// <summary>
        /// Assert that two unsigned integers are equal
        /// </summary>
        public static void Equal(uint expected, uint actual)
        {
            if (expected != actual)
            {
                SetFailed("Unsigned integer values are not equal");
            }
        }

        /// <summary>
        /// Assert that two longs are equal
        /// </summary>
        public static void Equal(long expected, long actual)
        {
            if (expected != actual)
            {
                SetFailed("Long values are not equal");
            }
        }

        /// <summary>
        /// Assert that two bytes are equal
        /// </summary>
        public static void Equal(byte expected, byte actual)
        {
            if (expected != actual)
            {
                SetFailed("Byte values are not equal");
            }
        }

        /// <summary>
        /// Assert that two booleans are equal
        /// </summary>
        public static void Equal(bool expected, bool actual)
        {
            if (expected != actual)
            {
                SetFailed("Boolean values are not equal");
            }
        }

        /// <summary>
        /// Assert that a value is not null
        /// </summary>
        public static void NotNull(object? obj)
        {
            if (obj == null)
            {
                SetFailed("Expected non-null value");
            }
        }

        /// <summary>
        /// Assert that a value is null
        /// </summary>
        public static void Null(object? obj)
        {
            if (obj != null)
            {
                SetFailed("Expected null value");
            }
        }

        /// <summary>
        /// Assert that a condition is true
        /// </summary>
        public static void True(bool condition, string? message = null)
        {
            if (!condition)
            {
                SetFailed(message ?? "Expected true");
            }
        }

        /// <summary>
        /// Assert that a condition is false
        /// </summary>
        public static void False(bool condition, string? message = null)
        {
            if (condition)
            {
                SetFailed(message ?? "Expected false");
            }
        }

        /// <summary>
        /// Unconditional failure
        /// </summary>
        public static void Fail(string message)
        {
            SetFailed(message);
        }
    }
}
