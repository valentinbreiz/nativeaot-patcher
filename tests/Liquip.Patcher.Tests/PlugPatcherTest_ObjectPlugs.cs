using Liquip.NativeWrapper;
using Mono.Cecil;
using System.Reflection;

namespace Liquip.Patcher.Tests;

public class PlugPatcherTest_ObjectPlugs
{
    private AssemblyDefinition CreateMockAssembly<T>()
    {
        string? assemblyPath = typeof(T).Assembly.Location;
        return AssemblyDefinition.ReadAssembly(assemblyPath);
    }

    [Fact]
    public void PatchObjectWithAThis_ShouldPlugInstanceCorrectly()
    {
        // Arrange
        PlugScanner? plugScanner = new();
        PlugPatcher? patcher = new(plugScanner);

        AssemblyDefinition? targetAssembly = CreateMockAssembly<NativeWrapperObject>();
        AssemblyDefinition? plugAssembly = CreateMockAssembly<NativeWrapperObjectPlug>();

        TypeDefinition? targetType =
            targetAssembly.MainModule.Types.FirstOrDefault(t => t.Name == nameof(NativeWrapperObject));
        Assert.NotNull(targetType);

        // Act: Apply the plug
        patcher.PatchAssembly(targetAssembly, plugAssembly);

        PlugUtils.Save(targetAssembly, "./", "targetObjectAssembly.dll");

        object? result = ExecuteObject(targetAssembly, "NativeWrapperObject", "InstanceMethod", [10]);

        Assert.Equal(20, result);
    }

    private object ExecuteObject(AssemblyDefinition assemblyDefinition, string typeName, string methodName,
        params object[] parameters)
    {
        using MemoryStream? memoryStream = new();
        assemblyDefinition.Write(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);

        Assembly? loadedAssembly = Assembly.Load(memoryStream.ToArray());
        Type? type = loadedAssembly.GetType("Liquip.NativeWrapper.NativeWrapperObject");
        Assert.NotNull(type);

        object? instance = Activator.CreateInstance(type);
        MethodInfo? method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);

        return method.Invoke(instance, parameters);
    }

    [Fact]
    public void PatchConstructor_ShouldPlugCtorCorrectly()
    {
        // Arrange
        PlugScanner? plugScanner = new();
        PlugPatcher? patcher = new(plugScanner);

        AssemblyDefinition? targetAssembly = CreateMockAssembly<NativeWrapperObject>();
        AssemblyDefinition? plugAssembly = CreateMockAssembly<NativeWrapperObjectPlug>();

        TypeDefinition? targetType =
            targetAssembly.MainModule.Types.FirstOrDefault(t => t.Name == nameof(NativeWrapperObject));
        Assert.NotNull(targetType);

        // Act: Apply the plug
        patcher.PatchAssembly(targetAssembly, plugAssembly);

        PlugUtils.Save(targetAssembly, "./", "targetCtorAssembly.dll");

        using StringWriter? stringWriter = new();
        Console.SetOut(stringWriter);

        object? instance = ExecuteConstructor(targetAssembly, "NativeWrapperObject");

        // Assert: Check the standard output for the constructor plug
        string? output = stringWriter.ToString();
        Assert.Contains("Plugged ctor", output);
    }

    private object ExecuteConstructor(AssemblyDefinition assemblyDefinition, string typeName)
    {
        using MemoryStream? memoryStream = new();
        assemblyDefinition.Write(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);

        Assembly? loadedAssembly = Assembly.Load(memoryStream.ToArray());
        Type? type = loadedAssembly.GetType("Liquip.NativeWrapper." + typeName);
        Assert.NotNull(type);

        return Activator.CreateInstance(type);
    }
}
