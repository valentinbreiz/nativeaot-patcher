// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System.Vfs;
using PalError = global::Interop.Error;
using PalSys = global::Interop.Sys;

namespace Cosmos.Kernel.Plugs.System.IO;

/// <summary>
/// Backing engine for the <c>Interop.Sys</c> file-I/O plugs: maps the BCL's
/// PAL contract (integer descriptors, <c>DirectoryEntry</c> streams, a process
/// current directory, PAL error codes) directly onto <see cref="VfsManager"/>.
/// </summary>
/// <remarks>
/// Descriptors are small non-negative integers because
/// <c>SafeFileHandle.IsInvalid</c> rejects anything outside [0, int.MaxValue].
/// Descriptors 0-2 are reserved for the stdio convention (the console is
/// plugged at the ConsolePal level and never allocates them). Like
/// <see cref="VfsManager"/> itself, this layer assumes single-threaded use and
/// takes no locks. Paths are the VFS's own unix-style absolute paths; the BCL
/// pre-collapses <c>.</c>/<c>..</c> segments before calling down.
/// </remarks>
internal static unsafe class VfsInterop
{
    private const int MaxOpenFiles = 64;
    private const int FirstFileDescriptor = 3;
    private const int MaxDirectoryStreams = 32;

    /// <summary>Longest VFS name (255 UTF-16 chars) at worst 3 UTF-8 bytes each, plus the NUL.</summary>
    private const int DirectoryNameBufferBytes = (3 * 255) + 1;

    private const int CopyChunkBytes = 32 * 1024;

    /// <summary>Leaf suffix used to move a rename destination aside until the real
    /// rename has succeeded (POSIX rename must not lose the destination on failure).</summary>
    private const string ReplaceBackupSuffix = ".~replace";

    /// <summary>Access-mode bits inside <see cref="PalSys.OpenFlags"/> (O_RDONLY/O_WRONLY/O_RDWR).</summary>
    private const PalSys.OpenFlags AccessModeMask = (PalSys.OpenFlags)0xF;

    private sealed class OpenFileState
    {
        public OpenFileState(IVfsFileHandle handle, string path, bool readable, bool writable)
        {
            Handle = handle;
            Path = path;
            Readable = readable;
            Writable = writable;
        }

        public IVfsFileHandle Handle { get; }
        public string Path { get; }
        public bool Readable { get; }
        public bool Writable { get; }

        /// <summary>Full path of a directory entry to remove once the last descriptor
        /// on this node closes — Windows-style delete-pending, because the FAT
        /// driver frees clusters immediately on unlink while open handles
        /// still reference them.</summary>
        public string? PendingUnlinkPath { get; set; }
    }

    private sealed class DirectoryStreamState
    {
        public DirectoryStreamState(string[] names, bool[] isDirectory, byte* nameBuffer)
        {
            Names = names;
            IsDirectory = isDirectory;
            NameBuffer = nameBuffer;
            Index = 0;
        }

        public string[] Names { get; }
        public bool[] IsDirectory { get; }
        public byte* NameBuffer { get; }
        public int Index { get; set; }
    }

    private static readonly OpenFileState?[] s_openFiles = new OpenFileState?[MaxOpenFiles];
    private static readonly DirectoryStreamState?[] s_directoryStreams = new DirectoryStreamState?[MaxDirectoryStreams];
    private static string s_currentDirectory = "/";

    internal static string CurrentDirectory => s_currentDirectory;

    // ---------------- descriptor operations ----------------

