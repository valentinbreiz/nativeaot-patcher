// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.ARM64.Cpu;

/// <summary>
/// ARM Generic Interrupt Controller v2 (GICv2) implementation.
/// Used on QEMU virt machine and many ARM64 platforms.
/// </summary>
public static class GICv2
{
    /// <summary>Default GICD (distributor) base address on the QEMU virt machine.</summary>
    private const ulong DefaultGicdBase = 0x08000000;
    /// <summary>Default GICC (CPU interface) base address on the QEMU virt machine.</summary>
    private const ulong DefaultGiccBase = 0x08010000;

    // Default QEMU virt machine GIC base addresses (overridable via Configure)
    private static ulong _gicDistBase = DefaultGicdBase;
    private static ulong _gicCpuBase = DefaultGiccBase;

    // Distributor registers (offsets from GICD_BASE)
    private const uint GICD_CTLR = 0x000;        // Distributor Control
    private const uint GICD_TYPER = 0x004;       // Interrupt Controller Type
    private const uint GICD_ISENABLER = 0x100;   // Interrupt Set-Enable (base)
    private const uint GICD_ICENABLER = 0x180;   // Interrupt Clear-Enable (base)
    private const uint GICD_ISPENDR = 0x200;     // Interrupt Set-Pending (base)
    private const uint GICD_ICPENDR = 0x280;     // Interrupt Clear-Pending (base)
    private const uint GICD_IPRIORITYR = 0x400;  // Interrupt Priority (base)
    private const uint GICD_ITARGETSR = 0x800;   // Interrupt Processor Targets (base)
    private const uint GICD_ICFGR = 0xC00;       // Interrupt Configuration (base)

    // CPU Interface registers (offsets from GICC_BASE)
    private const uint GICC_CTLR = 0x000;        // CPU Interface Control
    private const uint GICC_PMR = 0x004;         // Priority Mask
    private const uint GICC_BPR = 0x008;         // Binary Point
    private const uint GICC_IAR = 0x00C;         // Interrupt Acknowledge
    private const uint GICC_EOIR = 0x010;        // End of Interrupt
    private const uint GICC_RPR = 0x014;         // Running Priority
    private const uint GICC_HPPIR = 0x018;       // Highest Priority Pending Interrupt

    // Register field masks and layout values (GICv2 Architecture Specification)
    /// <summary>GICD_TYPER.ITLinesNumber field mask (bits [4:0]); interrupt count is 32 * (N + 1).</summary>
    private const uint ItLinesNumberMask = 0x1F;
    /// <summary>GICC_IAR interrupt ID field mask (bits [9:0]); 1023 indicates a spurious interrupt.</summary>
    private const uint IarInterruptIdMask = 0x3FF;
    /// <summary>Enable bit of GICD_CTLR / GICC_CTLR (bit 0).</summary>
    private const uint CtlrEnable = 1;
    /// <summary>GICC_PMR value allowing all priorities (0xFF = lowest priority threshold).</summary>
    private const uint PriorityMaskAllowAll = 0xFF;
    /// <summary>All-ones mask covering every interrupt bit in a 32-bit enable/pending register.</summary>
    private const uint AllInterruptsMask = 0xFFFFFFFF;
    /// <summary>Default priority 0xA0 replicated into each byte of a GICD_IPRIORITYR word (lower value = higher priority).</summary>
    private const uint DefaultPriorityAllBytes = 0xA0A0A0A0;
    /// <summary>CPU 0 target mask (0x01) replicated into each byte of a GICD_ITARGETSR word.</summary>
    private const uint TargetCpu0AllBytes = 0x01010101;
    /// <summary>Edge-triggered configuration value (0b10) of a 2-bit GICD_ICFGR field.</summary>
    private const uint CfgEdgeTriggeredValue = 2u;

    // Register layout strides (GICv2 Architecture Specification)
    /// <summary>Interrupts covered by one 32-bit enable/pending register (one bit per interrupt).</summary>
    private const uint IrqsPerBitRegister = 32;
    /// <summary>Interrupts covered by one 32-bit priority/target register (one byte per interrupt).</summary>
    private const uint IrqsPerByteRegister = 4;
    /// <summary>Interrupts covered by one 32-bit configuration register (two bits per interrupt).</summary>
    private const uint IrqsPerCfgRegister = 16;
    /// <summary>Width in bits of one GICD_ICFGR configuration field.</summary>
    private const uint CfgBitsPerIrq = 2;
    /// <summary>Byte stride between consecutive 32-bit GIC registers.</summary>
    private const uint RegisterStrideBytes = 4;

