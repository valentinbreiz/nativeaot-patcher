using System.ComponentModel;
using Cosmos.Build.API.Enum;
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
        public required string TargetAssembly { get; set; }

        [CommandOption("--target-platform <TARGET-PLATFORM>")]
        [Description("Target platform for the patching process.")]
        public required string TargetPlatform { get; set; }

        [CommandOption("--plugs <PLUGS>")]
        [Description("Plug assemblies, separated by ';' or ','.")]
        public required string PlugsReferencesRaw { get; set; }


        [CommandOption("--output <OUTPUT>")]
        [Description("Output path for the patched dll")]
        public required string OutputPath { get; set; }

        [CommandOption("--coverage")]
        [Description("Enable plug-map generation for coverage tracking.")]
        [DefaultValue(false)]
        public bool Coverage { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        ConsoleBuildLogger logger = new();
        logger.Info("Running PatchCommand...");

        if (!File.Exists(settings.TargetAssembly))
        {
            logger.Error($"Error: Target assembly '{settings.TargetAssembly}' not found.");
            return -1;
        }

        char[] separators = [';', ',', Path.PathSeparator];
        string[] plugPaths = [.. (settings.PlugsReferencesRaw ?? string.Empty)
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()];

        if (plugPaths.Length == 0)
        {
            logger.Error("Error: No plug assemblies specified. Use --plugs \"a.dll;b.dll\"");
            return -1;
        }

        plugPaths = [.. plugPaths.Where(File.Exists)];
        if (plugPaths.Length == 0)
        {
            logger.Error("Error: No valid plug assemblies provided (files not found).");
            return -1;
        }

        try
        {
            // Read with symbols to preserve debug info
            var readerParams = new Mono.Cecil.ReaderParameters { ReadSymbols = true };
            AssemblyDefinition targetAssembly;
            bool hasSymbols = false;
            try
            {
                targetAssembly = AssemblyDefinition.ReadAssembly(settings.TargetAssembly, readerParams);
                hasSymbols = true;
                logger.Info($"Loaded target assembly with symbols: {settings.TargetAssembly}");
            }
            catch
            {
                // Fallback without symbols if PDB not found
                targetAssembly = AssemblyDefinition.ReadAssembly(settings.TargetAssembly);
                logger.Info($"Loaded target assembly (no symbols): {settings.TargetAssembly}");
            }

            AssemblyDefinition[] plugAssemblies = [.. plugPaths.Select(AssemblyDefinition.ReadAssembly)];
            PlatformArchitecture targetPlatform = Enum.Parse<PlatformArchitecture>(settings.TargetPlatform.ToUpperInvariant());

            logger.Info("Loaded plug assemblies:");
            foreach (string plug in plugPaths)
            {
                logger.Info($" - {plug}");
            }

            PlugPatcher plugPatcher = new(new PlugScanner(logger))
            {
                CoverageEnabled = settings.Coverage
            };
            plugPatcher.PatchAssembly(targetAssembly, targetPlatform, plugAssemblies);

            string finalPath = settings.OutputPath ??
                               Path.Combine(
                                   Path.GetDirectoryName(settings.TargetAssembly)!,
                                   Path.GetFileNameWithoutExtension(settings.TargetAssembly) + "_patched.dll");

            // Write plug map for coverage tracking (plug method → target method)
            // Only generated when --coverage is passed; uses assembly-specific filename to
            // avoid overwrites (MSBuild batches one assembly per invocation)
            if (settings.Coverage && plugPatcher.PlugMappings.Count > 0)
            {
                string assemblyName = Path.GetFileNameWithoutExtension(settings.TargetAssembly);
                string plugMapPath = Path.Combine(
                    Path.GetDirectoryName(finalPath) ?? ".",
                    $"plug-map-{assemblyName}.txt");
                WritePlugMap(plugMapPath, plugPatcher.PlugMappings);
                logger.Info($"Plug map written: {plugPatcher.PlugMappings.Count} mappings to {plugMapPath}");
            }

            // Write with symbols if we read them
            if (hasSymbols)
            {
                var writerParams = new Mono.Cecil.WriterParameters { WriteSymbols = true };
                targetAssembly.Write(finalPath, writerParams);
            }
            else
            {
                targetAssembly.Write(finalPath);
            }

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

    private static void WritePlugMap(string path, List<PlugMapping> mappings)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("# Plug Map - generated by cosmos.patcher patch");
        writer.WriteLine("# PlugAssembly\tPlugType\tPlugMethod\tTargetAssembly\tTargetType\tTargetMethod");
        foreach (var m in mappings)
        {
            writer.WriteLine($"{m.PlugAssembly}\t{m.PlugType}\t{m.PlugMethod}\t{m.TargetAssembly}\t{m.TargetType}\t{m.TargetMethod}");
        }
    }
}
