// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL.Pci;

namespace Cosmos.Kernel.HAL.Devices.Storage;

/// <summary>
/// One initialized AHCI HBA. Each controller owns its ABAR (HHDM-mapped),
/// capability snapshot, port list, and a per-controller command region
/// allocated from <see cref="PageAllocator"/> — kernel-virtual for memset,
/// physical (via <see cref="PageAllocator.VirtualToPhysical"/>) for the
/// HBA's CLB / FB / CTBA registers. The driver scans PCI for every
/// matching SATA/AHCI controller and instantiates one
/// <see cref="AHCIController"/> per match, so a system with two HBAs gets
/// two instances and all of their ports show up in <see cref="AHCI.Ports"/>.
/// </summary>
public unsafe class AHCIController
{
    // Per-port memory layout inside the controller's command region. 0x4A000
    // (296 KiB, 74 pages) covers all 32 possible port indices' CLB + FB +
    // 32 command-table slots each, with the alignment AHCI requires
    // (CLB 1 KiB, FB 256 B, CTBA 128 B) inherited from page alignment.
    private const uint PortStrideCLB = 0x400;
    private const uint FBBaseOffset = 0x8000;
    private const uint PortStrideFB = 0x100;
    private const uint CTBABaseOffset = 0xA000;
    private const uint PortStrideCTBA = 0x2000;
    private const uint SlotStrideCTBA = 0x100;
    private const ulong CommandRegionBytes = 0x4A000;
    private const ulong CommandRegionPages = (CommandRegionBytes + 4095) / 4096;

    private readonly PciDevice _pci;
    private ulong _abarPhys;
    private ulong _abarVirt;
    private GenericRegisters? _generic;
    private readonly List<BlockDevice> _ports = new();
    private ulong _cmdRegionVirt;
    private ulong _cmdRegionPhys;

    // Capability snapshot (read once at init from CAP)
    private bool _supports64bitAddressing;
    private bool _supportsNativeCommandQueuing;
    private bool _supportsSNotificationRegister;
    private bool _supportsMechanicalPresenceSwitch;
    private bool _supportsStaggeredSpinup;
    private bool _supportsAggressiveLinkPowerManagement;
    private bool _supportsActivityLED;
    private bool _supportsCommandListOverride;
    private uint _interfaceSpeedSupport;
    private bool _supportsAHCIModeOnly;
    private bool _supportsPortMultiplier;
    private bool _fisBasedSwitchingSupported;
    private bool _pioMultipleDRQBlock;
    private bool _slumberStateCapable;
    private bool _partialStateCapable;
    private uint _numOfCommandSlots;
    private bool _commandCompletionCoalescingSupported;
    private bool _enclosureManagementSupported;
    private bool _supportsExternalSATA;
    private uint _numOfPorts;

    public PciDevice Device => _pci;
    public IReadOnlyList<BlockDevice> Ports => _ports;
    public uint AHCIVersion => _generic?.AHCIVersion ?? 0;
    public uint NumberOfCommandSlots => _numOfCommandSlots;
    public bool SupportsCommandListOverride => _supportsCommandListOverride;
    public bool Supports64BitAddressing => _supports64bitAddressing;

    public AHCIController(PciDevice device)
    {
        _pci = device;
    }

    /// <summary>Kernel-virtual base of port <paramref name="portNumber"/>'s command list.</summary>
    public ulong PortCommandListVirt(uint portNumber) =>
        _cmdRegionVirt + PortStrideCLB * portNumber;

    /// <summary>Physical (DMA) base of port <paramref name="portNumber"/>'s command list.</summary>
    public ulong PortCommandListPhys(uint portNumber) =>
        _cmdRegionPhys + PortStrideCLB * portNumber;

    /// <summary>Kernel-virtual base of port <paramref name="portNumber"/>'s FIS-receive area.</summary>
    public ulong PortFisReceiveVirt(uint portNumber) =>
        _cmdRegionVirt + FBBaseOffset + PortStrideFB * portNumber;

    /// <summary>Physical (DMA) base of port <paramref name="portNumber"/>'s FIS-receive area.</summary>
    public ulong PortFisReceivePhys(uint portNumber) =>
        _cmdRegionPhys + FBBaseOffset + PortStrideFB * portNumber;

