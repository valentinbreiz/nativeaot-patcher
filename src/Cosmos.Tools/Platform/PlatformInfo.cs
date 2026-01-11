using System.Runtime.InteropServices;

namespace Cosmos.Tools.Platform;

public enum OSPlatform
{
    Windows,
    Linux,
    MacOS,
    Unknown
}

public static class PlatformInfo
{
    public static OSPlatform CurrentOS
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                return OSPlatform.Windows;
            if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
                return OSPlatform.Linux;
            if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                return OSPlatform.MacOS;
            return OSPlatform.Unknown;
        }
    }

    public static Architecture CurrentArch => RuntimeInformation.OSArchitecture;

    public static bool IsArm64 => CurrentArch == Architecture.Arm64;
    public static bool IsX64 => CurrentArch == Architecture.X64;

    public static string GetPackageManager()
    {
        if (CurrentOS == OSPlatform.MacOS)
            return "brew";

        if (CurrentOS == OSPlatform.Linux)
        {
            // Detect Linux distribution
            if (File.Exists("/etc/debian_version") || File.Exists("/etc/ubuntu-release"))
                return "apt";
            if (File.Exists("/etc/fedora-release") || File.Exists("/etc/redhat-release"))
                return "dnf";
            if (File.Exists("/etc/arch-release"))
                return "pacman";
        }

        if (CurrentOS == OSPlatform.Windows)
            return "choco";

        return "unknown";
    }

    public static string GetDistroName()
    {
        if (CurrentOS != OSPlatform.Linux)
            return CurrentOS.ToString();

        try
        {
            if (File.Exists("/etc/os-release"))
            {
                var lines = File.ReadAllLines("/etc/os-release");
                var nameLine = lines.FirstOrDefault(l => l.StartsWith("PRETTY_NAME="));
                if (nameLine != null)
                {
                    return nameLine.Split('=')[1].Trim('"');
                }
            }
        }
        catch { }

        return "Linux";
    }
}
