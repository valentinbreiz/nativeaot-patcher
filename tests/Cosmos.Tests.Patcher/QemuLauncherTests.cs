using System;
using System.Text;
using Cosmos.Tools.Launcher;

namespace Cosmos.Tests.Patcher;

[Collection("PatcherTests")]
public class QemuLauncherTests
{
    private static QemuLaunchOptions OptionsWithDisk(string path, DiskKind kind, string extra = "")
        => new()
        {
            Architecture = "x64",
            IsoPath = "/tmp/kernel.iso",
            Disks = [new DiskAttachment { Path = path, Kind = kind, ExtraDeviceOptions = extra }]
        };

    [Fact]
    public void AppendStorageArgs_DoublesCommasInDrivePaths()
    {
        // QEMU's -drive option parser truncates file= at an unescaped comma
        // and parses the remainder as bogus drive options ("Could not open
        // '/tmp/a'"); doubled commas are its escape.
        StringBuilder args = new();
        QemuLauncher.AppendStorageArgs(args, OptionsWithDisk("/tmp/a,b.img", DiskKind.Nvme));

        Assert.Contains("file=\"/tmp/a,,b.img\"", args.ToString());
    }

    [Fact]
    public void AppendStorageArgs_RejectsQuotesInDrivePaths()
    {
        StringBuilder args = new();

        Assert.Throws<ArgumentException>(() =>
            QemuLauncher.AppendStorageArgs(args, OptionsWithDisk("/tmp/a\"b.img", DiskKind.Ahci)));
    }

    [Fact]
    public void AppendDeviceOptions_SplicesValidOptionsWithOneComma()
    {
        StringBuilder args = new();
        QemuLauncher.AppendDeviceOptions(args, "msix=off");
        Assert.Equal(",msix=off", args.ToString());

        args.Clear();
        QemuLauncher.AppendDeviceOptions(args, ",msix_qsize=1");
        Assert.Equal(",msix_qsize=1", args.ToString());
    }

    [Theory]
    [InlineData("msix=off -device e1000e")] // whitespace injects new argv tokens
    [InlineData("msix=\"off\"")]
    [InlineData("msix=off;rm")]
    public void AppendDeviceOptions_RejectsCharactersOutsideOptionAlphabet(string extra)
    {
        StringBuilder args = new();

        Assert.Throws<ArgumentException>(() => QemuLauncher.AppendDeviceOptions(args, extra));
    }

    [Fact]
    public void EscapeDriveFileValue_LeavesPlainPathsUntouched()
    {
        Assert.Equal("/tmp/plain.img", QemuLauncher.EscapeDriveFileValue("/tmp/plain.img"));
    }

    [Fact]
    public void AppendNetworkCardArgs_NoneDisablesTheDefaultNic()
    {
        StringBuilder args = new();
        QemuLauncher.AppendNetworkCardArgs(args, "none");
        Assert.Equal(" -nic none", args.ToString());
    }

    [Theory]
    [InlineData("e1000e")]
    [InlineData("virtio-net-device")]
    public void AppendNetworkCardArgs_AttachesUserModeNicForAModel(string model)
    {
        StringBuilder args = new();
        QemuLauncher.AppendNetworkCardArgs(args, model);
        Assert.Equal($" -netdev user,id=net0 -device {model},netdev=net0", args.ToString());
    }

    [Theory]
    [InlineData("e1000e -device rm")] // whitespace injects new argv tokens
    [InlineData("e1000e,netdev=x;rm")]
    public void AppendNetworkCardArgs_RejectsCharactersOutsideOptionAlphabet(string model)
    {
        StringBuilder args = new();

        Assert.Throws<ArgumentException>(() => QemuLauncher.AppendNetworkCardArgs(args, model));
    }

    [Theory]
    [InlineData("virtio-keyboard-device")]
    [InlineData("virtio-mouse-device")]
    public void AppendInputDevice_EmitsDeviceForRealModels(string model)
    {
        StringBuilder args = new();
        QemuLauncher.AppendInputDevice(args, model);
        Assert.Equal($" -device {model}", args.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("ps2")]
    [InlineData("PS2")]
    public void AppendInputDevice_AddsNothingForSentinels(string? model)
    {
        StringBuilder args = new();
        QemuLauncher.AppendInputDevice(args, model);
        Assert.Equal(string.Empty, args.ToString());
    }

    [Fact]
    public void AppendInputDevice_RejectsCharactersOutsideOptionAlphabet()
    {
        StringBuilder args = new();
        Assert.Throws<ArgumentException>(() => QemuLauncher.AppendInputDevice(args, "virtio-keyboard-device -device rm"));
    }
}
