using System.Reflection;
using Cosmos.Patcher;
using Cosmos.Tests.NativeWrapper;
using Mono.Cecil;
using Xunit;
using System.Linq;

namespace Cosmos.Tests.Patcher;

public class PlugPatcherTest_SkipUnpluggedAssembly
{
    private static AssemblyDefinition CreateMockAssembly<T>()
    {
        string assemblyPath = typeof(T).Assembly.Location;
        return AssemblyDefinition.ReadAssembly(assemblyPath);
    }

    [Fact]
    public void PatchAssembly_ShouldSkipWhenNoMatchingPlugs()
    {
        PlugScanner scanner = new();
        PlugPatcher patcher = new(scanner);

        AssemblyDefinition targetAssembly = CreateMockAssembly<NativeWrapperObject>();
        AssemblyDefinition plugAssembly = CreateMockAssembly<PlugPatcherTest_SkipUnpluggedAssembly>();

        TypeDefinition targetType = targetAssembly.MainModule.Types.First(t => t.FullName == typeof(NativeWrapperObject).FullName);
        MethodDefinition method = targetType.Methods.First(m => m.Name == nameof(NativeWrapperObject.InstanceMethod));
        string ilBefore = string.Join(";", method.Body.Instructions.Select(i => i.ToString()));

        bool patched = patcher.PatchAssembly(targetAssembly, plugAssembly);

        string ilAfter = string.Join(";", method.Body.Instructions.Select(i => i.ToString()));
        Assert.False(patched);
        Assert.Equal(ilBefore, ilAfter);
    }
}
