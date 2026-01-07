// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.System.VFS.Interfaces;

public interface IIsFileSystem
{
    /// <summary>
    ///
    /// </summary>
    /// <param name="partition"></param>
    public bool IsFormat(Partition partition);

    public IFileSystem GetFileSystem(Partition partition, string path);
}
