using System;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.TestRunner.Framework;
using static Cosmos.TestRunner.Framework.TestRunner;
using static Cosmos.TestRunner.Framework.Assert;

namespace Cosmos.Kernel.Tests.HelloWorld
{
    internal unsafe static partial class Program
    {
        /// <summary>
        /// Unmanaged entry point called by the bootloader
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
        private static void KernelMain() => Main();

        /// <summary>
        /// Main test kernel entry point
        /// </summary>
        private static void Main()
        {
            // Test that we can reach Main() at all
            Serial.WriteString("[HelloWorld] Main() reached!\n");
            Serial.WriteString("[HelloWorld] Starting tests...\n");

            // Initialize test suite
            Start("HelloWorld Basic Tests", expectedTests: 7);

            // Test 1: Basic arithmetic
            Run("Test_BasicArithmetic", () =>
            {
                int result = 2 + 2;
                Equal(4, result);
            });

            // Test 2: Boolean logic
            Run("Test_BooleanLogic", () =>
            {
                bool isTrue = true;
                True(isTrue);
                False(!isTrue);
            });

            // Test 3: Integer comparison
            Run("Test_IntegerComparison", () =>
            {
                int a = 10;
                int b = 10;
                int c = 20;

                Equal(a, b);
                True(a < c);
                False(a > c);
            });

            // Test 4: Try-catch basic functionality
            Run("Test_TryCatch_Basic", () =>
            {
                bool caughtException = false;
                try
                {
                    throw new InvalidOperationException("Test exception");
                }
                catch (InvalidOperationException)
                {
                    caughtException = true;
                }

                True(caughtException, "Exception should have been caught");
            });

            // Test 5: Try-catch with exception message
            Run("Test_TryCatch_Message", () =>
            {
                string caughtMessage = null;
                try
                {
                    throw new InvalidOperationException("Expected message");
                }
                catch (InvalidOperationException ex)
                {
                    caughtMessage = ex.Message;
                }

                Equal("Expected message", caughtMessage);
            });

            // Test 6: Try-catch with base exception type
            Run("Test_TryCatch_BaseType", () =>
            {
                bool caughtException = false;
                try
                {
                    throw new InvalidOperationException("Test");
                }
                catch (Exception)
                {
                    caughtException = true;
                }

                True(caughtException, "Base Exception type should catch derived exceptions");
            });

            // Test 7: Try-finally
            Run("Test_TryFinally", () =>
            {
                bool finallyExecuted = false;
                try
                {
                    // No exception
                }
                finally
                {
                    finallyExecuted = true;
                }

                True(finallyExecuted, "Finally block should always execute");
            });

            // Finish test suite
            Finish();

            // Output completion message
            Serial.WriteString("\n[Tests Complete - System Halting]\n");

            // Halt the system with infinite loop
            while (true) ;
        }
    }
}
