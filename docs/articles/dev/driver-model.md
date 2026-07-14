# Device Driver Model

> **Status:** design proposal / decision record. Nothing in this document is implemented yet;
> it captures *how we intend to handle device drivers* and *what we deliberately rejected*, so the
> decision is on record and does not get relitigated.

## The problem

We do not want to hardcode every driver into the kernel. The kernel must be **modular** and **adapt to the
device it finds** at boot — discover the hardware, pick a driver, bind it — without the kernel source
knowing the full hardware list ahead of time. Two follow-on questions shaped this design:

1. We don't want to *write* thousands of drivers.
2. Can drivers run **safely**, in the spirit of Cosmos' managed model — and can they be **loaded dynamically**?

This document answers all three.

## Where we are today

The building blocks already exist; the gap is that discovery and instantiation are **hardcoded**.

| Piece | Where | State |
|---|---|---|
| Device interfaces | `Cosmos.Kernel.HAL.Interfaces/Devices/` — `INetworkDevice`, `IBlockDevice`, `ITimerDevice`, `IKeyboardDevice`, `IMouseDevice`, `IGraphicDevice` | Good — keep as-is |
| `Device` base class | `Cosmos.Kernel.HAL/Devices/Device.cs` | Empty; no lifecycle/metadata |
| PCI enumeration | `Cosmos.Kernel.HAL/Pci/PciManager.cs` (`GetDevice`, `GetDeviceClass`, `GetAllDevicesClass`) | Full recursive bus walk; **pull-based** — drivers query it |
| Device-ownership flag | `PciDevice.Claimed` | Present but **unused** — the seed of driver binding |
| Category managers | `StorageManager`, `NetworkManager`, `TimerManager`, `KeyboardManager`, `MouseManager` | Runtime registries, but only hold *already-created* devices |
| A real registry pattern | `VfsManager.RegisterFilesystem(name, IVfsFilesystemType)` | The template to generalize |
| Feature gating | `Cosmos.Sdk/Sdk/Sdk.props` + `Sdk.targets` + `CosmosFeatures.cs` | Runtime `AppContext` switches + ILC trimming (cascade), **not** `#if` |
| Build-time IL substitution | `Cosmos.Patcher` (plugs) | Compile-time only — not a runtime driver mechanism |

**The hardcoded seams** (what this design removes):

- `E1000E.FindAndCreate()` probes a hand-written list of `PciManager.GetDevice(Intel, <deviceId>)` calls.
- `X64PlatformInitializer.InitializeHardware()` imperatively `new`s PS/2, E1000E, then `Ahci.Initialize()` / `Nvme.Initialize()`.
- `IPlatformInitializer` exposes `GetNetworkDevice()` (singular), `GetKeyboardDevices()`, etc. — the platform *decides* which drivers exist.

## Constraints that drive every decision

NativeAOT is not incidental — it defines the solution space:

- **No reflection.** Kernel code cannot scan assemblies or instantiate types by name at runtime.
- **No JIT, single static image.** There is no CLR at runtime, no `Assembly.Load`, no metadata to link
  against. A Linux `.ko` / Windows `.sys` loadable-module model is off the table for *managed* code.
- **Managed safety has one hole: DMA.** C# driver logic is memory/type-safe on the CPU side, but a
  device programmed with a bad physical address writes to RAM directly, bypassing the CPU and GC.
  Managed-ness alone does not close this; the driver's raw MMIO + DMA reach must be constrained explicitly.

## The core model: static-link + dynamic-bind + trim

"Dynamically loaded" and "adapts to the device at runtime" are different things. Under AOT we get the
second — which is what modularity actually requires — like this:

- **All drivers are linked into the image at build time**, discovered by a **source generator** (we already
  ship source generators, e.g. `CosmosEntryPointGenerator`). No reflection.
- **At runtime a binder discovers the hardware and instantiates only the matching drivers.** The driver
  *set* is chosen at runtime (dynamic bind); the *code* was all linked at build.
- **Trimming tailors the image** — a driver whose feature switch is off is removed from the binary by ILC.

### Shape

**1. A driver declares what it binds to** (instead of hunting for its device):

```csharp
[PciDriver(VendorId.Intel, DeviceId.E82574L, DeviceId.E82574 /* … */)]   // vendor/device-specific
public sealed class E1000E : PciDevice, INetworkDevice { … }

[PciClassDriver(ClassId.MassStorageController, SubclassId.SataController, ProgIf.SataAhci)] // generic
public sealed class AhciController { … }
```

