#include "kmain.h"

// CPU features definition
int g_cpuFeatures = 0;

// Entry point
void kmain()
{
    // Enable SIMD (SSE on x64, NEON on ARM64)
    _native_enable_simd();

#ifdef __aarch64__
    // ARM64: Disable alignment checking - NativeAOT generates unaligned accesses
    _native_arm64_disable_alignment_check();
#endif

    __cosmos_serial_write("[KMAIN] Starting kernel bootstrap...\n");

#ifdef ARCH_X64
    // Get RSDP address from Limine (via C# static data)
    void* rsdp_address = __get_limine_rsdp_address();

    // Initialize ACPI early (before managed code needs it)
    if (rsdp_address != 0)
    {
        __cosmos_serial_write("[KMAIN] RSDP found at: ");
        __cosmos_serial_write_hex_u64((uint64_t)rsdp_address);
        __cosmos_serial_write("\n");

        __cosmos_serial_write("[KMAIN] Calling acpi_early_init()...\n");
        acpi_early_init(rsdp_address);
        __cosmos_serial_write("[KMAIN] acpi_early_init() completed\n");
    }
    else
    {
        __cosmos_serial_write("[KMAIN] ERROR: RSDP not found from Limine!\n");
    }
#endif

    __cosmos_serial_write("[KMAIN] Initializing managed kernel...\n");
    __Initialize_Kernel();
    __managed__Startup();
    __managed__Main();
}

// Return the size of the '__modules' section and
// populates 'modules' with a pointer to the start of the section.
size_t GetModules(void** modules)
{
    *modules = __Modules_start;
    return __Modules_end - __Modules_start;
}
