using System.Reflection;
using Cosmos.Build.API.Attributes;
using Cosmos.Patcher;
using Cosmos.Tests.NativeWrapper;
using Mono.Cecil;

namespace Cosmos.Tests.Patcher;

public class PlugPatcherTest_StaticPlugs
{
    private AssemblyDefinition CreateMockAssembly<T>()
    {
        string assemblyPath = typeof(T).Assembly.Location;
        return AssemblyDefinition.ReadAssembly(assemblyPath);
    }

    [Fact]
    public void PatchType_ShouldReplaceAllMethodsCorrectly()
    {
        // Arrange
        PlugScanner plugScanner = new();
        PlugPatcher patcher = new(plugScanner);

        AssemblyDefinition targetAssembly = CreateMockAssembly<TestClass>();
        TypeDefinition targetType = targetAssembly.MainModule.Types.First(t => t.Name == nameof(TestClass));

        AssemblyDefinition plugAssembly = CreateMockAssembly<TestClassPlug>();
        TypeDefinition plugType = plugAssembly.MainModule.Types.First(t => t.Name == nameof(TestClassPlug));

        // Act
        patcher.PatchType(targetType, plugAssembly);

        // Assert
        foreach (MethodDefinition plugMethod in plugType.Methods)
        {
            if (!plugMethod.IsPublic || !plugMethod.IsStatic || plugMethod.IsConstructor)
            {
                continue;
            }

            bool hasPlugAttribute = plugMethod.CustomAttributes.Any(attr =>
                attr.AttributeType.FullName == typeof(PlugMemberAttribute).FullName);

            if (!hasPlugAttribute)
            {
                continue;
            }

            MethodDefinition targetMethod = targetType.Methods.FirstOrDefault(m => m.Name == plugMethod.Name);
            if (targetMethod?.Body == null)
            {
                continue;
            }

            Assert.Equal(plugMethod.Body.Instructions.Count, targetMethod.Body.Instructions.Count); // Ensure the method body matches
        }
    }

    [Fact]
    public void PatchType_ShouldPlugAssembly()
    {
        // Arrange
        PlugScanner plugScanner = new();
        PlugPatcher patcher = new(plugScanner);

        AssemblyDefinition targetAssembly = CreateMockAssembly<TestClass>();
        AssemblyDefinition plugAssembly = CreateMockAssembly<TestClassPlug>();

        // Act
        patcher.PatchAssembly(targetAssembly, plugAssembly);

        // Assert
        TypeDefinition targetType = targetAssembly.MainModule.Types.FirstOrDefault(t => t.Name == nameof(TestClass));
        TypeDefinition plugType = plugAssembly.MainModule.Types.FirstOrDefault(t => t.Name == nameof(TestClassPlug));

        Assert.NotNull(targetType); // Ensure the target type exists
        Assert.NotNull(plugType); // Ensure the plug type exists

        foreach (MethodDefinition plugMethod in plugType.Methods)
        {
            if (!plugMethod.IsPublic || !plugMethod.IsStatic || plugMethod.IsConstructor)
            {
                continue;
            }

            bool hasPlugAttribute = plugMethod.CustomAttributes.Any(attr =>
                attr.AttributeType.FullName == typeof(PlugMemberAttribute).FullName);

            if (!hasPlugAttribute)
            {
                continue;
            }

            MethodDefinition targetMethod = targetType.Methods.FirstOrDefault(m => m.Name == plugMethod.Name);
            if (targetMethod?.Body == null)
            {
                continue;
            }

            Assert.Equal(plugMethod.Body.Instructions.Count, targetMethod.Body.Instructions.Count); // Ensure the method body matches
        }
    }

    [Fact]
    public void AddMethod_BehaviorBeforeAndAfterPlug()
    {
        // Arrange
        PlugScanner plugScanner = new();
        PlugPatcher patcher = new(plugScanner);

        AssemblyDefinition targetAssembly = CreateMockAssembly<TestClass>();
        AssemblyDefinition plugAssembly = CreateMockAssembly<TestClassPlug>();

        // Act
        patcher.PatchAssembly(targetAssembly, plugAssembly);

        targetAssembly.Save("./", "targetAssembly.dll");

        object result = ExecuteObject(targetAssembly, "TestClass", "Add", 3, 4);
        Assert.Equal(12, result);
    }

    [Fact]
    public void ManagedAddMethod_ShouldRemainUnchangedWithoutPlugMember()
    {
        // Arrange
        PlugScanner plugScanner = new();
        PlugPatcher patcher = new(plugScanner);

        AssemblyDefinition targetAssembly = CreateMockAssembly<TestClass>();
        AssemblyDefinition plugAssembly = CreateMockAssembly<TestClassPlug>();

        // Act
        patcher.PatchAssembly(targetAssembly, plugAssembly);

        object result = ExecuteObject(targetAssembly, "TestClass", "ManagedAdd", 3, 4);

        // Assert
        Assert.Equal(7, result);
    }

    private object ExecuteObject(AssemblyDefinition assemblyDefinition, string typeName, string methodName,
        params object[] parameters)
    {
        using MemoryStream memoryStream = new();
        assemblyDefinition.Write(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);

        Assembly loadedAssembly = Assembly.Load(memoryStream.ToArray());
        Type? type = loadedAssembly.GetType("Cosmos.Tests.NativeWrapper.TestClass");
        Assert.NotNull(type);
        MethodInfo? method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);

        return (int)method.Invoke(null, parameters);
    }
}
