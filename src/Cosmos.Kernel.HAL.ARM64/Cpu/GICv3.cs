// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.HAL.ARM64.Cpu;

/// <summary>
/// ARM Generic Interrupt Controller v3 (GICv3) implementation.
/// GICv3 uses system registers (ICC_*) for the CPU interface instead of MMIO,
/// and adds a Redistributor component (one per CPU).
/// Base addresses are configurable to support both QEMU and real hardware.
/// </summary>
public static partial class GICv3
{
    // Default QEMU virt machine GICv3 base addresses (overridable via Configure)
    private static ulong _gicDistBase = 0x08000000;
    private static ulong _gicReDistBase = 0x080A0000;

    // Discovered redistributor base for the current CPU (found by TYPER walk)
    private static ulong _currentCpuRdBase;
    private static ulong _currentCpuSgiBase;

    // Distributor registers (offsets from GICD_BASE)
    private const uint GICD_CTLR = 0x000;        // Distributor Control
    private const uint GICD_TYPER = 0x004;        // Interrupt Controller Type
    private const uint GICD_IIDR = 0x008;         // Implementer Identification
    private const uint GICD_ISENABLER = 0x100;    // Interrupt Set-Enable (base)
    private const uint GICD_ICENABLER = 0x180;    // Interrupt Clear-Enable (base)
    private const uint GICD_ISPENDR = 0x200;      // Interrupt Set-Pending (base)
    private const uint GICD_ICPENDR = 0x280;      // Interrupt Clear-Pending (base)
    private const uint GICD_IPRIORITYR = 0x400;   // Interrupt Priority (base)
    private const uint GICD_ICFGR = 0xC00;        // Interrupt Configuration (base)
    private const uint GICD_IROUTER = 0x6100;     // Interrupt Routing (base, 64-bit per SPI)

    // GICv3 Distributor CTLR bits
    private const uint GICD_CTLR_RWP = (1u << 31);       // Register Write Pending
    private const uint GICD_CTLR_ARE_NS = (1u << 4);     // Affinity Routing Enable (Non-Secure)
    private const uint GICD_CTLR_ENABLE_G1NS = (1u << 1); // Enable Group 1 Non-Secure
    private const uint GICD_CTLR_ENABLE_G1S = (1u << 2);  // Enable Group 1 Secure
    private const uint GICD_CTLR_ENABLE_G0 = (1u << 0);   // Enable Group 0

    // Redistributor registers (offsets from GICR_BASE per CPU)
    // RD_base frame (first 64KB)
    private const uint GICR_CTLR = 0x000;        // Redistributor Control
    private const uint GICR_IIDR = 0x004;        // Implementer Identification
    private const uint GICR_TYPER = 0x008;       // Redistributor Type (64-bit)
    private const uint GICR_WAKER = 0x014;       // Wake Register

    // SGI_base frame (second 64KB, offset 0x10000 from GICR per-CPU base)
    private const uint GICR_SGI_OFFSET = 0x10000;
    private const uint GICR_ISENABLER0 = 0x100;  // SGI/PPI Set-Enable
    private const uint GICR_ICENABLER0 = 0x180;  // SGI/PPI Clear-Enable
    private const uint GICR_ISPENDR0 = 0x200;    // SGI/PPI Set-Pending
    private const uint GICR_ICPENDR0 = 0x280;    // SGI/PPI Clear-Pending
    private const uint GICR_IPRIORITYR = 0x400;  // SGI/PPI Priority (base)
    private const uint GICR_ICFGR0 = 0xC00;      // SGI Configuration
    private const uint GICR_ICFGR1 = 0xC04;      // PPI Configuration
    private const uint GICR_IGROUPR0 = 0x080;    // SGI/PPI Group
    private const uint GICR_IGRPMODR0 = 0xD00;   // SGI/PPI Group Modifier

    // GICR_WAKER bits
    private const uint GICR_WAKER_PROCESSOR_SLEEP = (1u << 1);
    private const uint GICR_WAKER_CHILDREN_ASLEEP = (1u << 2);

    // GICR_TYPER bits
    private const ulong GICR_TYPER_LAST = (1ul << 4);

    // Redistributor stride: each CPU gets RD_base (64KB) + SGI_base (64KB) = 128KB
    private const ulong GICR_STRIDE = 0x20000;

