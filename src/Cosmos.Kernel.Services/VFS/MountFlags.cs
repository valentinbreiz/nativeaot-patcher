// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.Services.VFS;

[Flags]
public enum MountFlags : uint
{
    None             = 0,

    // Read/write
    ReadOnly         = 1 << 0,
    ReadWrite        = 1 << 1,

    // Execution
    NoExec           = 1 << 2,
    Exec             = 1 << 3,

    // Access time updates
    NoAccessTime     = 1 << 4,
    AccessTime       = 1 << 5,

    // Directory access time updates
    NoDirAccessTime  = 1 << 6,
    DirAccessTime    = 1 << 7,

    // Sync mode
    Sync             = 1 << 8,
    Async            = 1 << 9,

    // Default
    Default = ReadWrite | Exec | NoAccessTime | NoDirAccessTime | Async,
}

