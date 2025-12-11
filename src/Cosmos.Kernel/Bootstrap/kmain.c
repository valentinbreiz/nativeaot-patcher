#include "kmain.h"

// CPU features definition
int g_cpuFeatures = 0;

// Entry point
void kmain()
{
    // === Phase 1: Architecture Detection ===
    __cosmos_serial_write("\n");
    __cosmos_serial_write("========================================\n");
    __cosmos_serial_write("  CosmosOS v" COSMOS_VERSION_STRING " (" COSMOS_CODENAME ")\n");
#ifdef __aarch64__
    __cosmos_serial_write("  Architecture: ARM64/AArch64\n");
#else
    __cosmos_serial_write("  Architecture: x86-64\n");
#endif
    __cosmos_serial_write("========================================\n");

    // === Phase 2: CPU Initialization ===
    __cosmos_serial_write("[KMAIN] Phase 1: CPU initialization\n");

#ifdef __aarch64__
    __cosmos_serial_write("[KMAIN]   - Enabling NEON/SIMD...\n");
#else
    __cosmos_serial_write("[KMAIN]   - Enabling SSE/AVX...\n");
#endif
    _native_enable_simd();
    __cosmos_serial_write("[KMAIN]   - SIMD enabled\n");

#ifdef __aarch64__
    __cosmos_serial_write("[KMAIN]   - Disabling alignment check (SCTLR_EL1.A)...\n");
    _native_arm64_disable_alignment_check();
    __cosmos_serial_write("[KMAIN]   - Alignment check disabled\n");
#endif

    // === Phase 3: Platform-specific early init ===
    __cosmos_serial_write("[KMAIN] Phase 2: Platform initialization\n");

#ifdef ARCH_X64
    __cosmos_serial_write("[KMAIN]   - Querying Limine for RSDP...\n");
    void* rsdp_address = __get_limine_rsdp_address();

    if (rsdp_address != 0)
    {
        __cosmos_serial_write("[KMAIN]   - RSDP found at: 0x");
        __cosmos_serial_write_hex_u64((uint64_t)rsdp_address);
        __cosmos_serial_write("\n");

        __cosmos_serial_write("[KMAIN]   - Initializing ACPI (LAI)...\n");
        acpi_early_init(rsdp_address);
        __cosmos_serial_write("[KMAIN]   - ACPI initialized\n");
    }
    else
    {
        __cosmos_serial_write("[KMAIN]   - WARNING: RSDP not found!\n");
    }
#else
    __cosmos_serial_write("[KMAIN]   - ARM64: No ACPI early init required\n");
#endif

    // === Phase 4: Managed Runtime ===
    __cosmos_serial_write("[KMAIN] Phase 3: Managed runtime startup\n");

    __cosmos_serial_write("[KMAIN]   - Calling __Initialize_Kernel()...\n");
    __Initialize_Kernel();

    __cosmos_serial_write("[KMAIN]   - Calling __managed__Startup()...\n");
    __managed__Startup();

    __cosmos_serial_write("[KMAIN]   - Calling __managed__Main()...\n");
    __managed__Main();

    // Should never reach here
    __cosmos_serial_write("[KMAIN] ERROR: Main() returned unexpectedly!\n");
    while(1) {}
}

// Return the size of the '__modules' section and
// populates 'modules' with a pointer to the start of the section.
size_t GetModules(void** modules)
{
    *modules = __Modules_start;
    return __Modules_end - __Modules_start;
}