    internal static PalError Open(string path, PalSys.OpenFlags flags, int mode, out int fd)
    {
        fd = -1;

        string? fullPath = MakeAbsolute(path);
        if (fullPath == null)
        {
            return PalError.ENOENT;
        }

        PalSys.OpenFlags access = flags & AccessModeMask;
        bool writable = access == PalSys.OpenFlags.O_WRONLY || access == PalSys.OpenFlags.O_RDWR;
        bool readable = access == PalSys.OpenFlags.O_RDONLY || access == PalSys.OpenFlags.O_RDWR;

        IVfsFileHandle? handle;
        if (TryStatNode(fullPath, out VfsStat stat))
        {
            // POSIX order: O_CREAT|O_EXCL reports EEXIST for ANY existing
            // name, directories included — File.Copy relies on EEXIST (not
            // EISDIR→EACCES) to produce its "is a directory" IOException.
            if ((flags & PalSys.OpenFlags.O_CREAT) != 0 && (flags & PalSys.OpenFlags.O_EXCL) != 0)
            {
                return PalError.EEXIST;
            }

            if ((stat.Mode & ModeEnum.FileTypeMask) == ModeEnum.Directory)
            {
                // SafeFileHandle.Open remaps EISDIR to EACCES, which is the
                // BCL's documented behavior for opening a directory path.
                return PalError.EISDIR;
            }

            if (!VfsManager.TryOpenFile(fullPath, out handle) || handle == null)
            {
                return PalError.EIO;
            }

            if ((flags & PalSys.OpenFlags.O_TRUNC) != 0 && writable && stat.Size > 0
                && !SetSize(handle.Inode, 0))
            {
                handle.Dispose();
                return PalError.EIO;
            }
        }
        else
        {
            if ((flags & PalSys.OpenFlags.O_CREAT) == 0)
            {
                return PalError.ENOENT;
            }

            PalError createError = CreateRegularFile(fullPath, mode);
            if (createError != PalError.SUCCESS)
            {
                return createError;
            }

            if (!VfsManager.TryOpenFile(fullPath, out handle) || handle == null)
            {
                return PalError.EIO;
            }
        }

        int slot = FindFreeSlot(s_openFiles);
        if (slot < 0)
        {
            handle.Dispose();
            return PalError.EMFILE;
        }

        s_openFiles[slot] = new OpenFileState(handle, fullPath, readable, writable);
        fd = slot + FirstFileDescriptor;
        return PalError.SUCCESS;
    }

    internal static PalError Close(int fd)
    {
        if (fd >= 0 && fd < FirstFileDescriptor)
        {
            // stdio descriptors are not table-backed; closing them is a no-op.
            return PalError.SUCCESS;
        }

        OpenFileState? file = GetOpenFile(fd);
        if (file == null)
        {
            return PalError.EBADF;
        }

        file.Handle.Dispose();
        s_openFiles[fd - FirstFileDescriptor] = null;

        if (file.PendingUnlinkPath != null && !AnyDescriptorPendingOn(file.PendingUnlinkPath))
        {
            RemoveEntryDirect(file.PendingUnlinkPath);
        }

        return PalError.SUCCESS;
    }

    internal static PalError Read(int fd, byte* buffer, int count, out int bytesRead)
    {
        bytesRead = 0;

        OpenFileState? file = GetOpenFile(fd);
        if (file == null || !file.Readable)
        {
            return PalError.EBADF;
        }

        if (count < 0)
        {
            return PalError.EINVAL;
        }

        bytesRead = (int)file.Handle.Read(new Span<byte>(buffer, count));
        return PalError.SUCCESS;
    }

    internal static PalError Write(int fd, byte* buffer, int count, out int bytesWritten)
    {
        bytesWritten = 0;

        OpenFileState? file = GetOpenFile(fd);
        if (file == null || !file.Writable)
        {
            return PalError.EBADF;
        }

        if (count < 0)
        {
            return PalError.EINVAL;
        }

        ReadOnlySpan<byte> source = new ReadOnlySpan<byte>(buffer, count);
        int total = 0;
        while (total < count)
        {
            long written = file.Handle.Write(source.Slice(total));
            if (written <= 0)
            {
                break;
            }

            total += (int)written;
        }

        bytesWritten = total;
        if (total == 0 && count > 0)
        {
            // The FAT driver reports failure as a zero-length write; the two
            // causes it cannot distinguish for us are the 4 GiB size cap and
            // cluster exhaustion.
            return file.Handle.Position + count > uint.MaxValue ? PalError.EFBIG : PalError.ENOSPC;
        }

        return PalError.SUCCESS;
    }

    internal static PalError PRead(int fd, byte* buffer, int count, long offset, out int bytesRead)
    {
        bytesRead = 0;

        OpenFileState? file = GetOpenFile(fd);
        if (file == null || !file.Readable)
        {
            return PalError.EBADF;
        }

        long saved = file.Handle.Position;
        if (!file.Handle.TrySeek(offset, SeekWhence.Set))
        {
            return PalError.EINVAL;
        }

        PalError error = Read(fd, buffer, count, out bytesRead);
        file.Handle.TrySeek(saved, SeekWhence.Set);
        return error;
    }

    internal static PalError PWrite(int fd, byte* buffer, int count, long offset, out int bytesWritten)
    {
        bytesWritten = 0;

        OpenFileState? file = GetOpenFile(fd);
        if (file == null || !file.Writable)
        {
            return PalError.EBADF;
        }

        long saved = file.Handle.Position;
        if (!file.Handle.TrySeek(offset, SeekWhence.Set))
        {
            return PalError.EINVAL;
        }

        PalError error = Write(fd, buffer, count, out bytesWritten);
        file.Handle.TrySeek(saved, SeekWhence.Set);
        return error;
    }

