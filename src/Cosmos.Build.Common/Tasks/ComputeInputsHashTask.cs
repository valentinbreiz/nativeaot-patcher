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
        using (SHA256 sha = SHA256.Create())
        {
            // Sort for deterministic ordering
            string[] sortedFiles = InputFiles
                .Select(f => f.ItemSpec)
                .Where(f => File.Exists(f))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (string filePath in sortedFiles)
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }

            sha.TransformFinalBlock(new byte[0], 0, 0);
            Hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLower();
        }

        Log.LogMessage(MessageImportance.Normal, "Build cache inputs hash: {0} ({1} files)", Hash, InputFiles.Length);
        return true;
    }
}
