using Liquip.NativeWrapper;
using Liquip.Patcher;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Liquip.Patcher.Tests
{
    public class PlugPatcherTest_ObjectPlugs
    {
        private AssemblyDefinition CreateMockAssembly<T>()
        {
            var assemblyPath = typeof(T).Assembly.Location;
            return AssemblyDefinition.ReadAssembly(assemblyPath);
        }

        [Fact]
        public void PatchObjectWithAThis_ShouldPlugInstanceCorrectly()
        {
            // Arrange
            var plugScanner = new PlugScanner();
            var patcher = new PlugPatcher(plugScanner);

            var targetAssembly = CreateMockAssembly<NativeWrapperObject>();
            var plugAssembly = CreateMockAssembly<NativeWrapperObjectPlug>();

            var targetType = targetAssembly.MainModule.Types.FirstOrDefault(t => t.Name == nameof(NativeWrapperObject));
            Assert.NotNull(targetType);

            // Act: Apply the plug
            patcher.PatchAssembly(targetAssembly, plugAssembly);

            PlugUtils.Save(targetAssembly, "./", "targetObjectAssembly.dll");

            var result = ExecuteObject(targetAssembly, "NativeWrapperObject", "InstanceMethod", new object[] { 10 });
            Assert.Equal(20, result);
        }

        private object ExecuteObject(AssemblyDefinition assemblyDefinition, string typeName, string methodName, params object[] parameters)
        {
            using var memoryStream = new System.IO.MemoryStream();
            assemblyDefinition.Write(memoryStream);
            memoryStream.Seek(0, System.IO.SeekOrigin.Begin);

            var loadedAssembly = Assembly.Load(memoryStream.ToArray());
            var type = loadedAssembly.GetType("Liquip.NativeWrapper.NativeWrapperObject");
            Assert.NotNull(type);

            var instance = Activator.CreateInstance(type);
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(method);

            return method.Invoke(instance, parameters);
        }
    }
}