**2. A source generator emits a static manifest** — a match table plus a **direct factory delegate**. The
delegate reference is exactly what keeps the trimmer from dropping the driver:

```csharp
// GENERATED
internal static class DriverManifest
{
    public static readonly DriverEntry[] Entries =
    {
        new(PciMatch.Device(VendorId.Intel, DeviceId.E82574L), priority: 100,
            factory: static d => new E1000E(d.Bus, d.Slot, d.Function)),
        new(PciMatch.Class(ClassId.MassStorageController, SubclassId.SataController), priority: 10,
            factory: static d => new AhciController(d)),
    };
}
```

**3. A binder replaces all three hardcoded seams.** After `PciManager.Setup()`, it walks discovered
devices, picks the **highest-priority match** (so a vendor/device driver beats a generic class driver —
the fallback logic `E1000E.FindAndCreate` hand-codes today), constructs it, sets `Claimed`, and routes it
by the interface it implements — so the category managers never change:

```csharp
foreach (PciDevice dev in PciManager.Devices)
{
    if (dev.Claimed) { continue; }
    DriverEntry? e = DriverRegistry.BestMatch(dev);
    if (e is null) { continue; }

    object driver = e.Factory(dev);
    dev.Claimed = true;

    switch (driver)
    {
        case INetworkDevice n: NetworkManager.RegisterDevice(n); break;
        case IBlockDevice   b: StorageManager.RegisterDevice(b); break;
        // timer / input / …
    }
}
```

**What dies:** `E1000E.FindAndCreate()`'s probe list, the device-probing in `InitializeHardware()`, and the
`GetNetworkDevice()`/`GetKeyboardDevices()`/`GetMouseDevices()` accessors on `IPlatformInitializer`. The
platform initializer goes back to being *just* platform primitives (port IO, MMIO mapping, interrupt
controller, timer) — it should not know what a NIC is.

**What stays:** every `I*Device` interface, the managers, PCI/virtio enumeration, the feature-switch/trim
infrastructure.

> A hand-registered variant (each driver's `LibraryInitializer` calling `DriverRegistry.Register(...)`,
> mirroring `VfsManager.RegisterFilesystem`) is a smaller first step with the *same* registry/binder design.
> The source generator's payoff is *zero-boilerplate* driver addition ("add a file = add a driver"). Start
> hand-registered if desired; the generator drops in later without rework.

## Not writing 10,000 drivers: virtio-first

The realistic deployment target is **VMs and cloud** (the Makefile boots QEMU). In a VM the **hypervisor
already wrote the drivers** — it presents every real NIC/disk/GPU as a small, stable set of **virtio**
devices. Implement the virtio family well and the hardware matrix collapses to ~10 drivers:

- `virtio-net`, `virtio-blk` / `virtio-scsi`, `virtio-gpu`, `virtio-input`, `virtio-console`, `virtio-rng`
- plus the legacy fallbacks we already have/target: `AHCI`, `NVMe`, `E1000E`.

We are most of the way there — `Virtqueue`, `VirtioNet`, `VirtioInput`, `VirtioMMIO` already exist, but
**only in `Cosmos.Kernel.HAL.ARM64` over MMIO transport**. The move:

1. **Hoist** `Virtqueue` + virtio device/negotiation logic into shared `Cosmos.Kernel.HAL` (it is arch-independent).
2. **Add a `virtio-pci` transport** so x64 VMs (which expose virtio over PCI) light up the *same* driver code.
3. Register virtio drivers through the binder above.

This single refactor turns already-written code into broad x64 hardware coverage. **This is the "don't
write thousands of drivers" answer.**

## Making managed drivers safe-by-construction: capability HAL

Today drivers get raw BAR addresses and do unguarded `Native.MMIO` access. The Cosmos-philosophy fix is
**capability handles** — the driver never receives a raw pointer, only a bounded object the API won't let
it escape:

```csharp
MmioRegion regs = dev.MapBar(0);      // bounded to the BAR's length
regs.Write32(offset, v);              // offset bounds-checked; out-of-BAR ⇒ managed exception

DmaBuffer buf = Dma.Allocate(4096);   // only hands out pages this driver owns
dev.Queue.Submit(buf.PhysicalAddress, buf.Length);   // driver can't forge an arbitrary phys addr
```

