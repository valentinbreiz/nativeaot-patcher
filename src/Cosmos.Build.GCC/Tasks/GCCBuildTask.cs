// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Cosmos.Build.GCC.Tasks;

public sealed class GCCBuildTask : ToolTask
{
    [Required] public string? GCCPath { get; set; }
    [Required] public string? SourceFiles { get; set; }
    [Required] public string? OutputPath { get; set; }
    [Required] public string? OutputFile { get; set; }

    // Optional additional compiler flags
    public string? CompilerFlags { get; set; }

    // ILC path for linking
    public string? IlcPath { get; set; }

    protected override MessageImportance StandardErrorLoggingImportance => MessageImportance.Normal;

    protected override string GenerateFullPathToTool() =>
            GCCPath!; protected override string GenerateCommandLineCommands()
    {
        var sb = new StringBuilder();

        // Compile to object file, not a shared library
        sb.Append(" -c ");

        // Add output flag
        sb.Append($" -o {Path.Combine(OutputPath!, OutputFile)} ");

        // Add any user-provided compiler flags
        if (!string.IsNullOrEmpty(CompilerFlags))
            sb.Append($" {CompilerFlags} ");

        // Include all source files
        string[] sourceFilePaths = Directory.GetFiles(SourceFiles!, "*.c", SearchOption.TopDirectoryOnly);
        sb.Append(string.Join(" ", sourceFilePaths));

        return sb.ToString();
    }
    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.High, "Running Cosmos.GCC Build Task...");
        Log.LogMessage(MessageImportance.High, $"Tool Path: {GCCPath}");
        Log.LogMessage(MessageImportance.High, $"Source Directory: {SourceFiles}");
        Log.LogMessage(MessageImportance.High, $"Output Path: {OutputPath}");
        Log.LogMessage(MessageImportance.High, $"Output File: {OutputFile}");

        if (!Directory.Exists(OutputPath))
        {
            Log.LogMessage(MessageImportance.Low, $"[Debug] Creating output directory: {OutputPath}");
            Directory.CreateDirectory(OutputPath!);
        }

        // Check if source directory exists and contains C files
        if (!Directory.Exists(SourceFiles))
        {
            Log.LogError($"Source directory {SourceFiles} does not exist");
            return false;
        }

        string[] sourceFilePaths = Directory.GetFiles(SourceFiles!, "*.c", SearchOption.TopDirectoryOnly);
        if (sourceFilePaths.Length == 0)
        {
            Log.LogWarning($"No C files found in directory {SourceFiles}");
            return true; // Not an error, just nothing to compile
        }

        Log.LogMessage(MessageImportance.Normal, $"Found {sourceFilePaths.Length} C files to compile");

        // Get GCC's freestanding include directory
        string? gccIncludePath = GetGCCIncludePath();
        if (gccIncludePath != null)
        {
            Log.LogMessage(MessageImportance.Normal, $"Using GCC include path: {gccIncludePath}");
        }

        // Execute the GCC command for each C file
        using SHA1? hasher = SHA1.Create();

        foreach (string file in sourceFilePaths)
        {
            // Compute hash of file contents for a deterministic output filename
            using FileStream stream = File.OpenRead(file);
            byte[] fileHash = hasher.ComputeHash(stream);
            string fileHashString = BitConverter.ToString(fileHash).Replace("-", "").ToLower();

            // Set file-specific output name
            string baseName = Path.GetFileNameWithoutExtension(file);
            // Produce Windows-friendly .obj extension so the linker (which currently searches for *.obj) can pick them up
            string objExt = Path.DirectorySeparatorChar == '\\' ? ".obj" : ".o";
            string outputName = $"{baseName}-{fileHashString.Substring(0, 8)}{objExt}";
            string outputPath = Path.Combine(OutputPath!, outputName);

            // Build and execute the command for this file
            StringBuilder sb = new();
            sb.Append(" -c ");  // Compile to object file
            sb.Append($" -o {outputPath} ");

            // Add any user-provided compiler flags
            if (!string.IsNullOrEmpty(CompilerFlags))
            {
                sb.Append($" {CompilerFlags} ");
            }

            // Add GCC's freestanding include directory for standard headers (stdint.h, stddef.h, etc.)
            if (gccIncludePath != null)
            {
                sb.Append($" -I{gccIncludePath} ");
            }

            // Add source directory as include path for local header files
            sb.Append($" -I{SourceFiles} ");

            // Add the source file
            sb.Append($" {file} ");
            // Execute GCC for this file
            string commandLineArguments = sb.ToString();
            Log.LogMessage(MessageImportance.Normal, $"Compiling {file} with args: {commandLineArguments}");

            // Validate GCC path exists
            string toolPath = GenerateFullPathToTool().Trim();
            GCCPath = toolPath; // normalize for downstream checks

            if (!File.Exists(toolPath) && !TestGCCInPath())
            {
                Log.LogError($"GCC not found at {toolPath}. Ensure the cross-compiler is installed and on PATH.");
                return false;
            }

            if (!ExecuteCommand(toolPath, commandLineArguments))
            {
                Log.LogError($"Failed to compile {file}");
                Log.LogError($"Command: {GenerateFullPathToTool()} {commandLineArguments}");
                return false;
            }
        }

        Log.LogMessage(MessageImportance.High, "✅ GCCBuildTask completed successfully.");
        return true;
    }

    protected override string ToolName => "Cosmos.GCC";

    private string? GetGCCIncludePath()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = GCCPath,
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

    private bool TestGCCInPath()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = GCCPath,
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
        // Create process start info
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = toolPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // Start the process
        using var process = new System.Diagnostics.Process();
        process.StartInfo = psi;
        process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log.LogMessage(MessageImportance.Normal, e.Data);
                };
        process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log.LogError(e.Data);
                };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return process.ExitCode == 0;
    }
}