    /// <summary>Kernel-virtual base of port/slot's command table.</summary>
    public ulong PortCommandTableVirt(uint portNumber, uint slot) =>
        _cmdRegionVirt + CTBABaseOffset + PortStrideCTBA * portNumber + SlotStrideCTBA * slot;

    /// <summary>Physical (DMA) base of port/slot's command table.</summary>
    public ulong PortCommandTablePhys(uint portNumber, uint slot) =>
        _cmdRegionPhys + CTBABaseOffset + PortStrideCTBA * portNumber + SlotStrideCTBA * slot;

    /// <summary>
    /// Bring this controller up: enable bus master + memory decoding, map
    /// ABAR via HHDM, allocate a command region via
    /// <see cref="PageAllocator"/>, enable AHCI mode, snapshot CAP, and
    /// probe every implemented port. After a successful call,
    /// <see cref="Ports"/> contains one <see cref="SATA"/> per attached
    /// SATA drive. Returns <c>false</c> if any precondition fails — the
    /// caller (<see cref="AHCI.InitDriver"/>) then drops this controller
    /// and moves on. Init-time failures are reported via return value
    /// rather than thrown so a misconfigured device can't take the boot
    /// down.
    /// </summary>
    public bool Initialize()
    {
        _pci.EnableBusMaster(true);
        _pci.EnableMemory(true);

        _abarPhys = _pci.GetBar64Address(5);
        if (_abarPhys == 0)
        {
            Serial.WriteString("[AHCI] BAR5 is not a memory BAR\n");
            return false;
        }

        ulong hhdmOffset = Limine.HHDM.Response != null ? Limine.HHDM.Response->Offset : 0;
        _abarVirt = _abarPhys + hhdmOffset;

        Serial.WriteString("[AHCI] ABAR phys=0x");
        Serial.WriteHex(_abarPhys);
        Serial.WriteString(" virt=0x");
        Serial.WriteHex(_abarVirt);
        Serial.WriteString("\n");

        _cmdRegionVirt = (ulong)PageAllocator.AllocPages(PageType.Unmanaged, CommandRegionPages, true);
        if (_cmdRegionVirt == 0)
        {
            Serial.WriteString("[AHCI] Failed to allocate command region\n");
            return false;
        }
        _cmdRegionPhys = PageAllocator.VirtualToPhysical(_cmdRegionVirt);

        Serial.WriteString("[AHCI] Cmd region phys=0x");
        Serial.WriteHex(_cmdRegionPhys);
        Serial.WriteString(" virt=0x");
        Serial.WriteHex(_cmdRegionVirt);
        Serial.WriteString("\n");

        _generic = new GenericRegisters(_abarVirt);
        _generic.GlobalHostControl |= 1U << 31; // Enable AHCI

        GetCapabilities();

        if (!_supports64bitAddressing && _cmdRegionPhys > 0xFFFFFFFF)
        {
            Serial.WriteString("[AHCI] Controller is 32-bit only but command region is above 4 GiB\n");
            return false;
        }

        _ports.Capacity = (int)_numOfPorts;
        GetPorts();

        Serial.WriteString("[AHCI] Version: ");
        PrintVersion();
        Serial.WriteString("\n[AHCI] Ports discovered: ");
        Serial.WriteNumber((uint)_ports.Count);
        Serial.WriteString("\n");
        return true;
    }

    /// <summary>HBA Reset. Forces the controller into its reset state and
    /// waits for it to come back.</summary>
    public void HBAReset()
    {
        if (_generic == null)
        {
            return;
        }

        _generic.GlobalHostControl = 1;
        uint hr;
        do
        {
            AHCI.Wait(1);
            hr = _generic.GlobalHostControl & 1;
        } while (hr != 0);
    }

    private void PrintVersion()
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

