using Mono.Cecil;
using Liquip.NativeWrapper;
using System.Reflection;

namespace Liquip.Patcher.Tests;

public class PlugPatcherTest_StaticPlugs
{
    private AssemblyDefinition CreateMockAssembly<T>()
    {
        string? assemblyPath = typeof(T).Assembly.Location;
        return AssemblyDefinition.ReadAssembly(assemblyPath);
    }

    [Fact]
    public void PatchType_ShouldReplaceAllMethodsCorrectly()
    {
        // Arrange
        PlugScanner? plugScanner = new();
        PlugPatcher? patcher = new(plugScanner);

        AssemblyDefinition? assembly = CreateMockAssembly<TestClass>();
        TypeDefinition? targetType = assembly.MainModule.Types.First(t => t.Name == nameof(TestClass));

        AssemblyDefinition? plugAssembly = CreateMockAssembly<TestClassPlug>();

        int count = 0;

        foreach (MethodDefinition? plugMethod in plugAssembly.MainModule.Types
                     .First(t => t.Name == nameof(TestClassPlug)).Methods)
        {
            if (!plugMethod.IsPublic || !plugMethod.IsStatic)
            {
                continue;
            }

            count = plugMethod.Body.Instructions.Count;
        }

        // Act
        patcher.PatchType(targetType, plugAssembly);

        // Assert
        foreach (MethodDefinition? plugMethod in plugAssembly.MainModule.Types
                     .First(t => t.Name == nameof(TestClassPlug)).Methods)
        {
            if (!plugMethod.IsPublic || !plugMethod.IsStatic)
            {
                continue;
            }

            MethodDefinition? targetMethod = targetType.Methods.FirstOrDefault(m => m.Name == plugMethod.Name);
            Assert.NotNull(targetMethod);
            Assert.NotNull(targetMethod.Body);
            Assert.Equal(count, targetMethod.Body.Instructions.Count);
        }
    }

    [Fact]
    public void PatchType_ShouldPlugAssembly()
    {
        // Arrange
        PlugScanner? plugScanner = new();
        PlugPatcher? patcher = new(plugScanner);

        AssemblyDefinition? targetAssembly = CreateMockAssembly<TestClass>();
        AssemblyDefinition? plugAssembly = CreateMockAssembly<TestClassPlug>();

        int count = 0;

        foreach (MethodDefinition? plugMethod in plugAssembly.MainModule.Types
                     .First(t => t.Name == nameof(TestClassPlug)).Methods)
        {
            if (!plugMethod.IsPublic || !plugMethod.IsStatic)
            {
                continue;
            }

            count = plugMethod.Body.Instructions.Count;
        }

        // Act
        patcher.PatchAssembly(targetAssembly, plugAssembly);

        // Assert
        TypeDefinition? targetType = targetAssembly.MainModule.Types.FirstOrDefault(t => t.Name == nameof(TestClass));
        TypeDefinition? plugType = plugAssembly.MainModule.Types.FirstOrDefault(t => t.Name == nameof(TestClassPlug));

        Assert.NotNull(targetType);
        Assert.NotNull(plugType);

        foreach (MethodDefinition? plugMethod in plugType.Methods)
        {
            if (!plugMethod.IsPublic || !plugMethod.IsStatic)
            {
                continue;
            }

            MethodDefinition? targetMethod = targetType.Methods.FirstOrDefault(m => m.Name == plugMethod.Name);
            Assert.NotNull(targetMethod);
            Assert.NotNull(targetMethod.Body);

            Assert.Equal(count, targetMethod.Body.Instructions.Count);
        }
    }

    [Fact]
    public void AddMethod_BehaviorBeforeAndAfterPlug()
    {
        // Arrange
        PlugScanner? plugScanner = new();
        PlugPatcher? patcher = new(plugScanner);

        AssemblyDefinition? targetAssembly = CreateMockAssembly<TestClass>();
        AssemblyDefinition? plugAssembly = CreateMockAssembly<TestClassPlug>();

        // Act
        patcher.PatchAssembly(targetAssembly, plugAssembly);

        targetAssembly.Save("./", "targetAssembly.dll");

        object? result = ExecuteObject(targetAssembly, "TestClass", "Add", 3, 4);
        Assert.Equal(12, result);
    }

    private object ExecuteObject(AssemblyDefinition assemblyDefinition, string typeName, string methodName,
        params object[] parameters)
    {
        using MemoryStream? memoryStream = new();
        assemblyDefinition.Write(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);

        Assembly? loadedAssembly = Assembly.Load(memoryStream.ToArray());
        Type? type = loadedAssembly.GetType("Liquip.NativeWrapper.TestClass");
        Assert.NotNull(type);
        MethodInfo? method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);

        return (int)method.Invoke(null, parameters);
    }
}
