// ACPI wrapper for Cosmos OS (Multi-Architecture)
// Shared RSDP/XSDT/RSDT walking code using LAI struct typedefs.
// x86: parses MADT for Local APIC / IO APIC / ISOs
// ARM64: parses MADT for GICD / GICR / GICC entries

#include <stdint.h>
#include <stddef.h>

#include <lai/core.h>
#include <acpispec/tables.h>

#define NULL_PTR ((void*)0)

// Serial output (implemented in C#)
extern void __cosmos_serial_write(const char* message);
extern void __cosmos_serial_write_hex_u32(uint32_t value);
extern void __cosmos_serial_write_hex_u64(uint64_t value);
extern void __cosmos_serial_write_dec_u32(uint32_t value);
extern void __cosmos_serial_write_dec_u64(uint64_t value);

// Cosmos support
extern void cosmos_acpi_set_rsdp(void* rsdp);

// ============================================================================
// ARM64 GIC structures (MADT subtable types 0x0B-0x0F)
// ============================================================================

#ifdef __aarch64__

#define MADT_TYPE_GICC  0x0B
#define MADT_TYPE_GICD  0x0C
#define MADT_TYPE_MSI   0x0D
#define MADT_TYPE_GICR  0x0E
#define MADT_TYPE_ITS   0x0F

typedef struct {
    uint8_t  found;
    uint8_t  version;       // GIC version: 2 or 3
    uint8_t  _pad[6];
    uint64_t dist_base;     // GICD physical base
    uint64_t redist_base;   // GICR physical base (GICv3)
    uint64_t redist_length; // GICR region length
    uint64_t cpu_if_base;   // GICC physical base (GICv2)
} acpi_gic_info_t;

static acpi_gic_info_t g_gic_info;

#endif // __aarch64__

// ============================================================================
// x86 MADT structures (MADT subtable types 0-2)
// ============================================================================

#ifdef ARCH_X64

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
    uint8_t source;
    uint32_t gsi;
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

static acpi_madt_info_t g_madt_info;

#endif // ARCH_X64

// ============================================================================
// Global state
// ============================================================================

static uint8_t g_initialized = 0;
static uint64_t g_hhdm_offset = 0;

// Physical-to-virtual address translation via Limine HHDM
// ACPI tables store physical addresses; the kernel accesses memory via HHDM.
static inline void* phys_to_virt(uint64_t phys) {
    if (phys == 0) return NULL_PTR;
    // If address already looks virtual (high bit set), don't translate
    if (phys >= g_hhdm_offset && g_hhdm_offset != 0) return (void*)phys;
    return (void*)(phys + g_hhdm_offset);
}

// ============================================================================
// MADT parsing - shared entry point
// ============================================================================

