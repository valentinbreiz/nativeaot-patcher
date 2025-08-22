using System.ComponentModel;
using Mono.Cecil;
using Spectre.Console.Cli;

namespace Cosmos.Patcher;

public sealed class PatchCommand : Command<PatchCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--target <TARGET>")]
        [Description("Path to the target assembly.")]
        public string TargetAssembly { get; set; } = null!;

        [CommandOption("--plugs <PLUGS>")]
        [Description("Plug assemblies, separated by ';' or ','.")]
        public string PlugsReferencesRaw { get; set; } = string.Empty;

        [CommandOption("--output <OUTPUT>")]
        [Description("Output path for the patched dll")]
        public string? OutputPath { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        Console.WriteLine("Running PatchCommand...");

        if (!File.Exists(settings.TargetAssembly))
        {
            Console.WriteLine($"Error: Target assembly '{settings.TargetAssembly}' not found.");
            return -1;
        }

        var separators = new[] { ';', ',', Path.PathSeparator };
        string[] plugPaths = (settings.PlugsReferencesRaw ?? string.Empty)
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToArray();

        if (plugPaths.Length == 0)
        {
            Console.WriteLine("Error: No plug assemblies specified. Use --plugs \"a.dll;b.dll\"");
            return -1;
        }

        plugPaths = plugPaths.Where(File.Exists).ToArray();
        if (plugPaths.Length == 0)
        {
            Console.WriteLine("Error: No valid plug assemblies provided (files not found).");
            return -1;
        }

        try
        {
            AssemblyDefinition targetAssembly = AssemblyDefinition.ReadAssembly(settings.TargetAssembly);
            Console.WriteLine($"Loaded target assembly: {settings.TargetAssembly}");

            AssemblyDefinition[] plugAssemblies = plugPaths
                .Select(AssemblyDefinition.ReadAssembly)
                .ToArray();

            Console.WriteLine("Loaded plug assemblies:");
            foreach (string plug in plugPaths)
                Console.WriteLine($" - {plug}");

            PlugPatcher plugPatcher = new(new PlugScanner());
            plugPatcher.PatchAssembly(targetAssembly, plugAssemblies);

            string finalPath = settings.OutputPath ??
                               Path.Combine(
                                   Path.GetDirectoryName(settings.TargetAssembly)!,
                                   Path.GetFileNameWithoutExtension(settings.TargetAssembly) + "_patched.dll");

            targetAssembly.Write(finalPath);

            Console.WriteLine($"Patched assembly saved to: {finalPath}");
            Console.WriteLine("Patching completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during patching: {ex}");
            return -1;
        }
    }
}
