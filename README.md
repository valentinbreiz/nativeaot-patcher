<img width="546" height="538" alt="image" src="https://github.com/user-attachments/assets/7679c86d-68a3-4678-99c2-d5e1f3987eee" />

> [Voted French law aimed at criminalizing free culture](https://www.assemblee-nationale.fr/dyn/17/textes/l17b1133_proposition-loi#). No to authoritarianism! No to fascism! Support your local sound systems!
> ### 🇫🇷 French citizens — **[SIGN THE PETITION](https://petitions.assemblee-nationale.fr/initiatives/i-5428)**

# Cosmos gen3

A bare-metal C# kernel framework built on **NativeAOT**. Cosmos gen3 is the next generation of the [Cosmos](https://github.com/CosmosOS/Cosmos) operating system project, replacing the IL2CPU transpiler with the official .NET ahead-of-time compiler. The result is an ordinary `dotnet build` that produces a bootable kernel ELF for **x64 or ARM64**, linked with an integrated runtime, plugged with the Cosmos plug system, and packaged into an ISO with the Limine bootloader.

Originally based on [Zarlo's NativeAOT patcher](https://gitlab.com/liquip/nativeaot-patcher). See [CosmosOS/Cosmos#3088](https://github.com/CosmosOS/Cosmos/issues/3088) for the design discussion behind the gen3 effort.

## Status

[![.NET Tests](https://github.com/valentinbreiz/nativeaot-patcher/actions/workflows/dotnet.yml/badge.svg?branch=main)](https://github.com/valentinbreiz/nativeaot-patcher/actions/workflows/dotnet.yml)
[![Kernel Tests](https://github.com/valentinbreiz/nativeaot-patcher/actions/workflows/kernel-tests.yml/badge.svg?branch=main)](https://github.com/valentinbreiz/nativeaot-patcher/actions/workflows/kernel-tests.yml)
[![Release](https://github.com/valentinbreiz/nativeaot-patcher/actions/workflows/release.yml/badge.svg)](https://github.com/valentinbreiz/nativeaot-patcher/actions/workflows/release.yml)
[![Gen3 Release Progress](https://img.shields.io/badge/Gen3_First_Release-90%25-yellow)](https://valentinbreiz.github.io/nativeaot-patcher/roadmap.html)

## Why gen3?

Cosmos gen2 (the current public Cosmos OS) compiles C# IL to x86 assembly through **IL2CPU**, a custom transpiler. IL2CPU is powerful but maintains its own JIT-like backend separate from the .NET ecosystem. Gen3 replaces it with **NativeAOT**, the official .NET ahead-of-time toolchain, so kernels benefit from the same optimizer used in the wider .NET ecosystem and stay aligned with upstream as it evolves. This also makes it possible to support modern .NET features and additional architectures (currently ARM64 and RISC-V in the future) without re-implementing them in the toolchain.

## Features

- NativeAOT compilation
- x64 and ARM64
- Limine boot protocol
- Cosmos plug system
- Native runtime stubs
- .NET runtime support (String, Collections, List, Dictionary, Math, Console, DateTime, Random, BitOperations, threading, generics, reflection basics)
- Mark-and-sweep GC
- Priority-based stride scheduler
- Exception handling
- Interrupts (APIC on x64, GIC on ARM64)
- ACPI (via LAI)
- PCI and MMIO drivers
- UART serial
- UEFI GOP framebuffer graphics
- Keyboard and mouse input
- Network stack
- Timer / clock

## Documentation

- [Documentation site](https://valentinbreiz.github.io/nativeaot-patcher/index.html)
- [Installation Guide](docs/articles/install.md)
- [Dev Container Setup](docs/articles/install-dev.md)
- [Kernel Compilation Steps](docs/articles/build/kernel-compilation-steps.md)
- [Debugging with VS Code and QEMU](docs/articles/debugging.md)
- [Kernel Project Layout](docs/articles/kernel-project-layout.md)
- [Coding Guidelines](docs/articles/coding-guidelines.md)
- [Plugs](docs/articles/plugs.md)
- [Garbage Collector](docs/articles/garbage-collector.md)
- [Testing](docs/articles/testing.md)
- [Cosmos.Build.Asm](docs/articles/build/asm-build.md), [.CC](docs/articles/build/cc-build.md), [.Patcher](docs/articles/build/patcher-build.md), [.Ilc](docs/articles/build/ilc-build.md)

## Related resources

- [Cosmos Gen3: The NativeAOT Era and the End of IL2CPU?](https://valentin.bzh/posts/3)
- [NativeAOT Developer Workflow](https://github.com/dotnet/runtime/blob/main/docs/workflow/building/coreclr/nativeaot.md)
- [NativeAOT Limitations](https://github.com/dotnet/runtime/blob/main/src/coreclr/nativeaot/docs/limitations.md)
- [Limine Boot Protocol](https://github.com/limine-bootloader/limine)

## Contributors

Thanks to everyone who has contributed code, reviews, plugs, and bug reports:

- [@zarlo](https://github.com/zarlo)
- [@valentinbreiz](https://github.com/valentinbreiz)
- [@Guillermo-Santos](https://github.com/Guillermo-Santos)
- [@kumja1](https://github.com/kumja1)
- [@AzureianGH](https://github.com/AzureianGH)
- [@warquys](https://github.com/warquys)
- [@ascpixi](https://github.com/ascpixi)
- [@Demiomad](https://github.com/Demiomad)
- [@ilobilo](https://github.com/ilobilo)
- [@spectradevv](https://github.com/spectradevv)
- All [Cosmos gen2 contributors](https://github.com/CosmosOS/Cosmos/graphs/contributors)

See the live list on the [Contributors page](https://github.com/valentinbreiz/nativeaot-patcher/graphs/contributors).

## License

[MIT](LICENSE) — Copyright (c) 2024 Kaleb McGhie (zarlo) and contributors.
