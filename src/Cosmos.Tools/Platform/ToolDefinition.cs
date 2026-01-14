namespace Cosmos.Tools.Platform;

public class ToolDefinition
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string[] Commands { get; init; }
    public string? VersionArg { get; init; } = "--version";
    public bool Required { get; init; } = true;
    public string[]? Architectures { get; init; } // null = all architectures
    public bool IsCrossCompiler { get; init; } = false; // Only needed when cross-compiling

    public InstallInfo? WindowsInstall { get; init; }
    public InstallInfo? LinuxInstall { get; init; }
    public InstallInfo? MacOSInstall { get; init; }

    public InstallInfo? GetInstallInfo(OSPlatform platform) => platform switch
    {
        OSPlatform.Windows => WindowsInstall,
        OSPlatform.Linux => LinuxInstall,
        OSPlatform.MacOS => MacOSInstall,
        _ => null
    };
}

public class InstallInfo
{
    public required string Method { get; init; } // "package", "download", "build", "manual"
    public string? PackageName { get; init; }
    public string? DownloadUrl { get; init; }
    public string? BuildScript { get; init; }
    public string? ManualInstructions { get; init; }
    public string[]? AptPackages { get; init; }
    public string[]? DnfPackages { get; init; }
    public string[]? PacmanPackages { get; init; }
    public string[]? BrewPackages { get; init; }
    public string[]? ChocoPackages { get; init; }
}

public static class ToolDefinitions
{
    public static readonly ToolDefinition DotNetSdk = new()
    {
        Name = "dotnet",
        DisplayName = ".NET SDK",
        Description = ".NET 10.0 SDK for building Cosmos kernels",
        Commands = ["dotnet"],
        VersionArg = "--version",
        Required = true,
        WindowsInstall = new() { Method = "manual", ManualInstructions = "Download from https://dot.net/download" },
        LinuxInstall = new() { Method = "manual", ManualInstructions = "Download from https://dot.net/download or use package manager" },
        MacOSInstall = new() { Method = "package", BrewPackages = ["dotnet-sdk"] }
    };

    public static readonly ToolDefinition LLD = new()
    {
        Name = "ld.lld",
        DisplayName = "LLD Linker",
        Description = "LLVM linker for linking kernel binaries",
        Commands = ["ld.lld", "lld"],
        VersionArg = "--version",
        Required = true,
        WindowsInstall = new() { Method = "package", ChocoPackages = ["llvm"] },
        LinuxInstall = new() { Method = "package", AptPackages = ["lld"], DnfPackages = ["lld"], PacmanPackages = ["lld"] },
        MacOSInstall = new() { Method = "package", BrewPackages = ["llvm"] }
    };

    public static readonly ToolDefinition Xorriso = new()
    {
        Name = "xorriso",
        DisplayName = "xorriso",
        Description = "ISO creation tool for bootable kernel images",
        Commands = ["xorriso"],
        VersionArg = "--version",
        Required = true,
        WindowsInstall = new() { Method = "download", DownloadUrl = "https://github.com/AzureianGH/cosmos-toolchain/releases" },
        LinuxInstall = new() { Method = "package", AptPackages = ["xorriso"], DnfPackages = ["xorriso"], PacmanPackages = ["libisoburn"] },
        MacOSInstall = new() { Method = "package", BrewPackages = ["xorriso"] }
    };

    public static readonly ToolDefinition Yasm = new()
    {
        Name = "yasm",
        DisplayName = "Yasm Assembler",
        Description = "x64 assembler for native code",
        Commands = ["yasm"],
        VersionArg = "--version",
        Required = true,
        Architectures = ["x64"],
        WindowsInstall = new() { Method = "package", ChocoPackages = ["yasm"] },
        LinuxInstall = new() { Method = "package", AptPackages = ["yasm"], DnfPackages = ["yasm"], PacmanPackages = ["yasm"] },
        MacOSInstall = new() { Method = "package", BrewPackages = ["yasm"] }
    };

    public static readonly ToolDefinition X64ElfGcc = new()
    {
        Name = "x86_64-elf-gcc",
        DisplayName = "x64 Cross Compiler",
        Description = "GCC cross-compiler for x64 bare-metal targets",
        Commands = ["x86_64-elf-gcc"],
        VersionArg = "--version",
        Required = true,
        Architectures = ["x64"],
        IsCrossCompiler = true,
        WindowsInstall = new() { Method = "download", DownloadUrl = "https://github.com/lordmilko/i686-elf-tools/releases/download/13.2.0/x86_64-elf-tools-windows.zip" },
        LinuxInstall = new() { Method = "download", DownloadUrl = "https://github.com/AzureianGH/cosmos-toolchain/releases", ManualInstructions = "Download and extract to ~/.cosmos/tools/" },
        MacOSInstall = new() { Method = "package", BrewPackages = ["x86_64-elf-gcc"] }
    };

