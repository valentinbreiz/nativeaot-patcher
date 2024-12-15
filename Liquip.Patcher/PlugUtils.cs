using System.Reflection;
using Mono.Cecil;

namespace Liquip.Patcher;

public static class PlugUtils
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="assembly"></param>
    /// <param name="rootPath"></param>
    public static void Save(this AssemblyDefinition assembly, string rootPath)
    {
        assembly.Write(Path.Combine(rootPath, $"{assembly.Name.Name}.dll"));
    }

    public static List<AssemblyDefinition> LoadAssemblies(params string[] paths)
    {
        return paths.Select(AssemblyDefinition.ReadAssembly).ToList();
    }
}