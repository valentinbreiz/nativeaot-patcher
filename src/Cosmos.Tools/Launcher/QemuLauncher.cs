using System.Diagnostics;
using System.Text;
using Cosmos.Tools.Platform;

namespace Cosmos.Tools.Launcher;

public sealed class QemuLaunchOptions
{
    public required string Architecture { get; init; }
    public required string IsoPath { get; init; }
    public int MemoryMb { get; init; } = 512;
    public bool Headless { get; init; }
    public bool Debug { get; init; }
    /// <summary>If null, serial goes to stdio (interactive CLI). Otherwise, to this file path (test runner).</summary>
    public string? SerialOutputFile { get; init; }
    /// <summary>Adds the test-runner port forwards (UDP 5556, TCP 5558) needed by network tests.</summary>
    public bool EnableNetworkTesting { get; init; }
    /// <summary>
    /// Raw disk images to attach as AHCI/SATA drives. The launcher adds a
    /// single <c>ich9-ahci</c> controller and one <c>ide-hd</c> per path on
    /// successive ports (<c>bus=ahci0.0</c>, <c>ahci0.1</c>, ...), so the AHCI
    /// driver enumerates all of them via one PCI scan. Honoured on both x64
    /// (q35) and ARM64 (virt).
    /// </summary>
    public IReadOnlyList<string> AhciDiskPaths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Raw disk images to attach as NVMe drives. The launcher adds one
    /// <c>nvme</c> PCIe controller per path (each with a unique <c>id</c> and
    /// <c>serial</c>) so the NVMe driver binds to every controller via PCI
    /// scan. Honoured on both x64 (q35) and ARM64 (virt).
    /// </summary>
    public IReadOnlyList<string> NvmeDiskPaths { get; init; } = Array.Empty<string>();
    /// <summary>
    /// When false (default, dev path), x64 launches with <c>-no-shutdown</c> so a guest-initiated
    /// ACPI _S5 / panic just pauses the VM and the user can inspect it. When true (test path),
    /// the flag is omitted so a working <c>Power.Shutdown()</c> actually exits QEMU and the test
    /// runner can observe a clean process exit.
    /// </summary>
    public bool AllowGuestShutdown { get; init; }
    public IReadOnlyList<string> ExtraArgs { get; init; } = Array.Empty<string>();
}

public sealed record QemuLaunchPlan(string BinaryPath, string Arguments, ToolSource Source);

