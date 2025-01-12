using System;
using System.Linq;
using Mono.Cecil;
using Xunit;
using Liquip.Patcher;
using Liquip.API.Attributes;
using Liquip.NativeWrapper;

namespace Liquip.Patcher.Tests;

public class PlugScannerTests_LoadPlugMethods
{
    private AssemblyDefinition CreateMockAssembly<T>()
    {
        string assemblyPath = typeof(T).Assembly.Location;
        return AssemblyDefinition.ReadAssembly(assemblyPath);
    }

    [Fact]
    public void LoadPlugMethods_ShouldReturnPublicStaticMethods()
    {
        // Arrange
        AssemblyDefinition assembly = CreateMockAssembly<MockPlugWithMethods>();
        PlugScanner scanner = new();
        TypeDefinition? plugType = assembly.MainModule.Types.First(t => t.Name == nameof(MockPlugWithMethods));

        // Act
        List<MethodDefinition> methods = scanner.LoadPlugMethods(plugType);

        // Assert
        Assert.NotEmpty(methods);
        Assert.Contains(methods, method => method.Name == nameof(MockPlugWithMethods.StaticMethod));
        Assert.DoesNotContain(methods, method => method.Name == nameof(MockPlugWithMethods.InstanceMethod));
    }

    [Fact]
    public void LoadPlugMethods_ShouldReturnEmpty_WhenNoMethodsExist()
    {
        // Arrange
        AssemblyDefinition assembly = CreateMockAssembly<EmptyPlug>();
        PlugScanner scanner = new();
        TypeDefinition? plugType = assembly.MainModule.Types.First(t => t.Name == nameof(EmptyPlug));

        // Act
        List<MethodDefinition> methods = scanner.LoadPlugMethods(plugType);

        // Assert
        Assert.Empty(methods);
    }

    [Fact]
    public void LoadPlugMethods_ShouldContainAddMethod_WhenPlugged()
    {
        // Arrange
        AssemblyDefinition assembly = CreateMockAssembly<TestClassPlug>();
        PlugScanner scanner = new();
        TypeDefinition? plugType = assembly.MainModule.Types.First(t => t.Name == nameof(TestClassPlug));

        // Act
        List<MethodDefinition> methods = scanner.LoadPlugMethods(plugType);

        // Assert
        Assert.Contains(methods, method => method.Name == "Add");
    }
}