    // Interrupt type constants (same as GICv2)
    public const uint SGI_START = 0;              // Software Generated Interrupts (0-15)
    public const uint PPI_START = 16;             // Private Peripheral Interrupts (16-31)
    public const uint SPI_START = 32;             // Shared Peripheral Interrupts (32+)

    // Timer interrupt IDs (PPIs - same as GICv2)
    public const uint TIMER_SECURE_PHYS = 29;
    public const uint TIMER_NONSEC_PHYS = 30;
    public const uint TIMER_VIRT = 27;
    public const uint TIMER_HYP = 26;

    private static bool _initialized;
    private static bool _mmioAvailable;

    /// <summary>
    /// Whether the GICv3 has been initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Whether GICD/GICR MMIO is accessible. False on devices where
    /// the GIC bus doesn't respond (e.g., Qualcomm wearable SoCs).
    /// </summary>
    public static bool IsMmioAvailable => _mmioAvailable;

    // Native ICC system register access (GICv3 CPU interface)
    [LibraryImport("*", EntryPoint = "_native_arm64_gicv3_read_icc_iar1_el1")]
    [SuppressGCTransition]
    private static partial uint ReadIAR1();

    [LibraryImport("*", EntryPoint = "_native_arm64_gicv3_write_icc_eoir1_el1")]
    [SuppressGCTransition]
    private static partial void WriteEOIR1(uint intId);

    [LibraryImport("*", EntryPoint = "_native_arm64_gicv3_read_icc_hppir1_el1")]
    [SuppressGCTransition]
    private static partial uint ReadHPPIR1();

    [LibraryImport("*", EntryPoint = "_native_arm64_gicv3_write_icc_pmr_el1")]
    [SuppressGCTransition]
    private static partial void WritePMR(uint priority);

    [LibraryImport("*", EntryPoint = "_native_arm64_gicv3_read_icc_pmr_el1")]
    [SuppressGCTransition]
    private static partial uint ReadPMR();

    [LibraryImport("*", EntryPoint = "_native_arm64_gicv3_write_icc_bpr1_el1")]
    [SuppressGCTransition]
    private static partial void WriteBPR1(uint value);

    [LibraryImport("*", EntryPoint = "_native_arm64_gicv3_write_icc_ctlr_el1")]
    [SuppressGCTransition]
    private static partial void WriteCTLR(uint value);

    [LibraryImport("*", EntryPoint = "_native_arm64_gicv3_read_icc_ctlr_el1")]
    [SuppressGCTransition]
    private static partial uint ReadCTLR();

    [LibraryImport("*", EntryPoint = "_native_arm64_gicv3_write_icc_igrpen1_el1")]
    [SuppressGCTransition]
    private static partial void WriteIGRPEN1(uint value);

    [LibraryImport("*", EntryPoint = "_native_arm64_gicv3_read_icc_igrpen1_el1")]
    [SuppressGCTransition]
    private static partial uint ReadIGRPEN1();

    [LibraryImport("*", EntryPoint = "_native_arm64_gicv3_write_icc_sgi1r_el1")]
    [SuppressGCTransition]
    private static partial void WriteSGI1R(ulong value);

    [LibraryImport("*", EntryPoint = "_native_arm64_gicv3_read_icc_sre_el1")]
    [SuppressGCTransition]
    private static partial uint ReadSRE();

    [LibraryImport("*", EntryPoint = "_native_arm64_gicv3_write_icc_sre_el1")]
    [SuppressGCTransition]
    private static partial void WriteSRE(uint value);

    [LibraryImport("*", EntryPoint = "_native_arm64_read_mpidr_el1")]
    [SuppressGCTransition]
    private static partial ulong ReadMPIDR();

    /// <summary>
    /// Configures the GICv3 base addresses. Must be called before Initialize()
    /// if running on hardware with non-QEMU addresses (e.g., from DTB).
    /// </summary>
    /// <param name="distBase">GICD base address.</param>
    /// <param name="redistBase">GICR base address.</param>
    public static void Configure(ulong distBase, ulong redistBase)
    {
        _gicDistBase = distBase;
        _gicReDistBase = redistBase;
        Serial.Write("[GIC] Configured GICD=0x");
        Serial.WriteHex(distBase);
        Serial.Write(" GICR=0x");
        Serial.WriteHex(redistBase);
        Serial.Write("\n");
    }