    public static readonly ToolDefinition Aarch64ElfGcc = new()
    {
        Name = "aarch64-elf-gcc",
        DisplayName = "ARM64 Cross Compiler",
        Description = "GCC cross-compiler for ARM64 bare-metal targets",
        Commands = ["aarch64-elf-gcc", "aarch64-linux-gnu-gcc"],
        VersionArg = "--version",
        Required = true,
        Architectures = ["arm64"],
        IsCrossCompiler = true,
        WindowsInstall = new() { Method = "download", DownloadUrl = "https://github.com/AzureianGH/cosmos-toolchain/releases" },
        LinuxInstall = new() { Method = "package", AptPackages = ["gcc-aarch64-linux-gnu", "binutils-aarch64-linux-gnu"], DnfPackages = ["gcc-aarch64-linux-gnu", "binutils-aarch64-linux-gnu"], PacmanPackages = ["aarch64-linux-gnu-gcc"] },
        MacOSInstall = new() { Method = "package", BrewPackages = ["aarch64-elf-gcc"] }
    };

    public static readonly ToolDefinition Aarch64ElfAs = new()
    {
        Name = "aarch64-elf-as",
        DisplayName = "ARM64 Assembler",
        Description = "GNU assembler for ARM64 architecture",
        Commands = ["aarch64-elf-as", "aarch64-linux-gnu-as"],
        VersionArg = "--version",
        Required = true,
        Architectures = ["arm64"],
        IsCrossCompiler = true,
        WindowsInstall = new() { Method = "download", DownloadUrl = "https://github.com/AzureianGH/cosmos-toolchain/releases" },
        LinuxInstall = new() { Method = "package", AptPackages = ["binutils-aarch64-linux-gnu"], DnfPackages = ["binutils-aarch64-linux-gnu"], PacmanPackages = ["aarch64-linux-gnu-binutils"] },
        MacOSInstall = new() { Method = "package", BrewPackages = ["aarch64-elf-binutils"] }
    };

    public static readonly ToolDefinition QemuX64 = new()
    {
        Name = "qemu-system-x86_64",
        DisplayName = "QEMU x64",
        Description = "x64 system emulator for testing kernels",
        Commands = ["qemu-system-x86_64"],
        VersionArg = "--version",
        Required = false,
        Architectures = ["x64"],
        WindowsInstall = new() { Method = "package", ChocoPackages = ["qemu"] },
        LinuxInstall = new() { Method = "package", AptPackages = ["qemu-system-x86"], DnfPackages = ["qemu-system-x86"], PacmanPackages = ["qemu-system-x86"] },
        MacOSInstall = new() { Method = "package", BrewPackages = ["qemu"] }
    };

    public static readonly ToolDefinition QemuArm64 = new()
    {
        Name = "qemu-system-aarch64",
        DisplayName = "QEMU ARM64",
        Description = "ARM64 system emulator for testing kernels",
        Commands = ["qemu-system-aarch64"],
        VersionArg = "--version",
        Required = false,
        Architectures = ["arm64"],
        WindowsInstall = new() { Method = "package", ChocoPackages = ["qemu"] },
        LinuxInstall = new() { Method = "package", AptPackages = ["qemu-system-arm"], DnfPackages = ["qemu-system-arm"], PacmanPackages = ["qemu-system-aarch64"] },
        MacOSInstall = new() { Method = "package", BrewPackages = ["qemu"] }
    };

    public static readonly ToolDefinition QemuEfiArm64 = new()
    {
        Name = "QEMU EFI (ARM64)",
        DisplayName = "QEMU UEFI Firmware",
        Description = "UEFI firmware for ARM64 QEMU",
        Commands = ["ls"],  // Check file existence instead
        VersionArg = null,
        Required = false,
        Architectures = ["arm64"],
        LinuxInstall = new() { Method = "package", AptPackages = ["qemu-efi-aarch64"], DnfPackages = ["edk2-aarch64"], PacmanPackages = ["edk2-aarch64"] }
    };

    public static IEnumerable<ToolDefinition> GetAllTools() =>
    [
        DotNetSdk,
        LLD,
        Xorriso,
        Yasm,
        X64ElfGcc,
        Aarch64ElfGcc,
        Aarch64ElfAs,
        QemuX64,
        QemuArm64,
        QemuEfiArm64
    ];

    public static IEnumerable<ToolDefinition> GetToolsForArchitecture(string? arch)
    {
        // Map host architecture to our naming convention
        string hostArch = PlatformInfo.IsX64 ? "x64" : PlatformInfo.IsArm64 ? "arm64" : "unknown";

        foreach (var tool in GetAllTools())
        {
            // If it's a cross-compiler and we're targeting the host architecture, skip it
            // (no cross-compiler needed when building for native arch)
            if (tool.IsCrossCompiler && tool.Architectures != null)
            {
                // When arch is null (checking all), only show cross-compilers for non-host architectures
                if (arch == null)
                {
                    // Skip cross-compilers that target the host architecture
                    if (tool.Architectures.Any(a => string.Equals(a, hostArch, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                }
                // When targeting a specific arch, skip cross-compiler if it matches host
                else if (string.Equals(arch, hostArch, StringComparison.OrdinalIgnoreCase) &&
                         tool.Architectures.Contains(arch, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            // Standard architecture filtering
            if (tool.Architectures == null || arch == null)
            {
                yield return tool;
            }
            else if (tool.Architectures.Contains(arch, StringComparer.OrdinalIgnoreCase))
            {
                yield return tool;
            }
        }
    }
}
