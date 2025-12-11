#ifndef KMAIN_H
#define KMAIN_H

#include <stdint.h>

typedef unsigned int size_t;

// Managed code entry points
extern void __Initialize_Kernel(void);
extern void __managed__Startup(void);
extern void __managed__Main(void);

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
extern void __cosmos_serial_write(const char* message);
extern void __cosmos_serial_write_hex_u64(uint64_t value);

#endif // KMAIN_H
