using System;
using System.IO;
using System.Linq;
using Cosmos.TestRunner.Engine;

namespace Cosmos.Tests.Patcher;

/// <summary>
/// Covers the device axis of the QEMU test-profile catalog: the NIC and input
/// models a profile attaches, and the architecture filter that lets one suite
/// declare per-arch hardware (virtio over PCI on x64, over MMIO on arm64).
/// </summary>
[Collection("PatcherTests")]
public class TestProfileLoaderTests : IDisposable
{
    private readonly string _root;
    private readonly string _suiteDir;

    public TestProfileLoaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cosmos-profile-tests-" + Guid.NewGuid().ToString("N"));
        _suiteDir = Path.Combine(_root, "SuiteUnderTest");
        Directory.CreateDirectory(_suiteDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Writes the catalog at the root and a suite csproj opting into the given items.</summary>
    private void WriteCatalogAndSuite(string catalogJson, string profiles, string modifiers = "")
    {
        File.WriteAllText(Path.Combine(_root, "profiles.json"), catalogJson);

        string items = string.Join(Environment.NewLine,
            profiles.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => $"    <CosmosTestProfile Include=\"{p.Trim()}\" />")
                .Concat(modifiers.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(m => $"    <CosmosTestModifier Include=\"{m.Trim()}\" />")));

        File.WriteAllText(Path.Combine(_suiteDir, "SuiteUnderTest.csproj"),
            $"<Project Sdk=\"Microsoft.NET.Sdk\">{Environment.NewLine}  <ItemGroup>{Environment.NewLine}{items}{Environment.NewLine}  </ItemGroup>{Environment.NewLine}</Project>");
    }

    private const string DeviceCatalog = """
    {
      "profiles": [
        {
          "name": "virtio-pci",
          "architectures": ["x64"],
          "nic": "virtio-net-pci",
          "keyboard": "virtio-keyboard-pci",
          "mouse": "virtio-mouse-pci"
        },
        {
          "name": "virtio-mmio",
          "architectures": ["arm64"],
          "nic": "virtio-net-device",
          "keyboard": "virtio-keyboard-device",
          "mouse": "virtio-mouse-device"
        },
        {
          "name": "plain"
        }
      ],
      "modifiers": [
        {
          "name": "gicv3",
          "architectures": ["arm64"],
          "machineOptions": { "gic-version": "3" }
        }
      ]
    }
    """;

    [Fact]
    public void LoadFor_ReadsTheDeviceModelsOffTheProfile()
    {
        WriteCatalogAndSuite(DeviceCatalog, "virtio-pci,virtio-mmio");

        TestProfile profile = Assert.Single(TestProfileLoader.LoadFor(_suiteDir, "x64"));

        Assert.Equal("virtio-pci", profile.Name);
        Assert.Equal("virtio-net-pci", profile.NetworkCard);
        Assert.Equal("virtio-keyboard-pci", profile.KeyboardDevice);
        Assert.Equal("virtio-mouse-pci", profile.MouseDevice);
    }

    // One suite, two architectures, one csproj: each arch keeps only the
    // profile describing hardware it can actually present.
    [Theory]
    [InlineData("x64", "virtio-pci", "virtio-net-pci")]
    [InlineData("arm64", "virtio-mmio", "virtio-net-device")]
    public void LoadFor_KeepsOnlyProfilesForTheTargetArchitecture(string architecture, string expectedName, string expectedNic)
    {
        WriteCatalogAndSuite(DeviceCatalog, "virtio-pci,virtio-mmio");

        TestProfile profile = Assert.Single(TestProfileLoader.LoadFor(_suiteDir, architecture));

        Assert.Equal(expectedName, profile.Name);
        Assert.Equal(expectedNic, profile.NetworkCard);
    }

    [Fact]
    public void LoadFor_LeavesDeviceModelsNullWhenTheProfileNamesNone()
    {
        WriteCatalogAndSuite(DeviceCatalog, "plain");

        TestProfile profile = Assert.Single(TestProfileLoader.LoadFor(_suiteDir, "x64"));

        Assert.Null(profile.NetworkCard);
        Assert.Null(profile.KeyboardDevice);
        Assert.Null(profile.MouseDevice);
    }

    // A modifier overlays machine/device options; everything it does not touch
    // has to survive. Rebuilding the profile from scratch instead of copying it
    // would silently drop the NIC and leave the cell testing the wrong driver.
    [Fact]
    public void LoadFor_ModifierOverlayPreservesTheDeviceModels()
    {
        WriteCatalogAndSuite(DeviceCatalog, "virtio-mmio", "gicv3");

        TestProfile[] cells = TestProfileLoader.LoadFor(_suiteDir, "arm64").ToArray();

        TestProfile withModifier = Assert.Single(cells, c => c.Name == "virtio-mmio+gicv3");
        Assert.Equal("virtio-net-device", withModifier.NetworkCard);
        Assert.Equal("virtio-keyboard-device", withModifier.KeyboardDevice);
        Assert.Equal("virtio-mouse-device", withModifier.MouseDevice);
        Assert.Equal("3", withModifier.MachineOptions["gic-version"]);
    }

    // Running zero cells would report a green suite that tested nothing, so a
    // suite whose every profile is pinned to another architecture is an error.
    [Fact]
    public void LoadFor_ThrowsWhenNoProfileAppliesToTheArchitecture()
    {
        WriteCatalogAndSuite(DeviceCatalog, "virtio-pci");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => TestProfileLoader.LoadFor(_suiteDir, "arm64"));

        Assert.Contains("arm64", ex.Message);
    }

    [Fact]
    public void LoadFor_FallsBackToTheDefaultProfileWhenTheSuiteOptsIntoNothing()
    {
        File.WriteAllText(Path.Combine(_root, "profiles.json"), DeviceCatalog);
        File.WriteAllText(Path.Combine(_suiteDir, "SuiteUnderTest.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        TestProfile profile = Assert.Single(TestProfileLoader.LoadFor(_suiteDir, "x64"));

        Assert.True(profile.IsDefault);
        Assert.Null(profile.NetworkCard);
    }
}