    /// <summary>
    /// Initializes the GICv3 distributor, redistributor, and CPU interface.
    /// On real hardware (Snapdragon, Exynos), the secure firmware (EL3/TZ) owns the
    /// distributor. We must NOT disable it entirely — only configure NS Group 1.
    /// </summary>
    /// <param name="sysregOnly">If true, skip all GICD/GICR MMIO and only configure
    /// the CPU interface via ICC_* system registers. Use this on platforms where
    /// GIC MMIO causes a bus hang (e.g., Qualcomm wearable SoCs).</param>
    public static void Initialize(bool sysregOnly = false)
    {
        Serial.Write("[GIC] Initializing GICv3...\n");
        Serial.Write("[GIC] GICD base: 0x");
        Serial.WriteHex(_gicDistBase);
        Serial.Write(" GICR base: 0x");
        Serial.WriteHex(_gicReDistBase);
        Serial.Write("\n");

        // Step 1: Enable system register access (critical for real hardware)
        EnableSystemRegisterAccess();

        if (sysregOnly)
        {
            Serial.Write("[GIC] Sysreg-only mode: skipping GICD/GICR MMIO\n");
            _mmioAvailable = false;
            InitializeSysregOnly();
            return;
        }
        _mmioAvailable = true;

        // Step 3: Read GIC type to get number of interrupt lines
        uint typer = ReadDistributor(GICD_TYPER);
        Serial.Write("[GIC] GICD_TYPER=0x");
        Serial.WriteHex(typer);
        uint itLinesNumber = typer & 0x1F;
        uint maxInterrupts = 32 * (itLinesNumber + 1);
        Serial.Write(" Max interrupts: ");
        Serial.WriteNumber(maxInterrupts);
        Serial.Write("\n");

        // Step 4: Read current CTLR to understand firmware state
        Serial.Write("[GIC] Reading GICD_CTLR...\n");
        uint ctlr = ReadDistributor(GICD_CTLR);
        Serial.Write("[GIC] Current GICD_CTLR=0x");
        Serial.WriteHex(ctlr);
        Serial.Write("\n");

        // Step 4a: Enable affinity routing (ARE_NS) without disabling the distributor.
        // On real hardware, secure firmware may have Group 0 active — do NOT write 0.
        // Just ensure ARE_NS is set, then enable Group 1 NS.
        Serial.Write("[GIC] Setting ARE_NS...\n");
        WriteDistributor(GICD_CTLR, ctlr | GICD_CTLR_ARE_NS);
        WaitForDistributorWrite();
        Serial.Write("[GIC] ARE_NS set\n");

        // Step 5: Configure all SPIs - disable, clear pending, set priority
        Serial.Write("[GIC] Configuring SPIs...\n");
        for (uint i = SPI_START; i < maxInterrupts; i += 32)
        {
            WriteDistributor(GICD_ICENABLER + ((i / 32) * 4), 0xFFFFFFFF);
            WriteDistributor(GICD_ICPENDR + ((i / 32) * 4), 0xFFFFFFFF);
        }

        for (uint i = SPI_START; i < maxInterrupts; i += 4)
        {
            WriteDistributor(GICD_IPRIORITYR + i, 0xA0A0A0A0);
        }

        for (uint i = SPI_START; i < maxInterrupts; i += 16)
        {
            WriteDistributor(GICD_ICFGR + ((i / 16) * 4), 0);
        }
        Serial.Write("[GIC] SPIs configured\n");

        // Step 6: Read current CPU's MPIDR affinity for SPI routing
        ulong mpidr = ReadMPIDR();
        ulong affinity = ExtractAffinity(mpidr);
        Serial.Write("[GIC] CPU MPIDR=0x");
        Serial.WriteHex(mpidr);
        Serial.Write(" affinity=0x");
        Serial.WriteHex(affinity);
        Serial.Write("\n");

        // Route all SPIs to current CPU using MPIDR affinity
        Serial.Write("[GIC] Routing SPIs...\n");
        for (uint i = SPI_START; i < maxInterrupts; i++)
        {
            WriteDistributor64(GICD_IROUTER + ((i - SPI_START) * 8), affinity);
        }
        Serial.Write("[GIC] SPIs routed\n");

        // Step 7: Enable Group 1 NS interrupts in distributor (additive, keep existing bits)
        Serial.Write("[GIC] Enabling G1NS in distributor...\n");
        ctlr = ReadDistributor(GICD_CTLR);
        WriteDistributor(GICD_CTLR, ctlr | GICD_CTLR_ARE_NS | GICD_CTLR_ENABLE_G1NS);
        WaitForDistributorWrite();
        Serial.Write("[GIC] Distributor enabled\n");

        // Step 8: Find and initialize redistributor for current CPU by MPIDR walk
        Serial.Write("[GIC] Finding redistributor...\n");
        if (!FindAndInitRedistributor(mpidr))
        {
            Serial.Write("[GIC] WARNING: Failed to find redistributor, trying index 0\n");
            // Fallback: assume first redistributor
            _currentCpuRdBase = _gicReDistBase;
            _currentCpuSgiBase = _gicReDistBase + GICR_SGI_OFFSET;
            InitializeRedistributorAt(_currentCpuRdBase, _currentCpuSgiBase);
        }

        // Step 9: Initialize CPU interface via system registers
        InitializeCpuInterface();

        _initialized = true;
        Serial.Write("[GIC] GICv3 initialized (full MMIO)\n");
    }

