// ACPI wrapper functions for C# interop
// Provides a simplified interface to LAI for Cosmos OS
// Full LAI integration for ACPI parsing

#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>

// LAI headers
#include <lai/core.h>
#include <acpispec/tables.h>
#include <acpispec/hw.h>

// Forward declare serial output functions (implemented in C#)
extern void __cosmos_serial_write(const char* message);
extern void __cosmos_serial_write_hex_u32(uint32_t value);
extern void __cosmos_serial_write_hex_u64(uint64_t value);
extern void __cosmos_serial_write_dec_u32(uint32_t value);
extern void __cosmos_serial_write_dec_u64(uint64_t value);

// ============================================================================
// Data structures for C# interop
// ============================================================================

#define MAX_CPUS 256
#define MAX_IOAPICS 16
#define MAX_ISO_ENTRIES 32

typedef struct {
    uint8_t processor_id;
    uint8_t apic_id;
    uint32_t flags;
} acpi_cpu_t;

typedef struct {
    uint8_t id;
    uint32_t address;
    uint32_t gsi_base;
} acpi_ioapic_t;

typedef struct {
    uint8_t source;      // ISA IRQ
    uint32_t gsi;        // Global System Interrupt
    uint16_t flags;
} acpi_iso_t;

typedef struct {
    uint32_t local_apic_address;
    uint32_t flags;

    uint32_t cpu_count;
    acpi_cpu_t cpus[MAX_CPUS];

    uint32_t ioapic_count;
    acpi_ioapic_t ioapics[MAX_IOAPICS];

    uint32_t iso_count;
    acpi_iso_t isos[MAX_ISO_ENTRIES];
} acpi_madt_info_t;

// ============================================================================
// Global state
// ============================================================================

static bool g_acpi_initialized = false;
static acpi_madt_info_t g_madt_info;

// ============================================================================
// Early boot ACPI initialization (called from C before C# starts)
// ============================================================================

// ============================================================================
// Early boot ACPI initialization (called from C before C# starts)
// ============================================================================

