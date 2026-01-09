using System;
using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.Runtime;

namespace Cosmos.Kernel.Plugs.Internal.Runtime.CompilerHelpers;

[Plug("Internal.Runtime.CompilerHelpers.StartupCodeHelpers")]
public unsafe partial class StartupCodeHelpersPlug
{
    [PlugMember]
    public static void RunModuleInitializers()
    {
        // Already run early in ManagedModule.InitializeModules()
    }
}
