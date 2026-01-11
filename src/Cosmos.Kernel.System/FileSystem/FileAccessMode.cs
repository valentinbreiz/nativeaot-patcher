// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.System.FileSystem;

/// <summary>
/// File access mode for opening files.
/// </summary>
public enum FileAccessMode
{
    /// <summary>
    /// Read-only access.
    /// </summary>
    Read,

    /// <summary>
    /// Write-only access.
    /// </summary>
    Write,

    /// <summary>
    /// Read and write access.
    /// </summary>
    ReadWrite
}

public static class FileAccessModeEx
{
    public static string AsString(this FileAccessMode mode)
    {
        return mode switch
        {
            FileAccessMode.Read => "Read",
            FileAccessMode.ReadWrite => "ReadWrite",
            FileAccessMode.Write => "Writes",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
    }
}
