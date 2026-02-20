---
_layout: landing
---

Welcome to the nativeaot-patcher wiki!

## How to Use

There are two ways to use nativeaot-patcher depending on your needs:

### As a Developer

If you want to develop or contribute to the project, clone the repository and open it in VS Code. The workspace includes tasks for building and debugging kernels, running tests, and launching QEMU. See the [Kernel Compilation Steps](articles/build/kernel-compilation-steps.md) and [Debugging with VSCode and QEMU](articles/debugging.md) articles to get started.

### As a User

If you want to build bare-metal C# kernels without setting up the full toolchain manually, use the [Cosmos VS Code Extension](https://github.com/valentinbreiz/CosmosVsCodeExtension). It integrates gen3 into VS Code, providing a streamlined experience for creating, building, and running Cosmos kernels directly from the editor. See the [Installation guide](articles/install.md) to get started.

## Documentation
 - [Installation (Cosmos VS Code Extension)](articles/install.md)
 - [Debugging with VSCode and QEMU](articles/debugging.md)
 - [Kernel Compilation Steps](articles/build/kernel-compilation-steps.md)
 - [Kernel Project Layout](articles/kernel-project-layout.md)
 - [Plugs](articles/plugs.md)
 - [Testing](articles/testing.md)
 - [Garbage Collector](articles/garbage-collector.md)
 - [Cosmos.Build.Asm](articles/build/asm-build.md)
 - [Cosmos.Build.GCC](articles/build/gcc-build.md)
 - [Cosmos.Build.Patcher](articles/build/patcher-build.md)
 - [Cosmos.Build.Ilc](articles/build/ilc-build.md)

## Resources
- [Cosmos Gen3: The NativeAOT Era and the End of IL2CPU?](https://valentin.bzh/posts/3)
- [NativeAOT Developer Workflow](https://github.com/dotnet/runtime/blob/main/docs/workflow/building/coreclr/nativeaot.md)
- [NativeAOT Limitations](https://github.com/dotnet/runtime/blob/main/src/coreclr/nativeaot/docs/limitations.md)

## Gen2/Gen3 Feature Comparison

| Feature | Gen2 | Gen3 Status | Notes |
|---------|------|-------------|-------|
| Low level assembly access | ✅ | ✅ | Before X# now x64 NASM + ARM64 GAS assembly. |
| ACPI | ✅ | ✅ | LAI (Lightweight ACPI Implementation) via C interop.  |
| Interrupt Handling | ✅  | ✅  | x64: APIC (Local + I/O). ARM64: GIC. |
| Memory Management | ✅ | ✅ ||
| Driver support | ✅ | 🟡 Partial | Only PCI on x64 |
| Garbage Collection | ✅ | ✅ | Mark-and-sweep GC |
| Filesystem | ✅ | 🟡 In progress |  |
| .NET core library features | ✅ | 🟡 Partial | Core types work (String, Collections, List, Dictionary). Console, DateTime, Random, BitOperations plugged. Missing: `System.Math` (Sin/Cos/Tan/Log/Exp/Pow), `System.IO.File`. |
| Plug system | ✅ | ✅  |  |
| Test Framework | ✅ | ✅  |  |
| Debugger| ✅ | 🟡 Partial | Source link + variables bugs in vscode |
| CPU/FPU accelerated math | ✅ | 🟡 Minimal | SSE enabled but only used for memory operations. Software `ceil`/`sqrt` only. No hardware FPU math, no `System.Math` plug. |
| Cosmos Graphic Subsystem | ✅ | ✅ | UEFI GOP framebuffer via Limine only. |
| Network interface | ✅ | 🟡 Partial | x64 only, no ARM64 network driver. |
| Timer / Clock | ✅ | ✅ | |
| Keyboard Input | ✅ | ✅ | |
| Mouse Input | ✅ | ❌ Not Started | |
| Audio interface | ✅ | ❌ Not Started | No audio, sound, or speaker support. |

## Additional Gen3 Features

Beyond Gen2 parity, Gen3 brings new capabilities:

| Feature | Status | Notes |
|---------|--------|-------|
| **NativeAOT Runtime** | 🟡 In progress | Full NativeAOT compilation with runtime, no IL2CPU. |
| **ARM64 Support** | 🟡 Partial  | Missing network driver, timer bugs. |
| **Limine Boot Protocol** | ✅ Complete |  |
| **Threading & Scheduler** | ✅ Complete | Priority-based stride scheduler (x64 + ARM64). |
| **Feature Flags** | ✅ Complete |  |
| **Cosmos Vs Code Extension** | ✅ Complete |  |

## Future Releases

Features planned for post-Gen3 releases:

| Feature | Status | Notes |
|---------|--------|-------|
| **SMP (Symmetric Multiprocessing)** | ❌ Not Started | Multi-core AP boot, per-CPU scheduling, load balancer. |
| **USB Support** | ❌ Not Started | XHCI/EHCI host controller drivers, USB HID (keyboard/mouse), mass storage. |
| **HTTPS** | ❌ Not Started | TLS/SSL implementation, certificate handling, secure sockets. |
| **Generational GC** | ❌ Not Started | Replace current mark-and-sweep with generational collector (Gen0/Gen1/Gen2) for better performance. |




xref link [xrefmap.yml](xrefmap.yml)
