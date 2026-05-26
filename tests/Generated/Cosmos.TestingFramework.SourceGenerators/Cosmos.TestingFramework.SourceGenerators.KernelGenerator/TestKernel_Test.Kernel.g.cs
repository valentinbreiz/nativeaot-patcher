namespace TestKernel;

// Auto-generated
[global::System.CodeDom.Compiler.GeneratedCode("Cosmos.TestingFramework.SourceGenerators.KernelGenerator", "3.0.58.0")]
public sealed class TestKernel_TestGeneratedKernel : global::Cosmos.Kernel.System.Kernel
{
    protected override void BeforeRun()
    {
        var instance = new global::TestKernel.Test();
        global::Cosmos.TestRunner.Framework.TestRunner.Start("TestKernel.Test Tests", expectedTests: 4);
        global::Cosmos.TestRunner.Framework.TestRunner.Run("TestKernel.Test.TestTer", () => instance.TestTer());
        global::Cosmos.TestRunner.Framework.TestRunner.Run("TestKernel.Test.TestTer2", () => instance.TestTer2());
        global::Cosmos.TestRunner.Framework.TestRunner.Run("TestKernel.Test.TestTer3", () => instance.TestTer3());
        global::Cosmos.TestRunner.Framework.TestRunner.Run("TestKernel.Test.TestTer5", () => global::TestKernel.Test.TestTer5());
        global::Cosmos.TestRunner.Framework.TestRunner.Finish();
    }

    protected override void Run() => Stop();

    protected override void AfterRun()
    {
        global::Cosmos.TestRunner.Framework.TestRunner.Complete();
        global::Cosmos.Kernel.System.Power.Halt();
    }
}
