// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Vfs;

namespace Cosmos.Kernel.System.Filesystems.Fat;

internal sealed class FatFileOperations : IFileOperations
{
    private readonly FatSuperblock _superblock;

    public FatFileOperations(FatSuperblock superblock)
    {
        _superblock = superblock;
    }

    public long Read(IVfsOpenFile openFile, Span<byte> buffer)
    {
        if (openFile.Inode is not FatInode inode)
        {
            return 0;
        }

        long position = openFile.Position;
        if (position < 0 || position >= inode.Size)
        {
            return 0;
        }

        long remainingFile = inode.Size - position;
        long toRead = buffer.Length < remainingFile ? buffer.Length : remainingFile;
        if (toRead <= 0)
        {
            return 0;
        }

        List<uint> chain = inode.ResolveChain();
        if (chain.Count == 0)
        {
            return 0;
        }

        uint clusterSize = _superblock.Boot.BytesPerCluster;
        long clusterIndex = position / clusterSize;
        long intraOffset = position % clusterSize;
        long copied = 0;

        Span<byte> clusterBuffer = new byte[clusterSize];

        while (copied < toRead && clusterIndex < chain.Count)
        {
            uint cluster = chain[(int)clusterIndex];
            _superblock.ReadCluster(cluster, clusterBuffer);

            long available = clusterSize - intraOffset;
            long want = toRead - copied;
            long chunk = available < want ? available : want;

            clusterBuffer.Slice((int)intraOffset, (int)chunk).CopyTo(buffer.Slice((int)copied, (int)chunk));
            copied += chunk;
            intraOffset = 0;
            clusterIndex++;
        }

        // Position advancement is the caller's responsibility (matches the
        // Linux VFS convention; VfsFileHandle re-applies it on top of this
        // return value).
        return copied;
    }

    public long Write(IVfsOpenFile openFile, ReadOnlySpan<byte> buffer)
    {
        if (openFile.Inode is not FatInode inode)
        {
            return 0;
        }

        if (buffer.Length == 0)
        {
            return 0;
        }

        long position = openFile.Position;
        if (position < 0)
        {
            return 0;
        }

        long endPosition = position + buffer.Length;
        uint clusterSize = _superblock.Boot.BytesPerCluster;
        long clustersNeeded = (endPosition + clusterSize - 1) / clusterSize;

        if (!EnsureChainLength(inode, (int)clustersNeeded))
        {
            return 0;
        }

        List<uint> chain = inode.ResolveChain();
        long clusterIndex = position / clusterSize;
        long intraOffset = position % clusterSize;
        long written = 0;

        Span<byte> clusterBuffer = new byte[clusterSize];

        while (written < buffer.Length && clusterIndex < chain.Count)
        {
            uint cluster = chain[(int)clusterIndex];

            long available = clusterSize - intraOffset;
            long want = buffer.Length - written;
            long chunk = available < want ? available : want;

            bool partial = chunk != clusterSize;
            if (partial)
            {
                _superblock.ReadCluster(cluster, clusterBuffer);
            }

            buffer.Slice((int)written, (int)chunk).CopyTo(clusterBuffer.Slice((int)intraOffset, (int)chunk));

            if (partial)
            {
                _superblock.WriteCluster(cluster, clusterBuffer);
            }
            else
            {
                _superblock.WriteCluster(cluster, clusterBuffer);
            }

            written += chunk;
            intraOffset = 0;
            clusterIndex++;
        }

        long newEnd = position + written;
        if (newEnd > inode.Size)
        {
            inode.Size = (uint)newEnd;
        }

        _superblock.UpdateInodeEntry(inode);
        return written;
    }

    public bool Seek(IVfsOpenFile openFile, long offset, SeekWhence whence, out long newPosition)
    {
        if (openFile.Inode is not FatInode inode)
        {
            newPosition = 0;
            return false;
        }

        long basePos = whence switch
        {
            SeekWhence.Set => 0,
            SeekWhence.Cur => openFile.Position,
            SeekWhence.End => inode.Size,
            _ => -1,
        };

        if (basePos < 0)
        {
            newPosition = 0;
            return false;
        }

        long target = basePos + offset;
        if (target < 0)
        {
            newPosition = 0;
            return false;
        }

        openFile.Position = target;
        newPosition = target;
        return true;
    }

    public bool Fsync(IVfsOpenFile openFile)
    {
        if (openFile.Inode is FatInode inode)
        {
            _superblock.UpdateInodeEntry(inode);
        }
        return true;
    }

    public void Release(IVfsOpenFile openFile)
    {
        Fsync(openFile);
    }

    private bool EnsureChainLength(FatInode inode, int targetCount)
    {
        List<uint> chain = inode.ResolveChain();
        if (chain.Count >= targetCount)
        {
            return true;
        }

        FatTable fat = _superblock.Fat;

        if (chain.Count == 0)
        {
            uint first = fat.AllocateChain(1);
            if (first == 0)
            {
                return false;
            }
            inode.FirstCluster = first;
            chain.Add(first);
        }

        while (chain.Count < targetCount)
        {
            uint last = chain[^1];
            uint added = fat.AllocateChain(1);
            if (added == 0)
            {
                return false;
            }
            fat.Set(last, added);
            chain.Add(added);
        }

        return true;
    }
}