A driver *cannot express* an out-of-bounds register poke or a wild DMA target because the API surface does
not offer the raw address. Later tiers:

- **IOMMU** (VT-d on x64 / SMMU on ARM64) makes the DMA guarantee *hardware-enforced* rather than by convention.
- Because drivers are managed, a driver fault can be a **catchable exception** the binder handles (mark the
  device failed, unbind it) instead of a triple-fault — fault isolation without a microkernel.

## Driver loading models — evaluated and rejected

The recurring question was "can drivers be loaded dynamically like `.sys`/`.ko`?" Here is the full
decision, so it does not come back.

### Rejected: native PE/ELF loader

Mechanically feasible (parse headers, map sections, relocate, resolve imports, jump to entry). But:

- The loaded driver is **native, unsafe** code — a wild pointer or bad DMA kills the kernel. It abandons the
  managed-safety thesis.
- **No driver reuse.** Existing Windows `.sys` files target the Windows kernel ABI (ntoskrnl exports, IRP/WDF).
  We can't satisfy that, so we'd define *our own* native ABI — meaning we still write every driver, just in C.
- If the native code is itself AOT-compiled C#, the real wall is not PE parsing but **runtime integration**:
  two independently-AOT-compiled images do not share a GC, type identity (`EEType`/`MethodTable`), generic
  instantiations, statics, or exception handling. Making them cooperate is deep, unsupported runtime surgery.

### Rejected: managed-IL PE + interpreter

The "load a C# driver DLL" idea. Blockers:

- **No IL engine.** NativeAOT ships neither JIT nor (in our image) interpreter. A loaded assembly is IL you
  cannot execute. Running it means **embedding a second runtime** — a Mono/CoreCLR-style IL interpreter.
- **IL is not safe by default.** Real drivers use `unsafe`/pointers; an interpreter that executes
  `ldind`/`stind` on raw pointers gives *zero* isolation. Making it a safety boundary means reintroducing
  **IL verification** (partial-trust IL) — which Microsoft retired — plus mediated memory.
- **NativeAOT integration walls:** the interpreter's dynamic evaluation stack must be a **GC root source**
  (NativeAOT scans statically-known compiled frames), and every interpreted→kernel call needs
  **type-identity marshaling** to AOT `MethodTable`s. These, not the opcode loop, are the cost.

### Chosen (for dynamic loading, if/when we want it): WASM sandbox

WASM is the only option that is **both dynamically loadable and safe**, because it hands us the two hardest
problems for free:

| | Native PE/ELF | Managed IL + interp | **WASM** |
|---|---|---|---|
| Memory isolation | ✗ none | ✗ needs IL verification | **✓ bounds-checked linear memory** |
| GC integration | ✗ shared heap / surgery | ✗ root the interp stack | **✓ none (memory is a byte[])** |
| Host interop | ✗ native ABI | ✗ marshal to `MethodTable`s | **✓ explicit import/export ABI** |
| Driver reuse | ✗ (foreign kernel ABI) | ✗ | n/a — write once, any language |
| Language | C/asm | C# only | **C#, Rust, C, Zig …** |
| Fault isolation | ✗ | partial | **✓ trap ⇒ catchable, driver-scoped** |

> Note: WASM does not change DMA physics — a granted DMA buffer's physical address is real. But it bounds
> the driver to only ever hand the device kernel-minted addresses, and IOMMU enforces that in hardware later.
> It is the *same* residual hole native drivers have, reduced to the *only* one.

**Where PE/ELF loading *does* earn its keep:** loading **user-mode programs** later — isolated by paging in
ring 3, unable to DMA, faulting alone. That is a different problem from drivers (which share the kernel's
address space and drive DMA), and PE/ELF is the right tool there.

## WASM driver architecture (future / optional)

Only worthwhile for three things: a **third-party driver ecosystem**, **hot-swappable/updatable** drivers,
and running **untrusted** driver code safely. Not needed for the VM/cloud baseline — treat as an upgrade path.

### The model

A driver is a `.wasm` module. **Its imports are the capabilities the kernel grants; its exports are the
lifecycle the kernel calls; its linear memory is the sandbox.** Everything good follows from those facts.

### The ABI

