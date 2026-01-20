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
        public static void Equal<T>(T expected, T actual, string? message = null) where T : System.IEquatable<T>
        {
            if (expected == null && actual == null)
                return;

            if (expected == null || actual == null || !expected.Equals(actual))
            {
                SetFailed(message ?? "Values are not equal");
            }
        }

        /// <summary>
        /// Assert that two integers are equal
        /// </summary>
        public static void Equal(int expected, int actual, string? message = null)
        {
            if (expected != actual)
            {
                SetFailed(message ?? "Integer values are not equal");
            }
        }

        /// <summary>
        /// Assert that two unsigned integers are equal
        /// </summary>
        public static void Equal(uint expected, uint actual, string? message = null)
        {
            if (expected != actual)
            {
                SetFailed(message ?? "Unsigned integer values are not equal");
            }
        }

        /// <summary>
        /// Assert that two longs are equal
        /// </summary>
        public static void Equal(long expected, long actual, string? message = null)
        {
            if (expected != actual)
            {
                SetFailed(message ?? "Long values are not equal");
            }
        }

        /// <summary>
        /// Assert that two bytes are equal
        /// </summary>
        public static void Equal(byte expected, byte actual, string? message = null)
        {
            if (expected != actual)
            {
                SetFailed(message ?? "Byte values are not equal");
            }
        }

        /// <summary>
        /// Assert that two booleans are equal
        /// </summary>
        public static void Equal(bool expected, bool actual, string? message = null)
        {
            if (expected != actual)
            {
                SetFailed(message ?? "Boolean values are not equal");
            }
        }

        /// <summary>
        /// Assert that two byte arrays are equal
        /// </summary>
        public static void Equal(byte[] expected, byte[] actual, string? message = null)
        {
            if (expected == null && actual == null) return;
            if (expected == null || actual == null)
            {
                SetFailed(message ?? "Byte arrays are not equal (one is null)");
                return;
            }
            if (expected.Length != actual.Length)
            {
                SetFailed(message ?? $"Byte arrays have different lengths: expected {expected.Length}, actual {actual.Length}");
                return;
            }
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i])
                {
                    SetFailed(message ?? $"Byte arrays differ at index {i}: expected {expected[i]}, actual {actual[i]}");
                    return;
                }
            }
        }

        /// <summary>
        /// Assert that two int arrays are equal
        /// </summary>
        public static void Equal(int[] expected, int[] actual, string? message = null)
        {
            if (expected == null && actual == null) return;
            if (expected == null || actual == null)
            {
                SetFailed(message ?? "Int arrays are not equal (one is null)");
                return;
            }
            if (expected.Length != actual.Length)
            {
                SetFailed(message ?? $"Int arrays have different lengths: expected {expected.Length}, actual {actual.Length}");
                return;
            }
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i])
                {
                    SetFailed(message ?? $"Int arrays differ at index {i}: expected {expected[i]}, actual {actual[i]}");
                    return;
                }
            }
        }

        /// <summary>
        /// Assert that a value is not null
        /// </summary>
        public static void NotNull(object? obj, string? message = null)
        {
            if (obj == null)
            {
                SetFailed("Expected non-null value");
            }
        }

        /// <summary>
        /// Assert that a value is null
        /// </summary>
        public static void Null(object? obj, string? message = null)
        {
            if (obj != null)
            {
                SetFailed(message ?? "Expected null value");
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
