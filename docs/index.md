---
_layout: landing
---

Welcome to the Cosmos gen3 wiki! 

Check out the [Roadmap](roadmap.md) to see our progress toward the first release 🚀.

The documentation is split in two parts, depending on what you want to do with gen3:

- **[User Guide](articles/user/install.md)** — you want to **build your own OS** with Cosmos: installing the toolchain, using the filesystem, debugging your kernel.
- **[Developer Docs](articles/dev/install-dev.md)** — you want to **contribute to Cosmos itself** or understand its internals: architecture, build pipeline, runtime subsystems.

## User Guide

Everything you need to create, build and run your own Cosmos kernel:

 - [Installation Guide](articles/user/install.md) — set up the toolchain and create your first kernel from VS Code.
 - [File System](articles/user/filesystem.md) — mount a disk and use the standard .NET `System.IO` API (`File`, `Directory`, streams).
 - [Network](articles/user/network.md) — DHCP, UDP and TCP through the standard .NET `System.Net.Sockets` API, plus DNS.
 - [Graphics](articles/user/graphics.md) — draw shapes, text and images on the screen with the Canvas API.
 - [Debugging with VSCode and QEMU](articles/user/debugging.md) — set breakpoints in your kernel with remote GDB.

## Developer Docs

Architecture and internals, for contributors and the curious:

 - [Dev Container Setup](articles/dev/install-dev.md) — build the framework from source.
 - [Kernel Project Layout](articles/dev/kernel-project-layout.md) — the layered project graph.
 - [Coding Guidelines](articles/dev/coding-guidelines.md) — style and architecture patterns.
 - [Plugs](articles/dev/plugs.md) — the IL-level method replacement system.
 - [Testing](articles/dev/testing.md) — unit tests and QEMU kernel test suites.
 - [Garbage Collector](articles/dev/garbage-collector.md) — the mark-and-sweep GC.
 - [Garbage Collector — Precise Stack Scan (GCInfo)](articles/dev/garbage-collector-gcinfo.md)
 - [Scheduler](articles/dev/scheduler.md) — the preemptive, pluggable scheduler.
 - [Kernel Compilation Steps](articles/dev/build/kernel-compilation-steps.md) — C# to bootable ISO, end to end.
 - [Cosmos.Build.Asm](articles/dev/build/asm-build.md), [Cosmos.Build.GCC](articles/dev/build/gcc-build.md), [Cosmos.Build.Patcher](articles/dev/build/patcher-build.md), [Cosmos.Build.Ilc](articles/dev/build/ilc-build.md) — the build pipeline components.

## Resources
- [Cosmos Gen3: The NativeAOT Era and the End of IL2CPU?](https://valentin.bzh/posts/3)
- [NativeAOT Developer Workflow](https://github.com/dotnet/runtime/blob/main/docs/workflow/building/coreclr/nativeaot.md)
- [NativeAOT Limitations](https://github.com/dotnet/runtime/blob/main/src/coreclr/nativeaot/docs/limitations.md)


xref link [xrefmap.yml](xrefmap.yml)
