// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;

// Mirror declarations of System.Private.CoreLib's private "Interop" tree.
//
// The patcher matches plug-method parameters against target parameters by
// exact Cecil FullName string ("Interop/Sys/OpenFlags", "Interop/Sys/FileStatus&",
// ...), so these types must live in the GLOBAL namespace, nested exactly like
// CoreLib's, with identical names. No unification happens at patch time: the
// transplanted IL keeps referencing these mirror types while the values they
// operate on are CoreLib's — correctness therefore depends on every struct
// below replicating CoreLib's field list, order and underlying types exactly
// (same mechanism as Internal.Runtime.TypeManagerHandle in Cosmos.Kernel.Core).
//
// Sources of truth: dotnet/runtime src/libraries/Common/src/Interop/Unix/
// (Interop.Errors.cs, System.Native/Interop.*.cs) and pal_io.h — values are
// the PAL's platform-agnostic constants, not any OS's native numbers.
internal static class Interop
{
    /// <summary>Platform-agnostic PAL error codes (CoreLib's <c>Interop.Error</c>).</summary>
    internal enum Error
    {
        SUCCESS = 0,

        E2BIG = 0x10001,
        EACCES = 0x10002,
        EAGAIN = 0x10006,
        EBADF = 0x10008,
        EBUSY = 0x1000A,
        ECANCELED = 0x1000B,
        EEXIST = 0x10014,
        EFBIG = 0x10016,
        EINTR = 0x1001B,
        EINVAL = 0x1001C,
        EIO = 0x1001D,
        EISDIR = 0x1001F,
        ELOOP = 0x10020,
        EMFILE = 0x10021,
        EMLINK = 0x10022,
        ENAMETOOLONG = 0x10025,
        ENFILE = 0x10029,
        ENODEV = 0x1002C,
        ENOENT = 0x1002D,
        ENOMEM = 0x10031,
        ENOSPC = 0x10034,
        ENOSYS = 0x10037,
        ENOTDIR = 0x10039,
        ENOTEMPTY = 0x1003A,
        ENOTSUP = 0x1003D,
        ENXIO = 0x1003F,
        EOVERFLOW = 0x10040,
        EPERM = 0x10042,
        ERANGE = 0x10047,
        EROFS = 0x10048,
        ESPIPE = 0x10049,
        EXDEV = 0x1004F,
    }

    // [Plug] (parameterless) makes the patcher skip this class when it scans
    // Cosmos.Kernel.Plugs itself as a patch target (the assembly contains a
    // type named "Interop/Sys", so it matches the plug group's TargetName);
    // nested classes are never scanned as plug sources, so this registers
    // nothing.
    [Plug]
    internal static class Sys
    {
        internal enum OpenFlags
        {
            // Access-mode bits (mask 0x000F).
            O_RDONLY = 0x0000,
            O_WRONLY = 0x0001,
            O_RDWR = 0x0002,

            // Operational flags.
            O_CLOEXEC = 0x0010,
            O_CREAT = 0x0020,
            O_EXCL = 0x0040,
            O_TRUNC = 0x0080,
            O_SYNC = 0x0100,
            O_NOFOLLOW = 0x0200,
        }

        internal enum SeekWhence
        {
            SEEK_SET = 0,
            SEEK_CUR = 1,
            SEEK_END = 2,
        }

        internal enum LockOperations
        {
            LOCK_SH = 1,
            LOCK_EX = 2,
            LOCK_NB = 4,
            LOCK_UN = 8,
        }

        internal enum LockType : short
        {
            F_RDLCK = 0,
            F_WRLCK = 1,
            F_UNLCK = 2,
        }

        internal enum FileAdvice
        {
            POSIX_FADV_NORMAL = 0,
            POSIX_FADV_RANDOM = 1,
            POSIX_FADV_SEQUENTIAL = 2,
            POSIX_FADV_WILLNEED = 3,
            POSIX_FADV_DONTNEED = 4,
            POSIX_FADV_NOREUSE = 5,
        }

        internal enum NodeType
        {
            DT_UNKNOWN = 0,
            DT_FIFO = 1,
            DT_CHR = 2,
            DT_DIR = 4,
            DT_BLK = 6,
            DT_REG = 8,
            DT_LNK = 10,
            DT_SOCK = 12,
            DT_WHT = 14,
        }

        internal enum FileStatusFlags
        {
            None = 0,
            HasBirthTime = 1,
        }

        /// <summary>CoreLib's <c>Interop.Sys.FileStatus</c> — 17 fields, exact order (pal_io.h).</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct FileStatus
        {
            internal FileStatusFlags Flags;
            internal int Mode;
            internal uint Uid;
            internal uint Gid;
            internal long Size;
            internal long ATime;
            internal long ATimeNsec;
            internal long MTime;
            internal long MTimeNsec;
            internal long CTime;
            internal long CTimeNsec;
            internal long BirthTime;
            internal long BirthTimeNsec;
            internal long Dev;
            internal long RDev;
            internal long Ino;
            internal uint UserFlags;
        }

        /// <summary>CoreLib's <c>Interop.Sys.DirectoryEntry</c>. <see cref="Name"/> must stay
        /// valid until the next ReadDir call on the same directory handle;
        /// <see cref="NameLength"/> of -1 means NUL-terminated.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct DirectoryEntry
        {
            internal byte* Name;
            internal int NameLength;
            internal NodeType InodeType;
        }

        /// <summary>CoreLib's <c>Interop.Sys.TimeSpec</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct TimeSpec
        {
            internal long TvSec;
            internal long TvNsec;
        }
    }
}
