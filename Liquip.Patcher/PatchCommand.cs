using Spectre.Console.Cli;
using System.ComponentModel;
using Mono.Cecil;
using System;
using System.IO;
using System.Linq;

namespace Liquip.Patcher
{
    public class PatchCommand : Command<PatchCommand.Settings>
    {
        public class Settings : CommandSettings
        {
            [CommandOption("--target <TARGET>")]
            [Description("Path to the target assembly.")]
            public string TargetAssembly { get; set; } = null!;

            [CommandOption("--plugs <PLUGS>")]
            [Description("Paths to plug assemblies.")]
            public string[] PlugsReferences { get; set; } = Array.Empty<string>();
        }

        public override int Execute(CommandContext context, Settings settings)
        {
            Console.WriteLine("Running PatchCommand...");

            if (!File.Exists(settings.TargetAssembly))
            {
                Console.WriteLine($"Error: Target assembly '{settings.TargetAssembly}' not found.");
                return -1;
            }

            var plugPaths = settings.PlugsReferences.Where(File.Exists).ToArray();
            if (!plugPaths.Any())
            {
                Console.WriteLine("Error: No valid plug assemblies provided.");
                return -1;
            }

            try
            {
                var targetAssembly = AssemblyDefinition.ReadAssembly(settings.TargetAssembly);
                Console.WriteLine($"Loaded target assembly: {settings.TargetAssembly}");

                var plugAssemblies = plugPaths
                    .Select(plugPath => AssemblyDefinition.ReadAssembly(plugPath))
                    .ToArray();

                Console.WriteLine("Loaded plug assemblies:");
                foreach (var plug in plugPaths)
                {
                    Console.WriteLine($" - {plug}");
                }

                var plugPatcher = new PlugPatcher(new PlugScanner());
                plugPatcher.PatchAssembly(targetAssembly, plugAssemblies);

                var outputPath = Path.Combine(Path.GetDirectoryName(settings.TargetAssembly)!,
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
}
