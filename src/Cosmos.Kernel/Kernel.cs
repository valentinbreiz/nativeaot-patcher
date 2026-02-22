using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL.Cpu;
using Cosmos.Kernel.HAL.Cpu.Data;

namespace Cosmos.Kernel;

public class Kernel
{
    // CosmosOS version - keep in sync with kmain.h
    public const int VersionMajor = 3;
    public const int VersionMinor = 0;
    public const int VersionPatch = 0;
    public const string VersionString = "3.0.37";
    public const string Codename = "gen3";
    public const string VersionBanner = $"CosmosOS v{VersionString} ({Codename}) - Managed runtime active";
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
    /// Initialize serial port (COM1 at 115200 baud, 8N1)
    /// Must be called before any serial output
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "__cosmos_serial_init")]
    public static void CosmosSerialInit()
    {
        Serial.ComInit();
    }

    /// <summary>
    /// Write string to serial port for C library code logging
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "__cosmos_serial_write")]
    public static void CosmosSerialWrite(byte* str)
    {
        if (str == null)
            return;

        // C strings are null-terminated, write char by char
        // Use while loop with explicit pointer arithmetic to avoid potential codegen issues
        byte* p = str;
        while (*p != 0)
        {
            Serial.ComWrite(*p);
            p++;
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
