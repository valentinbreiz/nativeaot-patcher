// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.Services.VFS.Interfaces;

public interface IFileSystemFormatter
{
    /// <summary>
    ///
    /// </summary>
    /// <param name="partition"></param>
    /// <param name="fast"></param>
    public void Format(Partition partition, bool fast);
}
