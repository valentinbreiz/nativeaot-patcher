// ACPI wrapper for Cosmos OS (Multi-Architecture)
// Shared RSDP/XSDT/RSDT walking code using LAI struct typedefs.
// x86: parses MADT for Local APIC / IO APIC / ISOs
// ARM64: parses MADT for GICD / GICR / GICC entries

#include <stdint.h>
#include <stddef.h>

#include <lai/core.h>
#include <lai/helpers/pm.h>
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
extern void* cosmos_acpi_get_rsdp(void);

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
    uint8_t  its_found;     // 1 if at least one GIC ITS subtable seen
    uint8_t  _pad[5];
    uint64_t dist_base;     // GICD physical base
    uint64_t redist_base;   // GICR physical base (GICv3)
    uint64_t redist_length; // GICR region length
    uint64_t cpu_if_base;   // GICC physical base (GICv2)
    uint64_t its_base;      // GIC ITS physical base (GICv3 + ITS only); 0 if absent
    uint32_t its_id;        // ITS Translation ID (referenced by IORT)
    uint32_t _pad2;
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
// PCI MCFG structure (shared across architectures)
// ============================================================================

typedef struct {
    uint8_t  found;
    uint8_t  start_bus;
    uint8_t  end_bus;
    uint8_t  _pad1;
    uint16_t segment;
    uint16_t _pad2;
    uint64_t base_address;  // ECAM physical base
} acpi_mcfg_info_t;

static acpi_mcfg_info_t g_mcfg_info;

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

// 4-byte signature compare (ACPI table signatures are exactly 4 ASCII chars).
static inline int sig_eq(const char* a, const char* b) {
    return a[0] == b[0] && a[1] == b[1] && a[2] == b[2] && a[3] == b[3];
}

