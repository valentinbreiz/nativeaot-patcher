namespace Cosmos.Tools.Platform;

public abstract class ToolDefinition
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public bool Required { get; init; } = true;
}

public class CommandToolDefinition : ToolDefinition
{
    public string[]? WindowsCommands { get; init; }
    public string[]? LinuxCommands { get; init; }
    public string[]? MacOSCommands { get; init; }
    public string? VersionArg { get; init; } = "--version";

    public string[] GetCommands(OSPlatform platform) => platform switch
    {
        OSPlatform.Windows => WindowsCommands ?? [],
        OSPlatform.Linux => LinuxCommands ?? [],
        OSPlatform.MacOS => MacOSCommands ?? [],
        _ => []
    };

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

public class FileToolDefinition : ToolDefinition
{
    public string[]? WindowsPaths { get; init; }
    public string[]? LinuxPaths { get; init; }
    public string[]? MacOSPaths { get; init; }

    public string[]? GetPaths(OSPlatform platform) => platform switch
    {
        OSPlatform.Windows => WindowsPaths,
        OSPlatform.Linux => LinuxPaths,
        OSPlatform.MacOS => MacOSPaths,
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
    public static readonly CommandToolDefinition DotNetSdk = new()
    {
        Name = "dotnet",
        DisplayName = ".NET SDK",
        Description = ".NET 10.0 SDK for building Cosmos kernels",
        WindowsCommands = ["dotnet"],
        LinuxCommands = ["dotnet"],
        MacOSCommands = ["dotnet"],
        VersionArg = "--version",
        Required = true,
        WindowsInstall = new() { Method = "manual", ManualInstructions = "Download from https://dot.net/download" },
        LinuxInstall = new() { Method = "manual", ManualInstructions = "Download from https://dot.net/download or use package manager" },
        MacOSInstall = new() { Method = "package", BrewPackages = ["dotnet-sdk"] }
    };

    public static readonly CommandToolDefinition LLD = new()
    {
        Name = "ld.lld",
        DisplayName = "LLD Linker",
        Description = "LLVM linker for linking kernel binaries",
        WindowsCommands = ["ld.lld", "lld"],
        LinuxCommands = ["ld.lld", "lld"],
        MacOSCommands = ["ld.lld", "lld"],
        VersionArg = "--version",
        Required = true,
        WindowsInstall = new() { Method = "package", ChocoPackages = ["llvm"] },
        LinuxInstall = new() { Method = "package", AptPackages = ["lld"], DnfPackages = ["lld"], PacmanPackages = ["lld"] },
        MacOSInstall = new() { Method = "package", BrewPackages = ["lld"] }
    };

    public static readonly CommandToolDefinition Xorriso = new()
    {
        Name = "xorriso",
        DisplayName = "xorriso",
        Description = "ISO creation tool for bootable kernel images",
        WindowsCommands = ["xorriso"],
        LinuxCommands = ["xorriso"],
        MacOSCommands = ["xorriso"],
        VersionArg = "--version",
        Required = true,
        WindowsInstall = new() { Method = "download", DownloadUrl = "https://github.com/PeyTy/xorriso-exe-for-windows.git" },
        LinuxInstall = new() { Method = "package", AptPackages = ["xorriso"], DnfPackages = ["xorriso"], PacmanPackages = ["libisoburn"] },
        MacOSInstall = new() { Method = "package", BrewPackages = ["xorriso"] }
    };

    public static readonly CommandToolDefinition Yasm = new()
    {
        Name = "yasm",
        DisplayName = "Yasm Assembler",
        Description = "x64 assembler for native code",
        WindowsCommands = ["yasm"],
        LinuxCommands = ["yasm"],
        MacOSCommands = ["yasm"],
        VersionArg = "--version",
        Required = true,
        WindowsInstall = new() { Method = "package", ChocoPackages = ["yasm"] },
        LinuxInstall = new() { Method = "package", AptPackages = ["yasm"], DnfPackages = ["yasm"], PacmanPackages = ["yasm"] },
        MacOSInstall = new() { Method = "package", BrewPackages = ["yasm"] }
    };

    public static readonly CommandToolDefinition X64ElfGcc = new()
    {
        Name = "x86_64-elf-gcc",
        DisplayName = "x64 Cross Compiler",
        Description = "GCC cross-compiler for x64 bare-metal targets",
        WindowsCommands = ["x86_64-elf-gcc"],
        LinuxCommands = ["gcc"],
        MacOSCommands = ["x86_64-elf-gcc"],
        VersionArg = "--version",
        Required = true,
        WindowsInstall = new() { Method = "download", DownloadUrl = "https://github.com/lordmilko/i686-elf-tools/releases/download/13.2.0/x86_64-elf-tools-windows.zip" },
        LinuxInstall = new() { Method = "package", AptPackages = ["gcc"], DnfPackages = ["gcc"], PacmanPackages = ["gcc"] },
        MacOSInstall = new() { Method = "package", BrewPackages = ["x86_64-elf-gcc"] }
    };

    public static readonly CommandToolDefinition Aarch64ElfGcc = new()
    {
        Name = "aarch64-elf-gcc",
        DisplayName = "ARM64 Cross Compiler",
        Description = "GCC cross-compiler for ARM64 bare-metal targets",
        WindowsCommands = ["aarch64-none-elf-gcc"],
        LinuxCommands = ["aarch64-linux-gnu-gcc"],
        MacOSCommands = ["aarch64-elf-gcc"],
        VersionArg = "--version",
        Required = true,
        WindowsInstall = new() { Method = "download", DownloadUrl = "https://github.com/mmozeiko/build-gcc-arm/releases/download/gcc-v15.2.0/gcc-v15.2.0-aarch64-none-elf.7z" },
        LinuxInstall = new() { Method = "package", AptPackages = ["gcc-aarch64-linux-gnu", "binutils-aarch64-linux-gnu"], DnfPackages = ["gcc-aarch64-linux-gnu", "binutils-aarch64-linux-gnu"], PacmanPackages = ["aarch64-linux-gnu-gcc"] },
        MacOSInstall = new() { Method = "package", BrewPackages = ["aarch64-elf-gcc"] }
    };

    public static readonly CommandToolDefinition Aarch64ElfAs = new()
    {
        Name = "aarch64-elf-as",
        DisplayName = "ARM64 Assembler",
        Description = "GNU assembler for ARM64 architecture",
        WindowsCommands = ["aarch64-none-elf-as"],
        LinuxCommands = ["aarch64-linux-gnu-as"],
        MacOSCommands = ["aarch64-elf-as"],
        VersionArg = "--version",
        Required = true,
        WindowsInstall = new() { Method = "download", DownloadUrl = "https://github.com/mmozeiko/build-gcc-arm/releases/download/gcc-v15.2.0/gcc-v15.2.0-aarch64-none-elf.7z" },
        LinuxInstall = new() { Method = "package", AptPackages = ["binutils-aarch64-linux-gnu"], DnfPackages = ["binutils-aarch64-linux-gnu"], PacmanPackages = ["aarch64-linux-gnu-binutils"] },
        MacOSInstall = new() { Method = "package", BrewPackages = ["aarch64-elf-binutils"] }
    };

    public static readonly CommandToolDefinition QemuX64 = new()
    {
        Name = "qemu-system-x86_64",
        DisplayName = "QEMU x64",
        Description = "x64 system emulator for testing kernels",
        WindowsCommands = ["qemu-system-x86_64"],
        LinuxCommands = ["qemu-system-x86_64"],
        MacOSCommands = ["qemu-system-x86_64"],
        VersionArg = "--version",
        Required = false,
        WindowsInstall = new() { Method = "package", ChocoPackages = ["qemu"] },
        LinuxInstall = new() { Method = "package", AptPackages = ["qemu-system-x86"], DnfPackages = ["qemu-system-x86"], PacmanPackages = ["qemu-system-x86"] },
        MacOSInstall = new() { Method = "package", BrewPackages = ["qemu"] }
    };

    public static readonly CommandToolDefinition QemuArm64 = new()
    {
        Name = "qemu-system-aarch64",
        DisplayName = "QEMU ARM64",
        Description = "ARM64 system emulator for testing kernels",
        WindowsCommands = ["qemu-system-aarch64"],
        LinuxCommands = ["qemu-system-aarch64"],
        MacOSCommands = ["qemu-system-aarch64"],
        VersionArg = "--version",
        Required = false,
        WindowsInstall = new() { Method = "package", ChocoPackages = ["qemu"] },
        LinuxInstall = new() { Method = "package", AptPackages = ["qemu-system-arm", "qemu-efi-aarch64"], DnfPackages = ["qemu-system-arm", "edk2-aarch64"], PacmanPackages = ["qemu-system-aarch64", "edk2-aarch64"] },
        MacOSInstall = new() { Method = "package", BrewPackages = ["qemu"] }
    };

    public static readonly FileToolDefinition QemuEfiArm64 = new()
    {
        Name = "QEMU EFI (ARM64)",
        DisplayName = "QEMU UEFI Firmware",
        Description = "UEFI firmware for ARM64 QEMU",
        Required = false,
        WindowsPaths = [
            @"C:\Program Files\qemu\share\edk2-aarch64-code.fd",
            @"%LOCALAPPDATA%\Cosmos\Tools\qemu\share\edk2-aarch64-code.fd",
            @"C:\Program Files\qemu\share\qemu\edk2-aarch64-code.fd",
            @"%LOCALAPPDATA%\Cosmos\Tools\qemu\share\qemu\edk2-aarch64-code.fd"
        ],
        LinuxPaths = ["/usr/share/qemu-efi-aarch64/QEMU_EFI.fd"],
        MacOSPaths = ["/opt/homebrew/share/qemu/edk2-aarch64-code.fd"]
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
}
