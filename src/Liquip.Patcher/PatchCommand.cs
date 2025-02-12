using Spectre.Console.Cli;
using System.ComponentModel;
using Mono.Cecil;

namespace Liquip.Patcher;

public class PatchCommand : Command<PatchCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--target <TARGET>")]
        [Description("Path to the target assembly.")]
        public string TargetAssembly { get; set; } = null!;

        [CommandOption("--plugs <PLUGS>")]
        [Description("Paths to plug assemblies.")]
        public string[] PlugsReferences { get; set; } = [];
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        Console.WriteLine("Running PatchCommand...");

        if (!File.Exists(settings.TargetAssembly))
        {
            Console.WriteLine($"Error: Target assembly '{settings.TargetAssembly}' not found.");
            return -1;
        }

        string[]? plugPaths = settings.PlugsReferences.Where(File.Exists).ToArray();
        if (plugPaths.Length == 0)
        {
            Console.WriteLine("Error: No valid plug assemblies provided.");
            return -1;
        }

        try
        {
            AssemblyDefinition? targetAssembly = AssemblyDefinition.ReadAssembly(settings.TargetAssembly);
            Console.WriteLine($"Loaded target assembly: {settings.TargetAssembly}");

            AssemblyDefinition[]? plugAssemblies = plugPaths
                .Select(plugPath => AssemblyDefinition.ReadAssembly(plugPath))
                .ToArray();

            Console.WriteLine("Loaded plug assemblies:");
            foreach (string? plug in plugPaths)
            {
                Console.WriteLine($" - {plug}");
            }

            PlugPatcher? plugPatcher = new(new PlugScanner());
            plugPatcher.PatchAssembly(targetAssembly, plugAssemblies);

            string? outputPath = Path.Combine(Path.GetDirectoryName(settings.TargetAssembly)!,
                Path.GetFileNameWithoutExtension(settings.TargetAssembly) + "_patched.dll");

            targetAssembly.Write(outputPath);
            Console.WriteLine($"Patched assembly saved to: {outputPath}");

            Console.WriteLine("Patching completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during patching: {ex.Message}");
            return -1;
        }
    }
}
