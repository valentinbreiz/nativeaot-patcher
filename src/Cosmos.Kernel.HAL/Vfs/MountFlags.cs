// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Vfs;

/// <summary>
/// Mount option bits.
/// </summary>
[Flags]
public enum MountFlags : uint
{
    None = 0,
    /// <summary>Read-only mount.</summary>
    ReadOnly = 1,
    /// <summary>Ignore suid/sgid.</summary>
    NoSuid = 2,
    /// <summary>No execution.</summary>
    NoExec = 8,
    /// <summary>Disallow device nodes.</summary>
    NoDev = 4,
}
