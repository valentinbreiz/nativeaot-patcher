using System.Runtime.InteropServices;
using Cosmos.Build.API.Enum;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL;
#if ARCH_X64
using Cosmos.Kernel.HAL.Acpi;
using Cosmos.Kernel.HAL.Devices.Input;
using Cosmos.Kernel.HAL.X64.Cpu;
using Cosmos.Kernel.Services.Keyboard;
#endif
using Cosmos.Kernel.HAL.Cpu;
using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.HAL.Pci;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Graphics;
using Cosmos.Kernel.Core.Runtime;

namespace Cosmos.Kernel;

public class Kernel
{
    // CosmosOS version - keep in sync with kmain.h
    public const int VersionMajor = 3;
    public const int VersionMinor = 0;
    public const int VersionPatch = 0;
    public const string VersionString = "3.0.0";
    public const string Codename = "gen3";

    /// <summary>
    /// Gets the current platform HAL, if available.
    /// </summary>
    public static PlatformArchitecture Architecture => PlatformHAL.Architecture;

    [UnmanagedCallersOnly(EntryPoint = "__Initialize_Kernel")]
    public static unsafe void Initialize()
    {
        // Display version banner
        Serial.WriteString("[KERNEL]   - CosmosOS v");
        Serial.WriteString(VersionString);
        Serial.WriteString(" (");
        Serial.WriteString(Codename);
        Serial.WriteString(") - Managed runtime active\n");

        // Display architecture
        Serial.WriteString("[KERNEL]   - Architecture: ");
        if (PlatformHAL.Architecture == PlatformArchitecture.X64)
        {
            Serial.WriteString("x86-64\n");
        }
        else if (PlatformHAL.Architecture == PlatformArchitecture.ARM64)
        {
            Serial.WriteString("ARM64/AArch64\n");
        }
        else
        {
            Serial.WriteString("Unknown\n");
        }

        // Initialize heap for memory allocations
        Serial.WriteString("[KERNEL]   - Initializing heap...\n");
        MemoryOp.InitializeHeap(0, 0);

        // Initialize managed modules
        Serial.WriteString("[KERNEL]   - Initializing managed modules...\n");
        ManagedModule.InitializeModules();

        // Initialize platform-specific HAL
        Serial.WriteString("[KERNEL]   - Initializing HAL...\n");
        PlatformHAL.Initialize();

        // Initialize interrupts
        Serial.WriteString("[KERNEL]   - Initializing interrupts...\n");
        InterruptManager.Initialize();

#if ARCH_X64
        Serial.WriteString("[KERNEL]   - Initializing PCI...\n");
        PciManager.Setup();

        // Retrieve and display ACPI MADT information (initialized during early boot)
        Serial.WriteString("[KERNEL]   - Displaying ACPI MADT info...\n");
        Acpi.DisplayMadtInfo();

        // Initialize APIC (Advanced Programmable Interrupt Controller)
        Serial.WriteString("[KERNEL]   - Initializing APIC...\n");
        ApicManager.Initialize();

        // Initialize PS/2 Controller BEFORE enabling keyboard IRQ
        Serial.WriteString("[KERNEL]   - Initializing PS/2 controller...\n");
        var ps2Controller = new PS2Controller();
        ps2Controller.Initialize();

        // Initialize Keyboard Manager (sets up KeyCallback)
        Serial.WriteString("[KERNEL]   - Initializing keyboard manager...\n");
        KeyboardManager.Initialize();

        // Register keyboard IRQ handler (this also routes IRQ1 through APIC)
        Serial.WriteString("[KERNEL]   - Registering keyboard IRQ handler...\n");
        PS2Keyboard.RegisterIRQHandler();
#endif

        Serial.WriteString("[KERNEL] Phase 3: Complete\n");
    }

