// LAI Host Implementation for Cosmos OS
// This file implements the required LAI host interface functions

#include <lai/core.h>
#include <stdint.h>
#include <stddef.h>

// External functions provided by Cosmos kernel
extern void* cosmos_acpi_get_rsdp(void);
extern void* cosmos_malloc(size_t size);
extern void cosmos_free(void* ptr);
extern void cosmos_log(const char* msg);

// ============================================================================
// LAI Host Interface - Memory Management
// ============================================================================

void* laihost_malloc(size_t size) {
    return cosmos_malloc(size);
}

void laihost_free(void* ptr, size_t size) {
    (void)size; // LAI provides size but we might not need it
    cosmos_free(ptr);
}

void* laihost_realloc(void* ptr, size_t newsize, size_t oldsize) {
    if (!ptr)
        return laihost_malloc(newsize);

    if (newsize == 0) {
        laihost_free(ptr, oldsize);
        return NULL;
    }

    // Simple realloc: allocate new, copy, free old
    void* new_ptr = laihost_malloc(newsize);
    if (!new_ptr)
        return NULL;

    // Copy old data
    size_t copy_size = (newsize < oldsize) ? newsize : oldsize;
    uint8_t* dst = (uint8_t*)new_ptr;
    uint8_t* src = (uint8_t*)ptr;
    for (size_t i = 0; i < copy_size; i++)
        dst[i] = src[i];

    laihost_free(ptr, oldsize);
    return new_ptr;
}

void* laihost_map(size_t address, size_t count) {
    // In Cosmos, all physical memory is identity mapped in kernel space
    // So we can just return the address directly
    (void)count;
    return (void*)address;
}

void laihost_unmap(void* pointer, size_t count) {
    // No-op: we don't actually map/unmap
    (void)pointer;
    (void)count;
}

// ============================================================================
// LAI Host Interface - ACPI Table Access
// ============================================================================

void* laihost_scan(const char* signature, size_t index) {
    // For now, we only support finding the RSDP (signature is NULL)
    // LAI will use this to bootstrap table discovery
    if (!signature) {
        return cosmos_acpi_get_rsdp();
    }

    // LAI will handle other table lookups internally after getting RSDP
    return NULL;
}

// ============================================================================
// LAI Host Interface - Logging
// ============================================================================

void laihost_log(int level, const char* message) {
    const char* prefix;

    switch (level) {
        case LAI_DEBUG_LOG:
            prefix = "[LAI DEBUG] ";
            break;
        case LAI_WARN_LOG:
            prefix = "[LAI WARN] ";
            break;
        default:
            prefix = "[LAI] ";
            break;
    }

    cosmos_log(prefix);
    cosmos_log(message);
}

// ============================================================================
// LAI Host Interface - Synchronization (stubs for single-core)
// ============================================================================

void laihost_panic(const char* message) {
    cosmos_log("[LAI PANIC] ");
    cosmos_log(message);

    // Halt the system
    while (1) {
        __asm__ volatile ("hlt");
    }
}

// For now, we don't support multi-threading, so these are no-ops
void* laihost_lock_alloc(void) {
    return (void*)1; // Return non-NULL to indicate success
}

void laihost_lock_free(void* lock) {
    (void)lock;
}

void laihost_lock_acquire(void* lock) {
    (void)lock;
}

void laihost_lock_release(void* lock) {
    (void)lock;
}

// ============================================================================
// LAI Host Interface - PCI Access (not needed for MADT parsing)
// ============================================================================

void laihost_pci_write(uint16_t seg, uint8_t bus, uint8_t slot,
                       uint8_t fun, uint16_t offset, uint32_t value, uint8_t size) {
    // TODO: Implement when PCI is needed
    (void)seg; (void)bus; (void)slot; (void)fun;
    (void)offset; (void)value; (void)size;
}

uint32_t laihost_pci_read(uint16_t seg, uint8_t bus, uint8_t slot,
                          uint8_t fun, uint16_t offset, uint8_t size) {
    // TODO: Implement when PCI is needed
    (void)seg; (void)bus; (void)slot; (void)fun;
    (void)offset; (void)size;
    return 0;
}

// ============================================================================
// LAI Host Interface - Port I/O
// ============================================================================

void laihost_outb(uint16_t port, uint8_t value) {
    __asm__ volatile ("outb %0, %1" : : "a"(value), "Nd"(port));
}

void laihost_outw(uint16_t port, uint16_t value) {
    __asm__ volatile ("outw %0, %1" : : "a"(value), "Nd"(port));
}

void laihost_outd(uint16_t port, uint32_t value) {
    __asm__ volatile ("outl %0, %1" : : "a"(value), "Nd"(port));
}

uint8_t laihost_inb(uint16_t port) {
    uint8_t value;
    __asm__ volatile ("inb %1, %0" : "=a"(value) : "Nd"(port));
    return value;
}

uint16_t laihost_inw(uint16_t port) {
    uint16_t value;
    __asm__ volatile ("inw %1, %0" : "=a"(value) : "Nd"(port));
    return value;
}

uint32_t laihost_ind(uint16_t port) {
    uint32_t value;
    __asm__ volatile ("inl %1, %0" : "=a"(value) : "Nd"(port));
    return value;
}

// ============================================================================
// LAI Host Interface - Sleep/Timing (stubs)
// ============================================================================

void laihost_sleep(uint64_t ms) {
    // TODO: Implement proper delay
    (void)ms;
    for (volatile uint64_t i = 0; i < ms * 1000000; i++)
        ;
}

uint64_t laihost_timer(void) {
    // TODO: Return actual timer value
    return 0;
}
