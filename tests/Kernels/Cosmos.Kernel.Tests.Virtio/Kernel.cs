using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Devices.Input;
using Cosmos.Kernel.HAL.Devices.Network;
using Cosmos.Kernel.HAL.Devices.Virtio;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.HAL.Pci;
using Cosmos.TestRunner.Framework;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;

namespace Cosmos.Kernel.Tests.Virtio;

/// <summary>
/// Covers virtio device binding over both transports. The suite's profiles
/// attach a virtio NIC, keyboard and mouse on every cell, so the bind tests
/// are unconditional: if a device is missing, that is the regression this
/// suite exists to catch, not an environment condition.
///
/// x64 runs the PCI transport (q35 has no virtio-mmio window) and arm64 the
/// MMIO transport, which is why the transport-name expectation and the
/// PCI-only tests are selected at compile time.
/// </summary>
public class Kernel : Sys.Kernel
{
#if ARCH_X64
    /// <summary>Transport the profile for this architecture presents.</summary>
    private const string ExpectedTransport = "PCI";

    /// <summary>True when virtio arrives over PCI, gating the capability/MSI-X tests.</summary>
    private const bool IsPciTransport = true;
#else
    private const string ExpectedTransport = "MMIO";
    private const bool IsPciTransport = false;
#endif

    /// <summary>Number of tests announced to the runner in TR.Start.</summary>
    private const int ExpectedTestCount = 10;

    /// <summary>Reason surfaced for the PCI-transport tests when the cell runs virtio over MMIO.</summary>
    private const string SkipNotPci = "virtio arrives over MMIO on this architecture";

    /// <summary>Bytes in a MAC address.</summary>
    private const int MacAddressLength = 6;

    // Captured once in BeforeRun so a state change between tests cannot show
    // up as cross-test interference.
    private static VirtioNet? s_net;
    private static IKeyboardDevice[] s_keyboards = Array.Empty<IKeyboardDevice>();
    private static IMouseDevice[] s_mice = Array.Empty<IMouseDevice>();
    private static PciDevice? s_virtioNetFunction;

    protected override void BeforeRun()
    {
        Serial.WriteString("[Virtio] BeforeRun() reached!\n");

        TR.Start("Virtio Device Tests", expectedTests: ExpectedTestCount);

        s_net = VirtioDevice.GetDevice<VirtioNet>();
        s_keyboards = VirtioDevice.GetKeyboards();
        s_mice = VirtioDevice.GetMice();
        s_virtioNetFunction = FindVirtioFunction(VirtioTransport.DeviceTypeNetwork);

        // ==================== Binding ====================
        TR.Run("Net_DriverBound", TestNet_DriverBound);
        TR.Run("Net_TransportMatchesArch", TestNet_TransportMatchesArch);
        TR.Run("Net_DeviceReady", TestNet_DeviceReady);
        TR.Run("Net_LinkUp", TestNet_LinkUp);
        TR.Run("Net_MacAddressProgrammed", TestNet_MacAddressProgrammed);

        // ==================== Input ====================
        TR.Run("Input_KeyboardBound", TestInput_KeyboardBound);
        TR.Run("Input_MouseBound", TestInput_MouseBound);

        // ==================== PCI transport ====================
        TR.RunIf(IsPciTransport, "Pci_FunctionClaimed",     TestPci_FunctionClaimed,     SkipNotPci);
        TR.RunIf(IsPciTransport, "Pci_MsiXActive",          TestPci_MsiXActive,          SkipNotPci);
        TR.RunIf(IsPciTransport, "Pci_Version1Negotiated",  TestPci_Version1Negotiated,  SkipNotPci);

        TR.Finish();

        Serial.WriteString("\n[Tests Complete - System Halting]\n");
    }

    protected override void Run() => Stop();

    protected override void AfterRun()
    {
        TR.Complete();
        Cosmos.Kernel.System.Power.Halt();
    }

    // ==================== Binding ====================

    // The registry only accepts a device once Initialize() succeeded, so a
    // null here means the whole probe → transport → driver chain failed, not
    // merely that the NIC is unhappy.
    private static void TestNet_DriverBound()
    {
        Assert.NotNull(s_net, "virtio-net driver should have bound to the attached NIC");
    }

    // Guards against a silent transport swap: on x64 the driver must have come
    // up over PCI, which is the path q35 can present at all.
    private static void TestNet_TransportMatchesArch()
    {
        if (s_net == null)
        {
            Assert.Fail("no virtio-net device bound");
            return;
        }

        Serial.WriteString("[Test] Transport: ");
        Serial.WriteString(s_net.Transport.TransportName);
        Serial.WriteString("\n");

        Assert.Equal(ExpectedTransport, s_net.Transport.TransportName);
    }

