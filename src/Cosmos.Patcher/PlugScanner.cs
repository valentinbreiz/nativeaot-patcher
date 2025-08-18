using Cosmos.Build.API.Attributes;
using Mono.Cecil;
using MonoMod.Utils;

namespace Cosmos.Patcher;

public sealed class PlugScanner
{
    public List<TypeDefinition> LoadPlugs(params AssemblyDefinition[] assemblies) => LoadPlugs(null, assemblies);
    public List<TypeDefinition> LoadPlugs(TypeDefinition? targetType = null, params AssemblyDefinition[] assemblies)
    {
        List<TypeDefinition> output =
        [
            ..assemblies
                .SelectMany(assembly => assembly.Modules)
                .SelectMany(module => module.Types)
                .Where(i =>
                {
                    CustomAttribute? plugAttr = i.GetCustomAttribute(typeof(PlugAttribute).FullName);
                    if (plugAttr == null)
                        return false;

                    if (targetType == null)
                        return true;

                    string? targetTypeName = plugAttr.GetArgument<string>(named: "Target")
                                             ?? plugAttr.GetArgument<string>(named: "TargetName");

                    return targetType.FullName == targetTypeName;
                })
        ];

        foreach (TypeDefinition type in output)
        {
            Console.WriteLine($"Plug found: {type.Name}");
        }

        return output;
    }

    public List<MethodDefinition> LoadPlugMethods(TypeDefinition plugType) =>
        [.. plugType.Methods.Where(i => i.IsPublic && i.IsStatic)];
}
