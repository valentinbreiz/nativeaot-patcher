// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Vfs;

/// <summary>
/// Linux <c>timespec64</c>-style instant for inode timestamps.
/// </summary>
public struct VfsTimespec
{
    public long TvSec;
    public long TvNsec;

    public VfsTimespec(long tvSec, long tvNsec)
    {
        TvSec = tvSec;
        TvNsec = tvNsec;
    }
}
