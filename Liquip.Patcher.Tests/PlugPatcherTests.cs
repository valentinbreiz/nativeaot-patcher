using Liquip.API.Attributes;
using System.Runtime.InteropServices;
using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Liquip.Patcher.Extensions;
using Xunit;
using NativeWrapper;
using System.Reflection;

namespace Liquip.Patcher.Tests
{
    public class PlugPatcherTests
    {
        private AssemblyDefinition CreateMockAssembly<T>()
        {
            var assemblyPath = typeof(T).Assembly.Location;
            return AssemblyDefinition.ReadAssembly(assemblyPath);
        }

        [Fact]
        public void PatchType_ShouldReplaceAllMethodsCorrectly()
        {
            // Arrange
            var plugScanner = new PlugScanner();
            var patcher = new PlugPatcher(plugScanner);

            var assembly = CreateMockAssembly<TestClass>();
            var targetType = assembly.MainModule.Types.First(t => t.Name == nameof(TestClass));

            var plugAssembly = CreateMockAssembly<TestClassPlug>();

            // Act
            patcher.PatchType(targetType, plugAssembly);

            // Assert
            foreach (var plugMethod in plugAssembly.MainModule.Types.First(t => t.Name == nameof(TestClassPlug)).Methods)
            {
                if (!plugMethod.IsPublic || !plugMethod.IsStatic) continue;

                var targetMethod = targetType.Methods.FirstOrDefault(m => m.Name == plugMethod.Name);
                Assert.NotNull(targetMethod);
                Assert.NotNull(targetMethod.Body);
                Assert.Equal(plugMethod.Body.Instructions.Count, targetMethod.Body.Instructions.Count);
            }
        }

        [Fact]
        public void PatchType_ShouldPlugAssembly()
        {
            // Arrange
            var plugScanner = new PlugScanner();
            var patcher = new PlugPatcher(plugScanner);

            var targetAssembly = CreateMockAssembly<TestClass>();
            var plugAssembly = CreateMockAssembly<TestClassPlug>();

            // Act
            patcher.PatchAssembly(targetAssembly, plugAssembly);

            // Assert
            var targetType = targetAssembly.MainModule.Types.FirstOrDefault(t => t.Name == nameof(TestClass));
            var plugType = plugAssembly.MainModule.Types.FirstOrDefault(t => t.Name == nameof(TestClassPlug));

            Assert.NotNull(targetType);
            Assert.NotNull(plugType);

            foreach (var plugMethod in plugType.Methods)
            {
                if (!plugMethod.IsPublic || !plugMethod.IsStatic) continue;

                var targetMethod = targetType.Methods.FirstOrDefault(m => m.Name == plugMethod.Name);
                Assert.NotNull(targetMethod);
                Assert.NotNull(targetMethod.Body);

                if (plugMethod.Body != null)
                {
                    Assert.Equal(plugMethod.Body.Instructions.Count, targetMethod.Body.Instructions.Count);
                }
            }
        }

        [Fact]
        public void AddMethod_BehaviorBeforeAndAfterPlug()
        {
            // Arrange
            var plugScanner = new PlugScanner();
            var patcher = new PlugPatcher(plugScanner);

            var targetAssembly = CreateMockAssembly<TestClass>();
            var plugAssembly = CreateMockAssembly<TestClassPlug>();

            // Act
            patcher.PatchAssembly(targetAssembly, plugAssembly);

            PlugUtils.Save(targetAssembly, "./", "targetAssembly.dll");

            var resultAfterPlug = ExecuteMethod(targetAssembly, "TestClass", "Add", 3, 4);
            Assert.Equal(12, resultAfterPlug);
        }

        private int ExecuteMethod(AssemblyDefinition assemblyDefinition, string typeName, string methodName, params object[] parameters)
        {
            // Save the patched assembly to a temporary location
            PlugUtils.Save(assemblyDefinition, "targetAssembly.dll");

            // Load the assembly into the AppDomain
            var loadedAssembly = Assembly.LoadFile("targetAssembly.dll");
            var type = loadedAssembly.GetType(typeName);
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);

            Assert.NotNull(method);

            // Execute the method and return the result
            return (int)method.Invoke(null, parameters);
        }

    }
}