    /// <summary>
    /// System-register-only initialization for platforms where GICD/GICR MMIO
    /// is inaccessible (bus hang). Relies on firmware having already configured
    /// the distributor and redistributor (common on Android/WearOS devices).
    /// Only the CPU interface (ICC_* registers) is touched.
    /// </summary>
    private static void InitializeSysregOnly()
    {
        Serial.Write("[GIC] Sysreg-only init: trusting firmware GIC config\n");

        // The CPU interface is fully system-register-based in GICv3.
        // Firmware already configured:
        //   - GICD: enabled, ARE_NS set, Group 1 NS enabled
        //   - GICR: awake, PPIs/SGIs configured
        // We just need to set up the CPU interface for our EL1 context.
        InitializeCpuInterface();

        _initialized = true;
        Serial.Write("[GIC] GICv3 initialized (sysreg-only)\n");
    }

    /// <summary>
    /// Enables ICC_SRE_EL1.SRE so that system registers can be used for the CPU interface.
    /// On QEMU this is a no-op (already enabled), but on real hardware (e.g., Exynos)
    /// this MUST be done before any ICC_* register access.
    /// </summary>
    private static void EnableSystemRegisterAccess()
    {
        uint sre = ReadSRE();
        Serial.Write("[GIC] ICC_SRE_EL1=0x");
        Serial.WriteHex(sre);
        Serial.Write("\n");

        if ((sre & 0x1) == 0)
        {
            // Enable SRE bit
            sre |= 0x1;
            WriteSRE(sre);

            // Verify it took effect
            sre = ReadSRE();
            if ((sre & 0x1) == 0)
            {
                Serial.Write("[GIC] WARNING: Failed to enable ICC_SRE_EL1.SRE\n");
            }
            else
            {
                Serial.Write("[GIC] ICC_SRE_EL1.SRE enabled\n");
            }
        }
        else
        {
            Serial.Write("[GIC] ICC_SRE_EL1.SRE already enabled\n");
        }
    }

    /// <summary>
    /// Extracts the routing affinity value from MPIDR_EL1 in the format
    /// expected by GICD_IROUTER and GICR_TYPER:
    /// bits [31:24] = Aff3, [23:16] = Aff2, [15:8] = Aff1, [7:0] = Aff0
    /// </summary>
    private static ulong ExtractAffinity(ulong mpidr)
    {
        // MPIDR_EL1: Aff3[39:32], Aff2[23:16], Aff1[15:8], Aff0[7:0]
        // IROUTER:    Aff3[39:32], Aff2[23:16], Aff1[15:8], Aff0[7:0]
        // They share the same layout for the affinity fields
        return mpidr & 0xFF00FFFFFFul; // mask out non-affinity bits (RES0, MT, U)
    }

