using System.Reflection;
using Cosmos.Patcher;
using Cosmos.Tests.NativeWrapper;
using Mono.Cecil;

namespace Cosmos.Tests.Patcher;

public class PlugPatcherTest_ObjectPlugs
{
    private AssemblyDefinition CreateMockAssembly<T>()
    {
        string assemblyPath = typeof(T).Assembly.Location;
        return AssemblyDefinition.ReadAssembly(assemblyPath);
    }

    [Fact]
    public void PatchObjectWithAThis_ShouldPlugInstanceCorrectly()
    {
        // Arrange
        PlugScanner plugScanner = new();
        PlugPatcher patcher = new(plugScanner);

        AssemblyDefinition targetAssembly = CreateMockAssembly<NativeWrapperObject>();
        AssemblyDefinition plugAssembly = CreateMockAssembly<NativeWrapperObjectImpl>();

        TypeDefinition? targetType =
            targetAssembly.MainModule.Types.FirstOrDefault(t => t.FullName == typeof(NativeWrapperObject).FullName);
        Assert.NotNull(targetType);

        // Act: Apply the plug
        patcher.PatchAssembly(targetAssembly, plugAssembly);

        targetAssembly.Save("./", "targetObjectAssembly.dll");

        object result = ExecuteObject(targetAssembly, typeof(NativeWrapperObject).FullName, nameof(NativeWrapperObject.InstanceMethod), 10);
        Assert.Equal(20, result);
    }

    private object ExecuteObject(AssemblyDefinition assemblyDefinition, string typeName, string methodName,
        params object[] parameters)
    {
        using MemoryStream memoryStream = new();
        assemblyDefinition.Write(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);

        Assembly loadedAssembly = Assembly.Load(memoryStream.ToArray());
        Type? type = loadedAssembly.GetType(typeof(NativeWrapperObject).FullName);
        Assert.NotNull(type);

        object? instance = Activator.CreateInstance(type);
        MethodInfo? method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);

        return method.Invoke(instance, parameters);
    }

    private object ExecutePropertyGet(AssemblyDefinition assemblyDefinition, string typeName, string propertyName)
    {
        using MemoryStream memoryStream = new();
        assemblyDefinition.Write(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);

        Assembly loadedAssembly = Assembly.Load(memoryStream.ToArray());
        Type? type = loadedAssembly.GetType(typeName);
        Assert.NotNull(type);

        object? instance = Activator.CreateInstance(type);
        MethodInfo? method = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance).GetMethod;

        Assert.NotNull(method);

        return method.Invoke(instance, null);
    }

    [Fact]
    public void PatchConstructor_ShouldPlugCtorCorrectly()
    {
        // Arrange
        PlugScanner plugScanner = new();
        PlugPatcher patcher = new(plugScanner);

        AssemblyDefinition targetAssembly = CreateMockAssembly<NativeWrapperObject>();
        AssemblyDefinition plugAssembly = CreateMockAssembly<NativeWrapperObjectImpl>();

        TypeDefinition? targetType =
            targetAssembly.MainModule.Types.FirstOrDefault(t => t.Name == "NativeWrapperObject");
        Assert.NotNull(targetType);

        // Act: Apply the plug
        patcher.PatchAssembly(targetAssembly, plugAssembly);

        targetAssembly.Save("./", "targetCtorAssembly.dll");

        TextWriter @out = Console.Out; // Store the original output
        using StringWriter stringWriter = new();
        Console.SetOut(stringWriter);

        object instance = ExecuteConstructor(targetAssembly, typeof(NativeWrapperObject).FullName);
        Console.SetOut(@out); // Restore the original output

        // Assert: Check the standard output for the constructor plug
        string output = stringWriter.ToString();
        Assert.Contains("Plugged ctor", output);
    }

    [Fact]
    public void PatchProperty_ShouldPlugProperty()
    {
        TextWriter originalOutput = Console.Out; // Store the original output
        try
        {
            TextWriter output = Console.Out;
            // TODO: Use a more robust way to capture console output in tests(patcher should have a logger)
            using (var stringWriter = new StringWriter())
            {

                Console.SetOut(stringWriter);

                PlugScanner plugScanner = new();
                PlugPatcher patcher = new(plugScanner);

                AssemblyDefinition targetAssembly = CreateMockAssembly<NativeWrapperObject>();
                AssemblyDefinition plugAssembly = CreateMockAssembly<NativeWrapperObjectImpl>();

                TypeDefinition targetType =
                    targetAssembly.MainModule.Types.FirstOrDefault(t => t.Name == nameof(NativeWrapperObject));
                Assert.NotNull(targetType);

                patcher.PatchAssembly(targetAssembly, plugAssembly);
                targetAssembly.Save("./", "targetPropertyAssembly.dll");
                Console.SetOut(output);
                // Test Get
                object getResult = ExecutePropertyGet(targetAssembly, typeof(NativeWrapperObject).FullName, nameof(NativeWrapperObject.InstanceProperty));
                Assert.Equal("Plugged Goodbye World", getResult);

                // Test Get
                object getResult2 = ExecutePropertyGet(targetAssembly, typeof(NativeWrapperObject).FullName, nameof(NativeWrapperObject.InstanceBackingFieldProperty));
                Assert.Equal("Plugged Backing Field", getResult2);
            }
        }
        finally
        {
            // Restore the original output
            Console.SetOut(originalOutput);
        }
    }

    private object ExecuteConstructor(AssemblyDefinition assemblyDefinition, string typeName)
    {
        using MemoryStream memoryStream = new();
        assemblyDefinition.Write(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);

        Assembly loadedAssembly = Assembly.Load(memoryStream.ToArray());
        Type? type = loadedAssembly.GetType(typeName);
        Assert.NotNull(type);

        return Activator.CreateInstance(type);
    }
}
