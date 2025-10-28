using System;
using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core.Runtime;

namespace Cosmos.Kernel.Plugs.Internal.Runtime.CompilerHelpers;

[Plug("Internal.Runtime.CompilerHelpers.StartupCodeHelpers")]
public unsafe partial class StartupCodeHelpersPlug
{
    [PlugMember]
    public static void RunModuleInitializers()
    {
        // TODO: Fix stack corruption in ManagedModule.InitializeStatics
        // Module initialization causes memory corruption and system crashes.
        // For now, skip module initialization to allow kernel to reach Main.
        // This means GC statics won't be initialized and some features may not work.

        Cosmos.Kernel.Core.IO.Serial.WriteString("[StartupCodeHelpers] RunModuleInitializers - Starting\n");
        ManagedModule.RunModuleInitializers();
        Cosmos.Kernel.Core.IO.Serial.WriteString("[StartupCodeHelpers] RunModuleInitializers - Complete\n");
    }
}