    /// <summary>
    /// Walks the redistributor chain by reading GICR_TYPER to find the
    /// redistributor whose affinity matches the current CPU's MPIDR.
    /// This is the correct way to find redistributors on real hardware
    /// where stride/layout may differ from QEMU.
    /// </summary>
    private static bool FindAndInitRedistributor(ulong mpidr)
    {
        ulong cpuAff = ExtractAffinity(mpidr);
        ulong rdBase = _gicReDistBase;

        // Walk redistributor frames using GICR_TYPER.Last bit
        for (int i = 0; i < 256; i++) // safety limit
        {
            ulong typer = ReadRedistributor64(rdBase, GICR_TYPER);

            // GICR_TYPER affinity is in bits [39:32][23:8] → same layout as MPIDR
            ulong rdAff = typer >> 32;
            // Shift to match: GICR_TYPER[63:32] contains Aff3[31:24]|0[23]|Aff2[20:16]|Aff1[15:8]|Aff0[7:0]
            // Actually GICR_TYPER bits [39:32] = Aff3, [31:24]=Aff2(??).. let's use the spec:
            // GICR_TYPER: [63:32] = Affinity_Value (Aff3[63:56] Aff2[55:48] Aff1[47:40] Aff0[39:32])
            // But reading 64-bit at offset 0x8 gives us the full register
            // Actually: GICR_TYPER[63:32] = Aff3[63:56] | res0[55:48] | Aff2[47:40] | Aff1[39:32]
            // Wait - let's just compare directly with MPIDR masked:
            // GICR_TYPER affinity in bits[39:32] = Aff0, [47:40] = Aff1, [55:48] = Aff2, [63:56] = Aff3
            ulong rdAffValue = (typer >> 32) & 0xFF00FFFFFFul;

            Serial.Write("[GIC] RD@0x");
            Serial.WriteHex(rdBase);
            Serial.Write(" TYPER=0x");
            Serial.WriteHex(typer);
            Serial.Write(" aff=0x");
            Serial.WriteHex(rdAffValue);
            Serial.Write("\n");

            if (rdAffValue == cpuAff)
            {
                Serial.Write("[GIC] Found matching redistributor at 0x");
                Serial.WriteHex(rdBase);
                Serial.Write("\n");

                _currentCpuRdBase = rdBase;
                _currentCpuSgiBase = rdBase + GICR_SGI_OFFSET;
                InitializeRedistributorAt(rdBase, rdBase + GICR_SGI_OFFSET);
                return true;
            }

            // Check if this is the last redistributor
            if ((typer & GICR_TYPER_LAST) != 0)
            {
                Serial.Write("[GIC] Reached last redistributor without match\n");
                return false;
            }

            // Move to next redistributor frame (RD_base + SGI_base = 128KB)
            rdBase += GICR_STRIDE;
        }

        return false;
    }

    /// <summary>
    /// Initializes a specific redistributor at the given base addresses.
    /// </summary>
    private static void InitializeRedistributorAt(ulong rdBase, ulong sgiBase)
    {
        Serial.Write("[GIC] Initializing redistributor at 0x");
        Serial.WriteHex(rdBase);
        Serial.Write("...\n");

        // Wake up the redistributor
        uint waker = ReadRedistributor(rdBase, GICR_WAKER);
        waker &= ~GICR_WAKER_PROCESSOR_SLEEP;
        WriteRedistributor(rdBase, GICR_WAKER, waker);

        // Wait for children to wake up (with generous timeout for real hardware)
        uint timeout = 10000000;
        while ((ReadRedistributor(rdBase, GICR_WAKER) & GICR_WAKER_CHILDREN_ASLEEP) != 0)
        {
            if (--timeout == 0)
            {
                Serial.Write("[GIC] WARNING: Redistributor wake timeout\n");
                break;
            }
        }

        // Set all SGIs/PPIs to Group 1 Non-Secure
        WriteRedistributor(sgiBase, GICR_IGROUPR0, 0xFFFFFFFF);
        WriteRedistributor(sgiBase, GICR_IGRPMODR0, 0x00000000);

        // Disable all SGIs and PPIs
        WriteRedistributor(sgiBase, GICR_ICENABLER0, 0xFFFFFFFF);

        // Clear all pending
        WriteRedistributor(sgiBase, GICR_ICPENDR0, 0xFFFFFFFF);

        // Set default priority for all SGIs and PPIs
        for (uint i = 0; i < 32; i += 4)
        {
            WriteRedistributor(sgiBase, GICR_IPRIORITYR + i, 0xA0A0A0A0);
        }

        // Configure SGIs as edge-triggered, PPIs as level-triggered
        WriteRedistributor(sgiBase, GICR_ICFGR0, 0);
        WriteRedistributor(sgiBase, GICR_ICFGR1, 0);

        Serial.Write("[GIC] Redistributor initialized\n");
    }

