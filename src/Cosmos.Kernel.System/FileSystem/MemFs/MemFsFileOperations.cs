// This code is licensed under MIT license (see LICENSE for details)

using System;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.System.FileSystem.MemFs;

/// <summary>
/// File operations implementation for MemFs.
/// </summary>
internal class MemFsFileOperations : IFileOperations
{
    private static void Log(params object?[] args)
    {
        Serial.WriteString("[VFS:MemFs:FileOperations] ");
        Serial.Write(args);
        Serial.WriteString("\n");
    }

    MemFsInode Checks(IInode inode)
    {
        if (inode is not MemFsInode memInode)
        {
            Log("Inode must be a MemFsInode.", nameof(inode));
            throw new ArgumentException("Inode must be a MemFsInode.", nameof(inode));
        }

        if (!memInode.IsFile)
        {
            Log("Cannot write to non-file inode.");
            throw new InvalidOperationException("Cannot write to non-file inode.");
        }
        return memInode;
    }

    public int Read(IInode inode, byte[] buffer, int offset, int count, long position)
    {
        Log("Read(", inode.InodeNumber, ",", buffer.Length, ",", offset, ",", count, ",", position, ")");
        MemFsInode memInode = Checks(inode);

        byte[] data = memInode.Data;
        long dataLength = data.Length;

        if (position >= dataLength)
            return 0;

        long available = dataLength - position;
        long bytesToReadLong = count < available ? count : available;
        int bytesToRead = (int)bytesToReadLong;

        Array.Copy(data, (int)position, buffer, offset, bytesToRead);
        return bytesToRead;
    }

    public int Write(IInode inode, byte[] buffer, int offset, int count, long position)
    {
        Log("Write(", inode.InodeNumber, ",", buffer.Length, ",", offset, ",", count, ",", position, ")");
        MemFsInode memInode = Checks(inode);

        byte[] data = memInode.Data;
        long newSize = (long)data.Length > (position + count) ? (long)data.Length : (position + count);

        // Resize data array if needed
        if (newSize > data.Length)
        {
            if (newSize > int.MaxValue)
            {
                Log(nameof(position), "File size exceeds maximum supported size.");
                throw new ArgumentOutOfRangeException(nameof(position), "File size exceeds maximum supported size.");
            }

            byte[] newData = new byte[(int)newSize];
            Array.Copy(data, 0, newData, 0, data.Length);
            data = newData;
        }

        Array.Copy(buffer, offset, data, (int)position, count);
        memInode.SetData(data);

        return count;
    }

    public void Flush(IInode inode)
    {
        Log("Flush(", inode.InodeNumber, ")");
        // In-memory file system doesn't need flushing
    }

    public void Truncate(IInode inode, long length)
    {
        Log("Truncate(", inode.InodeNumber, ",", length, ")");
        MemFsInode memInode = Checks(inode);

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        byte[] data = memInode.Data;
        if (length == 0)
        {
            memInode.SetData([]);
        }
        else if (length < data.Length)
        {
            byte[] newData = new byte[length];
            Array.Copy(data, 0, newData, 0, (int)length);
            memInode.SetData(newData);
        }
        else if (length > data.Length)
        {
            byte[] newData = new byte[length];
            Array.Copy(data, 0, newData, 0, data.Length);
            memInode.SetData(newData);
        }
    }
}