    internal static PalError Seek(int fd, long offset, int whence, out long newPosition)
    {
        newPosition = -1;

        OpenFileState? file = GetOpenFile(fd);
        if (file == null)
        {
            return PalError.EBADF;
        }

        if (whence < (int)SeekWhence.Set || whence > (int)SeekWhence.End)
        {
            return PalError.EINVAL;
        }

        if (!file.Handle.TrySeek(offset, (SeekWhence)whence))
        {
            return PalError.EINVAL;
        }

        newPosition = file.Handle.Position;
        return PalError.SUCCESS;
    }

    internal static PalError StatDescriptor(int fd, out PalSys.FileStatus status)
    {
        status = default;

        OpenFileState? file = GetOpenFile(fd);
        if (file == null)
        {
            return PalError.EBADF;
        }

        if (!file.Handle.TryStat(out VfsStat stat))
        {
            return PalError.EIO;
        }

        FillStatus(file.Path, in stat, out status);
        return PalError.SUCCESS;
    }

    internal static PalError Fsync(int fd)
    {
        OpenFileState? file = GetOpenFile(fd);
        if (file == null)
        {
            return PalError.EBADF;
        }

        return file.Handle.Flush() ? PalError.SUCCESS : PalError.EIO;
    }

    internal static PalError Truncate(int fd, long length)
    {
        OpenFileState? file = GetOpenFile(fd);
        if (file == null)
        {
            return PalError.EBADF;
        }

        if (length < 0 || !file.Writable)
        {
            return PalError.EINVAL;
        }

        if (!SetSize(file.Handle.Inode, (ulong)length))
        {
            return length > uint.MaxValue ? PalError.EFBIG : PalError.EIO;
        }

        return PalError.SUCCESS;
    }

    internal static PalError SetDescriptorMode(int fd, int mode)
    {
        OpenFileState? file = GetOpenFile(fd);
        if (file == null)
        {
            return PalError.EBADF;
        }

        return ApplyMode(file.Handle.Inode, mode);
    }

    internal static PalError CopyDescriptor(int sourceFd, int destinationFd)
    {
        OpenFileState? source = GetOpenFile(sourceFd);
        OpenFileState? destination = GetOpenFile(destinationFd);
        if (source == null || destination == null || !source.Readable || !destination.Writable)
        {
            return PalError.EBADF;
        }

        byte[] chunk = new byte[CopyChunkBytes];
        while (true)
        {
            long read = source.Handle.Read(chunk);
            if (read <= 0)
            {
                return PalError.SUCCESS;
            }

            int total = 0;
            while (total < (int)read)
            {
                long written = destination.Handle.Write(chunk.AsSpan(total, (int)read - total));
                if (written <= 0)
                {
                    return PalError.ENOSPC;
                }

                total += (int)written;
            }
        }
    }

    /// <summary>Routes fd 1/2 writes (Debug/stderr output from the BCL) to the serial console.</summary>
    internal static void WriteConsole(byte* buffer, int count)
    {
        if (count <= 0)
        {
            return;
        }

        Span<char> chars = count <= 512 ? stackalloc char[count] : new char[count];
        for (int i = 0; i < count; i++)
        {
            chars[i] = (char)buffer[i];
        }

        Cosmos.Kernel.Core.IO.Serial.Write(new string(chars.Slice(0, count)));
    }

    // ---------------- path operations ----------------

    internal static PalError StatPath(string path, out PalSys.FileStatus status)
    {
        status = default;

        string? fullPath = MakeAbsolute(path);
        if (fullPath == null)
        {
            return PalError.ENOENT;
        }

        if (!TryStatNode(fullPath, out VfsStat stat))
        {
            return PalError.ENOENT;
        }

        FillStatus(fullPath, in stat, out status);
        return PalError.SUCCESS;
    }

    internal static PalError UnlinkFile(string path)
    {
        string? fullPath = MakeAbsolute(path);
        if (fullPath == null)
        {
            return PalError.ENOENT;
        }

        if (IsRootOrMountPoint(fullPath))
        {
            return PalError.EISDIR;
        }

        if (!TryStatNode(fullPath, out VfsStat stat))
        {
            return PalError.ENOENT;
        }

        if ((stat.Mode & ModeEnum.FileTypeMask) == ModeEnum.Directory)
        {
            return PalError.EISDIR;
        }

        // The FAT driver frees the cluster chain immediately, out from under
        // any live descriptor. Defer the removal to the last Close instead
        // (Windows-style delete-pending); this also keeps DeleteOnClose
        // working, since SafeFileHandle unlinks before it closes.
        if (MarkPendingIfOpen(fullPath))
        {
            return PalError.SUCCESS;
        }

        SplitParentLeaf(fullPath, out string parentPath, out string leaf);
        if (!VfsManager.TryOpenDirectory(parentPath, out IVfsDirectoryHandle? parent) || parent == null)
        {
            return PalError.ENOENT;
        }

        return parent.TryUnlink(leaf) ? PalError.SUCCESS : PalError.EIO;
    }

