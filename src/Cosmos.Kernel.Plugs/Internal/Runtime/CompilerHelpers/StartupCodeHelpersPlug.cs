using System;
using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Runtime;

namespace Cosmos.Kernel.Plugs.Internal.Runtime.CompilerHelpers;

[Plug("Internal.Runtime.CompilerHelpers.StartupCodeHelpers")]
public unsafe partial class StartupCodeHelpersPlug
{
    [LibraryImport("*", EntryPoint = "GetModules")]
    public static unsafe partial uint GetModules(out nint* modules);
    [PlugMember]
    public static void RunModuleInitializers()
    {
        var count = GetModules(out var modulesptr);
        ManagedModule.InitializeAll(new(modulesptr, (int)count));
    }
}
