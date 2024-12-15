using System;
using System.Linq;
using Mono.Cecil;
using Xunit;
using Liquip.Patcher;
using Liquip.API.Attributes;

namespace Liquip.Patcher.Tests
{
    public class PlugScannerTests_LoadPlugMethods
    {
        private AssemblyDefinition CreateMockAssembly<T>()
        {
            var assemblyPath = typeof(T).Assembly.Location;
            return AssemblyDefinition.ReadAssembly(assemblyPath);
        }

        [Fact]
        public void LoadPlugMethods_ShouldReturnPublicStaticMethods()
        {
            // Arrange
            var assembly = CreateMockAssembly<MockPlugWithMethods>();
            var scanner = new PlugScanner();
            var plugType = assembly.MainModule.Types.First(t => t.Name == nameof(MockPlugWithMethods));

            // Act
            var methods = scanner.LoadPlugMethods(plugType);

            // Assert
            Assert.NotEmpty(methods);
            Assert.Contains(methods, method => method.Name == nameof(MockPlugWithMethods.StaticMethod));
            Assert.DoesNotContain(methods, method => method.Name == nameof(MockPlugWithMethods.InstanceMethod));
        }

        [Fact]
        public void LoadPlugMethods_ShouldReturnEmpty_WhenNoMethodsExist()
        {
            // Arrange
            var assembly = CreateMockAssembly<EmptyPlug>();
            var scanner = new PlugScanner();
            var plugType = assembly.MainModule.Types.First(t => t.Name == nameof(EmptyPlug));

            // Act
            var methods = scanner.LoadPlugMethods(plugType);

            // Assert
            Assert.Empty(methods);
        }
    }
}