    internal static PalError CreateDirectory(string path, int mode)
    {
        string? fullPath = MakeAbsolute(path);
        if (fullPath == null)
        {
            return PalError.EINVAL;
        }

        if (TryStatNode(fullPath, out _))
        {
            return PalError.EEXIST;
        }

        SplitParentLeaf(fullPath, out string parentPath, out string leaf);
        if (leaf.Length == 0)
        {
            return PalError.EINVAL;
        }

        if (!TryStatNode(parentPath, out VfsStat parentStat))
        {
            return PalError.ENOENT;
        }

        if ((parentStat.Mode & ModeEnum.FileTypeMask) != ModeEnum.Directory)
        {
            return PalError.ENOTDIR;
        }

        if (!VfsManager.TryOpenDirectory(parentPath, out IVfsDirectoryHandle? parent) || parent == null)
        {
            // The parent stat came from the virtual root: nothing is mounted
            // there, so there is nowhere to create the entry.
            return PalError.EROFS;
        }

        ModeEnum createMode = PermissionBits(mode) | ModeEnum.Directory;
        return parent.TryCreateDirectory(leaf, createMode, out _) ? PalError.SUCCESS : PalError.EIO;
    }

    internal static PalError RemoveDirectory(string path)
    {
        string? fullPath = MakeAbsolute(path);
        if (fullPath == null)
        {
            return PalError.ENOENT;
        }

        if (IsRootOrMountPoint(fullPath))
        {
            return PalError.EBUSY;
        }

        if (!TryStatNode(fullPath, out VfsStat stat))
        {
            return PalError.ENOENT;
        }

        if ((stat.Mode & ModeEnum.FileTypeMask) != ModeEnum.Directory)
        {
            return PalError.ENOTDIR;
        }

        if (VfsManager.TryOpenDirectory(fullPath, out IVfsDirectoryHandle? target) && target != null
            && target.TryReadDir(out IReadOnlyList<IVfsInode> entries) && entries.Count > 0)
        {
            return PalError.ENOTEMPTY;
        }

        SplitParentLeaf(fullPath, out string parentPath, out string leaf);
        if (!VfsManager.TryOpenDirectory(parentPath, out IVfsDirectoryHandle? parent) || parent == null)
        {
            return PalError.ENOENT;
        }

        return parent.TryRemoveDirectory(leaf) ? PalError.SUCCESS : PalError.EIO;
    }

