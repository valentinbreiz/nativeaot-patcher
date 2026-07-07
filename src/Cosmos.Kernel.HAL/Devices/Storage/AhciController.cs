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
/// <see cref="AhciController"/> per match, so a system with two HBAs gets
/// two instances and all of their ports show up in <see cref="Ahci.Ports"/>.
/// </summary>
public unsafe class AhciController
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
    private bool _supportsAhciModeOnly;
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
    public uint AhciVersion => _generic?.AhciVersion ?? 0;
    public uint NumberOfCommandSlots => _numOfCommandSlots;
    public bool SupportsCommandListOverride => _supportsCommandListOverride;
    public bool Supports64BitAddressing => _supports64bitAddressing;

    public AhciController(PciDevice device)
    {
        _pci = device;
    }

    /// <summary>Kernel-virtual base of port <paramref name="portNumber"/>'s command list.</summary>
    internal ulong PortCommandListVirt(uint portNumber) =>
        _cmdRegionVirt + PortStrideCLB * portNumber;

    /// <summary>Physical (DMA) base of port <paramref name="portNumber"/>'s command list.</summary>
    internal ulong PortCommandListPhys(uint portNumber) =>
        _cmdRegionPhys + PortStrideCLB * portNumber;

    /// <summary>Kernel-virtual base of port <paramref name="portNumber"/>'s FIS-receive area.</summary>
    internal ulong PortFisReceiveVirt(uint portNumber) =>
        _cmdRegionVirt + FBBaseOffset + PortStrideFB * portNumber;

    /// <summary>Physical (DMA) base of port <paramref name="portNumber"/>'s FIS-receive area.</summary>
    internal ulong PortFisReceivePhys(uint portNumber) =>
        _cmdRegionPhys + FBBaseOffset + PortStrideFB * portNumber;

    /// <summary>Kernel-virtual base of port/slot's command table.</summary>
    internal ulong PortCommandTableVirt(uint portNumber, uint slot) =>
        _cmdRegionVirt + CTBABaseOffset + PortStrideCTBA * portNumber + SlotStrideCTBA * slot;

    /// <summary>Physical (DMA) base of port/slot's command table.</summary>
    internal ulong PortCommandTablePhys(uint portNumber, uint slot) =>
        _cmdRegionPhys + CTBABaseOffset + PortStrideCTBA * portNumber + SlotStrideCTBA * slot;

    /// <summary>
    /// Bring this controller up: enable bus master + memory decoding, map
    /// ABAR via HHDM, allocate a command region via
    /// <see cref="PageAllocator"/>, enable AHCI mode, snapshot CAP, and
    /// probe every implemented port. After a successful call,
    /// <see cref="Ports"/> contains one <see cref="Sata"/> per attached
    /// SATA drive. Returns <c>false</c> if any precondition fails — the
    /// caller (<see cref="Ahci.Initialize"/>) then drops this controller
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

        // ARM64 needs the ABAR page installed as Device memory in TTBR1
        // before the HHDM virtual is dereferenceable for MMIO. No-op on x64.
        PlatformHAL.Initializer?.EnsureMmioMapped(_abarPhys);

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

        // AHCI 1.3.1 s10.1.2: with CAP.SAM=0, software must set GHC.AE=1
        // before accessing any other AHCI register. SeaBIOS/EDK2 normally
        // leave AE set, but firmware that bound the device in AHCI ProgIf
        // with AE=0 would make all port MMIO below undefined. With SAM=1
        // the bit is read-only 1, so the OR is harmless either way —
        // matches Linux's ahci_enable_ahci.
        if ((_generic.GlobalHostControl & (1U << 31)) == 0)
        {
            _generic.GlobalHostControl |= 1U << 31;
        }

        Serial.WriteString("[AHCI] CAP=0x");
        Serial.WriteHex(_generic.Capabilities);
        Serial.WriteString(" PI=0x");
        Serial.WriteHex(_generic.ImplementedPorts);
        Serial.WriteString(" GHC=0x");
        Serial.WriteHex(_generic.GlobalHostControl);
        Serial.WriteString(" VS=0x");
        Serial.WriteHex(_generic.AhciVersion);
        Serial.WriteString("\n");

        // Only reset the HBA when firmware didn't enumerate ports — EDK2 on
        // aarch64 boots via virtio and leaves PI at 0; SeaBIOS on x86 already
        // populated PI via its own AHCI init. Driving an HR there zeroes the
        // working PI on ich9-ahci (PI is nominally HwInit but QEMU doesn't
        // restore the firmware-populated bits across HR). When we do reset,
        // poll HR for self-clear then re-set AE — matches the sequence in
        // Linux's ahci_reset_controller.
        if (_generic.ImplementedPorts == 0)
        {
            Serial.WriteString("[AHCI] PI=0, doing HBA reset\n");
            _generic.GlobalHostControl = (1U << 31) | 1U;
            uint resetSpin = 0;
            while ((_generic.GlobalHostControl & 1U) != 0)
            {
                if (++resetSpin > 10_000_000)
                {
                    Serial.WriteString("[AHCI] HBA reset did not complete\n");
                    return false;
                }
            }
            _generic.GlobalHostControl |= 1U << 31; // Re-enable AHCI mode
            Serial.WriteString("[AHCI] After reset: CAP=0x");
            Serial.WriteHex(_generic.Capabilities);
            Serial.WriteString(" PI=0x");
            Serial.WriteHex(_generic.ImplementedPorts);
            Serial.WriteString("\n");
        }

        // QEMU's ich9-ahci on aarch64 virt leaves PI at 0 even after HR
        // (the bits would normally be HwInit-restored). When PI is still
        // empty, write the mask derived from CAP.NP so we can enumerate
        // the controller's ports — CheckPortType filters out empty slots.
        if (_generic.ImplementedPorts == 0)
        {
            uint cap = _generic.Capabilities;
            uint nports = (cap & 0x1F) + 1;
            uint piMask = nports >= 32 ? 0xFFFFFFFFu : ((1u << (int)nports) - 1u);
            Serial.WriteString("[AHCI] PI still 0; deriving from CAP.NP=");
            Serial.WriteNumber(nports);
            Serial.WriteString(" → PI=0x");
            Serial.WriteHex(piMask);
            Serial.WriteString("\n");
            _generic.ImplementedPorts = piMask;
            Serial.WriteString("[AHCI] PI readback=0x");
            Serial.WriteHex(_generic.ImplementedPorts);
            Serial.WriteString("\n");
        }

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

    /// <summary>
    /// HBA reset: write GHC.AE|HR, poll for HR to self-clear, re-enable
    /// AHCI mode. Pattern modeled on Linux's ahci_reset_controller.
    /// </summary>
    public void HbaReset()
    {
        if (_generic == null)
        {
            return;
        }

        _generic.GlobalHostControl = (1U << 31) | 1U;
        uint spin = 0;
        while ((_generic.GlobalHostControl & 1U) != 0)
        {
            Ahci.Wait(1);
            if (++spin > 1_000_000)
            {
                Serial.WriteString("[AHCI] HBA reset did not complete\n");
                return;
            }
        }
        _generic.GlobalHostControl |= 1U << 31; // Re-enable AHCI mode
    }

    private void PrintVersion()
    {
        if (_generic == null)
        {
            Serial.WriteString("Unknown");
            return;
        }
        Serial.WriteNumber((byte)(_generic.AhciVersion >> 24));
        Serial.WriteString(".");
        Serial.WriteNumber((byte)(_generic.AhciVersion >> 16));
        Serial.WriteString(".");
        Serial.WriteNumber((byte)(_generic.AhciVersion >> 8));
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
        _supportsAhciModeOnly = ((_generic.Capabilities >> 18) & 1) == 1;
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

                // Only run a COMRESET when the PHY isn't already up — on x64
                // SeaBIOS trained the link and captured the device's D2H FIS
                // (so PxSIG holds 0x00000101 for SATA). Kicking that port
                // would clear PxSIG to 0xFFFFFFFF and we'd lose the type
                // classification. EDK2 on aarch64 leaves DET=0, so we kick
                // there to train the PHY ourselves.
                if ((portReg.SSTS & 0x0F) != 3)
                {
                    KickPort(portReg);
                }

                uint ssts = portReg.SSTS;
                Serial.WriteString("[AHCI] Port ");
                Serial.WriteNumber(port);
                Serial.WriteString(" SSTS=0x");
                Serial.WriteHex(ssts);
                Serial.WriteString(" SIG=0x");
                Serial.WriteHex(portReg.SIG);
                Serial.WriteString("\n");

                var ipm = (InterfacePowerManagementStatus)((ssts >> 8) & 0x0F);
                var det = (DeviceDetectionStatus)(ssts & 0x0F);
                if (ipm != InterfacePowerManagementStatus.Active ||
                    det != DeviceDetectionStatus.DeviceDetectedWithPhy)
                {
                    implementedPort >>= 1;
                    continue;
                }

                // PortRebase sets FB and enables FIS Receive so the device's
                // post-reset D2H signature FIS gets captured. We use PxSIG
                // afterwards to distinguish SATA from SATAPI/SEMB. The reset
                // path (KickPort + PHY retrain) clears PxSIG to 0xFFFFFFFF
                // first, so re-reading it before FRE is meaningless.
                if (!PortRebase(portReg, port))
                {
                    // Un-rebased port: CLB/FB still hold firmware values, so
                    // doorbells would run the firmware's stale command list.
                    implementedPort >>= 1;
                    continue;
                }

                uint sigRaw = portReg.SIG;
                for (int retry = 0; retry < 200 && sigRaw == 0xFFFFFFFFu; retry++)
                {
                    Ahci.Wait(1000);
                    sigRaw = portReg.SIG;
                }

                PortType portType;
                if (sigRaw == 0xFFFFFFFFu)
                {
                    // No D2H FIS arrived even though PHY is up. Assume SATA —
                    // the only type we currently support — so the test suite
                    // still exercises this port. SATAPI / SEMB drives would
                    // misbehave here; not worth guarding until they're real.
                    Serial.WriteString("[AHCI] Port ");
                    Serial.WriteNumber(port);
                    Serial.WriteString(" PxSIG never populated; assuming SATA\n");
                    portType = PortType.Sata;
                }
                else
                {
                    portType = ClassifySignature(sigRaw, port);
                }
                portReg.PortType = portType;

                if (portType == PortType.Sata)
                {
                    // Per-port containment: a port that misidentifies (the
                    // assume-SATA fallback can hit ATAPI) or whose Identify
                    // times out must be skipped, not unwind the whole boot —
                    // Initialize()'s contract is report-don't-throw.
                    try
                    {
                        Sata sataPort = new(portReg);
                        _ports.Add(sataPort);
                        Serial.WriteString("[AHCI] Initialized SATA port ");
                        Serial.WriteNumber(port);
                        Serial.WriteString("\n");
                    }
                    catch (Exception ex)
                    {
                        Serial.WriteString("[AHCI] Port ");
                        Serial.WriteNumber(port);
                        Serial.WriteString(" bring-up failed: ");
                        Serial.WriteString(ex.Message);
                        Serial.WriteString("\n");
                    }
                }
                else if (portType == PortType.Satapi)
                {
                    Serial.WriteString("[AHCI] Found SATAPI port ");
                    Serial.WriteNumber(port);
                    Serial.WriteString(" (not supported yet)\n");
                }
                else if (portType == PortType.Semb)
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

    private static PortType ClassifySignature(uint sig, uint port)
    {
        uint sigHi = sig >> 16;
        switch ((AhciSignature)sigHi)
        {
            case AhciSignature.Sata: return PortType.Sata;
            case AhciSignature.Satapi: return PortType.Satapi;
            case AhciSignature.Semb: return PortType.Semb;
            case AhciSignature.PortMultiplier: return PortType.PM;
            case AhciSignature.Nothing: return PortType.Nothing;
            default:
                Serial.WriteString("[AHCI] Unknown drive signature 0x");
                Serial.WriteHex(sig);
                Serial.WriteString(" at port ");
                Serial.WriteNumber(port);
                Serial.WriteString(" — skipping\n");
                return PortType.Nothing;
        }
    }

    /// <summary>
    /// Triggers a SATA COMRESET on the port so its PHY runs OOB even when
    /// firmware (EDK2 on aarch64 virt) didn't bring it up. Stops the port's
    /// command engine first, writes <c>PxSCTL.DET=1</c> for ≥1 ms to issue
    /// COMRESET, then clears DET so the PHY trains. Returns early instead of
    /// hanging if no device responds — empty slots stay empty.
    /// </summary>
    private static void KickPort(PortRegisters port)
    {
        port.CMD &= ~(1U << 0); // ST
        port.CMD &= ~(1U << 4); // FRE
        for (int i = 0; i < 100; i++)
        {
            if ((port.CMD & ((1U << 14) | (1U << 15))) == 0)
            {
                break;
            }
            Ahci.Wait(1000);
        }

        port.SCTL = (port.SCTL & ~0xFU) | 1U;
        Ahci.Wait(2000); // hold COMRESET ≥1 ms before clearing
        port.SCTL &= ~0xFU;

        for (int i = 0; i < 100; i++)
        {
            if ((port.SSTS & 0x0F) == 3)
            {
                break;
            }
            Ahci.Wait(1000);
        }

        port.SERR = 0xFFFFFFFFu;
    }

    private bool PortRebase(PortRegisters port, uint portNumber)
    {
        Serial.WriteString("[AHCI] Rebasing port...\n");
        if (!StopCMD(port) && !Sata.PortReset(port))
        {
            // The command engine never stopped: reprogramming CLB/FB with a
            // live engine violates AHCI 10.1.2 (the HBA could fetch garbage
            // command headers and DMA anywhere). Leave the port untouched
            // and report failure — the caller must skip the port, because
            // issuing commands against the firmware's stale command list
            // could "succeed" reading a zeroed bounce buffer and register
            // a bogus zero-capacity device.
            Serial.WriteString("[AHCI] Skipping rebase; port engine still running\n");
            return false;
        }

        ulong clbPhys = PortCommandListPhys(portNumber);
        ulong fbPhys = PortFisReceivePhys(portNumber);
        ulong clbVirt = PortCommandListVirt(portNumber);
        ulong fbVirt = PortFisReceiveVirt(portNumber);

        port.CLB = (uint)(clbPhys & 0xFFFFFFFF);
        port.CLBU = (uint)(clbPhys >> 32);
        port.FB = (uint)(fbPhys & 0xFFFFFFFF);
        port.FBU = (uint)(fbPhys >> 32);

        // PxSERR / PxIS are RW1C: writing all ones clears every latched
        // bit (AHCI 10.1.2 requires SERR fully cleared before ST=1);
        // writing 0 or a partial mask clears nothing.
        port.SERR = 0xFFFFFFFFu;
        port.IS = 0xFFFFFFFFu;
        port.IE = 0;

        MemoryOp.MemSet((byte*)clbVirt, 0, 1024);
        MemoryOp.MemSet((byte*)fbVirt, 0, 256);

        GetCommandHeader(port, portNumber);

        if (!StartCMD(port))
        {
            // PortReset stops the engine; without a StartCMD retry the port
            // would be left with ST=0 and every future doorbell write would
            // hang the command wait.
            if (!Sata.PortReset(port) || !StartCMD(port))
            {
                // Same containment as the engine-still-running case: an
                // offline port must be skipped, not handed to Sata.
                Serial.WriteString("[AHCI] Port failed to start after reset; leaving it offline\n");
                return false;
            }
        }

        // Clear latched events (RW1C) but keep every interrupt source
        // masked: this driver is strictly polled (IssueCommandCore clears
        // PxIS itself) and no AHCI ISR exists. Enabling PxIE here was only
        // benign while GHC.IE stayed 0 — any future GHC.IE=1 would turn the
        // latched events into an interrupt storm with no handler.
        port.IS = 0xFFFFFFFFu;
        port.IE = 0;

        Serial.WriteString("[AHCI] Port rebased\n");
        return true;
    }

    private void GetCommandHeader(PortRegisters port, uint portNumber)
    {
        ulong clbVirt = PortCommandListVirt(portNumber);
        for (uint i = 0; i < 32; i++)
        {
            ulong ctbaPhys = PortCommandTablePhys(portNumber, i);
            ulong ctbaVirt = PortCommandTableVirt(portNumber, i);
            var cmdHeader = new HbaCommandHeader(clbVirt, i)
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
            Ahci.Wait(5000);
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
            Ahci.Wait(5000);
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
            Ahci.Wait(50);
        }
        if (spin == 101)
        {
            return false;
        }

        port.CMD &= ~(1U << 4); // Clear FIS Receive Enable

        if (_supportsCommandListOverride)
        {
            if ((port.TFD & (uint)AtaDeviceStatus.Busy) != 0)
            {
                port.CMD |= 1U << 3;
                // AHCI 1.3.1 s3.3.7: software must wait for CLO to read
                // back 0 before setting ST again; proceeding early would
                // reprogram the port mid-override.
                for (spin = 0; spin < 101; spin++)
                {
                    if ((port.CMD & (1U << 3)) == 0)
                    {
                        break;
                    }
                    Ahci.Wait(50);
                }
            }
        }

        for (spin = 0; spin < 101; spin++)
        {
            if ((port.CMD & (uint)CommandAndStatus.CMDListRunning) == 0 &&
                (port.CMD & (uint)CommandAndStatus.FISReceiveRunning) == 0 &&
                (port.CMD & (uint)CommandAndStatus.StartProcess) == 0 &&
                (port.CMD & (uint)CommandAndStatus.FISReceiveEnable) == 0)
            {
                break;
            }
            Ahci.Wait(5000);
        }
        if (spin == 101)
        {
            // Last-ditch CLO on the way out for HBAs that support it; the
            // old else-arm "clear CLO" write was a no-op (CLO is cleared by
            // hardware, not by software writing 0).
            if (_supportsCommandListOverride)
            {
                port.CMD |= 1U << 3;
            }
            return false;
        }

        return true;
    }
}
