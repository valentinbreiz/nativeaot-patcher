using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Runtime;
using Internal.Runtime;

namespace Cosmos.Kernel.Plugs.Internal.Runtime.CompilerHelpers;

[Plug("Internal.Runtime.CompilerHelpers.StartupCodeHelpers")]
public unsafe partial class StartupCodeHelpersPlug
{
    [PlugMember]
    public static void RunModuleInitializers()
    {
        // Already run early in ManagedModule.InitializeModules()
    }
    
    [PlugMember]
    internal static int GetLoadedModules(TypeManagerHandle[] outputModules)
    {
        Serial.WriteString("[StartupCodeHelpers] - Getting Loaded Modules\n");
        return ManagedModule.GetLoadedModules(outputModules);
    }
}