    internal static PalError Rename(string oldPath, string newPath)
    {
        string? oldFull = MakeAbsolute(oldPath);
        string? newFull = MakeAbsolute(newPath);
        if (oldFull == null || newFull == null)
        {
            return PalError.ENOENT;
        }

        if (string.Equals(oldFull, newFull, StringComparison.Ordinal))
        {
            return PalError.SUCCESS;
        }

        if (IsRootOrMountPoint(oldFull) || IsRootOrMountPoint(newFull))
        {
            return PalError.EBUSY;
        }

        if (!TryStatNode(oldFull, out VfsStat oldStat))
        {
            return PalError.ENOENT;
        }

        bool oldIsDirectory = (oldStat.Mode & ModeEnum.FileTypeMask) == ModeEnum.Directory;
        if (oldIsDirectory && newFull.StartsWith(oldFull + "/", StringComparison.Ordinal))
        {
            return PalError.EINVAL;
        }

        VfsManager.VfsMount? oldMount = FindMount(oldFull, out _);
        VfsManager.VfsMount? newMount = FindMount(newFull, out _);
        if (newMount == null)
        {
            return PalError.ENOENT;
        }

        if (!ReferenceEquals(oldMount, newMount))
        {
            return PalError.EXDEV;
        }

        SplitParentLeaf(oldFull, out string oldParentPath, out string oldLeaf);
        SplitParentLeaf(newFull, out string newParentPath, out string newLeaf);

        if (!VfsManager.TryOpenDirectory(oldParentPath, out IVfsDirectoryHandle? oldParent) || oldParent == null
            || !VfsManager.TryOpenDirectory(newParentPath, out IVfsDirectoryHandle? newParent) || newParent == null)
        {
            return PalError.ENOENT;
        }

        bool destinationExists = TryStatNode(newFull, out VfsStat newStat);

        // FAT lookups are case-insensitive, so the destination stat can
        // resolve to the SOURCE entry itself (case-only rename). Dropping it
        // would destroy the file — detect the case and hand the pair
        // straight to the driver. Raw driver Ino (first cluster) identifies
        // every non-empty node; the path comparison covers empty files.
        bool sameEntry = destinationExists
            && ((oldStat.Ino != 0 && newStat.Ino == oldStat.Ino)
                || string.Equals(oldFull, newFull, StringComparison.OrdinalIgnoreCase));

        if (destinationExists && !sameEntry)
        {
            // POSIX rename replaces the destination, and must leave it
            // intact when the rename fails; the FAT driver refuses existing
            // destinations, so move it aside first and only discard it once
            // the real rename has succeeded.
            bool newIsDirectory = (newStat.Mode & ModeEnum.FileTypeMask) == ModeEnum.Directory;
            if (oldIsDirectory && !newIsDirectory)
            {
                return PalError.ENOTDIR;
            }

            if (!oldIsDirectory && newIsDirectory)
            {
                return PalError.EISDIR;
            }

            if (newIsDirectory
                && VfsManager.TryOpenDirectory(newFull, out IVfsDirectoryHandle? target) && target != null
                && target.TryReadDir(out IReadOnlyList<IVfsInode> entries) && entries.Count > 0)
            {
                return PalError.ENOTEMPTY;
            }

            string backupLeaf = newLeaf + ReplaceBackupSuffix;
            if (!newParent.TryRename(newLeaf, newParent, backupLeaf))
            {
                return PalError.EIO;
            }

            if (!oldParent.TryRename(oldLeaf, newParent, newLeaf))
            {
                // Best-effort restore; the destination survives the failure.
                newParent.TryRename(backupLeaf, newParent, newLeaf);
                return PalError.EIO;
            }

            DropDisplacedEntry(newParent, newParentPath, backupLeaf, newIsDirectory, newFull);
            return PalError.SUCCESS;
        }

        return oldParent.TryRename(oldLeaf, newParent, newLeaf) ? PalError.SUCCESS : PalError.EIO;
    }

    internal static PalError SetPathMode(string path, int mode)
    {
        string? fullPath = MakeAbsolute(path);
        if (fullPath == null)
        {
            return PalError.ENOENT;
        }

        if (fullPath == "/")
        {
            return PalError.EPERM;
        }

        if (!VfsManager.TryOpenDirectory(fullPath, out IVfsDirectoryHandle? node) || node == null)
        {
            return PalError.ENOENT;
        }

        return ApplyMode(node.Inode, mode);
    }

    internal static PalError SetCurrentDirectory(string path)
    {
        string? fullPath = MakeAbsolute(path);
        if (fullPath == null)
        {
            return PalError.ENOENT;
        }

        if (!TryStatNode(fullPath, out VfsStat stat))
        {
            return PalError.ENOENT;
        }

        if ((stat.Mode & ModeEnum.FileTypeMask) != ModeEnum.Directory)
        {
            return PalError.ENOTDIR;
        }

        s_currentDirectory = fullPath;
        return PalError.SUCCESS;
    }

    // ---------------- directory streams ----------------

    internal static PalError OpenDirectoryStream(string path, out IntPtr handle)
    {
        handle = IntPtr.Zero;

        string? fullPath = MakeAbsolute(path);
        if (fullPath == null)
        {
            return PalError.ENOENT;
        }

        string[] names;
        bool[] isDirectory;
        if (fullPath == "/")
        {
            CollectRootEntries(out names, out isDirectory);
        }
        else
        {
            if (!TryStatNode(fullPath, out VfsStat stat))
            {
                return PalError.ENOENT;
            }

            if ((stat.Mode & ModeEnum.FileTypeMask) != ModeEnum.Directory)
            {
                return PalError.ENOTDIR;
            }

            if (!VfsManager.TryOpenDirectory(fullPath, out IVfsDirectoryHandle? directory) || directory == null
                || !directory.TryReadDir(out IReadOnlyList<IVfsInode> entries))
            {
                return PalError.EIO;
            }

            names = new string[entries.Count];
            isDirectory = new bool[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                IVfsInode entry = entries[i];
                names[i] = entry.Name;
                bool directoryEntry = entry.FileOperations == null;
                if (entry.InodeOperations != null && entry.InodeOperations.GetAttr(entry, out VfsStat entryStat))
                {
                    directoryEntry = (entryStat.Mode & ModeEnum.FileTypeMask) == ModeEnum.Directory;
                }

                isDirectory[i] = directoryEntry;
            }
        }

        int slot = FindFreeSlot(s_directoryStreams);
        if (slot < 0)
        {
            return PalError.EMFILE;
        }

        byte* nameBuffer = (byte*)NativeMemory.Alloc(DirectoryNameBufferBytes);
        s_directoryStreams[slot] = new DirectoryStreamState(names, isDirectory, nameBuffer);
        handle = new IntPtr(slot + 1);
        return PalError.SUCCESS;
    }

