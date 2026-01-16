using System.Runtime.InteropServices;
using Cosmos.Build.API.Enum;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.Core.Scheduler.Stride;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.Cpu;
using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Timer;

namespace Cosmos.Kernel;

public class Kernel
{
    // CosmosOS version - keep in sync with kmain.h
    public const int VersionMajor = 3;
    public const int VersionMinor = 0;
    public const int VersionPatch = 0;
    public const string VersionString = "3.0.33";
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

        // Initialize heap for memory allocations
        Serial.WriteString("[KERNEL]   - Initializing heap...\n");
        MemoryOp.InitializeHeap(0, 0);

        // Initialize managed modules
        Serial.WriteString("[KERNEL]   - Initializing managed modules...\n");
        ManagedModule.InitializeModules();

        // Get the platform initializer (registered by HAL.X64 or HAL.ARM64 module initializer)
        var initializer = PlatformHAL.Initializer;
        if (initializer == null)
        {
            Serial.WriteString("[KERNEL] ERROR: No platform initializer registered!\n");
            Serial.WriteString("[KERNEL] Make sure Cosmos.Kernel.HAL.X64 or HAL.ARM64 is referenced.\n");
            while (true) { }
        }

        // Display architecture
        Serial.WriteString("[KERNEL]   - Architecture: ");
        Serial.WriteString(initializer.PlatformName);
        Serial.WriteString("\n");

        // Initialize platform-specific HAL
        Serial.WriteString("[KERNEL]   - Initializing HAL...\n");
        PlatformHAL.Initialize(initializer);

        // Initialize interrupts
        Serial.WriteString("[KERNEL]   - Initializing interrupts...\n");
        InterruptManager.Initialize(initializer.CreateInterruptController());

        // Initialize exception handlers (must be after InterruptManager)
        Serial.WriteString("[KERNEL]   - Initializing exception handlers...\n");
        ExceptionHandler.Initialize();

        // Initialize platform-specific hardware (PCI, ACPI, APIC, GIC, timers, etc.)
        Serial.WriteString("[KERNEL]   - Initializing platform hardware...\n");
        initializer.InitializeHardware();

        // Initialize Timer Manager and register platform timer
        Serial.WriteString("[KERNEL]   - Initializing timer manager...\n");
        TimerManager.Initialize();
        var timer = initializer.CreateTimer();
        TimerManager.RegisterTimer(timer);

        // Initialize Scheduler
        if (SchedulerManager.IsEnabled)
        {
            Serial.WriteString("[KERNEL]   - Initializing scheduler...\n");
            InitializeScheduler(initializer.GetCpuCount());
        }

        // Disable interrupts during device initialization
        InternalCpu.DisableInterrupts();

        // Initialize Keyboard Manager and register platform keyboards
        if (KeyboardManager.IsEnabled)
        {
            Serial.WriteString("[KERNEL]   - Initializing keyboard manager...\n");
            KeyboardManager.Initialize();
            var keyboards = initializer.GetKeyboardDevices();
            foreach (var keyboard in keyboards)
            {
                KeyboardManager.RegisterKeyboard(keyboard);
            }
        }

        // Initialize Network Manager and register platform network device
        if (NetworkManager.IsEnabled)
        {
            Serial.WriteString("[KERNEL]   - Initializing network manager...\n");
            NetworkManager.Initialize();
            var networkDevice = initializer.GetNetworkDevice();
            if (networkDevice != null)
            {
                NetworkManager.RegisterDevice(networkDevice);
            }
        }

        // Start scheduler timer for preemptive scheduling (after all init is complete)
        if (SchedulerManager.IsEnabled)
        {
            Serial.WriteString("[KERNEL]   - Starting scheduler timer...\n");
            initializer.StartSchedulerTimer(10);  // 10ms quantum
        }

        Serial.WriteString("[KERNEL] Phase 3: Complete\n");
    }

    /// <summary>
    /// Initializes the scheduler subsystem with idle threads for each CPU.
    /// </summary>
    private static void InitializeScheduler(uint cpuCount)
    {
        Serial.WriteString("[SCHED] Detected ");
        Serial.WriteNumber(cpuCount);
        Serial.WriteString(" CPU(s)\n");

        // Initialize scheduler manager
        SchedulerManager.Initialize(cpuCount);

        // Set up stride scheduler
        var scheduler = new StrideScheduler();
        SchedulerManager.SetScheduler(scheduler);

        Serial.WriteString("[SCHED] Using ");
        Serial.WriteString(scheduler.Name);
        Serial.WriteString(" scheduler\n");

        // Create idle thread for each CPU
        // The idle thread represents the main kernel - no separate stack needed
        // When the shell is preempted, its context is saved to this thread
        for (uint cpu = 0; cpu < cpuCount; cpu++)
        {
            var idleThread = new Core.Scheduler.Thread
            {
                Id = SchedulerManager.AllocateThreadId(),
                CpuId = cpu,
                State = Core.Scheduler.ThreadState.Running,  // Already running (it's the current code!)
                Flags = ThreadFlags.Pinned | ThreadFlags.IdleThread
            };

            // DON'T initialize a separate stack - the idle thread IS the current execution
            // When preempted, the IRQ stub saves context to the current stack
            // and we store that RSP in StackPointer

            // Register with scheduler (but don't add to run queue)
            SchedulerManager.CreateThread(cpu, idleThread);

            // Set as CPU's idle and current thread
            SchedulerManager.SetupIdleThread(cpu, idleThread);

            Serial.WriteString("[SCHED] Idle thread ");
            Serial.WriteNumber(idleThread.Id);
            Serial.WriteString(" (main kernel) for CPU ");
            Serial.WriteNumber(cpu);
            Serial.WriteString("\n");
        }

        // Enable scheduler (timer will start invoking it)
        SchedulerManager.Enabled = true;
        Serial.WriteString("[SCHED] Scheduler enabled\n");
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
