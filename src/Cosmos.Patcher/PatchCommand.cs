using System.ComponentModel;
using Cosmos.Patcher.Logging;
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
        IBuildLogger logger = new ConsoleBuildLogger();
        logger.Info("Running PatchCommand...");

        if (!File.Exists(settings.TargetAssembly))
        {
            logger.Error($"Error: Target assembly '{settings.TargetAssembly}' not found.");
            return -1;
        }

        var separators = new[] { ';', ',', Path.PathSeparator };
        string[] plugPaths = (settings.PlugsReferencesRaw ?? string.Empty)
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToArray();

        if (plugPaths.Length == 0)
        {
            logger.Error("Error: No plug assemblies specified. Use --plugs \"a.dll;b.dll\"");
            return -1;
        }

        plugPaths = plugPaths.Where(File.Exists).ToArray();
        if (plugPaths.Length == 0)
        {
            logger.Error("Error: No valid plug assemblies provided (files not found).");
            return -1;
        }

        try
        {
            AssemblyDefinition targetAssembly = AssemblyDefinition.ReadAssembly(settings.TargetAssembly);
            logger.Info($"Loaded target assembly: {settings.TargetAssembly}");

            AssemblyDefinition[] plugAssemblies = plugPaths
                .Select(AssemblyDefinition.ReadAssembly)
                .ToArray();

            logger.Info("Loaded plug assemblies:");
            foreach (string plug in plugPaths)
                logger.Info($" - {plug}");

            PlugPatcher plugPatcher = new(new PlugScanner(logger));
            plugPatcher.PatchAssembly(targetAssembly, plugAssemblies);

            string finalPath = settings.OutputPath ??
                               Path.Combine(
                                   Path.GetDirectoryName(settings.TargetAssembly)!,
                                   Path.GetFileNameWithoutExtension(settings.TargetAssembly) + "_patched.dll");

            targetAssembly.Write(finalPath);

            logger.Info($"Patched assembly saved to: {finalPath}");
            logger.Info("Patching completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.Error($"Error during patching: {ex}");
            return -1;
        }
    }
}
