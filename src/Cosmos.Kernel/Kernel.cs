using System.Runtime.InteropServices;
using Cosmos.Build.API.Enum;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.Cpu;
using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Graphics;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.Core.CPU;

#if ARCH_X64
using Cosmos.Kernel.HAL.Acpi;
using Cosmos.Kernel.HAL.Devices.Input;
using Cosmos.Kernel.HAL.X64;
using Cosmos.Kernel.HAL.X64.Devices.Clock;
using Cosmos.Kernel.HAL.X64.Devices.Input;
using Cosmos.Kernel.HAL.X64.Devices.Network;
using Cosmos.Kernel.HAL.X64.Devices.Storage;
using Cosmos.Kernel.HAL.X64.Devices.Timer;
using Cosmos.Kernel.HAL.X64.Cpu;
using Cosmos.Kernel.HAL.X64.Pci;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Timer;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.Core.Scheduler.Stride;
#elif ARCH_ARM64
using Cosmos.Kernel.HAL.ARM64;
using Cosmos.Kernel.HAL.ARM64.Cpu;
using Cosmos.Kernel.HAL.ARM64.Devices.Timer;
using Cosmos.Kernel.HAL.ARM64.Devices.Input;
using Cosmos.Kernel.HAL.ARM64.Devices.Virtio;
using Cosmos.Kernel.System.Timer;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.Core.Scheduler.Stride;
#endif

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
#if ARCH_X64
        PlatformHAL.Initialize(new X64PlatformInitializer());
#elif ARCH_ARM64
        PlatformHAL.Initialize(new ARM64PlatformInitializer());
#endif

        // Initialize interrupts
        Serial.WriteString("[KERNEL]   - Initializing interrupts...\n");
#if ARCH_X64
        InterruptManager.Initialize(new X64InterruptController());
#elif ARCH_ARM64
        InterruptManager.Initialize(new ARM64InterruptController());
#endif

        // Initialize exception handlers (must be after InterruptManager)
        Serial.WriteString("[KERNEL]   - Initializing exception handlers...\n");
        ExceptionHandler.Initialize();

#if ARCH_X64
        Serial.WriteString("[KERNEL]   - Initializing PCI...\n");
        PciManager.Setup();

        // Initialize AHCI (SATA storage)
        Serial.WriteString("[KERNEL]   - Initializing AHCI...\n");
        AHCI.InitDriver();

        // Retrieve and display ACPI MADT information (initialized during early boot)
        Serial.WriteString("[KERNEL]   - Displaying ACPI MADT info...\n");
        Acpi.DisplayMadtInfo();

        // Initialize APIC (Advanced Programmable Interrupt Controller)
        Serial.WriteString("[KERNEL]   - Initializing APIC...\n");
        ApicManager.Initialize();

        // Calibrate TSC frequency using LAPIC timer (already calibrated during APIC init)
        Serial.WriteString("[KERNEL]   - Calibrating TSC frequency...\n");
        X64CpuOps.CalibrateTsc();
        Serial.WriteString("[KERNEL]   - TSC frequency: ");
        Serial.WriteNumber((ulong)X64CpuOps.TscFrequency);
        Serial.WriteString(" Hz\n");

        // Initialize RTC for DateTime support (after TSC calibration)
        Serial.WriteString("[KERNEL]   - Initializing RTC...\n");
        var rtc = new RTC();
        rtc.Initialize();

        // Initialize PIT (Programmable Interval Timer)
        Serial.WriteString("[KERNEL]   - Initializing PIT...\n");
        var pit = new PIT();
        pit.Initialize();
        pit.RegisterIRQHandler();
        TimerManager.Initialize();
        TimerManager.RegisterTimer(pit);

        // Initialize Scheduler
        Serial.WriteString("[KERNEL]   - Initializing scheduler...\n");
        InitializeScheduler();

        // Register LAPIC timer handler (but don't start yet - wait for all init to complete)
        Serial.WriteString("[KERNEL]   - Registering LAPIC timer handler...\n");
        LocalApic.RegisterTimerHandler();

        InternalCpu.DisableInterrupts();

        // Initialize PS/2 Controller BEFORE enabling keyboard IRQ
        Serial.WriteString("[KERNEL]   - Initializing PS/2 controller...\n");
        var ps2Controller = new PS2Controller();
        ps2Controller.Initialize();

        // Initialize Keyboard Manager
        Serial.WriteString("[KERNEL]   - Initializing keyboard manager...\n");
        KeyboardManager.Initialize();

        // Register keyboards with KeyboardManager
        var keyboards = PS2Controller.GetKeyboardDevices();
        foreach (var keyboard in keyboards)
        {
            KeyboardManager.RegisterKeyboard(keyboard);
        }

        // Set static callback for PS2 keyboard IRQ handler
        PS2Keyboard.KeyCallback = KeyboardManager.HandleScanCode;

        // Register keyboard IRQ handler (this also routes IRQ1 through APIC)
        Serial.WriteString("[KERNEL]   - Registering keyboard IRQ handler...\n");
        PS2Keyboard.RegisterIRQHandler();

        // Initialize Network Manager
        Serial.WriteString("[KERNEL]   - Initializing network manager...\n");
        NetworkManager.Initialize();

        // Try to find and initialize E1000E network device
        Serial.WriteString("[KERNEL]   - Looking for E1000E network device...\n");
        var e1000e = E1000E.FindAndCreate();
        if (e1000e != null)
        {
            Serial.WriteString("[KERNEL]   - E1000E device found, initializing...\n");
            e1000e.InitializeNetwork();
            NetworkManager.RegisterDevice(e1000e);
            e1000e.RegisterIRQHandler();
        }
        else
        {
            Serial.WriteString("[KERNEL]   - No E1000E device found\n");
        }

        // Start LAPIC timer for preemptive scheduling (after all init is complete)
        Serial.WriteString("[KERNEL]   - Starting LAPIC timer for scheduling...\n");
        LocalApic.StartPeriodicTimer(10);  // 10ms quantum