/// <summary>
/// Single source of truth for QEMU command-line construction. Used by both
/// `cosmos run` and `Cosmos.TestRunner.Engine` so a tweak (e.g. dropping a
/// broken display backend) propagates to all callers.
/// </summary>
public static class QemuLauncher
{
    public static async Task<QemuLaunchPlan> BuildAsync(QemuLaunchOptions options)
    {
        CommandToolDefinition tool = options.Architecture switch
        {
            "x64" => ToolDefinitions.QemuX64,
            "arm64" => ToolDefinitions.QemuArm64,
            _ => throw new ArgumentException($"Unsupported architecture: {options.Architecture}", nameof(options))
        };

        ResolvedTool resolved = await ToolResolver.ResolveAsync(tool);
        if (resolved.Source == ToolSource.NotFound)
        {
            throw new InvalidOperationException(
                $"QEMU for {options.Architecture} not found. Run `cosmos install` to fetch the bundled toolchain, " +
                $"or install qemu-system-{(options.Architecture == "x64" ? "x86_64" : "aarch64")} system-wide.");
        }

        var args = new StringBuilder();

        // Single rule, all OSes: when QEMU is bundled, point it at the bundle's
        // share/qemu/ for BIOS/firmware lookup. The MSYS2 Windows build never
        // auto-discovers its data dir, and even the Linux/macOS build's runtime
        // search depends on the build's compile-time prefix matching the install
        // prefix — we ship a portable bundle, so neither holds. Explicit -L is
        // the only universally reliable mechanism.
        if (resolved.Source == ToolSource.Bundle)
        {
            string shareQemu = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(resolved.Path)!, "..", "share", "qemu"));
            args.Append($"-L \"{shareQemu}\" ");
        }

        if (options.Architecture == "x64")
        {
            AppendX64Args(args, options);
        }
        else
        {
            AppendArm64Args(args, options);
        }

        // Display: omit -display when a window is wanted so QEMU picks its compiled-in
        // default (SDL on Windows, GTK on Linux/macOS). Passing a backend QEMU wasn't
        // built with causes it to abort with "Display 'X' is not available".
        if (options.Headless)
        {
            args.Append(" -display none");
        }

        // Serial: file for tests (parseable log), stdio for CLI (interactive).
        if (options.SerialOutputFile is not null)
        {
            args.Append($" -serial file:\"{options.SerialOutputFile}\"");
        }
        else
        {
            args.Append(" -serial stdio");
        }

        if (options.EnableNetworkTesting)
        {
            string nic = options.Architecture == "x64" ? "e1000e" : "virtio-net-device";
            args.Append($" -netdev user,id=net0,hostfwd=udp::5556-:5556,hostfwd=tcp::5558-:5558 -device {nic},netdev=net0");
        }

        if (options.Debug)
        {
            args.Append(" -s -S");
        }

        foreach (string extra in options.ExtraArgs)
        {
            args.Append(' ');
            args.Append(extra);
        }

        return new QemuLaunchPlan(resolved.Path, args.ToString().TrimStart(), resolved.Source);
    }

    private static void AppendX64Args(StringBuilder args, QemuLaunchOptions options)
    {
        args.Append($"-M q35 -cpu max -m {options.MemoryMb}M");
        args.Append($" -cdrom \"{options.IsoPath}\"");
        args.Append(" -boot d -no-reboot");
        if (!options.AllowGuestShutdown)
        {
            args.Append(" -no-shutdown");
        }
        if (!options.Headless)
        {
            args.Append(" -vga std");
        }
        AppendStorageArgs(args, options);
    }

    private static void AppendArm64Args(StringBuilder args, QemuLaunchOptions options)
    {
        // -bios takes a bare filename when not absolute — QEMU resolves it
        // through its data dir search, which our `-L "<exe>/../share/qemu"`
        // (added above when Source==Bundle) points at the bundle's
        // edk2-aarch64-code.fd. No separate firmware-lookup logic needed.
        args.Append($"-M virt,highmem=off -cpu cortex-a72 -m {options.MemoryMb}M");
        args.Append(" -bios edk2-aarch64-code.fd");
        args.Append($" -cdrom \"{options.IsoPath}\"");
        args.Append(" -boot d -no-reboot");
        // ramfb is required for Limine framebuffer support even when headless.
        args.Append(" -device ramfb");
        AppendStorageArgs(args, options);
    }

    /// <summary>
    /// Attach AHCI/SATA + NVMe disks. AHCI disks share one <c>ich9-ahci</c>
    /// controller and consume successive ports; NVMe disks each get a
    /// dedicated <c>nvme</c> controller so the guest exercises multi-controller
    /// binding.
    /// </summary>
    private static void AppendStorageArgs(StringBuilder args, QemuLaunchOptions options)
    {
        if (options.AhciDiskPaths.Count > 0)
        {
            args.Append(" -device ich9-ahci,id=ahci0");
            for (int i = 0; i < options.AhciDiskPaths.Count; i++)
            {
                string path = options.AhciDiskPaths[i];
                args.Append($" -drive file=\"{path}\",if=none,id=ahcidisk{i},format=raw");
                args.Append($" -device ide-hd,drive=ahcidisk{i},bus=ahci0.{i}");
            }
        }
        for (int i = 0; i < options.NvmeDiskPaths.Count; i++)
        {
            string path = options.NvmeDiskPaths[i];
            args.Append($" -drive file=\"{path}\",if=none,id=nvmedisk{i},format=raw");
            args.Append($" -device nvme,id=nvme{i},drive=nvmedisk{i},serial=cosmos-nvme-{i}");
        }
    }

    public static ProcessStartInfo ToProcessStartInfo(QemuLaunchPlan plan)
    {
        return new ProcessStartInfo
        {
            FileName = plan.BinaryPath,
            Arguments = plan.Arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        };
    }
}
