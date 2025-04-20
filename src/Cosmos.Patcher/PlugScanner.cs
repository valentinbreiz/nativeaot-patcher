using Cosmos.API.Attributes;
using Mono.Cecil;
using MonoMod.Utils;

namespace Cosmos.Patcher;

public sealed class PlugScanner
{
    public List<TypeDefinition> LoadPlugs(params AssemblyDefinition[] assemblies)
    {
        List<TypeDefinition> output =
        [
            ..assemblies
                .SelectMany(assembly => assembly.Modules)
                .SelectMany(module => module.Types)
                .Where(i=> i.HasCustomAttribute(typeof(PlugAttribute).FullName))

        ];

        foreach (TypeDefinition? type in output)
        {
            Console.WriteLine($"Plug found: {type.Name}");
        }

        return output;
    }

    public List<MethodDefinition> LoadPlugMethods(TypeDefinition plugType) =>
        [.. plugType.Methods.Where(i => i.IsPublic && i.IsStatic)];
}