    private void GetCapabilities()
    {
        if (_generic == null)
        {
            return;
        }

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

    private void GetPorts()
    {
        if (_generic == null)
        {
            return;
        }

        var implementedPort = _generic.ImplementedPorts;

        for (uint port = 0; port < 32; port++)
        {
            if ((implementedPort & 1) != 0)
            {
                var portReg = new PortRegisters(_abarVirt + 0x100, port, this);
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

        if (ipm != InterfacePowerManagementStatus.Active)
        {
            return PortType.Nothing;
        }
        if (det != DeviceDetectionStatus.DeviceDetectedWithPhy)
        {
            return PortType.Nothing;
        }

        signature >>= 16;

        switch ((AHCISignature)signature)
        {
            case AHCISignature.SATA: return PortType.SATA;
            case AHCISignature.SATAPI: return PortType.SATAPI;
            case AHCISignature.SEMB: return PortType.SEMB;
            case AHCISignature.PortMultiplier: return PortType.PM;
            case AHCISignature.Nothing: return PortType.Nothing;
            default:
                Serial.WriteString("[AHCI] Unknown drive signature 0x");
                Serial.WriteHex(signature);
                Serial.WriteString(" at port ");
                Serial.WriteNumber(port.PortNumber);
                Serial.WriteString(" — skipping\n");
                return PortType.Nothing;
        }
    }

    private void PortRebase(PortRegisters port, uint portNumber)
    {
        Serial.WriteString("[AHCI] Rebasing port...\n");
        if (!StopCMD(port))
        {
            SATA.PortReset(port);
        }

        ulong clbPhys = PortCommandListPhys(portNumber);
        ulong fbPhys = PortFisReceivePhys(portNumber);
        ulong clbVirt = PortCommandListVirt(portNumber);
        ulong fbVirt = PortFisReceiveVirt(portNumber);

        port.CLB = (uint)(clbPhys & 0xFFFFFFFF);
        port.CLBU = (uint)(clbPhys >> 32);
        port.FB = (uint)(fbPhys & 0xFFFFFFFF);
        port.FBU = (uint)(fbPhys >> 32);

        port.SERR = 1;
        port.IS = 0;
        port.IE = 0;

        MemoryOp.MemSet((byte*)clbVirt, 0, 1024);
        MemoryOp.MemSet((byte*)fbVirt, 0, 256);

        GetCommandHeader(port, portNumber);

        if (!StartCMD(port))
        {
            SATA.PortReset(port);
        }

        port.IS = 0;
        port.IE = 0xFFFFFFFF;

        Serial.WriteString("[AHCI] Port rebased\n");
    }

    private void GetCommandHeader(PortRegisters port, uint portNumber)
    {
        ulong clbVirt = PortCommandListVirt(portNumber);
        for (uint i = 0; i < 32; i++)
        {
            ulong ctbaPhys = PortCommandTablePhys(portNumber, i);
            ulong ctbaVirt = PortCommandTableVirt(portNumber, i);
            var cmdHeader = new HBACommandHeader(clbVirt, i)
            {
                PRDTL = 8,
                CTBA = (uint)(ctbaPhys & 0xFFFFFFFF),
                CTBAU = (uint)(ctbaPhys >> 32)
            };
            MemoryOp.MemSet((byte*)ctbaVirt, 0, 0x100);
        }
    }

    private bool StartCMD(PortRegisters port)
    {
        int spin;
        for (spin = 0; spin < 101; spin++)
        {
            if ((port.CMD & (uint)CommandAndStatus.CMDListRunning) == 0)
            {
                break;
            }
            AHCI.Wait(5000);
        }
        if (spin == 101)
        {
            return false;
        }

        port.CMD |= 1 << 4; // FIS Receive Enable
        port.CMD |= 1 << 0; // Start

        return true;
    }

    private bool StopCMD(PortRegisters port)
    {
        int spin;
        port.CMD &= ~(1U << 0); // Clear Start bit

        for (spin = 0; spin < 101; spin++)
        {
            if ((port.CMD & (uint)CommandAndStatus.CMDListRunning) == 0)
            {
                break;
            }
            AHCI.Wait(5000);
        }
        if (spin == 101)
        {
            return false;
        }

        for (spin = 0; spin < 101; spin++)
        {
            if (port.CI == 0)
            {
                break;
            }
            AHCI.Wait(50);
        }
        if (spin == 101)
        {
            return false;
        }

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
                (port.CMD & (uint)CommandAndStatus.FISRecieveEnable) == 0)
            {
                break;
            }
            AHCI.Wait(5000);
        }
        if (spin == 101)
        {
            if (_supportsCommandListOverride)
            {
                port.CMD |= 1U << 3;
            }
            else
            {
                port.CMD &= ~(1U << 3);
            }
            return false;
        }

        return true;
    }
}