    /// <summary>
    /// Initializes the GICv3 CPU interface via system registers.
    /// </summary>
    public static void InitializeCpuInterface()
    {
        Serial.Write("[GIC] Initializing CPU interface (system registers)...\n");

        // Disable Group 1 interrupts during configuration
        WriteIGRPEN1(0);

        // Set priority mask to allow all priorities
        WritePMR(0xFF);

        // Set binary point to 0
        WriteBPR1(0);

        // Enable Group 1 Non-Secure interrupts
        WriteIGRPEN1(1);

        Serial.Write("[GIC] CPU interface initialized\n");
    }

    /// <summary>
    /// Enables a specific interrupt.
    /// In sysreg-only mode, this is a no-op (firmware config is trusted).
    /// </summary>
    /// <param name="intId">Interrupt ID (0-1019).</param>
    public static void EnableInterrupt(uint intId)
    {
        if (!_mmioAvailable)
        {
            Serial.Write("[GIC] EnableInterrupt(");
            Serial.WriteNumber(intId);
            Serial.Write("): skipped (no MMIO, trusting firmware)\n");
            return;
        }

        if (intId < SPI_START)
        {
            // SGI/PPI: use redistributor SGI_base frame
            uint bit = 1u << (int)(intId % 32);
            WriteRedistributor(_currentCpuSgiBase, GICR_ISENABLER0, bit);
        }
        else
        {
            // SPI: use distributor
            uint regOffset = GICD_ISENABLER + ((intId / 32) * 4);
            uint bit = 1u << (int)(intId % 32);
            WriteDistributor(regOffset, bit);
        }

        Serial.Write("[GIC] Enabled interrupt ");
        Serial.WriteNumber(intId);
        Serial.Write("\n");
    }

    /// <summary>
    /// Disables a specific interrupt.
    /// In sysreg-only mode, this is a no-op.
    /// </summary>
    /// <param name="intId">Interrupt ID.</param>
    public static void DisableInterrupt(uint intId)
    {
        if (!_mmioAvailable)
        {
            return;
        }

        if (intId < SPI_START)
        {
            uint bit = 1u << (int)(intId % 32);
            WriteRedistributor(_currentCpuSgiBase, GICR_ICENABLER0, bit);
        }
        else
        {
            uint regOffset = GICD_ICENABLER + ((intId / 32) * 4);
            uint bit = 1u << (int)(intId % 32);
            WriteDistributor(regOffset, bit);
        }
    }

    /// <summary>
    /// Sets the priority of an interrupt.
    /// In sysreg-only mode, this is a no-op (firmware config is trusted).
    /// </summary>
    /// <param name="intId">Interrupt ID.</param>
    /// <param name="priority">Priority (0 = highest, 0xFF = lowest).</param>
    public static void SetPriority(uint intId, byte priority)
    {
        if (!_mmioAvailable)
        {
            Serial.Write("[GIC] SetPriority(");
            Serial.WriteNumber(intId);
            Serial.Write(", 0x");
            Serial.WriteHex(priority);
            Serial.Write("): skipped (no MMIO)\n");
            return;
        }

        if (intId < SPI_START)
        {
            // SGI/PPI: use redistributor SGI_base frame
            unsafe
            {
                byte* ptr = (byte*)(_currentCpuSgiBase + GICR_IPRIORITYR + intId);
                *ptr = priority;
            }
        }
        else
        {
            // SPI: use distributor
            uint regOffset = GICD_IPRIORITYR + intId;
            unsafe
            {
                byte* ptr = (byte*)(_gicDistBase + regOffset);
                *ptr = priority;
            }
        }
    }

    /// <summary>
    /// Acknowledges an interrupt and returns its ID.
    /// Uses ICC_IAR1_EL1 system register.
    /// </summary>
    /// <returns>The interrupt ID, or 1023 if spurious.</returns>
    public static uint AcknowledgeInterrupt()
    {
        return ReadIAR1() & 0x3FF;
    }

