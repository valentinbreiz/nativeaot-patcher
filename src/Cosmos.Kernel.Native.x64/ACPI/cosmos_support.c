// Cosmos kernel support functions for LAI
// These functions bridge LAI to Cosmos kernel services

#include <stdint.h>
#include <stddef.h>

// External symbols from Cosmos kernel
extern void* __cosmos_heap_alloc(size_t size);
extern void __cosmos_heap_free(void* ptr);
extern void __cosmos_serial_write(const char* str);

// ============================================================================
// Memory allocation (uses Cosmos heap)
// ============================================================================

void* cosmos_malloc(size_t size) {
    return __cosmos_heap_alloc(size);
}

void cosmos_free(void* ptr) {
    __cosmos_heap_free(ptr);
}

// ============================================================================
// Logging (uses Cosmos serial output)
// ============================================================================

void cosmos_log(const char* msg) {
    __cosmos_serial_write(msg);
}

// ============================================================================
// ACPI RSDP access
// ============================================================================

// RSDP address provided during acpi_early_init()
static void* g_rsdp_address = NULL;

void cosmos_acpi_set_rsdp(void* rsdp) {
    g_rsdp_address = rsdp;
}

void* cosmos_acpi_get_rsdp(void) {
    return g_rsdp_address;
}
