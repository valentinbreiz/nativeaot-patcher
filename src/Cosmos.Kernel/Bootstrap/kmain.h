#ifndef KMAIN_H
#define KMAIN_H

#include <stdint.h>

typedef unsigned int size_t;

// CosmosOS version
#define COSMOS_VERSION_MAJOR 3
#define COSMOS_VERSION_MINOR 0
#define COSMOS_VERSION_PATCH 0
#define COSMOS_VERSION_STRING "3.0.37"
#define COSMOS_CODENAME "gen3"
#define NULL (void*)0

// Linker-defined symbols for module section
extern void* __Modules_start[];
extern void* __Modules_end[];
extern char* __kernel_start;

// Managed code entry points
extern void __managed__Startup(void);
extern int __managed__Main(int argc, char* argv[]);
extern void* RhpRegisterOsModule(void* osmodule);

// CPU features (inspected by generated code)
extern int g_cpuFeatures;
extern int g_requiredCpuFeatures;

// Cross-platform SIMD enable
extern void _native_enable_simd(void);

#ifdef __aarch64__
// ARM64-specific: Disable alignment checking
extern void _native_arm64_disable_alignment_check(void);
#endif

#ifdef ARCH_X64
// ACPI early initialization (x64 only)
extern void acpi_early_init(void* rsdp_address);
extern void* __get_limine_rsdp_address(void);
#endif

// Serial logging (C# functions)
extern void __cosmos_serial_init(void);
extern void __cosmos_serial_write(const char* message);
extern void __cosmos_serial_write_hex_u64(uint64_t value);

#endif // KMAIN_H