#elif ARCH_ARM64
        // Initialize Generic Timer (ARM64 architected timer)
        Serial.WriteString("[KERNEL]   - Initializing Generic Timer...\n");
        var timer = new GenericTimer();
        timer.Initialize();
        TimerManager.Initialize();
        TimerManager.RegisterTimer(timer);

        // Initialize Scheduler
        Serial.WriteString("[KERNEL]   - Initializing scheduler...\n");
        InitializeSchedulerARM64();

        // Register timer interrupt handler
        Serial.WriteString("[KERNEL]   - Registering timer interrupt handler...\n");
        timer.RegisterIRQHandler();

        // Disable interrupts during final init
        InternalCpu.DisableInterrupts();

        // Scan for virtio devices
        Serial.WriteString("[KERNEL]   - Scanning for virtio devices...\n");
        VirtioMMIO.ScanDevices();

        // Initialize Keyboard Manager
        Serial.WriteString("[KERNEL]   - Initializing keyboard manager...\n");
        KeyboardManager.Initialize();

        // Initialize virtio keyboard
        Serial.WriteString("[KERNEL]   - Initializing virtio keyboard...\n");
        var virtioKeyboard = VirtioKeyboard.FindAndCreate();
        if (virtioKeyboard != null)
        {
            virtioKeyboard.Initialize();
            if (virtioKeyboard.IsInitialized)
            {
                KeyboardManager.RegisterKeyboard(virtioKeyboard);

                // Set static callback (mirrors x64 PS2Keyboard pattern)
                VirtioKeyboard.KeyCallback = KeyboardManager.HandleScanCode;

                virtioKeyboard.RegisterIRQHandler();
                Serial.WriteString("[KERNEL]   - Virtio keyboard initialized\n");
            }
            else
            {
                Serial.WriteString("[KERNEL]   - Virtio keyboard initialization failed\n");
            }
        }
        else
        {
            Serial.WriteString("[KERNEL]   - No virtio keyboard found\n");
        }

        // Start the timer for preemptive scheduling
        Serial.WriteString("[KERNEL]   - Starting Generic Timer for scheduling...\n");
        timer.Start();
#endif

        Serial.WriteString("[KERNEL] Phase 3: Complete\n");
    }

#if ARCH_X64
    /// <summary>
    /// Initializes the scheduler subsystem with idle threads for each CPU.
    /// </summary>
    private static unsafe void InitializeScheduler()
    {
        // Get CPU count from ACPI MADT
        var madtInfo = Acpi.GetMadtInfoPtr();
        uint cpuCount = madtInfo != null ? madtInfo->CpuCount : 1;

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

        // Get code selector for thread initialization
        ushort cs = (ushort)Idt.GetCurrentCodeSelector();

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
    /// Idle thread entry point - runs when no other threads are ready.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void IdleLoop()
    {
        while (true)
        {
            // Halt until next interrupt
            if (PlatformHAL.CpuOps != null)
            {
                PlatformHAL.CpuOps.Halt();
            }
        }
    }
#elif ARCH_ARM64
    /// <summary>
    /// Initializes the scheduler subsystem for ARM64 with idle threads.
    /// </summary>
    private static unsafe void InitializeSchedulerARM64()
    {
        // For now, single CPU
        uint cpuCount = 1;

        Serial.WriteString("[SCHED] ARM64: Using ");
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
        for (uint cpu = 0; cpu < cpuCount; cpu++)
        {
            var idleThread = new Core.Scheduler.Thread
            {
                Id = SchedulerManager.AllocateThreadId(),
                CpuId = cpu,
                State = Core.Scheduler.ThreadState.Running,  // Already running
                Flags = ThreadFlags.Pinned | ThreadFlags.IdleThread
            };

            // Register with scheduler (but don't add to run queue)
            SchedulerManager.CreateThread(cpu, idleThread);

            // Set as CPU's idle and current thread
            SchedulerManager.SetupIdleThread(cpu, idleThread);

            Serial.WriteString("[SCHED] Idle thread ");
            Serial.WriteNumber(idleThread.Id);
            Serial.WriteString(" for CPU ");
            Serial.WriteNumber(cpu);
            Serial.WriteString("\n");
        }

        // Enable scheduler
        SchedulerManager.Enabled = true;
        Serial.WriteString("[SCHED] Scheduler enabled\n");
    }
#endif

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
