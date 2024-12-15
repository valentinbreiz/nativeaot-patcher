using System.Reflection;
using Liquip.API.Attributes;
using Mono.Cecil;
using MonoMod.Utils;

namespace Liquip.Patcher;

public static class PlugScanner
{

    public static List<TypeDefinition> LoadPlugs(params AssemblyDefinition[] assemblies)
    {
        var output = assemblies
            .SelectMany(assembly =>
                assembly.Modules
                    .SelectMany(type => type.Types)
                    .Where(i=> 
                        i.HasCustomAttribute(typeof(PlugAttribute).FullName)
                    )
            );
        return output.ToList();
    }

    public static List<MethodDefinition> LoadPlugMethods(TypeDefinition plugType)
    {
        return plugType.Methods.Where(i => i.IsPublic && i.IsStatic).ToList();
    }

}