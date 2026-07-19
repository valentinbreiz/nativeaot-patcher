// This code is licensed under MIT license (see LICENSE for details)
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Cosmos.Build.CC.Tasks;

public sealed class CCBuildTask : ToolTask
{
    [Required] public string? CCPath { get; set; }
    [Required] public string[]? SourceFiles { get; set; }
    [Required] public string? OutputPath { get; set; }

    // Optional additional compiler flags
    public string? CompilerFlags { get; set; }

    // ILC path for linking
    public string? IlcPath { get; set; }

    protected override MessageImportance StandardErrorLoggingImportance => MessageImportance.Normal;

    protected override string GenerateFullPathToTool() => CCPath!;

    protected override string GenerateCommandLineCommands() => string.Empty;

    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.High, "Running Cosmos.CC Build Task...");
        Log.LogMessage(MessageImportance.High, $"Tool Path: {CCPath}");
        Log.LogMessage(MessageImportance.High, $"Output Path: {OutputPath}");

        if (!Directory.Exists(OutputPath))
        {
            Log.LogMessage(MessageImportance.Low, $"[Debug] Creating output directory: {OutputPath}");
            Directory.CreateDirectory(OutputPath!);
        }

        if (SourceFiles == null || SourceFiles.Length == 0)
        {
            Log.LogMessage(MessageImportance.Normal, "No C source files to compile.");
            return true;
        }

        Log.LogMessage(MessageImportance.Normal, $"Found {SourceFiles.Length} C files to compile");

        // Get compiler's freestanding include directory
        string? includePath = GetCompilerIncludePath();
        if (includePath != null)
        {
            Log.LogMessage(MessageImportance.Normal, $"Using compiler include path: {includePath}");
        }

        // Validate compiler path exists (once, before the loop)
        string toolPath = GenerateFullPathToTool().Trim();
        CCPath = toolPath; // normalize for downstream checks

        if (!File.Exists(toolPath) && !TestCompilerInPath())
        {
            Log.LogError($"Compiler not found at {toolPath}. Ensure clang is installed and on PATH.");
            return false;
        }

        // Execute the compiler command for each C file (with incremental support via content-hash filenames)
        using SHA1? hasher = SHA1.Create();
        var validOutputFiles = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Produce Windows-friendly .obj extension so the linker (which currently searches for *.obj) can pick them up
        string objExt = Path.DirectorySeparatorChar == '\\' ? ".obj" : ".o";

        foreach (string file in SourceFiles)
        {
            // Compute hash of file contents for a deterministic output filename
            using FileStream stream = File.OpenRead(file);
            byte[] fileHash = hasher.ComputeHash(stream);
            string fileHashString = BitConverter.ToString(fileHash).Replace("-", "").ToLower();

            // Set file-specific output name (full SHA1 hex — matches AsmBuildTask's filename convention)
            string baseName = Path.GetFileNameWithoutExtension(file);
            string outputName = $"{baseName}-{fileHashString}{objExt}";
            string outputPath = Path.GetFullPath(Path.Combine(OutputPath!, outputName));

            validOutputFiles.Add(outputPath);

            // Skip if output already exists (content-hash filename = up-to-date)
            if (File.Exists(outputPath))
            {
                Log.LogMessage(MessageImportance.Normal, $"Skipping {file} (up to date: {outputName})");
                continue;
            }

            // Build and execute the command for this file
            StringBuilder sb = new();
            // Compile to object file, not a shared library
            sb.Append(" -c ");
            // Add output flag
            sb.Append($" -o \"{outputPath}\" ");

            // Add any user-provided compiler flags
            if (!string.IsNullOrEmpty(CompilerFlags))
            {
                sb.Append($" {CompilerFlags} ");
            }

            // Add compiler's freestanding include directory for standard headers (stdint.h, stddef.h, etc.)
            if (includePath != null)
            {
                sb.Append($" -I\"{includePath}\" ");
            }

            // Per-file include path: the directory containing this source file
            string fileDir = Path.GetDirectoryName(file)!;
            sb.Append($" -I\"{fileDir}\" ");

            // Add the source file
            sb.Append($" \"{file}\" ");
            string commandLineArguments = sb.ToString();
            Log.LogMessage(MessageImportance.Normal, $"Compiling {file} with args: {commandLineArguments}");

            if (!ExecuteCommand(toolPath, commandLineArguments))
            {
                Log.LogError($"Failed to compile {file}");
                Log.LogError($"Command: {GenerateFullPathToTool()} {commandLineArguments}");
                return false;
            }
        }

        // Remove orphan object files (from renamed/deleted source files)
        foreach (string existing in Directory.GetFiles(OutputPath!, "*" + objExt))
        {
            string normalizedExisting = Path.GetFullPath(existing);
            if (!validOutputFiles.Contains(normalizedExisting))
            {
                Log.LogMessage(MessageImportance.Normal, $"Removing orphan object: {Path.GetFileName(existing)}");
                File.Delete(existing);
            }
        }

        Log.LogMessage(MessageImportance.High, "CCBuildTask completed successfully.");
        return true;
    }

    protected override string ToolName => "Cosmos.CC";

    private string? GetCompilerIncludePath()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = CCPath,
                Arguments = "--print-file-name=include",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new System.Diagnostics.Process();
            process.StartInfo = psi;
            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output) && Directory.Exists(output))
            {
                return output;
            }
        }
        catch
        {
            // If we can't get the include path, continue without it
        }
        return null;
    }

    private bool TestCompilerInPath()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = CCPath,
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new System.Diagnostics.Process();
            process.StartInfo = psi;
            process.Start();
            process.WaitForExit();

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private bool ExecuteCommand(string toolPath, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = toolPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new System.Diagnostics.Process();
        process.StartInfo = psi;
        process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Log.LogMessage(MessageImportance.Normal, e.Data);
                    }
                };
        process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Log.LogError(e.Data);
                    }
                };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return process.ExitCode == 0;
    }
}
