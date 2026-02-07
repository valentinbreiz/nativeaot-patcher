// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.System.FileSystem;


public readonly struct FileHandleId : IEquatable<FileHandleId>
{
    public static readonly FileHandleId Null = new FileHandleId() { Id = 0, VfsId = 0 };

    public ulong Id { get; init; }
    public ulong VfsId { get; init; }

    public bool Equals(FileHandleId other) => Id == other.Id && VfsId == other.VfsId;

    public override bool Equals(object? obj) => obj is FileHandleId other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, VfsId);
}