```
;; host functions the KERNEL provides — the driver's ENTIRE reach
(import "hal" "get_bar"   (func (param i32) (result i32)))   ;; -> opaque BAR handle
(import "hal" "mmio_r32"  (func (param i32 i32) (result i32)))
(import "hal" "mmio_w32"  (func (param i32 i32 i32)))
(import "hal" "dma_alloc" (func (param i32) (result i32)))   ;; -> ptr in module memory
(import "hal" "dma_phys"  (func (param i32) (result i64)))   ;; -> device-visible phys addr
(import "hal" "irq_ack"   (func (param i32)))
(import "hal" "log"       (func (param i32 i32)))

;; functions the DRIVER exports — what the kernel calls
(export "init")  (export "get_mac")  (export "send")  (export "on_irq")  (export "memory")
```

The import list *is* the security policy: a driver can only do what it imports, and the kernel bounds-checks
every call. A driver that never imports `dma_alloc` is provably incapable of DMA — auditable in seconds.

### Binding manifest

Each module carries a `cosmos.driver` **custom section** the binder reads to match hardware and grant caps:

```
CDRV  ver=1  bus=PCI  vendor=0x8086  device=0x10F0  class=02/00  caps=0b0111 (MMIO|DMA|IRQ)
```

### Kernel side (where safety is enforced)

```csharp
// host functions handed to ONE driver instance, closed over its exact grants
public void MmioW32(int barHandle, int off, uint val)
{
    MmioRegion r = ResolveBar(barHandle);
    if ((uint)off + 4 > r.Length) { throw new WasmTrap("MMIO out of BAR"); }  // ← the wall
    r.Write32(off, val);
}
public int  DmaAlloc(int size) => _mod.MapIntoLinearMemory(Dma.Allocate(size)); // kernel-minted, quota'd
public long DmaPhys(int linptr) => (long)_mod.PhysOf(linptr);

// adapter — makes a module quack like the existing INetworkDevice, so managers never learn it's wasm
internal sealed class WasmNetworkDevice : INetworkDevice
{
    private readonly WasmInstance _mod;
    public void Initialize() => _mod.Call("init");
    public bool Send(byte[] d, int len)
    {
        int p = _mod.Alloc(len); _mod.WriteBytes(p, d, len);
        return _mod.Call("send", p, len) != 0;
    }
    // MacAddress ← _mod.Call("get_mac", buf);  device IRQ ⇒ _mod.Call("on_irq")
}
```

The binder from the core model gains a second entry kind: a PCI match either calls a native factory or
instantiates a `.wasm` module (found on disk via VFS/FAT) + adapter. Same match-and-bind loop.

### Engine reality

- **No JIT ⇒ interpreter.** WASM is a small, rigorously-specified stack machine (~170 opcodes) — far simpler
  to interpret than IL, with **no GC integration** (memory is a `byte[]`) and **no type-identity marshaling**
  (boundary values are i32/i64/f/bytes). References: wasm3 (~64 KB C interpreter), wasmi (Rust). Writing a
  compact AOT-safe C# interpreter is the one substantial build item.
- **Performance:** interpreted WASM ≈ 5–15× native. Fine for the long tail (input, RTC, slow NICs, control
  planes). For hot datapaths, keep the DMA ring native and only the control logic in WASM, or make the few
  hot devices native drivers. **WASM-for-the-tail, native-for-the-hot-few.**
- **Memory footprint is explicit in the file** — `(memory N)` is exactly how much RAM the driver can ever
  touch, and the kernel backs (and caps) it per instance.

### Worked example: ToyNIC

A complete driver for a minimal MMIO NIC, authored twice (C and Rust) — both compile to interchangeable
modules with the *identical* kernel-facing interface. Rust source (`no_std`, `wasm32-unknown-unknown`):

