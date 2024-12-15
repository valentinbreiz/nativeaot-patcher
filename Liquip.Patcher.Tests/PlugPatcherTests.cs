using Liquip.API.Attributes;
using System.Runtime.InteropServices;
using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Liquip.Patcher.Extensions;
using Xunit;
using NativeWrapper;

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
    }
}
