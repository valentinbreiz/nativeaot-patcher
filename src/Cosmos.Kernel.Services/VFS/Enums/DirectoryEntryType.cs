// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.Services.VFS.Enums;

public enum DirectoryEntryType : byte
{
    Directory = 0x01,
    File = 0x02,
    Link = 0x03,
    Unknown = 0x04
}
