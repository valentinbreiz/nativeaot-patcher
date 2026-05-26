
// Auto-generated
namespace Cosmos.Kernel.System.Internal;

[global::System.CodeDom.Compiler.GeneratedCode("Cosmos.Kernel.SourceGenerators.CosmosEntryPointGenerator", "3.0.58.0")]
public static class CosmosEntryPoint
{
    public static void Main()
    {
        Cosmos.Kernel.System.Global.RegisterKernel(new global::TestKernel.TestKernel_TestGeneratedKernel());
        Cosmos.Kernel.System.Global.StartKernel();
    }
}
