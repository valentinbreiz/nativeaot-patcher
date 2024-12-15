using Liquip.API.Attributes;
using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Liquip.Patcher.Extensions;

namespace Liquip.Patcher
{
    public class PlugPatcher
    {
        private readonly PlugScanner _scanner;

        public PlugPatcher(PlugScanner scanner)
        {
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        }

        /// <summary>
        /// Patches the target method by replacing its body with the body of the plug method.
        /// </summary>
        /// <param name="targetMethod">The method in the base class to patch.</param>
        /// <param name="plugMethod">The plug method providing the replacement body.</param>
        public void PatchMethod(MethodDefinition targetMethod, MethodDefinition plugMethod)
        {
            if (targetMethod == null) throw new ArgumentNullException(nameof(targetMethod));
            if (plugMethod == null) throw new ArgumentNullException(nameof(plugMethod));

            Console.WriteLine($"Patching method: {targetMethod.FullName} with plug: {plugMethod.FullName}");

            targetMethod.SwapMethods(plugMethod);

            Console.WriteLine($"Patched method: {targetMethod.Name} successfully.");
        }

        /// <summary>
        /// Scans for plug methods and patches corresponding target methods in the target type.
        /// </summary>
        /// <param name="targetType">The target class to patch.</param>
        /// <param name="plugAssemblies">The assemblies to search for plug methods.</param>
        public void PatchType(TypeDefinition targetType, params AssemblyDefinition[] plugAssemblies)
        {
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));
            if (plugAssemblies == null || plugAssemblies.Length == 0) throw new ArgumentNullException(nameof(plugAssemblies));

            Console.WriteLine($"Scanning and patching type: {targetType.FullName}");

            var plugTypes = _scanner.LoadPlugs(plugAssemblies);

            if (plugTypes != null)
            {
                foreach (var plugType in plugTypes)
                {
                    // Match if the plug is intended for this target type
                    var plugAttribute = plugType.CustomAttributes
                        .FirstOrDefault(attr => attr.AttributeType.FullName == typeof(PlugAttribute).FullName);

                    if (plugAttribute == null) continue;

                    var targetTypeName = plugAttribute.ConstructorArguments.FirstOrDefault().Value?.ToString();

                    if (targetTypeName != targetType.FullName) continue;

                    foreach (var plugMethod in plugType.Methods)
                    {
                        if (!plugMethod.IsPublic || !plugMethod.IsStatic) continue;

                        var targetMethod = targetType.Methods.FirstOrDefault(m => m.Name == plugMethod.Name &&
                                                                                 m.Parameters.Count == plugMethod.Parameters.Count);
                        if (targetMethod != null)
                        {
                            Console.WriteLine($"Found matching method: {targetMethod.Name}. Patching...");
                            PatchMethod(targetMethod, plugMethod);
                        }
                        else
                        {
                            Console.WriteLine($"No matching method found for plug: {plugMethod.Name}");
                        }

                    }
                }
            }

            Console.WriteLine($"Patched type: {targetType.Name} successfully.");
        }

        /// <summary>
        /// Patches an entire assembly by scanning for target types and their corresponding plugs.
        /// </summary>
        /// <param name="targetAssembly">The assembly to patch.</param>
        /// <param name="plugAssemblies">The assemblies to search for plug methods.</param>
        public void PatchAssembly(AssemblyDefinition targetAssembly, params AssemblyDefinition[] plugAssemblies)
        {
            if (targetAssembly == null) throw new ArgumentNullException(nameof(targetAssembly));
            if (plugAssemblies == null || plugAssemblies.Length == 0) throw new ArgumentNullException(nameof(plugAssemblies));

            Console.WriteLine($"Scanning and patching assembly: {targetAssembly.MainModule.Name}");

            foreach (var targetType in targetAssembly.MainModule.Types)
            {
                PatchType(targetType, plugAssemblies);
            }

            Console.WriteLine($"Patched assembly: {targetAssembly.MainModule.Name} successfully.");
        }
    }
}
