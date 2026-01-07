// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL.X64.Pci;
using Cosmos.Kernel.HAL.X64.Pci.Enums;

namespace Cosmos.Kernel.HAL.X64.Devices.Storage;

/// <summary>
/// AHCI (Advanced Host Controller Interface) driver.
/// </summary>
public class AHCI
{
    private static PciDevice? _device;
    private static List<StoragePort>? _ports;
    private static GenericRegisters? _generic;
    private static ulong _abar;

    // Capabilities
    private static bool _supports64bitAddressing;
    private static bool _supportsNativeCommandQueuing;
    private static bool _supportsSNotificationRegister;
    private static bool _supportsMechanicalPresenceSwitch;
    private static bool _supportsStaggeredSpinup;
    private static bool _supportsAggressiveLinkPowerManagement;
    private static bool _supportsActivityLED;
    private static bool _supportsCommandListOverride;
    private static uint _interfaceSpeedSupport;
    private static bool _supportsAHCIModeOnly;
    private static bool _supportsPortMultiplier;
    private static bool _fisBasedSwitchingSupported;
    private static bool _pioMultipleDRQBlock;
    private static bool _slumberStateCapable;
    private static bool _partialStateCapable;
    private static uint _numOfCommandSlots;
    private static bool _commandCompletionCoalescingSupported;
    private static bool _enclosureManagementSupported;
    private static bool _supportsExternalSATA;
    private static uint _numOfPorts;

    /// <summary>
    /// Regular sector size (512 bytes).
    /// </summary>
    public const ulong RegularSectorSize = 512UL;

    /// <summary>
    /// AHCI Version (major, minor, revision).
    /// </summary>
    public static void PrintVersion()
    {
        if (_generic == null)
        {
            Serial.WriteString("Unknown");
            return;
        }
        Serial.WriteNumber((byte)(_generic.AHCIVersion >> 24));
        Serial.WriteString(".");
        Serial.WriteNumber((byte)(_generic.AHCIVersion >> 16));
        Serial.WriteString(".");
        Serial.WriteNumber((byte)(_generic.AHCIVersion >> 8));
    }

    /// <summary>
    /// List of discovered storage ports.
    /// </summary>
    public static List<StoragePort> Ports => _ports ?? new List<StoragePort>();

    /// <summary>
    /// Initialize the AHCI driver.
    /// </summary>
    public static void InitDriver()
    {
        Serial.WriteString("[AHCI] Looking for AHCI controller...\n");

        // Initialize ports list
        _ports = new List<StoragePort>();

        // Try to find AHCI controller (ProgIf = 0x01 for AHCI mode)
        _device = PciManager.GetDeviceClass(ClassId.MassStorageController, SubclassId.SataController, ProgramIf.SataAhci);

        // If not found with AHCI ProgIf, try without ProgIf check
        if (_device == null)
        {
            Serial.WriteString("[AHCI] AHCI mode not found, trying SATA controller...\n");
            _device = PciManager.GetDeviceClass(ClassId.MassStorageController, SubclassId.SataController);
        }

        if (_device != null)
        {
            Serial.WriteString("[AHCI] Found AHCI controller\n");
            Initialize(_device);
        }
        else
        {
            Serial.WriteString("[AHCI] No AHCI controller found\n");
        }
    }

    private static void Initialize(PciDevice device)
    {
        device.EnableBusMaster(true);
        device.EnableMemory(true);

        if (device.BaseAddressBar == null || device.BaseAddressBar.Length < 6)
        {
            Serial.WriteString("[AHCI] Invalid BAR configuration\n");
            return;
        }

        _abar = device.BaseAddressBar[5].BaseAddress;
        Serial.WriteString("[AHCI] ABAR: 0x");
        Serial.WriteHex(_abar);
        Serial.WriteString("\n");

        _generic = new GenericRegisters(_abar);
        _generic.GlobalHostControl |= 1U << 31; // Enable AHCI

        GetCapabilities();
        if (_ports != null)
        {
            _ports.Capacity = (int)_numOfPorts;
        }
        GetPorts();

        Serial.WriteString("[AHCI] Version: ");
        PrintVersion();
        Serial.WriteString("\n");
        Serial.WriteString("[AHCI] Ports discovered: ");
        Serial.WriteNumber((uint)(_ports?.Count ?? 0));
        Serial.WriteString("\n");
    }