    /// <summary>Implements the raw <c>SystemNative_ReadDir</c> protocol:
    /// 0 = entry produced, -1 = end of stream, positive = error code.</summary>
    internal static int ReadDirectoryStream(IntPtr handle, PalSys.DirectoryEntry* entry)
    {
        DirectoryStreamState? stream = GetDirectoryStream(handle);
        if (stream == null)
        {
            return (int)PalError.EBADF;
        }

        if (stream.Index >= stream.Names.Length)
        {
            *entry = default;
            return -1;
        }

        int index = stream.Index;
        stream.Index = index + 1;

        int length = EncodeUtf8(stream.Names[index], stream.NameBuffer, DirectoryNameBufferBytes - 1);
        stream.NameBuffer[length] = 0;

        entry->Name = stream.NameBuffer;
        entry->NameLength = length;
        entry->InodeType = stream.IsDirectory[index] ? PalSys.NodeType.DT_DIR : PalSys.NodeType.DT_REG;
        return 0;
    }

    internal static PalError CloseDirectoryStream(IntPtr handle)
    {
        DirectoryStreamState? stream = GetDirectoryStream(handle);
        if (stream == null)
        {
            return PalError.EBADF;
        }

        NativeMemory.Free(stream.NameBuffer);
        s_directoryStreams[(int)handle - 1] = null;
        return PalError.SUCCESS;
    }

    // ---------------- internals ----------------

    private static OpenFileState? GetOpenFile(int fd)
    {
        int slot = fd - FirstFileDescriptor;
        if (slot < 0 || slot >= MaxOpenFiles)
        {
            return null;
        }

        return s_openFiles[slot];
    }

    private static DirectoryStreamState? GetDirectoryStream(IntPtr handle)
    {
        int slot = (int)handle - 1;
        if (slot < 0 || slot >= MaxDirectoryStreams)
        {
            return null;
        }

        return s_directoryStreams[slot];
    }