// Walk XSDT/RSDT looking for the Nth table whose signature matches `signature`.
// Returns a virtual pointer to the table header, or NULL if not found.
// Used by laihost_scan() to fulfill LAI's namespace-creation table requests
// (FACP/DSDT/SSDT/PSDT) and by lai_acpi_reset() for the FADT reset register.
//
// DSDT is special-cased: per ACPI spec it is NOT a top-level XSDT/RSDT entry —
// its physical address is stored inside the FADT (x_dsdt / dsdt fields), so we
// must resolve it via FACP before falling back to the XSDT/RSDT walk.
void* cosmos_acpi_scan_table(const char* signature, size_t index) {
    if (signature == NULL_PTR) return NULL_PTR;

    acpi_rsdp_t* rsdp = (acpi_rsdp_t*)cosmos_acpi_get_rsdp();
    if (rsdp == NULL_PTR) return NULL_PTR;

    if (index == 0 && sig_eq(signature, "DSDT")) {
        acpi_fadt_t* fadt = (acpi_fadt_t*)cosmos_acpi_scan_table("FACP", 0);
        if (fadt == NULL_PTR) return NULL_PTR;
        uint64_t dsdt_phys = 0;
        if (fadt->header.length >= sizeof(acpi_fadt_t) && fadt->x_dsdt != 0) {
            dsdt_phys = fadt->x_dsdt;
        } else if (fadt->dsdt != 0) {
            dsdt_phys = (uint64_t)fadt->dsdt;
        }
        if (dsdt_phys == 0) return NULL_PTR;
        acpi_header_t* dsdt = (acpi_header_t*)phys_to_virt(dsdt_phys);
        if (dsdt && sig_eq(dsdt->signature, "DSDT")) return dsdt;
        return NULL_PTR;
    }

    size_t matches = 0;

    if (rsdp->revision >= 2 && ((acpi_xsdp_t*)rsdp)->xsdt != 0) {
        acpi_xsdt_t* xsdt = (acpi_xsdt_t*)phys_to_virt(((acpi_xsdp_t*)rsdp)->xsdt);
        uint32_t count = (xsdt->header.length - sizeof(acpi_header_t)) / sizeof(uint64_t);
        for (uint32_t i = 0; i < count; i++) {
            acpi_header_t* tbl = (acpi_header_t*)phys_to_virt(xsdt->tables[i]);
            if (tbl && sig_eq(tbl->signature, signature)) {
                if (matches++ == index) return tbl;
            }
        }
    } else if (rsdp->rsdt != 0) {
        acpi_rsdt_t* rsdt = (acpi_rsdt_t*)phys_to_virt((uint64_t)rsdp->rsdt);
        uint32_t count = (rsdt->header.length - sizeof(acpi_header_t)) / sizeof(uint32_t);
        for (uint32_t i = 0; i < count; i++) {
            acpi_header_t* tbl = (acpi_header_t*)phys_to_virt((uint64_t)rsdt->tables[i]);
            if (tbl && sig_eq(tbl->signature, signature)) {
                if (matches++ == index) return tbl;
            }
        }
    }

    return NULL_PTR;
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
            case MADT_TYPE_ITS: {
                // GIC ITS subtable (ACPI 6.5 §5.2.12.18):
                // type(1) length(1) reserved(2) translation_id(4)
                // physical_base(8) reserved(4)
                if (entry_len >= 20 && !g_gic_info.its_found) {
                    uint32_t id = *(uint32_t*)(madt + offset + 4);
                    uint64_t base = *(uint64_t*)(madt + offset + 8);
                    g_gic_info.its_base = base;
                    g_gic_info.its_id = id;
                    g_gic_info.its_found = 1;
                    __cosmos_serial_write("[ACPI-GIC] ITS: id=");
                    __cosmos_serial_write_dec_u32(id);
                    __cosmos_serial_write(" base=0x");
                    __cosmos_serial_write_hex_u64(base);
                    __cosmos_serial_write("\n");
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
// MCFG parsing (PCI ECAM base address discovery)
// ============================================================================

static void parse_mcfg(acpi_header_t* mcfg_header) {
    uint8_t* mcfg = (uint8_t*)mcfg_header;
    uint32_t length = mcfg_header->length;

    // MCFG: header(36) + reserved(8) + entries(16 each)
    uint32_t offset = sizeof(acpi_header_t) + 8;

    if (offset + 16 > length) {
        __cosmos_serial_write("[ACPI-MCFG] No entries in MCFG table\n");
        return;
    }

    // Parse first entry (segment 0)
    g_mcfg_info.base_address = *(uint64_t*)(mcfg + offset);
    g_mcfg_info.segment = *(uint16_t*)(mcfg + offset + 8);
    g_mcfg_info.start_bus = mcfg[offset + 10];
    g_mcfg_info.end_bus = mcfg[offset + 11];
    g_mcfg_info.found = 1;

    __cosmos_serial_write("[ACPI-MCFG] ECAM base=0x");
    __cosmos_serial_write_hex_u64(g_mcfg_info.base_address);
    __cosmos_serial_write(" segment=");
    __cosmos_serial_write_dec_u32(g_mcfg_info.segment);
    __cosmos_serial_write(" bus=");
    __cosmos_serial_write_dec_u32(g_mcfg_info.start_bus);
    __cosmos_serial_write("-");
    __cosmos_serial_write_dec_u32(g_mcfg_info.end_bus);
    __cosmos_serial_write("\n");
}

// ============================================================================
// IORT parsing (ARM64 only) — translates PCI requester IDs to ITS DeviceIDs
// per ARM DEN 0049 "IO Remapping Table" specification.
// ============================================================================

#ifdef __aarch64__

#define IORT_NODE_ITS_GROUP    0x00
#define IORT_NODE_ROOT_COMPLEX 0x02
#define IORT_NODE_SMMU_V1V2    0x03
#define IORT_NODE_SMMU_V3      0x04

// Cached pointer to the IORT, virtual address. NULL if no IORT was found.
static uint8_t* g_iort_base = NULL_PTR;
static uint32_t g_iort_length = 0;

// Read 32-bit little-endian field at unaligned offset.
static inline uint32_t iort_rd32(const uint8_t* p, uint32_t off) {
    return (uint32_t)p[off] | ((uint32_t)p[off+1] << 8)
         | ((uint32_t)p[off+2] << 16) | ((uint32_t)p[off+3] << 24);
}
static inline uint16_t iort_rd16(const uint8_t* p, uint32_t off) {
    return (uint16_t)p[off] | ((uint16_t)p[off+1] << 8);
}

// Find the node at file-relative byte offset `node_off`. Returns NULL if it
// would extend beyond the table.
static const uint8_t* iort_node_at(uint32_t node_off) {
    if (g_iort_base == NULL_PTR) return NULL_PTR;
    if (node_off + 16 > g_iort_length) return NULL_PTR;
    uint16_t len = iort_rd16(g_iort_base, node_off + 1);
    if (len < 16 || node_off + len > g_iort_length) return NULL_PTR;
    return g_iort_base + node_off;
}

static void parse_iort(acpi_header_t* iort_header) {
    g_iort_base = (uint8_t*)iort_header;
    g_iort_length = iort_header->length;

    uint32_t node_count = iort_rd32(g_iort_base, sizeof(acpi_header_t));
    uint32_t node_array_off = iort_rd32(g_iort_base, sizeof(acpi_header_t) + 4);

    __cosmos_serial_write("[ACPI-IORT] nodes=");
    __cosmos_serial_write_dec_u32(node_count);
    __cosmos_serial_write(" base=0x");
    __cosmos_serial_write_hex_u64((uint64_t)(uintptr_t)g_iort_base);
    __cosmos_serial_write("\n");

    // Sanity: walk once for diagnostics.
    uint32_t off = node_array_off;
    for (uint32_t i = 0; i < node_count && off + 16 <= g_iort_length; i++) {
        uint8_t  type = g_iort_base[off];
        uint16_t len  = iort_rd16(g_iort_base, off + 1);
        if (len < 16) break;
        if (type == IORT_NODE_ROOT_COMPLEX && len >= 32) {
            uint32_t segment = iort_rd32(g_iort_base, off + 28);
            uint32_t mappings = iort_rd32(g_iort_base, off + 8);
            __cosmos_serial_write("[ACPI-IORT] Root Complex seg=");
            __cosmos_serial_write_dec_u32(segment);
            __cosmos_serial_write(" mappings=");
            __cosmos_serial_write_dec_u32(mappings);
            __cosmos_serial_write("\n");
        } else if (type == IORT_NODE_ITS_GROUP) {
            uint32_t its_count = (len >= 20) ? iort_rd32(g_iort_base, off + 16) : 0;
            __cosmos_serial_write("[ACPI-IORT] ITS Group count=");
            __cosmos_serial_write_dec_u32(its_count);
            __cosmos_serial_write("\n");
        }
        off += len;
    }
}

// Resolve a (segment, bdf) requester ID to an ITS DeviceID by walking IORT
// Root Complex → ID mappings → ITS Group. Returns 0 on success, nonzero
// otherwise. SMMU pass-through nodes are followed once (their input is the
// requester ID, output continues to ITS).
int acpi_iort_resolve_device_id(uint32_t segment, uint32_t bdf, uint32_t* out_device_id) {
    if (out_device_id == NULL_PTR) return 1;
    *out_device_id = bdf;  // identity fallback even on early return

    if (g_iort_base == NULL_PTR) return 1;

    uint32_t node_count = iort_rd32(g_iort_base, sizeof(acpi_header_t));
    uint32_t node_array_off = iort_rd32(g_iort_base, sizeof(acpi_header_t) + 4);

    // Find Root Complex matching segment.
    uint32_t off = node_array_off;
    const uint8_t* rc = NULL_PTR;
    for (uint32_t i = 0; i < node_count; i++) {
        const uint8_t* n = iort_node_at(off);
        if (n == NULL_PTR) return 1;
        uint16_t len = iort_rd16(n, 1);
        if (n[0] == IORT_NODE_ROOT_COMPLEX && len >= 32) {
            uint32_t seg = iort_rd32(n, 28);
            if (seg == segment) { rc = n; break; }
        }
        off += len;
    }
    if (rc == NULL_PTR) return 1;

    uint32_t cur_id = bdf;
    const uint8_t* node = rc;

    // Walk up to two hops: RC → (optional) SMMU → ITS.
    for (int hop = 0; hop < 3; hop++) {
        uint8_t  ntype = node[0];
        if (ntype == IORT_NODE_ITS_GROUP) {
            *out_device_id = cur_id;
            return 0;
        }

        uint32_t num_map = iort_rd32(node, 8);
        uint32_t map_off = iort_rd32(node, 12);
        if (num_map == 0) return 1;

        uint32_t node_off_in_table = (uint32_t)(node - g_iort_base);
        const uint8_t* map = node + map_off;
        // ID mapping: input_base(4) id_count(4) output_base(4) output_ref(4) flags(4) — 20 bytes
        const uint8_t* hit = NULL_PTR;
        for (uint32_t i = 0; i < num_map; i++) {
            const uint8_t* m = map + i * 20;
            // Bound check: map entry must fit within the IORT.
            uint32_t m_off = node_off_in_table + map_off + i * 20;
            if (m_off + 20 > g_iort_length) return 1;
            uint32_t in_base = iort_rd32(m, 0);
            uint32_t id_cnt  = iort_rd32(m, 4);
            uint32_t flags   = iort_rd32(m, 16);
            // Single-mapping entries (flags bit 0) are only architecturally
            // valid on Named Component / PMCG nodes (ARM DEN 0049), neither
            // of which appears on this RC → SMMU → ITS walk. On an RC hop a
            // single-mapping entry would collapse EVERY RID into one
            // DeviceID (two devices sharing a DeviceID: the second MAPD
            // re-issues a fresh ITT and destroys the first device's event
            // mappings); on SMMU nodes it describes the SMMU's own MSI
            // DeviceID. Linux's iort_id_map rejects the flag on both as a
            // firmware bug — skip such entries entirely, and never
            // range-match them (id_count is 0, so the inclusive check
            // below could fire on in_base).
            if (flags & 1u) {
                continue;
            }
            // "Number of IDs" holds the range size MINUS ONE (ARM DEN 0049),
            // so the match is inclusive. An exclusive `< in_base + id_cnt`
            // check drops the last ID of every range and never matches
            // single-ID ranges (id_count == 0) — firmware with a
            // non-identity mapping would then silently fall back to
            // DeviceID = BDF and the ITS would discard every MSI.
            if (cur_id >= in_base && cur_id - in_base <= id_cnt) {
                hit = m;
                break;
            }
        }
        if (hit == NULL_PTR) return 1;

        uint32_t in_base    = iort_rd32(hit, 0);
        uint32_t out_base   = iort_rd32(hit, 8);
        uint32_t out_ref    = iort_rd32(hit, 12);
        // hit is always a range mapping (single-mapping entries are
        // skipped above), so the output ID is base plus offset.
        cur_id = out_base + (cur_id - in_base);

        const uint8_t* next = iort_node_at(out_ref);
        if (next == NULL_PTR) return 1;
        node = next;
    }
    return 1;
}

#else // !__aarch64__

int acpi_iort_resolve_device_id(uint32_t segment, uint32_t bdf, uint32_t* out_device_id) {
    if (out_device_id != NULL_PTR) *out_device_id = bdf;
    return 1;
}

#endif // __aarch64__

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
    for (int i = 0; i < (int)sizeof(g_mcfg_info); i++)
        ((uint8_t*)&g_mcfg_info)[i] = 0;

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

    acpi_header_t* madt = (acpi_header_t*)cosmos_acpi_scan_table("APIC", 0);
    acpi_header_t* mcfg = (acpi_header_t*)cosmos_acpi_scan_table("MCFG", 0);
    if (madt) __cosmos_serial_write("[ACPI] MADT found\n");
    if (mcfg) __cosmos_serial_write("[ACPI] MCFG found\n");

    if (madt) {
        __cosmos_serial_write("[ACPI] Parsing MADT...\n");
        parse_madt(madt);
    } else {
        __cosmos_serial_write("[ACPI] WARNING: MADT not found\n");
    }

    if (mcfg) {
        __cosmos_serial_write("[ACPI] Parsing MCFG...\n");
        parse_mcfg(mcfg);
    }

#ifdef __aarch64__
    acpi_header_t* iort = (acpi_header_t*)cosmos_acpi_scan_table("IORT", 0);
    if (iort) {
        __cosmos_serial_write("[ACPI] IORT found, parsing...\n");
        parse_iort(iort);
    } else {
        __cosmos_serial_write("[ACPI] IORT not present (DeviceID = BDF)\n");
    }
#endif

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

const acpi_mcfg_info_t* acpi_get_mcfg_info(void) {
    return g_initialized ? &g_mcfg_info : NULL_PTR;
}

// ============================================================================
// Power management — ACPI _S5 / FADT reset via LAI (x86 only)
// ============================================================================

#ifdef ARCH_X64

// Lazily build the AML namespace on first PM call. lai_acpi_reset() reads the
// FADT directly via laihost_scan and doesn't need the namespace, but
// lai_enter_sleep(5) must resolve \\_S5_ from the DSDT.
static int g_namespace_created = 0;

int cosmos_acpi_shutdown(void) {
    if (cosmos_acpi_get_rsdp() == NULL_PTR) return 1;
    if (!g_namespace_created) {
        __cosmos_serial_write("[ACPI-PM] Creating AML namespace...\n");
        lai_create_namespace();
        g_namespace_created = 1;
    }
    __cosmos_serial_write("[ACPI-PM] lai_enter_sleep(S5)\n");
    return (int)lai_enter_sleep(5);
}

int cosmos_acpi_reset(void) {
    __cosmos_serial_write("[ACPI-PM] lai_acpi_reset\n");
    return (int)lai_acpi_reset();
}

#else

// LAI's PM helpers are x86-only (port I/O via FADT PM1a). ARM64 uses PSCI.
int cosmos_acpi_shutdown(void) { return 1; }
int cosmos_acpi_reset(void) { return 1; }

#endif
