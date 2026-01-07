// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.System.VFS.Interfaces;

public interface IFileEntry : IDirectoryEntry
{
    /// <summary>
    /// Get file stream.
    /// </summary>
    /// <returns>Stream value.</returns>
    public Stream GetFileStream();
}