    /// <summary>
    /// Halt the CPU using platform-specific implementation.
    /// </summary>
    public static void Halt()
    {
        if (PlatformHAL.CpuOps != null)
        {
            PlatformHAL.CpuOps.Halt();
        }
        else
        {
            while (true) { }
        }
    }
}

public static unsafe class InterruptBridge
{
    [UnmanagedCallersOnly(EntryPoint = "__managed__irq")]
    public static void IrqHandlerNative(IRQContext* ctx)
    {
        InterruptManager.Dispatch(ref *ctx);
    }
}

/// <summary>
/// Bridge functions for C library code (ACPI, libc stubs) to call C# methods
/// NOTE: These are NOT called from C bootstrap (kmain.c) - only from library code
/// C bootstrap uses pure C implementations for clean architecture
/// </summary>
public static unsafe class KernelBridge
{
    /// <summary>
    /// Write string to serial port for C library code logging
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "__cosmos_serial_write")]
    public static void CosmosSerialWrite(byte* str)
    {
        // C strings are null-terminated, write char by char
        for (int i = 0; str[i] != 0; i++)
        {
            Serial.ComWrite(str[i]);
        }
    }

    /// <summary>
    /// Write a 32-bit value as hex to serial port
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "__cosmos_serial_write_hex_u32")]
    public static void CosmosSerialWriteHexU32(uint value)
    {
        Serial.WriteHexWithPrefix(value);
    }

    /// <summary>
    /// Write a 64-bit value as hex to serial port
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "__cosmos_serial_write_hex_u64")]
    public static void CosmosSerialWriteHexU64(ulong value)
    {
        Serial.WriteHexWithPrefix(value);
    }

    /// <summary>
    /// Write a 32-bit value as decimal to serial port
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "__cosmos_serial_write_dec_u32")]
    public static void CosmosSerialWriteDecU32(uint value)
    {
        Serial.WriteNumber(value);
    }

    /// <summary>
    /// Write a 64-bit value as decimal to serial port
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "__cosmos_serial_write_dec_u64")]
    public static void CosmosSerialWriteDecU64(ulong value)
    {
        Serial.WriteNumber(value);
    }

    /// <summary>
    /// Allocate memory from Cosmos heap
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "__cosmos_heap_alloc")]
    public static void* CosmosHeapAlloc(nuint size)
    {
        return MemoryOp.Alloc((uint)size);
    }

    /// <summary>
    /// Free memory from Cosmos heap
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "__cosmos_heap_free")]
    public static void CosmosHeapFree(void* ptr)
    {
        MemoryOp.Free(ptr);
    }

    /// <summary>
    /// Copy memory using Cosmos MemoryOp.MemCopy
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "__cosmos_memcpy")]
    public static unsafe void* CosmosMemCopy(void* dest, void* src, nuint count)
    {
        MemoryOp.MemCopy((byte*)dest, (byte*)src, (int)count);
        return dest;
    }

    /// <summary>
    /// Compare memory using Cosmos MemoryOp.MemCmp
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "__cosmos_memcmp")]
    public static unsafe int CosmosMemCmp(void* s1, void* s2, nuint count)
    {
        bool equal = MemoryOp.MemCmp((uint*)s1, (uint*)s2, (int)(count / sizeof(uint)));
        return equal ? 0 : 1;
    }
}

#if ARCH_X64
/// <summary>
/// Wrapper for C code to access managed Limine data (RSDP address)
/// NOTE: This is a data accessor wrapper - C code gets managed data, then continues in C
/// We do NOT call C code from managed code - only provide data access
/// </summary>
public static unsafe class AcpiBridge
{
    /// <summary>
    /// Wrapper to expose Limine RSDP address to C bootstrap for LAI ACPI initialization
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "__get_limine_rsdp_address")]
    public static void* GetLimineRsdpAddress()
    {
        if (Limine.Rsdp.Response != null)
        {
            return Limine.Rsdp.Response->Address;
        }
        return null;
    }
}
#endif
