using Mono.Cecil;

namespace Liquip.Patcher;

public static class PlugUtils
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="assembly"></param>
    /// <param name="rootPath"></param>
    public static void Save(this AssemblyDefinition assembly, string rootPath) =>
        assembly.Write(Path.Combine(rootPath, $"{assembly.Name.Name}.dll"));

    /// <summary>
    /// 
    /// </summary>
    /// <param name="assembly"></param>
    /// <param name="rootPath"></param>
    /// <param name="fileName"></param>
    public static void Save(this AssemblyDefinition assembly, string rootPath, string fileName) =>
        assembly.Write(Path.Combine(rootPath, $"{fileName}"));

    public static List<AssemblyDefinition> LoadAssemblies(params string[] paths) =>
        paths.Select(AssemblyDefinition.ReadAssembly).ToList();
}