    // Ready is only set after queues are configured, buffers are posted and an
    // interrupt source was secured — the driver now refuses to come up
    // without one, so this also covers MSI-X binding on the PCI cell.
    private static void TestNet_DeviceReady()
    {
        if (s_net == null)
        {
            Assert.Fail("no virtio-net device bound");
            return;
        }

        Assert.True(s_net.Ready, "virtio-net should report ready after initialization");
    }

    private static void TestNet_LinkUp()
    {
        if (s_net == null)
        {
            Assert.Fail("no virtio-net device bound");
            return;
        }

        // QEMU's user-mode backend brings the link up immediately, so a down
        // link means the status field was read from the wrong config offset.
        Assert.True(s_net.LinkUp, "virtio-net link should be up with QEMU user networking");
    }

    // The MAC is read byte-by-byte out of the device configuration region,
    // which on PCI is a separate BAR window located through its own vendor
    // capability. An all-zero address means those reads landed nowhere.
    private static void TestNet_MacAddressProgrammed()
    {
        if (s_net == null)
        {
            Assert.Fail("no virtio-net device bound");
            return;
        }

        MACAddress mac = s_net.MacAddress;
        Assert.NotNull(mac);

        bool anyNonZero = false;
        for (int i = 0; i < MacAddressLength; i++)
        {
            if (mac.bytes[i] != 0)
            {
                anyNonZero = true;
                break;
            }
        }

        Serial.WriteString("[Test] MAC: ");
        Serial.WriteString(mac.ToString());
        Serial.WriteString("\n");

        Assert.True(anyNonZero, "MAC address read from device config should not be all zeros");
    }

    // ==================== Input ====================
    //
    // Both profiles attach a virtio keyboard and mouse. Binding them exercises
    // the event-type probe in the registry, which reads the input config
    // select/subsel window — a different device-config access pattern from the
    // NIC's flat MAC read.

    private static void TestInput_KeyboardBound()
    {
        Assert.True(s_keyboards.Length > 0, "a virtio keyboard should have bound");
    }

    private static void TestInput_MouseBound()
    {
        Assert.True(s_mice.Length > 0, "a virtio mouse should have bound");
    }

    // ==================== PCI transport ====================

    // Claimed is set by the virtio PCI scan when it takes ownership. If the
    // function is enumerated but unclaimed, capability parsing rejected the
    // device and the driver never saw it.
    private static void TestPci_FunctionClaimed()
    {
        Assert.NotNull(s_virtioNetFunction, "a virtio-net PCI function should have been enumerated");
        if (s_virtioNetFunction != null)
        {
            Assert.True(s_virtioNetFunction.Claimed, "the virtio-net PCI function should be claimed by the driver");
        }
    }

    // MSI-X is the only interrupt path the PCI transport offers, so this
    // failing means the device would run blind even if everything else bound.
    private static void TestPci_MsiXActive()
    {
        VirtioPciTransport? transport = s_net?.Transport as VirtioPciTransport;
        Assert.NotNull(transport, "virtio-net should be running over the PCI transport");
        if (transport != null)
        {
            Assert.True(transport.MsiXActive, "MSI-X should be enabled for the virtio-net PCI function");
        }
    }

    // Modern virtio-pci must land on VERSION_1, which is what selects the
    // 12-byte net header. Negotiating it away here while the driver still
    // sized the header for it would corrupt every frame.
    private static void TestPci_Version1Negotiated()
    {
        if (s_net == null)
        {
            Assert.Fail("no virtio-net device bound");
            return;
        }

        Assert.True(s_net.Transport.Version1Negotiated, "virtio-pci should negotiate VIRTIO_F_VERSION_1");
    }

    // ==================== Helpers ====================

    /// <summary>
    /// Finds the first enumerated PCI function that is a virtio device of the
    /// given type, or null when none is present (the MMIO cell).
    /// </summary>
    private static PciDevice? FindVirtioFunction(uint deviceType)
    {
        if (PciManager.Devices == null)
        {
            return null;
        }

        for (uint i = 0; i < PciManager.Count; i++)
        {
            PciDevice device = PciManager.Devices[i];
            if (device.VendorId == VirtioPciTransport.VirtioVendorId && VirtioPciTransport.GetDeviceType(device) == deviceType)
            {
                return device;
            }
        }

        return null;
    }
}
