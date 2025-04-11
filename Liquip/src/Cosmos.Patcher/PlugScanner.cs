using Cosmos.API.Attributes;
using Mono.Cecil;
using MonoMod.Utils;

namespace Cosmos.Patcher;

public sealed class PlugScanner
{
    public List<MethodDefinition> LoadPlugs(params AssemblyDefinition[] assemblies)
    {
        List<MethodDefinition> output =
        [
            ..assemblies
                .SelectMany(assembly => assembly.Modules)
                .SelectMany(module => module.Types)
                .SelectMany(type => type.Methods)
                .Where(method =>
                    method.HasCustomAttributes &&
                    method.CustomAttributes.Any(attr =>
                        attr.AttributeType.FullName == typeof(PlugMethodAttribute).FullName ||
                        attr.AttributeType.FullName ==
                        typeof(NativeMethodAttribute).FullName
                    )
                )
        ];

        foreach (MethodDefinition? type in output)
        {
            Console.WriteLine($"Plug found: {type.Name}");
        }

        return output;
    }

    public List<MethodDefinition> LoadPlugMethods(TypeDefinition plugType) =>
        plugType.Methods.Where(i => i.IsPublic && i.IsStatic).ToList();
}