    /// <summary>
    /// HBA Reset.
    /// </summary>
    public static void HBAReset()
    {
        if (_generic == null) return;

        _generic.GlobalHostControl = 1;
        uint hr;
        do
        {
            Wait(1);
            hr = _generic.GlobalHostControl & 1;
        } while (hr != 0);
    }

    /// <summary>
    /// Wait for a number of microseconds.
    /// </summary>
    public static void Wait(int microsecondsTimeout)
    {
        for (int i = 0; i < microsecondsTimeout; i++)
        {
            PlatformHAL.PortIO.ReadByte(0x80); // IO Wait
            PlatformHAL.PortIO.ReadByte(0x80);
            PlatformHAL.PortIO.ReadByte(0x80);
            PlatformHAL.PortIO.ReadByte(0x80);
        }
    }

    private static void GetCapabilities()
    {
        if (_generic == null) return;

        _numOfPorts = (_generic.Capabilities & 0x1F) + 1;
        _supportsExternalSATA = ((_generic.Capabilities >> 5) & 1) == 1;
        _enclosureManagementSupported = ((_generic.Capabilities >> 6) & 1) == 1;
        _commandCompletionCoalescingSupported = ((_generic.Capabilities >> 7) & 1) == 1;
        _numOfCommandSlots = ((_generic.Capabilities >> 8) & 0x1F) + 1;
        _partialStateCapable = ((_generic.Capabilities >> 13) & 1) == 1;
        _slumberStateCapable = ((_generic.Capabilities >> 14) & 1) == 1;
        _pioMultipleDRQBlock = ((_generic.Capabilities >> 15) & 1) == 1;
        _fisBasedSwitchingSupported = ((_generic.Capabilities >> 16) & 1) == 1;
        _supportsPortMultiplier = ((_generic.Capabilities >> 17) & 1) == 1;
        _supportsAHCIModeOnly = ((_generic.Capabilities >> 18) & 1) == 1;
        _interfaceSpeedSupport = (_generic.Capabilities >> 20) & 0x0F;
        _supportsCommandListOverride = ((_generic.Capabilities >> 24) & 1) == 1;
        _supportsActivityLED = ((_generic.Capabilities >> 25) & 1) == 1;
        _supportsAggressiveLinkPowerManagement = ((_generic.Capabilities >> 26) & 1) == 1;
        _supportsStaggeredSpinup = ((_generic.Capabilities >> 27) & 1) == 1;
        _supportsMechanicalPresenceSwitch = ((_generic.Capabilities >> 28) & 1) == 1;
        _supportsSNotificationRegister = ((_generic.Capabilities >> 29) & 1) == 1;
        _supportsNativeCommandQueuing = ((_generic.Capabilities >> 30) & 1) == 1;
        _supports64bitAddressing = ((_generic.Capabilities >> 31) & 1) == 1;
    }

    private static void GetPorts()
    {
        if (_generic == null || _ports == null) return;

        var implementedPort = _generic.ImplementedPorts;

        for (uint port = 0; port < 32; port++)
        {
            if ((implementedPort & 1) != 0)
            {
                var portReg = new PortRegisters(_abar + 0x100, port);
                PortType portType = CheckPortType(portReg);
                portReg.PortType = portType;

                if (portType == PortType.SATA)
                {
                    Serial.WriteString("[AHCI] Initializing SATA port ");
                    Serial.WriteNumber(port);
                    Serial.WriteString("\n");
                    PortRebase(portReg, port);
                    var sataPort = new SATA(portReg);
                    _ports.Add(sataPort);
                }
                else if (portType == PortType.SATAPI)
                {
                    Serial.WriteString("[AHCI] Found SATAPI port ");
                    Serial.WriteNumber(port);
                    Serial.WriteString(" (not supported yet)\n");
                }
                else if (portType == PortType.SEMB)
                {
                    Serial.WriteString("[AHCI] Found SEMB port ");
                    Serial.WriteNumber(port);
                    Serial.WriteString(" (not supported yet)\n");
                }
                else if (portType == PortType.PM)
                {
                    Serial.WriteString("[AHCI] Found Port Multiplier at port ");
                    Serial.WriteNumber(port);
                    Serial.WriteString(" (not supported yet)\n");
                }
            }
            implementedPort >>= 1;
        }
    }