    private static int FindFreeSlot<T>(T?[] table) where T : class
    {
        for (int i = 0; i < table.Length; i++)
        {
            if (table[i] == null)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>When any live descriptor references the node at <paramref name="fullPath"/>,
    /// marks those descriptors delete-pending and returns true (the caller
    /// reports success without touching the filesystem yet).</summary>
    private static bool MarkPendingIfOpen(string fullPath)
    {
        IVfsInode? inode = null;
        if (VfsManager.TryOpenDirectory(fullPath, out IVfsDirectoryHandle? node) && node != null)
        {
            inode = node.Inode;
        }

        bool any = false;
        for (int i = 0; i < s_openFiles.Length; i++)
        {
            OpenFileState? state = s_openFiles[i];
            if (state == null)
            {
                continue;
            }

            // Empty FAT files are not in the driver's inode cache, so the
            // resolved inode can be a fresh object — the path comparison is
            // the reliable fallback there.
            if (string.Equals(state.Path, fullPath, StringComparison.OrdinalIgnoreCase)
                || (inode != null && ReferenceEquals(state.Handle.Inode, inode)))
            {
                state.PendingUnlinkPath = fullPath;
                any = true;
            }
        }

        return any;
    }

    private static bool AnyDescriptorPendingOn(string fullPath)
    {
        for (int i = 0; i < s_openFiles.Length; i++)
        {
            OpenFileState? state = s_openFiles[i];
            if (state != null && string.Equals(state.PendingUnlinkPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Removes a directory entry without the public-path guards; used for
    /// deferred (delete-pending) removals where all checks already ran.</summary>
    private static void RemoveEntryDirect(string fullPath)
    {
        SplitParentLeaf(fullPath, out string parentPath, out string leaf);
        if (VfsManager.TryOpenDirectory(parentPath, out IVfsDirectoryHandle? parent) && parent != null)
        {
            parent.TryUnlink(leaf);
        }
    }

    /// <summary>Discards the destination entry a successful replacing rename moved
    /// aside; descriptors still open on it go delete-pending instead.</summary>
    private static void DropDisplacedEntry(
        IVfsDirectoryHandle parent, string parentPath, string backupLeaf, bool isDirectory, string originalFullPath)
    {
        if (isDirectory)
        {
            // Verified empty before the rename; a failure here only leaks a
            // stray entry, the rename itself already succeeded.
            parent.TryRemoveDirectory(backupLeaf);
            return;
        }

        string backupPath = parentPath == "/" ? "/" + backupLeaf : parentPath + "/" + backupLeaf;

        IVfsInode? backupInode = null;
        if (parent.TryLookup(backupLeaf, out IVfsNodeHandle? backupNode) && backupNode != null)
        {
            backupInode = backupNode.Inode;
        }

        bool pending = false;
        for (int i = 0; i < s_openFiles.Length; i++)
        {
            OpenFileState? state = s_openFiles[i];
            if (state == null)
            {
                continue;
            }

            if (string.Equals(state.Path, originalFullPath, StringComparison.OrdinalIgnoreCase)
                || (backupInode != null && ReferenceEquals(state.Handle.Inode, backupInode)))
            {
                state.PendingUnlinkPath = backupPath;
                pending = true;
            }
        }

        if (!pending)
        {
            parent.TryUnlink(backupLeaf);
        }
    }

    private static PalError CreateRegularFile(string fullPath, int mode)
    {
        SplitParentLeaf(fullPath, out string parentPath, out string leaf);
        if (leaf.Length == 0)
        {
            return PalError.EINVAL;
        }

        if (!TryStatNode(parentPath, out VfsStat parentStat))
        {
            return PalError.ENOENT;
        }

        if ((parentStat.Mode & ModeEnum.FileTypeMask) != ModeEnum.Directory)
        {
            return PalError.ENOTDIR;
        }

        if (!VfsManager.TryOpenDirectory(parentPath, out IVfsDirectoryHandle? parent) || parent == null)
        {
            return PalError.EROFS;
        }

        ModeEnum createMode = PermissionBits(mode) | ModeEnum.RegularFile;
        return parent.TryCreateFile(leaf, createMode, out _) ? PalError.SUCCESS : PalError.EIO;
    }

    private static bool TryStatNode(string fullPath, out VfsStat stat)
    {
        if (fullPath == "/")
        {
            stat = VirtualRootStat();
            return true;
        }

        stat = default;
        if (!VfsManager.TryOpenDirectory(fullPath, out IVfsDirectoryHandle? node) || node == null)
        {
            return false;
        }

        return node.TryStat(out stat);
    }

    private static VfsStat VirtualRootStat()
    {
        VfsStat stat = default;
        stat.Mode = ModeEnum.Directory
            | ModeEnum.OwnerRead | ModeEnum.OwnerWrite | ModeEnum.OwnerExecute
            | ModeEnum.GroupRead | ModeEnum.GroupExecute
            | ModeEnum.OtherRead | ModeEnum.OtherExecute;
        stat.NLink = 1;
        return stat;
    }

    private static bool SetSize(IVfsInode inode, ulong size)
    {
        VfsStat attributes = default;
        attributes.Size = size;
        return inode.InodeOperations != null
            && inode.InodeOperations.SetAttr(inode, SetAttrFlags.Size, in attributes);
    }

    private static PalError ApplyMode(IVfsInode inode, int mode)
    {
        if (inode.InodeOperations == null || !inode.InodeOperations.GetAttr(inode, out VfsStat current))
        {
            return PalError.EIO;
        }

        VfsStat attributes = default;
        attributes.Mode = PermissionBits(mode) | (current.Mode & ModeEnum.FileTypeMask);
        return inode.InodeOperations.SetAttr(inode, SetAttrFlags.Mode, in attributes)
            ? PalError.SUCCESS
            : PalError.EPERM;
    }

    private static ModeEnum PermissionBits(int mode) => (ModeEnum)mode & ModeEnum.PermissionMask;

    private static void FillStatus(string fullPath, in VfsStat stat, out PalSys.FileStatus status)
    {
        status = default;
        status.Flags = PalSys.FileStatusFlags.None;
        status.Mode = (int)stat.Mode;
        status.Uid = stat.Uid;
        status.Gid = stat.Gid;
        status.Size = (long)stat.Size;
        status.ATime = stat.Atime.TvSec;
        status.ATimeNsec = stat.Atime.TvNsec;
        status.MTime = stat.Mtime.TvSec;
        status.MTimeNsec = stat.Mtime.TvNsec;
        status.CTime = stat.Ctime.TvSec;
        status.CTimeNsec = stat.Ctime.TvNsec;
        status.BirthTime = 0;
        status.BirthTimeNsec = 0;
        FindMount(fullPath, out int mountOrdinal);
        status.Dev = mountOrdinal;
        status.RDev = 0;
        // FAT reports Ino 0 for empty files and the fixed FAT12/16 root; the
        // BCL compares Dev+Ino to detect "same file" (File.Move), so synthesize
        // distinct, stable inode numbers from the path for those.
        status.Ino = stat.Ino != 0 ? (long)stat.Ino : SyntheticInode(fullPath);
        status.UserFlags = 0;
    }

    private static long SyntheticInode(string fullPath)
    {
        // FNV-1a; bit 62 keeps synthetic numbers clear of real cluster numbers.
        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < fullPath.Length; i++)
        {
            hash ^= fullPath[i];
            hash *= 1099511628211UL;
        }

        return (long)((hash & 0x3FFFFFFFFFFFFFFFUL) | 0x4000000000000000UL);
    }

    private static string? MakeAbsolute(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        string result = path[0] == '/'
            ? path
            : (s_currentDirectory == "/" ? "/" + path : s_currentDirectory + "/" + path);

        int end = result.Length;
        while (end > 1 && result[end - 1] == '/')
        {
            end--;
        }

        return end == result.Length ? result : result.Substring(0, end);
    }

    private static void SplitParentLeaf(string fullPath, out string parentPath, out string leaf)
    {
        int lastSeparator = fullPath.LastIndexOf('/');
        if (lastSeparator <= 0)
        {
            parentPath = "/";
            leaf = fullPath.Length > 1 ? fullPath.Substring(1) : string.Empty;
            return;
        }

        parentPath = fullPath.Substring(0, lastSeparator);
        leaf = fullPath.Substring(lastSeparator + 1);
    }

    private static bool IsRootOrMountPoint(string fullPath)
    {
        if (fullPath == "/")
        {
            return true;
        }

        IReadOnlyList<VfsManager.VfsMount> mounts = VfsManager.Mounts;
        for (int i = 0; i < mounts.Count; i++)
        {
            if (string.Equals(mounts[i].MountPoint, fullPath, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static VfsManager.VfsMount? FindMount(string fullPath, out int ordinal)
    {
        VfsManager.VfsMount? best = null;
        ordinal = 0;

        IReadOnlyList<VfsManager.VfsMount> mounts = VfsManager.Mounts;
        for (int i = 0; i < mounts.Count; i++)
        {
            VfsManager.VfsMount candidate = mounts[i];
            if (VfsManager.MountCovers(candidate.MountPoint, fullPath)
                && (best == null || candidate.MountPoint.Length > best.MountPoint.Length))
            {
                best = candidate;
                ordinal = i + 1;
            }
        }

        return best;
    }

    private static void CollectRootEntries(out string[] names, out bool[] isDirectory)
    {
        IReadOnlyList<VfsManager.VfsMount> mounts = VfsManager.Mounts;
        List<string> collected = new List<string>(mounts.Count);
        for (int i = 0; i < mounts.Count; i++)
        {
            string mountPoint = mounts[i].MountPoint;
            if (mountPoint == "/")
            {
                continue;
            }

            int nextSeparator = mountPoint.IndexOf('/', 1);
            string firstSegment = nextSeparator < 0
                ? mountPoint.Substring(1)
                : mountPoint.Substring(1, nextSeparator - 1);

            if (!collected.Contains(firstSegment))
            {
                collected.Add(firstSegment);
            }
        }

        names = collected.ToArray();
        isDirectory = new bool[names.Length];
        for (int i = 0; i < isDirectory.Length; i++)
        {
            isDirectory[i] = true;
        }
    }

    private static int EncodeUtf8(string value, byte* destination, int capacity)
    {
        int offset = 0;
        for (int i = 0; i < value.Length; i++)
        {
            int codePoint = value[i];
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                codePoint = char.ConvertToUtf32(value[i], value[i + 1]);
                i++;
            }

            if (codePoint < 0x80)
            {
                if (offset + 1 > capacity)
                {
                    break;
                }

                destination[offset++] = (byte)codePoint;
            }
            else if (codePoint < 0x800)
            {
                if (offset + 2 > capacity)
                {
                    break;
                }

                destination[offset++] = (byte)(0xC0 | (codePoint >> 6));
                destination[offset++] = (byte)(0x80 | (codePoint & 0x3F));
            }
            else if (codePoint < 0x10000)
            {
                if (offset + 3 > capacity)
                {
                    break;
                }

                destination[offset++] = (byte)(0xE0 | (codePoint >> 12));
                destination[offset++] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
                destination[offset++] = (byte)(0x80 | (codePoint & 0x3F));
            }
            else
            {
                if (offset + 4 > capacity)
                {
                    break;
                }

                destination[offset++] = (byte)(0xF0 | (codePoint >> 18));
                destination[offset++] = (byte)(0x80 | ((codePoint >> 12) & 0x3F));
                destination[offset++] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
                destination[offset++] = (byte)(0x80 | (codePoint & 0x3F));
            }
        }

        return offset;
    }
}
