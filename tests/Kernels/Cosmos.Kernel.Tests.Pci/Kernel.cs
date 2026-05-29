using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Pci;
using Cosmos.TestRunner.Framework;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;

namespace Cosmos.Kernel.Tests.Pci;

public class Kernel : Sys.Kernel
{
    // First device PciManager enumerated. Used by every ConfigSpace_* test;
    // captured once at BeforeRun time so a transient state change between
    // tests can't show up as cross-test interference.
    private static PciDevice? s_firstDevice;

    // Reason string surfaced through TR.RunIf when a test depends on at
    // least one PCI device having been enumerated. A profile that disables
    // ACPI on arm64 (no MCFG, no FDT fallback for the ECAM base) lands
    // here and the device tests skip cleanly.
    private const string SkipNoDevice = "no PCI devices enumerated — host bridge / ECAM not discovered";

    protected override void BeforeRun()
    {
        Serial.WriteString("[Pci] BeforeRun() reached!\n");

        TR.Start("PCI Subsystem Tests", expectedTests: 6);

        s_firstDevice = PciManager.Count > 0 ? PciManager.Devices![0] : null;
        bool anyDevice = s_firstDevice != null;

        // ==================== Manager ====================
        TR.Run("Manager_Initialized", TestManager_Initialized);
        TR.RunIf(anyDevice, "Manager_HasDevices", TestManager_HasDevices, SkipNoDevice);

        // ==================== ConfigSpace ====================
        TR.RunIf(anyDevice, "ConfigSpace_VendorId_NotAllOnes",     TestConfigSpace_VendorIdNotAllOnes,     SkipNoDevice);
        TR.RunIf(anyDevice, "ConfigSpace_DeviceId_NotAllOnes",     TestConfigSpace_DeviceIdNotAllOnes,     SkipNoDevice);
        TR.RunIf(anyDevice, "ConfigSpace_ClassCode_InRange",       TestConfigSpace_ClassCodeInRange,       SkipNoDevice);
        TR.RunIf(anyDevice, "ConfigSpace_VendorRead_StableAcrossCalls", TestConfigSpace_VendorReadStable,  SkipNoDevice);

        TR.Finish();

        Serial.WriteString("\n[Tests Complete - System Halting]\n");
    }

    protected override void Run() => Stop();

    protected override void AfterRun()
    {
        TR.Complete();
        Cosmos.Kernel.System.Power.Halt();
    }

    // ==================== Manager ====================

    // Devices array is allocated by PciManager.Setup() during boot, even when
    // zero devices end up being found. A null array means Setup never ran —
    // a kernel-init regression, not a "no PCI on this profile" condition.
    private static void TestManager_Initialized()
    {
        Assert.NotNull(PciManager.Devices);
    }

    private static void TestManager_HasDevices()
    {
        Assert.True(PciManager.Count > 0);
    }

    // ==================== ConfigSpace ====================
    //
    // Spot-checks against the first enumerated device. We don't pin any
    // particular vendor/device id because that varies by QEMU machine type
    // and version; what we assert is that the values look like a real
    // config-space response rather than the all-ones / all-zeros patterns
    // that surface when ECAM is unmapped or the bus is empty.

    private static void TestConfigSpace_VendorIdNotAllOnes()
    {
        Assert.True(s_firstDevice!.VendorId != 0xFFFF && s_firstDevice.VendorId != 0x0000);
    }

    private static void TestConfigSpace_DeviceIdNotAllOnes()
    {
        Assert.True(s_firstDevice!.DeviceId != 0xFFFF);
    }

    private static void TestConfigSpace_ClassCodeInRange()
    {
        // PCI base class codes 0x00..0x13 are spec-defined; 0xFF is
        // reserved for "unassigned". Anything outside means the byte we
        // read is not a real class code (typically a stale-cache or
        // unmapped read returning all-ones).
        Assert.True(s_firstDevice!.ClassCode <= 0x13);
    }

    private static void TestConfigSpace_VendorReadStable()
    {
        // Two reads of the same offset must agree. Catches half-baked ECAM
        // mappings (one read hits cached zeros, the next hits real config),
        // and catches register-side-effect bugs in ReadRegister16 (it must
        // be a pure read, not advance any internal pointer).
        ushort first = s_firstDevice!.ReadRegister16(0x00);
        ushort second = s_firstDevice.ReadRegister16(0x00);
        Assert.Equal(first, second);
    }
}
