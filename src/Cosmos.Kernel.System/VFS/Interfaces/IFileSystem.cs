// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.System.VFS.Interfaces;

public interface IFileSystem
{
    /// <summary>
    /// Get list of sub-directories in a directory.
    /// </summary>
    /// <param name="directoryEntry">A base directory.</param>
    /// <returns>DirectoryEntry list.</returns>
    /// <exception cref="ArgumentNullException">Thrown when baseDirectory is null / memory error.</exception>
    /// <exception cref="OverflowException">Thrown when data lenght is greater then Int32.MaxValue.</exception>
    /// <exception cref="Exception">Thrown when data size invalid / invalid directory entry type.</exception>
    /// <exception cref="ArgumentException">Thrown on memory error.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown on memory error.</exception>
    /// <exception cref="DecoderFallbackException">Thrown on memory error.</exception>
    public abstract List<IDirectoryEntry> GetDirectoryListing(IDirectoryEntry directoryEntry);

    /// <summary>
    /// Get root directory.
    /// </summary>
    /// <returns>DirectoryEntry value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when root directory address is smaller then root directory address.</exception>
    /// <exception cref="ArgumentNullException">Thrown when filesystem is null.</exception>
    /// <exception cref="ArgumentException">Thrown when root path is null or empty.</exception>
    public IDirectoryEntry GetRootDirectory();

    /// <summary>
    /// Create directory.
    /// </summary>
    /// <param name="directoryEntry">A parent directory.</param>
    /// <param name="aNewDirectory">A new directory name.</param>
    /// <returns>DirectoryEntry value.</returns>
    /// <exception cref="ArgumentNullException">
    /// <list type="bullet">
    /// <item>Thrown when aParentDirectory is null.</item>
    /// <item>aNewDirectory is null or empty.</item>
    /// <item>memory error.</item>
    /// </list>
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown on memory error / unknown directory entry type.</exception>
    /// <exception cref="OverflowException">Thrown when data lenght is greater then Int32.MaxValue.</exception>
    /// <exception cref="Exception">Thrown when data size invalid / invalid directory entry type / memory error.</exception>
    /// <exception cref="ArgumentException">Thrown on memory error.</exception>
    /// <exception cref="DecoderFallbackException">Thrown on memory error.</exception>
    /// <exception cref="RankException">Thrown on fatal error.</exception>
    /// <exception cref="ArrayTypeMismatchException">Thrown on fatal error.</exception>
    /// <exception cref="InvalidCastException">Thrown on memory error.</exception>
    public IDirectoryEntry CreateDirectory(IDirectoryEntry directoryEntry, string aNewDirectory);

    /// <summary>
    /// Create file.
    /// </summary>
    /// <param name="directoryEntry">A parent directory.</param>
    /// <param name="aNewFile">A new file name.</param>
    /// <returns>DirectoryEntry value.</returns>
    /// <exception cref="ArgumentNullException">
    /// <list type="bullet">
    /// <item>Thrown when aParentDirectory is null.</item>
    /// <item>aNewFile is null or empty.</item>
    /// <item>memory error.</item>
    /// </list>
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown on memory error / unknown directory entry type.</exception>
    /// <exception cref="OverflowException">Thrown when data lenght is greater then Int32.MaxValue.</exception>
    /// <exception cref="Exception">Thrown when data size invalid / invalid directory entry type / memory error.</exception>
    /// <exception cref="ArgumentException">Thrown on memory error.</exception>
    /// <exception cref="dDecoderFallbackException">Thrown on memory error.</exception>
    /// <exception cref="RankException">Thrown on fatal error.</exception>
    /// <exception cref="ArrayTypeMismatchException">Thrown on fatal error.</exception>
    /// <exception cref="InvalidCastException">Thrown on memory error.</exception>
    public IDirectoryEntry CreateFile(IDirectoryEntry directoryEntry, string aNewFile);

