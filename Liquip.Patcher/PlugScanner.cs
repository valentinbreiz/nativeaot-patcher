using System.Reflection;
using Liquip.API.Attributes;
using Mono.Cecil;
using MonoMod.Utils;

namespace Liquip.Patcher;

public class PlugScanner
{

    public List<TypeDefinition> LoadPlugs(params AssemblyDefinition[] assemblies)
    {
        var output = assemblies
        .SelectMany(assembly =>
            assembly.Modules
                .SelectMany(type => type.Types)
                .Where(i => i.HasCustomAttribute(typeof(PlugAttribute).FullName))
        ).ToList();

        foreach (var type in output)
        {
            Console.WriteLine($"Plug found: {type.Name}");
        }

        return output;
    }

    public List<MethodDefinition> LoadPlugMethods(TypeDefinition plugType)
    {
        return plugType.Methods.Where(i => i.IsPublic && i.IsStatic).ToList();
    }

}