    /// <summary>
    /// Signals the end of interrupt processing.
    /// Uses ICC_EOIR1_EL1 system register.
    /// </summary>
    /// <param name="intId">The interrupt ID that was acknowledged.</param>
    public static void EndOfInterrupt(uint intId)
    {
        WriteEOIR1(intId);
    }

    /// <summary>
    /// Checks if an interrupt is pending.
    /// In sysreg-only mode, uses ICC_HPPIR1_EL1 to check highest pending.
    /// </summary>
    /// <param name="intId">Interrupt ID.</param>
    /// <returns>True if pending.</returns>
    public static bool IsInterruptPending(uint intId)
    {
        if (!_mmioAvailable)
        {
            // Best effort: check if the highest pending interrupt matches
            uint hppir = ReadHPPIR1() & 0x3FF;
            return hppir == intId;
        }

        if (intId < SPI_START)
        {
            uint bit = 1u << (int)(intId % 32);
            return (ReadRedistributor(_currentCpuSgiBase, GICR_ISPENDR0) & bit) != 0;
        }
        else
        {
            uint regOffset = GICD_ISPENDR + ((intId / 32) * 4);
            uint bit = 1u << (int)(intId % 32);
            return (ReadDistributor(regOffset) & bit) != 0;
        }
    }

    /// <summary>
    /// Clears a pending interrupt.
    /// In sysreg-only mode, this is a no-op.
    /// </summary>
    /// <param name="intId">Interrupt ID.</param>
    public static void ClearPending(uint intId)
    {
        if (!_mmioAvailable)
        {
            return;
        }

        if (intId < SPI_START)
        {
            uint bit = 1u << (int)(intId % 32);
            WriteRedistributor(_currentCpuSgiBase, GICR_ICPENDR0, bit);
        }
        else
        {
            uint regOffset = GICD_ICPENDR + ((intId / 32) * 4);
            uint bit = 1u << (int)(intId % 32);
            WriteDistributor(regOffset, bit);
        }
    }

    /// <summary>
    /// Configures an interrupt as edge-triggered or level-triggered.
    /// In sysreg-only mode, this is a no-op (firmware config is trusted).
    /// </summary>
    /// <param name="intId">Interrupt ID.</param>
    /// <param name="edgeTriggered">True for edge-triggered, false for level-triggered.</param>
    public static void ConfigureInterrupt(uint intId, bool edgeTriggered)
    {
        if (!_mmioAvailable)
        {
            return;
        }

        if (intId < SPI_START)
        {
            // SGI/PPI: use redistributor
            uint regIdx = intId < 16 ? 0u : 1u;
            uint regOffset = (regIdx == 0) ? GICR_ICFGR0 : GICR_ICFGR1;
            uint localId = intId - (regIdx * 16);
            uint shift = localId * 2;
            uint value = ReadRedistributor(_currentCpuSgiBase, regOffset);

            if (edgeTriggered)
            {
                value |= (2u << (int)shift);
            }
            else
            {
                value &= ~(2u << (int)shift);
            }

            WriteRedistributor(_currentCpuSgiBase, regOffset, value);
        }
        else
        {
            uint regOffset = GICD_ICFGR + ((intId / 16) * 4);
            uint shift = (intId % 16) * 2;
            uint value = ReadDistributor(regOffset);

            if (edgeTriggered)
            {
                value |= (2u << (int)shift);
            }
            else
            {
                value &= ~(2u << (int)shift);
            }

            WriteDistributor(regOffset, value);
        }
    }

    /// <summary>
    /// Sends a Software Generated Interrupt (SGI) to the specified target.
    /// GICv3 uses ICC_SGI1R_EL1 for SGI generation.
    /// </summary>
    /// <param name="sgiId">SGI ID (0-15).</param>
    /// <param name="targetSelf">If true, target the current CPU.</param>
    public static void SendSGI(uint sgiId, bool targetSelf)
    {
        // ICC_SGI1R_EL1 format:
        // [27:24] = INTID (SGI number)
        // [40]    = IRM (1 = all other PEs, 0 = use target list)
        // [15:0]  = TargetList (bit mask of target CPUs in lowest affinity level)
        ulong value = ((ulong)(sgiId & 0xF) << 24);

        if (targetSelf)
        {
            // Target self: set TargetList bit 0 (assumes CPU 0)
            value |= 1;
        }
        else
        {
            // Broadcast to all other PEs
            value |= (1ul << 40); // IRM = 1
        }

        WriteSGI1R(value);
    }

