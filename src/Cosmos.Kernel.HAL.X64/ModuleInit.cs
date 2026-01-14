// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;

namespace Cosmos.Kernel.HAL.X64;

/// <summary>
/// Module initializer that registers the X64 platform initializer.
/// This runs automatically when the assembly is loaded.
/// </summary>
internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        PlatformHAL.SetInitializer(new X64PlatformInitializer());
    }
}
