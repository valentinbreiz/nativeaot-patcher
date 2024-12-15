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
using MonoMod.Utils;

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

            int count = 0;

            foreach (var plugMethod in plugAssembly.MainModule.Types.First(t => t.Name == nameof(TestClassPlug)).Methods)
            {
                if (!plugMethod.IsPublic || !plugMethod.IsStatic) continue;

                count = plugMethod.Body.Instructions.Count;
            }

            // Act
            patcher.PatchType(targetType, plugAssembly);

            // Assert
            foreach (var plugMethod in plugAssembly.MainModule.Types.First(t => t.Name == nameof(TestClassPlug)).Methods)
            {
                if (!plugMethod.IsPublic || !plugMethod.IsStatic) continue;

                var targetMethod = targetType.Methods.FirstOrDefault(m => m.Name == plugMethod.Name);
                Assert.NotNull(targetMethod);
                Assert.NotNull(targetMethod.Body);
                Assert.Equal(count, targetMethod.Body.Instructions.Count);
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

            int count = 0;

            foreach (var plugMethod in plugAssembly.MainModule.Types.First(t => t.Name == nameof(TestClassPlug)).Methods)
            {
                if (!plugMethod.IsPublic || !plugMethod.IsStatic) continue;

                count = plugMethod.Body.Instructions.Count;
            }

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

                Assert.Equal(count, targetMethod.Body.Instructions.Count);
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

            var resultAfterPlug = ExecuteMethod(targetAssembly, "TestClass", "Add", 3, 4);
            Assert.Equal(12, resultAfterPlug);
        }

        private int ExecuteMethod(AssemblyDefinition assemblyDefinition, string typeName, string methodName, params object[] parameters)
        {
            PlugUtils.Save(assemblyDefinition, "./", "targetAssembly.dll");

            var loadedAssembly = Assembly.LoadFile("./targetAssembly.dll");
            var type = loadedAssembly.GetType(typeName);
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);

            Assert.NotNull(method);

            return (int)method.Invoke(null, parameters);
        }



    }
}