    /// <summary>
    /// Detects whether the system has GICv3 support by reading GICD_PIDR2.ArchRev.
    /// Uses a two-step probe: first reads at the GICv2-compatible offset (0xFE8, within
    /// the 4KB page - always safe), then falls back to the GICv3 offset (0xFFE8, within
    /// the 64KB page) if the first read returns zero (GICv3 returns RAZ for reserved
    /// offsets in its first 4KB).
    /// </summary>
    /// <param name="distBase">GICD base address to probe.</param>
    /// <returns>True if GICv3 (ArchRev >= 3).</returns>
    public static bool IsGICv3Available(ulong distBase)
    {
        unsafe
        {
            // Step 1: Read PIDR2 at GICv2 offset (0xFE8) - always within 4KB, safe for both v2 and v3
            uint* ptr = (uint*)(distBase + 0xFE8);
            uint pidr2 = System.Threading.Volatile.Read(ref *ptr);
            uint archRev = (pidr2 >> 4) & 0xF;

            Serial.Write("[GIC] PIDR2@0xFE8=0x");
            Serial.WriteHex(pidr2);

            if (pidr2 != 0)
            {
                // Got a valid PIDR2 from the 4KB-compatible offset
                Serial.Write(" ArchRev=");
                Serial.WriteNumber(archRev);
                Serial.Write("\n");
                return archRev >= 3;
            }

            // Step 2: PIDR2 at 0xFE8 returned 0 - this happens on GICv3 where
            // the first 4KB doesn't have ID registers. Try 0xFFE8 (64KB layout).
            // This is safe because if 0xFE8 returned 0 (not faulted), the region
            // is mapped to at least 4KB, and a GICv3 maps 64KB.
            Serial.Write(" (zero, trying 0xFFE8)\n");
            ptr = (uint*)(distBase + 0xFFE8);
            pidr2 = System.Threading.Volatile.Read(ref *ptr);
            archRev = (pidr2 >> 4) & 0xF;

            Serial.Write("[GIC] PIDR2@0xFFE8=0x");
            Serial.WriteHex(pidr2);
            Serial.Write(" ArchRev=");
            Serial.WriteNumber(archRev);
            Serial.Write("\n");

            return archRev >= 3;
        }
    }

    // Wait for distributor write to complete
    private static void WaitForDistributorWrite()
    {
        uint timeout = 1000000;
        while ((ReadDistributor(GICD_CTLR) & GICD_CTLR_RWP) != 0)
        {
            if (--timeout == 0)
            {
                Serial.Write("[GIC] WARNING: Distributor RWP timeout\n");
                break;
            }
        }
    }

    // Distributor MMIO access (uses runtime _gicDistBase)
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.NoInlining)]
    private static uint ReadDistributor(uint offset)
    {
        unsafe
        {
            uint* ptr = (uint*)(_gicDistBase + offset);
            return System.Threading.Volatile.Read(ref *ptr);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.NoInlining)]
    private static void WriteDistributor(uint offset, uint value)
    {
        unsafe
        {
            uint* ptr = (uint*)(_gicDistBase + offset);
            System.Threading.Volatile.Write(ref *ptr, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.NoInlining)]
    private static void WriteDistributor64(uint offset, ulong value)
    {
        unsafe
        {
            ulong* ptr = (ulong*)(_gicDistBase + offset);
            System.Threading.Volatile.Write(ref *ptr, value);
        }
    }

    // Redistributor MMIO access
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.NoInlining)]
    private static uint ReadRedistributor(ulong baseAddr, uint offset)
    {
        unsafe
        {
            uint* ptr = (uint*)(baseAddr + offset);
            return System.Threading.Volatile.Read(ref *ptr);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.NoInlining)]
    private static ulong ReadRedistributor64(ulong baseAddr, uint offset)
    {
        unsafe
        {
            ulong* ptr = (ulong*)(baseAddr + offset);
            return System.Threading.Volatile.Read(ref *ptr);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.NoInlining)]
    private static void WriteRedistributor(ulong baseAddr, uint offset, uint value)
    {
        unsafe
        {
            uint* ptr = (uint*)(baseAddr + offset);
            System.Threading.Volatile.Write(ref *ptr, value);
        }
    }
}