static void parse_madt(acpi_header_t* madt_header) {
    uint8_t* madt = (uint8_t*)madt_header;
    uint32_t length = madt_header->length;

    // MADT: header(36) + local_controller_addr(4) + flags(4) + entries
    uint32_t offset = sizeof(acpi_header_t) + 8;

#ifdef ARCH_X64
    // x86: Extract Local APIC address from MADT header
    g_madt_info.local_apic_address = *(uint32_t*)(madt + sizeof(acpi_header_t));
    __cosmos_serial_write("[ACPI] Local APIC at: 0x");
    __cosmos_serial_write_hex_u32(g_madt_info.local_apic_address);
    __cosmos_serial_write("\n");
#endif

#ifdef __aarch64__
    uint8_t found_gicd = 0;
    uint8_t found_gicr = 0;
#endif

    while (offset + 2 <= length) {
        uint8_t type = madt[offset];
        uint8_t entry_len = madt[offset + 1];
        if (entry_len < 2) break;

#ifdef ARCH_X64
        switch (type) {
            case 0: { // Processor Local APIC
                if (g_madt_info.cpu_count < MAX_CPUS) {
                    uint8_t acpi_id = madt[offset + 2];
                    uint8_t apic_id = madt[offset + 3];
                    uint32_t flags = *(uint32_t*)(madt + offset + 4);
                    if (flags & 1) {
                        g_madt_info.cpus[g_madt_info.cpu_count].processor_id = acpi_id;
                        g_madt_info.cpus[g_madt_info.cpu_count].apic_id = apic_id;
                        g_madt_info.cpus[g_madt_info.cpu_count].flags = flags;
                        g_madt_info.cpu_count++;
                        __cosmos_serial_write("[ACPI] CPU (ID=");
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
                    g_madt_info.ioapics[g_madt_info.ioapic_count].id = madt[offset + 2];
                    g_madt_info.ioapics[g_madt_info.ioapic_count].address = *(uint32_t*)(madt + offset + 4);
                    g_madt_info.ioapics[g_madt_info.ioapic_count].gsi_base = *(uint32_t*)(madt + offset + 8);
                    __cosmos_serial_write("[ACPI] I/O APIC (ID=");
                    __cosmos_serial_write_dec_u32(madt[offset + 2]);
                    __cosmos_serial_write(" at 0x");
                    __cosmos_serial_write_hex_u32(*(uint32_t*)(madt + offset + 4));
                    __cosmos_serial_write(")\n");
                    g_madt_info.ioapic_count++;
                }
                break;
            }
            case 2: { // Interrupt Source Override
                if (g_madt_info.iso_count < MAX_ISO_ENTRIES) {
                    g_madt_info.isos[g_madt_info.iso_count].source = madt[offset + 3];
                    g_madt_info.isos[g_madt_info.iso_count].gsi = *(uint32_t*)(madt + offset + 4);
                    g_madt_info.isos[g_madt_info.iso_count].flags = *(uint16_t*)(madt + offset + 8);
                    g_madt_info.iso_count++;
                }
                break;
            }
        }
#endif // ARCH_X64

#ifdef __aarch64__
        switch (type) {
            case MADT_TYPE_GICD: {
                // GICD: offset+8=base(8), offset+20=version(1)
                if (entry_len >= 24) {
                    uint64_t base = *(uint64_t*)(madt + offset + 8);
                    uint8_t ver = madt[offset + 20];
                    __cosmos_serial_write("[ACPI-GIC] GICD: base=0x");
                    __cosmos_serial_write_hex_u64(base);
                    __cosmos_serial_write(" ver=");
                    char buf[2] = { '0' + ver, 0 };
                    __cosmos_serial_write(buf);
                    __cosmos_serial_write("\n");
                    g_gic_info.dist_base = base;
                    if (ver >= 3) g_gic_info.version = 3;
                    else if (ver >= 1) g_gic_info.version = ver;
                    found_gicd = 1;
                }
                break;
            }
            case MADT_TYPE_GICR: {
                // GICR: offset+4=base(8), offset+12=length(4)
                if (entry_len >= 16 && !found_gicr) {
                    uint64_t base = *(uint64_t*)(madt + offset + 4);
                    uint32_t len = *(uint32_t*)(madt + offset + 12);
                    __cosmos_serial_write("[ACPI-GIC] GICR: base=0x");
                    __cosmos_serial_write_hex_u64(base);
                    __cosmos_serial_write(" len=0x");
                    __cosmos_serial_write_hex_u64(len);
                    __cosmos_serial_write("\n");
                    g_gic_info.redist_base = base;
                    g_gic_info.redist_length = len;
                    if (g_gic_info.version < 3) g_gic_info.version = 3;
                    found_gicr = 1;
                }
                break;
            }
            case MADT_TYPE_GICC: {
                // GICC: offset+32=Physical Base(8)
                if (entry_len >= 40 && g_gic_info.cpu_if_base == 0) {
                    uint64_t base = *(uint64_t*)(madt + offset + 32);
                    if (base != 0) {
                        g_gic_info.cpu_if_base = base;
                        __cosmos_serial_write("[ACPI-GIC] GICC: base=0x");
                        __cosmos_serial_write_hex_u64(base);
                        __cosmos_serial_write("\n");
                    }
                }
                break;
            }
        }
#endif // __aarch64__

        offset += entry_len;
    }

#ifdef __aarch64__
    if (found_gicd) {
        g_gic_info.found = 1;
        if (g_gic_info.version == 0) g_gic_info.version = 2;
        __cosmos_serial_write("[ACPI-GIC] Result: GICv");
        char buf[2] = { '0' + g_gic_info.version, 0 };
        __cosmos_serial_write(buf);
        __cosmos_serial_write(" GICD=0x");
        __cosmos_serial_write_hex_u64(g_gic_info.dist_base);
        if (found_gicr) {
            __cosmos_serial_write(" GICR=0x");
            __cosmos_serial_write_hex_u64(g_gic_info.redist_base);
        }
        __cosmos_serial_write("\n");
    } else {
        __cosmos_serial_write("[ACPI-GIC] No GICD found in MADT\n");
    }
#endif

    __cosmos_serial_write("[ACPI] MADT parsing complete\n");
}

// ============================================================================
// XSDT/RSDT table walking (shared)
// ============================================================================

void acpi_early_init(void* rsdp_address, uint64_t hhdm_offset) {
    __cosmos_serial_write("[ACPI] acpi_early_init()\n");

    if (rsdp_address == NULL_PTR) {
        __cosmos_serial_write("[ACPI] ERROR: RSDP is NULL\n");
        return;
    }

    g_hhdm_offset = hhdm_offset;

    // Clear state
#ifdef ARCH_X64
    for (int i = 0; i < (int)sizeof(g_madt_info); i++)
        ((uint8_t*)&g_madt_info)[i] = 0;
#endif
#ifdef __aarch64__
    for (int i = 0; i < (int)sizeof(g_gic_info); i++)
        ((uint8_t*)&g_gic_info)[i] = 0;
#endif

    acpi_rsdp_t* rsdp = (acpi_rsdp_t*)rsdp_address;

    // Validate RSDP signature
    if (rsdp->signature[0] != 'R' || rsdp->signature[1] != 'S' ||
        rsdp->signature[2] != 'D' || rsdp->signature[3] != ' ' ||
        rsdp->signature[4] != 'P' || rsdp->signature[5] != 'T' ||
        rsdp->signature[6] != 'R' || rsdp->signature[7] != ' ') {
        __cosmos_serial_write("[ACPI] Invalid RSDP signature\n");
        return;
    }

    int acpi_rev = (rsdp->revision == 0) ? 1 : 2;
    __cosmos_serial_write("[ACPI] ACPI revision: ");
    if (acpi_rev == 1) __cosmos_serial_write("1.0\n");
    else __cosmos_serial_write("2.0+\n");

    lai_set_acpi_revision(acpi_rev);
    cosmos_acpi_set_rsdp(rsdp_address);

    // Find MADT in XSDT or RSDT
    acpi_header_t* madt = NULL_PTR;

    if (rsdp->revision >= 2 && ((acpi_xsdp_t*)rsdp)->xsdt != 0) {
        uint64_t xsdt_phys = ((acpi_xsdp_t*)rsdp)->xsdt;
        acpi_xsdt_t* xsdt = (acpi_xsdt_t*)phys_to_virt(xsdt_phys);
        __cosmos_serial_write("[ACPI] XSDT at phys 0x");
        __cosmos_serial_write_hex_u64(xsdt_phys);
        __cosmos_serial_write(" -> virt 0x");
        __cosmos_serial_write_hex_u64((uint64_t)xsdt);
        __cosmos_serial_write("\n");

        uint32_t count = (xsdt->header.length - sizeof(acpi_header_t)) / sizeof(uint64_t);
        for (uint32_t i = 0; i < count; i++) {
            acpi_header_t* tbl = (acpi_header_t*)phys_to_virt(xsdt->tables[i]);
            if (tbl->signature[0] == 'A' && tbl->signature[1] == 'P' &&
                tbl->signature[2] == 'I' && tbl->signature[3] == 'C') {
                madt = tbl;
                __cosmos_serial_write("[ACPI] MADT found\n");
                break;
            }
        }
    } else if (rsdp->rsdt != 0) {
        acpi_rsdt_t* rsdt = (acpi_rsdt_t*)phys_to_virt((uint64_t)rsdp->rsdt);
        __cosmos_serial_write("[ACPI] RSDT at: 0x");
        __cosmos_serial_write_hex_u32(rsdp->rsdt);
        __cosmos_serial_write("\n");

        uint32_t count = (rsdt->header.length - sizeof(acpi_header_t)) / sizeof(uint32_t);
        for (uint32_t i = 0; i < count; i++) {
            acpi_header_t* tbl = (acpi_header_t*)phys_to_virt((uint64_t)rsdt->tables[i]);
            if (tbl->signature[0] == 'A' && tbl->signature[1] == 'P' &&
                tbl->signature[2] == 'I' && tbl->signature[3] == 'C') {
                madt = tbl;
                __cosmos_serial_write("[ACPI] MADT found\n");
                break;
            }
        }
    }

    if (madt) {
        __cosmos_serial_write("[ACPI] Parsing MADT...\n");
        parse_madt(madt);
    } else {
        __cosmos_serial_write("[ACPI] WARNING: MADT not found\n");
    }

    g_initialized = 1;
    __cosmos_serial_write("[ACPI] Init complete\n");
}

// ============================================================================
// Public API for C# interop
// ============================================================================

#ifdef ARCH_X64
const acpi_madt_info_t* acpi_get_madt_info(void) {
    return g_initialized ? &g_madt_info : NULL_PTR;
}
#endif

#ifdef __aarch64__
const acpi_gic_info_t* acpi_get_gic_info(void) {
    return g_initialized ? &g_gic_info : NULL_PTR;
}
#endif
