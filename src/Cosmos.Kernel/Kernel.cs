using System.Runtime.InteropServices;
using Cosmos.Build.API.Enum;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.Acpi;
using Cosmos.Kernel.HAL.Cpu;
using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.HAL.Pci;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Graphics;
using Cosmos.Kernel.Core.Runtime;

namespace Cosmos.Kernel;

public class Kernel
{

    /// <summary>
    /// Gets the current platform HAL, if available.
    /// </summary>
    public static PlatformArchitecture Architecture => PlatformHAL.Architecture;

    [UnmanagedCallersOnly(EntryPoint = "__Initialize_Kernel")]
    public static unsafe void Initialize()
    {
        // Initialize serial output first for diagnostics
        Serial.ComInit();
        Serial.WriteString("UART started.\n");
        Serial.WriteString("CosmosOS gen3 v0.1.3 booted.\n");

        // Initialize heap for memory allocations
        // Parameters are ignored - heap initialization uses Limine memory map
        MemoryOp.InitializeHeap(0, 0);

        // Initialize platform-specific HAL
        PlatformHAL.Initialize();

        // TODO: Re-enable after fixing stack corruption in module initialization
        // Initialize graphics framebuffer
        // KernelConsole.Initialize();

        // Initialize serial output
        Serial.ComInit();

        if (PlatformHAL.Architecture == PlatformArchitecture.X64)
        {
            Serial.WriteString("Architecture: x86-64.\n");
        }
        else if (PlatformHAL.Architecture == PlatformArchitecture.ARM64)
        {
            Serial.WriteString("Architecture: ARM64/AArch64.\n");
        }
        else
        {
            Serial.WriteString("Architecture: Unknown.\n");
        }

        // Retrieve and display ACPI MADT information (initialized during early boot)
        if (Acpi.DisplayMadtInfo())
        {
            Serial.WriteString("\n");
        }

        InterruptManager.Initialize();

        PciManager.Setup();

        Serial.WriteString("CosmosOS gen3 v0.1.3 booted.\n");

        // Initialize managed modules
        ManagedModule.InitializeModules();
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

public static unsafe class AcpiBridge
{
    /// <summary>
    /// Get RSDP address from Limine bootloader for early ACPI initialization in C code
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

    /// <summary>
    /// Allocate memory from Cosmos heap for ACPI/LAI use
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
    /// Write string to serial port for ACPI/LAI logging
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
