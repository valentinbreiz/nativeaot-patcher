// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Build.Framework;

namespace Cosmos.Build.Common.Tasks;

/// <summary>
/// Computes a combined SHA256 content hash of all input files.
/// Used for content-based incremental build caching (immune to timestamp changes from dotnet publish).
/// </summary>
public sealed class ComputeInputsHashTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[] InputFiles { get; set; } = Array.Empty<ITaskItem>();

    [Output]
    public string Hash { get; set; } = string.Empty;

    public override bool Execute()
    {
        // Sort for deterministic ordering and skip optional inputs that don't exist on disk.
        string[] sortedFiles = InputFiles
            .Select(f => f.ItemSpec)
            .Where(File.Exists)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        using SHA256 sha = SHA256.Create();
        foreach (string filePath in sortedFiles)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        Hash = BitConverter.ToString(sha.Hash!).Replace("-", string.Empty).ToLowerInvariant();

        Log.LogMessage(MessageImportance.Normal, "Build cache inputs hash: {0} ({1} files)", Hash, sortedFiles.Length);
        return true;
    }
}