```rust
#![no_std]
use core::panic::PanicInfo;
#[panic_handler] fn panic(_: &PanicInfo) -> ! { loop {} }

#[link(wasm_import_module = "hal")]
extern "C" {
    fn get_bar(index: i32) -> i32;
    fn mmio_r32(bar: i32, off: i32) -> u32;
    fn mmio_w32(bar: i32, off: i32, val: u32);
    fn dma_alloc(size: i32) -> i32;
    fn dma_phys(linptr: i32) -> i64;
    fn irq_ack(bar: i32);
    fn log(ptr: i32, len: i32);
}
const REG_STATUS: i32 = 0x08; const REG_CTRL: i32 = 0x0C;
const REG_TX_LO: i32 = 0x10;  const REG_TX_HI: i32 = 0x14;
const REG_TX_LEN: i32 = 0x18; const REG_TX_KICK: i32 = 0x1C; const REG_IRQ: i32 = 0x20;

static mut BAR0: i32 = 0; static mut TXBUF: i32 = 0; static mut TXPHYS: i64 = 0;
static BANNER: &[u8] = b"toynic: up";

#[no_mangle] pub extern "C" fn init() -> i32 { unsafe {
    BAR0 = get_bar(0);
    mmio_w32(BAR0, REG_CTRL, 1);            // enable
    TXBUF = dma_alloc(2048);               // kernel-minted DMA buffer
    TXPHYS = dma_phys(TXBUF);
    log(BANNER.as_ptr() as i32, BANNER.len() as i32);
    if mmio_r32(BAR0, REG_STATUS) & 1 != 0 { 0 } else { 1 }
}}

#[no_mangle] pub extern "C" fn send(frame: i32, len: i32) -> i32 {
    if len > 2048 { return 0; }
    unsafe {
        core::ptr::copy_nonoverlapping(frame as *const u8, TXBUF as *mut u8, len as usize);
        mmio_w32(BAR0, REG_TX_LO,  TXPHYS as u32);
        mmio_w32(BAR0, REG_TX_HI, (TXPHYS >> 32) as u32);
        mmio_w32(BAR0, REG_TX_LEN, len as u32);
        mmio_w32(BAR0, REG_TX_KICK, 1);    // doorbell
    }
    1
}

#[no_mangle] pub extern "C" fn on_irq() -> i32 { unsafe {
    let cause = mmio_r32(BAR0, REG_IRQ);
    mmio_w32(BAR0, REG_IRQ, cause);        // write-to-clear
    irq_ack(BAR0);
    cause as i32
}}
```

Build commands used to produce real modules (~1.1–1.3 KB each):

```bash
# Rust
rustup target add wasm32-unknown-unknown
rustc --edition 2021 --target wasm32-unknown-unknown -O --crate-type=cdylib -C panic=abort -o toynic.wasm toynic.rs

# C (clang has a built-in wasm backend + wasm-ld)
clang --target=wasm32 -O2 -nostdlib -ffreestanding \
  -Wl,--no-entry -Wl,--export-memory \
  -Wl,--export=init -Wl,--export=get_mac -Wl,--export=send -Wl,--export=on_irq \
  -o toynic.wasm toynic.c
```

Both compile `init` to the *same* opcode logic; the binder cannot tell C from Rust — it sees only imports,
exports, and linear memory. That interchangeability is the point.

### The spike (smallest end-to-end proof)

1. Embed a bare-bones wasm interpreter (enough opcodes for one driver).
2. Define the capability ABI for one class (e.g. `virtio-input` or the ToyNIC above).
3. Author that driver in Rust or C → `wasm32`, drop it on the FAT image.
4. Binder finds it, grants BAR + DMA + IRQ, adapter registers it into the category manager.
5. `make test` in QEMU — watch an interpreted, sandboxed, disk-loaded driver drive real hardware.

## Roadmap

| Phase | Deliverable | Payoff |
|---|---|---|
| 1 | `DriverRegistry` + binder + `[PciDriver]` seam; convert **E1000E** (network) and **AHCI** (storage), hand-registered | Kernel adapts to hardware; both routing paths proven |
| 2 | Source generator to auto-populate the manifest; drop hand registration | "Add a file = add a driver" |
| 3 | Hoist virtio to shared HAL + `virtio-pci` transport; virtio on x64 through the binder | Broad VM/cloud coverage from existing code |
| 4 | Capability HAL (`MmioRegion`, `DmaBuffer`); optionally IOMMU | Safe-by-construction managed drivers |
| 5 *(optional)* | WASM driver sandbox (interpreter + ABI + adapter) | Loadable, updatable, untrusted-safe third-party drivers |

## Summary

- **Modularity under AOT = static-link + dynamic-bind + trim.** The kernel adapts to the hardware it finds;
  each build contains only the drivers it needs. Not "dynamically loaded" — dynamically *bound*.
- **~10 drivers, not 10,000**, by leaning on the hypervisor via a first-class **virtio** stack.
- **Safe managed drivers** come from **capability-bounded MMIO/DMA**, not from managed-ness alone (DMA is the hole).
- **Runtime-loadable drivers**, if ever wanted, are a **WASM sandbox** — the only path that keeps safety and a
  clean kernel boundary. **PE and IL both fail** (unsafe / no engine / brutal runtime integration).
- PE/ELF loading is filed under a future **user-mode program loader**, a different problem.
