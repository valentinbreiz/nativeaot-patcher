// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;

namespace Cosmos.Kernel.HAL.ARM64;

/// <summary>
/// Eager Constructor that registers the ARM64 platform initializer.
/// This runs automatically when the assembly is loaded.
/// </summary>
[EagerStaticClassConstruction]
internal static class EagerCtor
{
    static EagerCtor()
    {
        PlatformHAL.SetInitializer(new ARM64PlatformInitializer());
    }
}
