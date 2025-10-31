// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Services.VFS.Enums;

namespace Cosmos.Kernel.Services.VFS.Interfaces;

public interface IDirectoryEntry
{

    /// <summary>
    /// the entry type
    /// </summary>
    public DirectoryEntryType Type { get; }

    /// <summary>
    /// size of the entry
    /// </summary>
    public ulong Size { get; }

    /// <summary>
    /// the path of the node
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Set entry name.
    /// </summary>
    /// <param name="name">A name to be set.</param>
    public void SetName(string name);

    /// <summary>
    /// Get used space.
    /// </summary>
    /// <returns>long value.</returns>
    public ulong GetUsedSpace();

    /// <summary>
    /// MetaData
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    ///
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value">if null then it will clear the value</param>
    public void SetMetadata(string key, string? value);

}
