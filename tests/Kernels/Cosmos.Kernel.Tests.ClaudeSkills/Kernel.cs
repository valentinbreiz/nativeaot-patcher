using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.TestRunner.Framework;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;

namespace Cosmos.Kernel.Tests.ClaudeSkills;

public class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
        Serial.WriteString("[ClaudeSkills] BeforeRun() reached!\n");
        Serial.WriteString("[ClaudeSkills] Starting tests...\n");

        TR.Start("ClaudeSkills Tests", expectedTests: 1);

        TR.Run("Test_SkillNameFormatting", () =>
        {
            string skillName = "customizing-copilot-cloud-agents-environment";
            string[] nameParts = skillName.Split('-');

            Assert.Equal(5, nameParts.Length);
            Assert.Equal("customizing", nameParts[0]);
            Assert.Equal("environment", nameParts[4]);
        });

        TR.Finish();
        Serial.WriteString("\n[Tests Complete - System Halting]\n");
    }

    protected override void Run()
    {
        Stop();
    }

    protected override void AfterRun()
    {
        TR.Complete();
        Cosmos.Kernel.Kernel.Halt();
    }
}
