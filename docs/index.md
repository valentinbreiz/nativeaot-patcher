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


xref link [xrefmap.yml](xrefmap.yml)
