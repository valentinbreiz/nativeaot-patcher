// This code is licensed under the BSD 3-Clause license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Vfs;

/// <summary>
/// Per-superblock callbacks.
/// </summary>
public interface ISuperblockOperations
{
    bool Sync(IVfsSuperblock superblock);

    bool StatFs(IVfsSuperblock superblock, out VfsStatFs statFs);

    /// <summary>
    /// Tear down this mount (unmount); analogous to <c>kill_sb</c> / final put.
    /// </summary>
    void Drop(IVfsSuperblock superblock);
}
