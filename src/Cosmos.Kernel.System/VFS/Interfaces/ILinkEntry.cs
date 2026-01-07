// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.System.VFS.Interfaces;

public interface ILinkEntry : IDirectoryEntry
{
    public IDirectoryEntry GetReal();
}
