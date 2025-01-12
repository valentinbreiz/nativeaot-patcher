using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Linq;

namespace Liquip.Patcher.Build.Tasks
{
    public class PatcherTask : ToolTask
    {
        [Required] public string PatcherPath { get; set; } = null;

        [Required] public string TargetAssembly { get; set; } = null;

        [Required] public ITaskItem[] PlugsReferences { get; set; } = null;

        protected override string GenerateFullPathToTool() =>
            // Retourne le chemin complet vers Liquip.Patcher.exe
            PatcherPath;

        protected override string GenerateCommandLineCommands()
        {
            CommandLineBuilder builder = new CommandLineBuilder();

            // Ajoute la commande principale
            builder.AppendSwitch("patch");

            // Ajoute l'argument --target
            builder.AppendSwitch("--target");
            builder.AppendFileNameIfNotNull(TargetAssembly);

            // Ajoute les plugs
            foreach (ITaskItem plug in PlugsReferences)
            {
                builder.AppendSwitch("--plugs");
                builder.AppendFileNameIfNotNull(plug.ItemSpec);
            }

            return builder.ToString();
        }

        public override bool Execute()
        {
            Log.LogMessage(MessageImportance.High, "Running Liquip.Patcher...");
            Log.LogMessage(MessageImportance.High, $"Tool Path: {PatcherPath}");
            Log.LogMessage(MessageImportance.High, $"Target Assembly: {TargetAssembly}");
            Log.LogMessage(MessageImportance.High,
                $"Plugs References: {string.Join(", ", PlugsReferences.Select(p => p.ItemSpec))}");

            return base.Execute();
        }

        protected override string ToolName => "Liquip.Patcher.exe";
    }
}