    // Interrupt type constants
    public const uint SGI_START = 0;             // Software Generated Interrupts (0-15)
    public const uint PPI_START = 16;            // Private Peripheral Interrupts (16-31)
    public const uint SPI_START = 32;            // Shared Peripheral Interrupts (32+)

    // Timer interrupt IDs (PPIs)
    public const uint TIMER_SECURE_PHYS = 29;    // Secure Physical Timer
    public const uint TIMER_NONSEC_PHYS = 30;    // Non-secure Physical Timer
    public const uint TIMER_VIRT = 27;           // Virtual Timer
    public const uint TIMER_HYP = 26;            // Hypervisor Timer

    private static bool _initialized;

    /// <summary>
    /// Whether the GIC has been initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Configures the GICv2 base addresses. Must be called before Initialize()
    /// if running on hardware with non-QEMU addresses (e.g., from DTB).
    /// </summary>
    /// <param name="distBase">GICD base address.</param>
    /// <param name="cpuBase">GICC base address.</param>
    public static void Configure(ulong distBase, ulong cpuBase)
    {
        _gicDistBase = distBase;
        _gicCpuBase = cpuBase;
        Serial.Write("[GIC] Configured GICD=0x");
        Serial.WriteHex(distBase);
        Serial.Write(" GICC=0x");
        Serial.WriteHex(cpuBase);
        Serial.Write("\n");
    }

    /// <summary>
    /// Initializes the GIC distributor and CPU interface.
    /// </summary>
    public static void Initialize()
    {
        Serial.Write("[GIC] Initializing GICv2...\n");

        // Read GIC type to get number of interrupt lines
        uint typer = ReadDistributor(GICD_TYPER);
        uint itLinesNumber = typer & ItLinesNumberMask;
        uint maxInterrupts = IrqsPerBitRegister * (itLinesNumber + 1);
        Serial.Write("[GIC] Max interrupts: ");
        Serial.WriteNumber(maxInterrupts);
        Serial.Write("\n");

        // Disable distributor during configuration
        WriteDistributor(GICD_CTLR, 0);

        // Configure all SPIs (shared peripheral interrupts)
        // For PPIs (16-31), they are banked per-CPU and need different handling
        for (uint i = SPI_START; i < maxInterrupts; i += IrqsPerBitRegister)
        {
            // Disable all interrupts in this group
            WriteDistributor(GICD_ICENABLER + ((i / IrqsPerBitRegister) * RegisterStrideBytes), AllInterruptsMask);
            // Clear all pending
            WriteDistributor(GICD_ICPENDR + ((i / IrqsPerBitRegister) * RegisterStrideBytes), AllInterruptsMask);
        }

        // Set all SPI priorities to default (lower value = higher priority)
        for (uint i = SPI_START; i < maxInterrupts; i += IrqsPerByteRegister)
        {
            WriteDistributor(GICD_IPRIORITYR + i, DefaultPriorityAllBytes);
        }

        // Target all SPIs to CPU 0
        for (uint i = SPI_START; i < maxInterrupts; i += IrqsPerByteRegister)
        {
            WriteDistributor(GICD_ITARGETSR + i, TargetCpu0AllBytes);
        }

        // Configure all SPIs as level-triggered
        for (uint i = SPI_START; i < maxInterrupts; i += IrqsPerCfgRegister)
        {
            WriteDistributor(GICD_ICFGR + ((i / IrqsPerCfgRegister) * RegisterStrideBytes), 0);
        }

        // Enable distributor
        WriteDistributor(GICD_CTLR, CtlrEnable);

        // Initialize CPU interface
        InitializeCpuInterface();

        _initialized = true;
        Serial.Write("[GIC] GICv2 initialized\n");
    }

    /// <summary>
    /// Initializes the GIC CPU interface for the current CPU.
    /// </summary>
    public static void InitializeCpuInterface()
    {
        Serial.Write("[GIC] Initializing CPU interface...\n");

        // Disable CPU interface during configuration
        WriteCpuInterface(GICC_CTLR, 0);

        // Set priority mask to allow all priorities (0xFF = lowest priority threshold)
        WriteCpuInterface(GICC_PMR, PriorityMaskAllowAll);

        // Set binary point to 0 (all priority bits used for preemption)
        WriteCpuInterface(GICC_BPR, 0);

        // Enable CPU interface
        WriteCpuInterface(GICC_CTLR, CtlrEnable);

        Serial.Write("[GIC] CPU interface initialized\n");
    }