void acpi_early_init(void* rsdp_address) {
    __cosmos_serial_write("[ACPI] acpi_early_init() called\n");
    
    if (rsdp_address == NULL) {
        __cosmos_serial_write("[ACPI] ERROR: RSDP address is NULL!\n");
        return;
    }
    
    __cosmos_serial_write("[ACPI] Initializing LAI with RSDP\n");
    
    // Get the RSDP and check revision
    acpi_rsdp_t* rsdp = (acpi_rsdp_t*)rsdp_address;
    
    // Check RSDP signature - "RSD PTR " (8 bytes)
    if (rsdp->signature[0] != 'R' || rsdp->signature[1] != 'S' || 
        rsdp->signature[2] != 'D' || rsdp->signature[3] != ' ' ||
        rsdp->signature[4] != 'P' || rsdp->signature[5] != 'T' ||
        rsdp->signature[6] != 'R' || rsdp->signature[7] != ' ') {
        __cosmos_serial_write("[ACPI] ERROR: Invalid RSDP signature\n");
        return;
    }
    
    __cosmos_serial_write("[ACPI] Valid RSDP signature found\n");
    
    // Set ACPI revision based on RSDP version
    int acpi_rev = (rsdp->revision == 0) ? 1 : 2;
    __cosmos_serial_write("[ACPI] ACPI revision: ");
    if (acpi_rev == 1) __cosmos_serial_write("1.0\n");
    else __cosmos_serial_write("2.0+\n");
    
    // Initialize LAI with the revision
    lai_set_acpi_revision(acpi_rev);
    __cosmos_serial_write("[ACPI] LAI ACPI revision set\n");
    
    // Note: We skip lai_create_namespace() here and do direct ACPI table parsing
    // instead, as LAI's namespace creation requires full AML support
    __cosmos_serial_write("[ACPI] Skipping LAI namespace creation, using direct table parsing\n");
    
    // Get FADT (if available) to determine system configuration
    acpi_fadt_t* fadt = NULL;
    
    // Navigate RSDT/XSDT to find FADT
    if (rsdp->revision >= 2 && ((acpi_xsdp_t*)rsdp)->xsdt != 0) {
        // Use XSDT (64-bit pointers)
        acpi_xsdt_t* xsdt = (acpi_xsdt_t*)((acpi_xsdp_t*)rsdp)->xsdt;
        __cosmos_serial_write("[ACPI] Using XSDT at: ");
        __cosmos_serial_write_hex_u64(((acpi_xsdp_t*)rsdp)->xsdt);
        __cosmos_serial_write("\n");
        
        // Calculate number of tables
        uint32_t table_count = (xsdt->header.length - sizeof(acpi_header_t)) / sizeof(uint64_t);
        for (uint32_t i = 0; i < table_count; i++) {
            acpi_header_t* table = (acpi_header_t*)xsdt->tables[i];
            if (table->signature[0] == 'F' && table->signature[1] == 'A' && 
                table->signature[2] == 'D' && table->signature[3] == 'T') {
                fadt = (acpi_fadt_t*)table;
                __cosmos_serial_write("[ACPI] FADT found via XSDT\n");
                break;
            }
        }
    } else if (rsdp->rsdt != 0) {
        // Use RSDT (32-bit pointers)
        acpi_rsdt_t* rsdt = (acpi_rsdt_t*)(uintptr_t)rsdp->rsdt;
        __cosmos_serial_write("[ACPI] Using RSDT at: ");
        __cosmos_serial_write_hex_u32(rsdp->rsdt);
        __cosmos_serial_write("\n");
        
        // Calculate number of tables
        uint32_t table_count = (rsdt->header.length - sizeof(acpi_header_t)) / sizeof(uint32_t);
        for (uint32_t i = 0; i < table_count; i++) {
            acpi_header_t* table = (acpi_header_t*)(uintptr_t)rsdt->tables[i];
            if (table->signature[0] == 'F' && table->signature[1] == 'A' && 
                table->signature[2] == 'D' && table->signature[3] == 'T') {
                fadt = (acpi_fadt_t*)table;
                __cosmos_serial_write("[ACPI] FADT found via RSDT\n");
                break;
            }
        }
    }
    
    if (fadt) {
        __cosmos_serial_write("[ACPI] FADT found at: ");
        __cosmos_serial_write_hex_u32((uint32_t)(uintptr_t)fadt);
        __cosmos_serial_write("\n");
        // Local APIC address is in MADT header, not FADT
    }
    
    // Now search for MADT (Multiple APIC Description Table)
    acpi_header_t* madt_header = NULL;
    
    if (rsdp->revision >= 2 && ((acpi_xsdp_t*)rsdp)->xsdt != 0) {
        acpi_xsdt_t* xsdt = (acpi_xsdt_t*)((acpi_xsdp_t*)rsdp)->xsdt;
        uint32_t table_count = (xsdt->header.length - sizeof(acpi_header_t)) / sizeof(uint64_t);
        
        for (uint32_t i = 0; i < table_count; i++) {
            acpi_header_t* table = (acpi_header_t*)xsdt->tables[i];
            if (table->signature[0] == 'A' && table->signature[1] == 'P' && 
                table->signature[2] == 'I' && table->signature[3] == 'C') {
                madt_header = table;
                __cosmos_serial_write("[ACPI] MADT found via XSDT\n");
                break;
            }
        }
    } else if (rsdp->rsdt != 0) {
        acpi_rsdt_t* rsdt = (acpi_rsdt_t*)(uintptr_t)rsdp->rsdt;
        uint32_t table_count = (rsdt->header.length - sizeof(acpi_header_t)) / sizeof(uint32_t);
        
        for (uint32_t i = 0; i < table_count; i++) {
            acpi_header_t* table = (acpi_header_t*)(uintptr_t)rsdt->tables[i];
            if (table->signature[0] == 'A' && table->signature[1] == 'P' && 
                table->signature[2] == 'I' && table->signature[3] == 'C') {
                madt_header = table;
                __cosmos_serial_write("[ACPI] MADT found via RSDT\n");
                break;
            }
        }
    }
    
    // Parse MADT if found
    if (madt_header) {
        __cosmos_serial_write("[ACPI] MADT found at: ");
        __cosmos_serial_write_hex_u32((uint32_t)(uintptr_t)madt_header);
        __cosmos_serial_write("\n");
        __cosmos_serial_write("[ACPI] Parsing MADT entries...\n");
        
        // MADT structure: header (36 bytes) + controller address (4 bytes) + flags (4 bytes) + entries
        uint8_t* madt_data = (uint8_t*)madt_header;
        uint32_t madt_size = madt_header->length;
        
        // Extract local APIC address from MADT header (at offset 36)
        uint32_t* local_apic_ptr = (uint32_t*)(madt_data + sizeof(acpi_header_t));
        g_madt_info.local_apic_address = *local_apic_ptr;
        __cosmos_serial_write("[ACPI] Local APIC address: ");
        __cosmos_serial_write_hex_u32(g_madt_info.local_apic_address);
        __cosmos_serial_write("\n");
        
        // Skip header (36 bytes) + local APIC address (4) + flags (4)
        uint32_t offset = sizeof(acpi_header_t) + 8;
        
        while (offset < madt_size && offset < madt_size) {
            uint8_t* entry = madt_data + offset;
            uint8_t entry_type = entry[0];
            uint8_t entry_length = entry[1];
            
            if (entry_length == 0) break;
            
            switch (entry_type) {
                case 0: { // Processor Local APIC
                    if (g_madt_info.cpu_count < MAX_CPUS) {
                        uint8_t acpi_id = entry[2];
                        uint8_t apic_id = entry[3];
                        uint32_t flags = *(uint32_t*)(entry + 4);
                        
                        if (flags & 1) { // Enabled flag
                            g_madt_info.cpus[g_madt_info.cpu_count].processor_id = acpi_id;
                            g_madt_info.cpus[g_madt_info.cpu_count].apic_id = apic_id;
                            g_madt_info.cpus[g_madt_info.cpu_count].flags = flags;
                            g_madt_info.cpu_count++;
                            
                            __cosmos_serial_write("[ACPI] CPU found (ID=");
                            __cosmos_serial_write_dec_u32(acpi_id);
                            __cosmos_serial_write(" APIC=");
                            __cosmos_serial_write_dec_u32(apic_id);
                            __cosmos_serial_write(")\n");
                        }
                    }
                    break;
                }
                
                case 1: { // I/O APIC
                    if (g_madt_info.ioapic_count < MAX_IOAPICS) {
                        uint8_t ioapic_id = entry[2];
                        uint32_t ioapic_address = *(uint32_t*)(entry + 4);
                        uint32_t gsi_base = *(uint32_t*)(entry + 8);
                        
                        g_madt_info.ioapics[g_madt_info.ioapic_count].id = ioapic_id;
                        g_madt_info.ioapics[g_madt_info.ioapic_count].address = ioapic_address;
                        g_madt_info.ioapics[g_madt_info.ioapic_count].gsi_base = gsi_base;
                        g_madt_info.ioapic_count++;
                        
                        __cosmos_serial_write("[ACPI] I/O APIC found (ID=");
                        __cosmos_serial_write_dec_u32(ioapic_id);
                        __cosmos_serial_write(" at ");
                        __cosmos_serial_write_hex_u32(ioapic_address);
                        __cosmos_serial_write(" GSI base=");
                        __cosmos_serial_write_dec_u32(gsi_base);
                        __cosmos_serial_write(")\n");
                    }
                    break;
                }
                
                case 2: { // Interrupt Source Override
                    if (g_madt_info.iso_count < MAX_ISO_ENTRIES) {
                        uint8_t source = entry[3];
                        uint32_t gsi = *(uint32_t*)(entry + 4);
                        uint16_t flags = *(uint16_t*)(entry + 8);
                        
                        g_madt_info.isos[g_madt_info.iso_count].source = source;
                        g_madt_info.isos[g_madt_info.iso_count].gsi = gsi;
                        g_madt_info.isos[g_madt_info.iso_count].flags = flags;
                        g_madt_info.iso_count++;
                    }
                    break;
                }
            }
            
            offset += entry_length;
        }
        
        __cosmos_serial_write("[ACPI] MADT parsing complete\n");
    } else {
        __cosmos_serial_write("[ACPI] WARNING: MADT not found\n");
    }
    
    g_acpi_initialized = true;
    __cosmos_serial_write("[ACPI] ACPI initialization complete\n");
}

// ============================================================================
// Public API for C# interop
// ============================================================================

// Get MADT information
const acpi_madt_info_t* acpi_get_madt_info(void) {
    return g_acpi_initialized ? &g_madt_info : NULL;
}
