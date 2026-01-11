// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.System.FileSystem;

public struct FileHandle
{
    private sealed class IdEqualityComparer : IEqualityComparer<FileHandle>
    {
        public bool Equals(FileHandle x, FileHandle y)
        {
            return x.Id == y.Id;
        }

        public int GetHashCode(FileHandle obj)
        {
            return (int)obj.Id;
        }
    }

    public static IEqualityComparer<FileHandle> IdComparer { get; } = new IdEqualityComparer();

    public uint Id { get; internal set; }
}