    /// <summary>
    /// Enables a specific interrupt.
    /// </summary>
    /// <param name="intId">Interrupt ID (0-1019).</param>
    public static void EnableInterrupt(uint intId)
    {
        uint regOffset = GICD_ISENABLER + ((intId / IrqsPerBitRegister) * RegisterStrideBytes);
        uint bit = 1u << (int)(intId % IrqsPerBitRegister);
        WriteDistributor(regOffset, bit);

        Serial.Write("[GIC] Enabled interrupt ");
        Serial.WriteNumber(intId);
        Serial.Write("\n");
    }

    /// <summary>
    /// Disables a specific interrupt.
    /// </summary>
    /// <param name="intId">Interrupt ID.</param>
    public static void DisableInterrupt(uint intId)
    {
        uint regOffset = GICD_ICENABLER + ((intId / IrqsPerBitRegister) * RegisterStrideBytes);
        uint bit = 1u << (int)(intId % IrqsPerBitRegister);
        WriteDistributor(regOffset, bit);
    }

    /// <summary>
    /// Sets the priority of an interrupt.
    /// </summary>
    /// <param name="intId">Interrupt ID.</param>
    /// <param name="priority">Priority (0 = highest, 0xFF = lowest).</param>
    public static void SetPriority(uint intId, byte priority)
    {
        uint regOffset = GICD_IPRIORITYR + intId;
        // Write single byte at the correct offset
        unsafe
        {
            byte* ptr = (byte*)(_gicDistBase + regOffset);
            *ptr = priority;
        }
    }

    /// <summary>
    /// Acknowledges an interrupt and returns its ID.
    /// Must be called at the start of interrupt handling.
    /// </summary>
    /// <returns>The interrupt ID, or 1023 if spurious.</returns>
    public static uint AcknowledgeInterrupt()
    {
        return ReadCpuInterface(GICC_IAR) & IarInterruptIdMask;
    }

    /// <summary>
    /// Signals the end of interrupt processing.
    /// Must be called at the end of interrupt handling.
    /// </summary>
    /// <param name="intId">The interrupt ID that was acknowledged.</param>
    public static void EndOfInterrupt(uint intId)
    {
        WriteCpuInterface(GICC_EOIR, intId);
    }

    /// <summary>
    /// Checks if an interrupt is pending.
    /// </summary>
    /// <param name="intId">Interrupt ID.</param>
    /// <returns>True if pending.</returns>
    public static bool IsInterruptPending(uint intId)
    {
        uint regOffset = GICD_ISPENDR + ((intId / IrqsPerBitRegister) * RegisterStrideBytes);
        uint bit = 1u << (int)(intId % IrqsPerBitRegister);
        return (ReadDistributor(regOffset) & bit) != 0;
    }

    /// <summary>
    /// Clears a pending interrupt.
    /// </summary>
    /// <param name="intId">Interrupt ID.</param>
    public static void ClearPending(uint intId)
    {
        uint regOffset = GICD_ICPENDR + ((intId / IrqsPerBitRegister) * RegisterStrideBytes);
        uint bit = 1u << (int)(intId % IrqsPerBitRegister);
        WriteDistributor(regOffset, bit);
    }

    /// <summary>
    /// Configures an interrupt as edge-triggered or level-triggered.
    /// </summary>
    /// <param name="intId">Interrupt ID.</param>
    /// <param name="edgeTriggered">True for edge-triggered, false for level-triggered.</param>
    public static void ConfigureInterrupt(uint intId, bool edgeTriggered)
    {
        uint regOffset = GICD_ICFGR + ((intId / IrqsPerCfgRegister) * RegisterStrideBytes);
        uint shift = (intId % IrqsPerCfgRegister) * CfgBitsPerIrq;
        uint value = ReadDistributor(regOffset);

        if (edgeTriggered)
        {
            value |= (CfgEdgeTriggeredValue << (int)shift);  // Edge-triggered
        }
        else
        {
            value &= ~(CfgEdgeTriggeredValue << (int)shift); // Level-triggered
        }

        WriteDistributor(regOffset, value);
    }

    // Helper methods for memory-mapped I/O
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
    private static uint ReadCpuInterface(uint offset)
    {
        unsafe
        {
            uint* ptr = (uint*)(_gicCpuBase + offset);
            return System.Threading.Volatile.Read(ref *ptr);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.NoInlining)]
    private static void WriteCpuInterface(uint offset, uint value)
    {
        unsafe
        {
            uint* ptr = (uint*)(_gicCpuBase + offset);
            System.Threading.Volatile.Write(ref *ptr, value);
        }
    }
}
