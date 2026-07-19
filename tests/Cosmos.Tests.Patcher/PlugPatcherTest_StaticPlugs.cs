using System.Reflection;
using Cosmos.Patcher;
using Cosmos.Tests.NativeWrapper;
using Mono.Cecil;

namespace Cosmos.Tests.Patcher;

[Collection("PatcherTests")]
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
            //TODO: Only evaluate methods with PlugMemberAttribute
            if (!plugMethod.IsPublic || !plugMethod.IsStatic || !plugMethod.IsConstructor)
            {
                continue;
            }

            MethodDefinition targetMethod = targetType.Methods.FirstOrDefault(m => m.Name == plugMethod.Name);
            Assert.NotNull(targetMethod); // Ensure the method exists in the target type
            Assert.NotNull(targetMethod.Body); // Ensure the method has a body
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
            //TODO: Only evaluate methods with PlugMemberAttribute
            if (!plugMethod.IsPublic || !plugMethod.IsStatic || !plugMethod.IsConstructor)
            {
                continue;
            }

            MethodDefinition targetMethod = targetType.Methods.FirstOrDefault(m => m.Name == plugMethod.Name);
            Assert.NotNull(targetMethod); // Ensure the method exists in the target type
            Assert.NotNull(targetMethod.Body); // Ensure the method has a body
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

        TypeDefinition targetType = targetAssembly.MainModule.Types.First(t => t.Name == nameof(TestClass));
        MethodDefinition targetMethod = targetType.Methods.First(m => m.Name == "Add");
        Assert.False(targetMethod.IsPInvokeImpl);
        Assert.Null(targetMethod.PInvokeInfo);

        targetAssembly.Save("./", "targetAssembly.dll");

        object result = ExecuteObject(targetAssembly, "TestClass", "Add", 3, 4);
        Assert.Equal(12, result);
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