    /// <summary>
    /// Delete directory.
    /// </summary>
    /// <param name="directoryEntry">A directory entry to delete.</param>
    /// <exception cref="NotImplementedException">Thrown when given entry type is unknown.</exception>
    /// <exception cref="Exception">
    /// <list type="bullet">
    /// <item>Thrown when tring to delete root directory.</item>
    /// <item>directory entry type is invalid.</item>
    /// <item>data size invalid.</item>
    /// <item>FAT table not found.</item>
    /// <item>out of memory.</item>
    /// </list>
    /// </exception>
    /// <exception cref="OverflowException">Thrown when data lenght is greater then Int32.MaxValue.</exception>
    /// <exception cref="ArgumentNullException">
    /// <list type="bullet">
    /// <item>Thrown when aDirectoryEntry is null.</item>
    /// <item>aData is null.</item>
    /// <item>Out of memory.</item>
    /// </list>
    /// </exception>
    /// <exception cref="RankException">Thrown on fatal error.</exception>
    /// <exception cref="ArrayTypeMismatchException">Thrown on fatal error.</exception>
    /// <exception cref="InvalidCastException">Thrown when the data in aData is corrupted.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <list type = "bullet" >
    /// <item>Thrown when the data length is 0 or greater then Int32.MaxValue.</item>
    /// <item>Entrys matadata offset value is invalid.</item>
    /// </list>
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <list type="bullet">
    /// <item>aData length is 0.</item>
    /// </list>
    /// </exception>
    /// <exception cref="NotSupportedException">Thrown when FAT type is unknown.</exception>
    public void DeleteDirectory(IDirectoryEntry directoryEntry);

    /// <summary>
    /// Delete file.
    /// </summary>
    /// <param name="directoryEntry">A directory entry to delete.</param>
    /// <exception cref="NotImplementedException">Thrown when given entry type is unknown.</exception>
    /// <exception cref="Exception">
    /// <list type="bullet">
    /// <item>Thrown when tring to delete root directory.</item>
    /// <item>directory entry type is invalid.</item>
    /// <item>data size invalid.</item>
    /// <item>FAT table not found.</item>
    /// <item>out of memory.</item>
    /// </list>
    /// </exception>
    /// <exception cref="OverflowException">
    /// <list type="bullet">
    /// <item>Thrown when data lenght is greater then Int32.MaxValue.</item>
    /// <item>The number of clusters in the FAT entry is greater than Int32.MaxValue.</item>
    /// </list>
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <list type="bullet">
    /// <item>Thrown when aDirectoryEntry is null.</item>
    /// <item>aData is null.</item>
    /// <item>Out of memory.</item>
    /// </list>
    /// </exception>
    /// <exception cref="RankException">Thrown on fatal error.</exception>
    /// <exception cref="ArrayTypeMismatchException">Thrown on fatal error.</exception>
    /// <exception cref="InvalidCastException">Thrown when the data in aData is corrupted.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <list type = "bullet" >
    /// <item>Thrown when the data length is 0 or greater then Int32.MaxValue.</item>
    /// <item>The size of the chain is less then zero.</item>
    /// <item>Entrys matadata offset value is invalid.</item>
    /// </list>
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <list type="bullet">
    /// <item>Thrown when FAT type is unknown.</item>
    /// <item>aData length is 0.</item>
    /// </list>
    /// </exception>
    /// <exception cref="NotSupportedException">Thrown when FAT type is unknown.</exception>
    public void DeleteFile(IDirectoryEntry directoryEntry);

    /// <summary>
    ///
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public IDirectoryEntry? Get(string path);

    /// <summary>
    /// Get Partition.
    /// </summary>
    public Partition Partition { get; }

    /// <summary>
    /// Get root path.
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Get size.
    /// </summary>
    public ulong Size { get; }

    /// <summary>
    /// Get available free space.
    /// </summary>
    public ulong AvailableFreeSpace { get; }

    /// <summary>
    /// Get type.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Get label.
    /// </summary>
    public string Label { get; set; }

    public MountFlags Flags { get; set; }
}
