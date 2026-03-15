using System.ComponentModel;
using Spectre.Console.Cli;

namespace Cosmos.Patcher.Coverage;

public sealed class InstrumentCoverageCommand : Command<InstrumentCoverageCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--assembly-dir <DIR>")]
        [Description("Directory containing assemblies to instrument (e.g. obj/.../cosmos/).")]
        public required string AssemblyDir { get; set; }

        [CommandOption("--output-map <PATH>")]
        [Description("Path to write the coverage map file.")]
        public required string OutputMapPath { get; set; }

        [CommandOption("--include <PREFIX>")]
        [Description("Assembly name prefix to include (default: Cosmos.Kernel).")]
        [DefaultValue("Cosmos.Kernel")]
        public string IncludePrefix { get; set; } = "Cosmos.Kernel";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (!Directory.Exists(settings.AssemblyDir))
        {
            Console.Error.WriteLine($"[Coverage] Error: Assembly directory not found: {settings.AssemblyDir}");
            return -1;
        }

        try
        {
            var instrumenter = new CoverageInstrumenter(
                settings.AssemblyDir,
                settings.OutputMapPath,
                settings.IncludePrefix);

            int count = instrumenter.Instrument();

            if (count == 0)
            {
                Console.WriteLine("[Coverage] No methods instrumented.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Coverage] Error: {ex.Message}");
            return -1;
        }
    }
}
