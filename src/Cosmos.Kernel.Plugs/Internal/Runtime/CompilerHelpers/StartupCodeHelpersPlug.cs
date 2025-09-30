using System;
using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core.Runtime;

namespace Cosmos.Kernel.Plugs.Internal.Runtime.CompilerHelpers;

[Plug("Internal.Runtime.CompilerHelpers.StartupCodeHelpers")]
public unsafe partial class StartupCodeHelpersPlug
{
    [LibraryImport("*", EntryPoint = "GetModules")]
    public static unsafe partial uint GetModules(out nint* modules);
    [PlugMember]
    public static void RunModuleInitializers()
    {
        // TODO: Fix stack corruption in ManagedModule.InitializeStatics
        // Module initialization causes memory corruption and system crashes.
        // For now, skip module initialization to allow kernel to reach Main.
        // This means GC statics won't be initialized and some features may not work.

        // Cosmos.Kernel.System.IO.Serial.WriteString("[StartupCodeHelpers] RunModuleInitializers - Starting\n");
        // var count = GetModules(out var modulesptr);
        // Cosmos.Kernel.System.IO.Serial.WriteString("[StartupCodeHelpers] Found ");
        // Cosmos.Kernel.System.IO.Serial.WriteNumber(count);
        // Cosmos.Kernel.System.IO.Serial.WriteString(" modules\n");
        // ManagedModule.InitializeAll(new(modulesptr, (int)count));
        // Cosmos.Kernel.System.IO.Serial.WriteString("[StartupCodeHelpers] Module initialization complete\n");
    }
}
