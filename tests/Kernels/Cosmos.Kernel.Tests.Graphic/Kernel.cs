using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.TestRunner.Framework;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;

namespace Cosmos.Kernel.Tests.Gtaphic;

public class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
        Serial.WriteString("[Gtaphic] BeforeRun() reached!\n");
        Serial.WriteString("[Gtaphic] Starting tests...\n");

        // Initialize test suite
        TR.Start("Gtaphic Basic Tests", expectedTests: 3);

        // Test 1: Basic arithmetic
        TR.Run("Test_BasicArithmetic", () =>
        {
            int result = 2 + 2;
            Assert.Equal(4, result);
        });

        // Test 2: Boolean logic
        TR.Run("Test_BooleanLogic", () =>
        {
            bool isTrue = true;
            Assert.True(isTrue);
            Assert.False(!isTrue);
        });

        // Test 3: Integer comparison
        TR.Run("Test_IntegerComparison", () =>
        {
            int a = 10;
            int b = 10;
            int c = 20;

            Assert.Equal(a, b);
            Assert.True(a < c);
            Assert.False(a > c);
        });

        // Finish test suite
        TR.Finish();

        // Output completion message
        Serial.WriteString("\n[Tests Complete - System Halting]\n");

        // Stop the kernel loop
        Stop();
    }

    protected override void Run()
    {
        // Tests completed in BeforeRun, nothing to do here
    }

    protected override void AfterRun()
    {
        Cosmos.Kernel.Kernel.Halt();
    }
}
