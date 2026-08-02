// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Bridge;

/// <summary>
/// Native imports for boot-time facts recorded by the C bootstrap (kmain.c).
/// </summary>
public static partial class BootNative
{
    /// <summary>
    /// Top of the bootloader-provided stack, captured at kmain entry before any
    /// managed code runs. Every managed frame of the boot thread lies below it.
    /// </summary>
    [LibraryImport("*", EntryPoint = "__cosmos_get_boot_stack_top")]
    [SuppressGCTransition]
    public static partial nuint GetBootStackTop();
}
