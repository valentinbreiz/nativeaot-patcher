## Gen2/Gen3 Feature Comparison

![Gen3 Release Progress](https://img.shields.io/badge/Gen3_First_Release-90%25-yellow?style=for-the-badge)

| Feature | Gen2 | Gen3 | Notes |
|---------|------|-------------|-------|
| Low level assembly access | ✅ | ✅ | Before X# now x64 NASM + ARM64 GAS assembly. |
| ACPI | ✅ | ✅ | LAI (Lightweight ACPI Implementation) via C interop.  |
| Interrupt Handling | ✅  | ✅  | x64: APIC (Local + I/O). ARM64: GIC. |
| Memory Management | ✅ | ✅ ||
| Driver support | ✅ | ✅ | PCI and MMIO |
| Garbage Collection | ✅ | ✅ | Mark-and-sweep GC |
| Filesystem | ✅ | 🟡 In progress |  |
| .NET core library features | 🟡 | 🟡 Partial | Core types work (String, Collections, List, Dictionary). Console, DateTime, Random, BitOperations plugged. Missing: `System.Math` (Sin/Cos/Tan/Log/Exp/Pow), `System.IO.File`. |
| Plug system | ✅ | ✅  |  |
| Test Framework | ✅ | ✅  |  |
| Debugger| ✅ | 🟡 Partial | Source link + variables bugs in vscode |
| CPU/FPU accelerated math | ✅ | 🟡 Minimal | No hardware FPU math, no `System.Math` plug. |
| Cosmos Graphic Subsystem | ✅ | ✅ | UEFI GOP framebuffer via Limine only. |
| Network interface | ✅ | ✅ | |
| Timer / Clock | ✅ | ✅ | |
| Keyboard Input | ✅ | ✅ | |
| Mouse Input | ✅ | ✅ | |
| Audio interface | 🟡 | ❌ | No audio, sound, or speaker support. |

## Additional Gen3 Features

Beyond Gen2 parity, Gen3 brings new capabilities:

| Feature | Status | Notes |
|---------|--------|-------|
| **NativeAOT Runtime** | 🟡 In progress | Full NativeAOT compilation with runtime, no IL2CPU. |
| **ARM64 Support** | ✅  |  |
| **Limine Boot Protocol** | ✅ Complete |  |
| **Threading & Scheduler** | ✅ Complete | Priority-based stride scheduler (x64 + ARM64). |
| **Feature Flags** | ✅ Complete |  |
| **Cosmos Vs Code Extension** | ✅ Complete |  |

## Future Releases

Features planned after first release:

| Feature | Status | Notes |
|---------|--------|-------|
| **SMP (Symmetric Multiprocessing)** | ❌ Not Started | Multi-core AP boot, per-CPU scheduling, load balancer. |
| **USB Support** | ❌ Not Started | XHCI/EHCI host controller drivers, USB HID (keyboard/mouse), mass storage. |
| **HTTPS** | ❌ Not Started | TLS/SSL implementation, certificate handling, secure sockets. |
| **Generational GC** | ❌ Not Started | Replace current mark-and-sweep with generational collector (Gen0/Gen1/Gen2) for better performance. |
| **Code execution** | ❌ Not Started | Userland WASM VM |