    private static PortType CheckPortType(PortRegisters port)
    {
        var ipm = (InterfacePowerManagementStatus)((port.SSTS >> 8) & 0x0F);
        var det = (DeviceDetectionStatus)(port.SSTS & 0x0F);
        var signature = port.SIG;

        // Check if the port is active
        if (ipm != InterfacePowerManagementStatus.Active)
            return PortType.Nothing;
        if (det != DeviceDetectionStatus.DeviceDetectedWithPhy)
            return PortType.Nothing;

        signature >>= 16;

        return (AHCISignature)signature switch
        {
            AHCISignature.SATA => PortType.SATA,
            AHCISignature.SATAPI => PortType.SATAPI,
            AHCISignature.SEMB => PortType.SEMB,
            AHCISignature.PortMultiplier => PortType.PM,
            AHCISignature.Nothing => PortType.Nothing,
            _ => throw new Exception($"SATA Error: Unknown drive found at port: {port.PortNumber}")
        };
    }

    private static unsafe void PortRebase(PortRegisters port, uint portNumber)
    {
        Serial.WriteString("[AHCI] Rebasing port...\n");
        if (!StopCMD(port)) SATA.PortReset(port);

        port.CLB = (uint)AHCIBase.AHCI + 0x400 * portNumber;
        port.FB = (uint)AHCIBase.AHCI + 0x8000 + 0x100 * portNumber;

        port.SERR = 1;
        port.IS = 0;
        port.IE = 0;

        // Clear command list and FIS memory
        MemoryOp.MemSet((byte*)(ulong)port.CLB, 0, 1024);
        MemoryOp.MemSet((byte*)(ulong)port.FB, 0, 256);

        GetCommandHeader(port); // Rebase Command header

        if (!StartCMD(port)) SATA.PortReset(port);

        port.IS = 0;
        port.IE = 0xFFFFFFFF;

        Serial.WriteString("[AHCI] Port rebased\n");
    }

    private static unsafe void GetCommandHeader(PortRegisters port)
    {
        for (uint i = 0; i < 32; i++)
        {
            var cmdHeader = new HBACommandHeader(port.CLB, i)
            {
                PRDTL = 8,
                CTBA = (uint)(AHCIBase.AHCI + 0xA000) + 0x2000 * port.PortNumber + 0x100 * i,
                CTBAU = 0
            };
            MemoryOp.MemSet((byte*)(ulong)cmdHeader.CTBA, 0, 0x100);
        }
    }

    private static bool StartCMD(PortRegisters port)
    {
        int spin;
        for (spin = 0; spin < 101; spin++)
        {
            if ((port.CMD & (uint)CommandAndStatus.CMDListRunning) == 0) break;
            Wait(5000);
        }
        if (spin == 101) return false;

        port.CMD |= 1 << 4; // FIS Receive Enable
        port.CMD |= 1 << 0; // Start

        return true;
    }

    private static bool StopCMD(PortRegisters port)
    {
        int spin;
        port.CMD &= ~(1U << 0); // Clear Start bit

        for (spin = 0; spin < 101; spin++)
        {
            if ((port.CMD & (uint)CommandAndStatus.CMDListRunning) == 0) break;
            Wait(5000);
        }
        if (spin == 101) return false;

        for (spin = 0; spin < 101; spin++)
        {
            if (port.CI == 0) break;
            Wait(50);
        }
        if (spin == 101) return false;

        port.CMD &= ~(1U << 4); // Clear FIS Receive Enable

        if (_supportsCommandListOverride)
        {
            if ((port.TFD & (uint)ATADeviceStatus.Busy) != 0)
            {
                port.CMD |= 1U << 3;
            }
        }

        for (spin = 0; spin < 101; spin++)
        {
            if ((port.CMD & (uint)CommandAndStatus.CMDListRunning) == 0 &&
                (port.CMD & (uint)CommandAndStatus.FISRecieveRunning) == 0 &&
                (port.CMD & (uint)CommandAndStatus.StartProccess) == 0 &&
                (port.CMD & (uint)CommandAndStatus.FISRecieveEnable) == 0) break;
            Wait(5000);
        }
        if (spin == 101)
        {
            if (_supportsCommandListOverride)
                port.CMD |= 1U << 3;
            else
                port.CMD &= ~(1U << 3);
            return false;
        }

        return true;
    }
}
