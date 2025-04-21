This document outlines the build process for the C# kernel, which utilizes .NET NativeAOT compilation combined with a custom patching mechanism ([Plugs](/articles/Plugs.html)) to produce a bootable ELF kernel file and a final bootable ISO image.

## Overview

The compilation process transforms C# source code into a native executable kernel (`Kernel.elf`) and packages it with the Limine bootloader into a bootable ISO image (`.iso`). It involves several stages orchestrated by MSBuild, leveraging custom build components like `Cosmos.Asm.Build`, `Cosmos.Patcher.Analyzer`, `Cosmos.Ilc.Build`, and `Cosmos.Common.Build`. These components provide MSBuild tasks and targets to manage assembly, C# compilation, static analysis, IL patching, NativeAOT compilation, linking, and ISO image creation.

![image](https://github.com/user-attachments/assets/d0cb98a5-9c61-48e4-8722-0a7dd151f86f)
> Visual by [Guillermo-Santos](https://github.com/Guillermo-Santos)

## Prerequisites

Ensure the following tools and SDKs are installed:

* **.NET SDK**: Version 9.0.104 or later (as specified in `global.json`), including the NativeAOT compilation tools.
* **YASM Assembler**: Required by `Cosmos.Asm.Build` for compiling `.asm` files.
* **LLD Linker (`ld.lld`)**: Part of the LLVM toolchain, required by `Cosmos.Common.Build` for linking object files into the final ELF executable.
* **xorriso**: Required by `Cosmos.Common.Build` for creating the final bootable ISO image.

## Compilation Stages

The build process, orchestrated by MSBuild using tasks and targets from the various `Cosmos.*.Build` components follows these main steps:

1.  **Assembly Compilation (`.asm` -> `.obj`) - via [Cosmos.Asm.Build](/articles/Cosmos.Asm.Build.html)**:
    * Handwritten assembly files (`.asm`) containing low-level x86 and ARM routines (e.g., implementations for functions marked `[RuntimeImport]` like `_native_io_*`) are identified.
    * The MSBuild task provided by `Cosmos.Asm.Build`, `YasmBuildTask` is executed.
    * `yasm` is invoked (with `-felf64`) for each `.asm` file.
    * Resulting object files (*.obj`) are generated.

2.  **C# Compilation & Static Analysis (`.cs` -> IL `.dll`) - via .NET SDK + `Cosmos.Patcher.Analyzer`**:
    * MSBuild invokes the Roslyn C# compiler.
    * Kernel source files (`KernelEntry.cs`, `Internal.cs`, `Stdlib.cs`, etc.) are compiled into a standard .NET IL assembly (`Kernel.dll`).
    * During compilation, the **`PatcherAnalyzer`** (from `Cosmos.Patcher.Analyzer`) runs, checking code against plug rules (`DiagnosticMessages.cs`) and reporting diagnostics.
    * The `PatcherCodeFixProvider` offers IDE assistance.

3.  **IL Patching (Mono.Cecil) - via [Cosmos.Patcher.Build](/articles/Liquip.Patcher.html)**:
    * MSBuild targets execute the `PatcherTask` (from `Cosmos.Patcher.Build.Tasks`).
    * `PatcherTask` runs the `Cosmos.Patcher` tool.
    * The tool uses **Mono.Cecil** to load the target IL assembly and plug assemblies.
    * It **modifies the IL** of target methods based on `[Plug]` attributes, replacing their bodies with the plug code.
    * The modified (patched) IL assembly (`Kernel_patched.dll`) is saved.

4.  **NativeAOT Compilation (ILC: Patched IL -> Native `.obj`) - via [Cosmos.Ilc.Build](/articles/Liquip.ilc.Build.html)**:
    * MSBuild targets (likely from **`Cosmos.Ilc.Build`**) invoke the .NET ILCompiler (ILC).
    * ILC processes the **patched IL assembly** (`Kernel_patched.dll`).
    * It performs tree-shaking and compiles the reachable IL into native object files (`.obj` or `.o`) for the target architecture.

5.  **Linking (`.obj` -> `.elf`) - via `Cosmos.Common.Build`**:
    * MSBuild targets provided by **`Cosmos.Common.Build`** invoke the LLVM linker (`ld.lld`).
    * It takes all native object files from ILC (C#) and YASM (`.asm`).
    * It links them using a **linker script** (essential for memory layout, entry point `kmain`, etc.).
    * It resolves external symbols (like `_native_io_*`).
    * The final kernel executable is produced: `Kernel.elf`.

6.  **ISO Image Creation (`.iso`) - via `Cosmos.Common.Build`**:
    * MSBuild targets provided by **`Cosmos.Common.Build`** execute this step.
    * `xorriso` assembles the necessary components into a bootable ISO 9660 image:
        * Limine bootloader files.
        * The bootloader configuration file (`limine.conf`).
        * The compiled kernel (`Kernel.elf` from step 5).
    * The final output is a bootable `.iso` file (e.g., `Kernel.iso`).

7.  **Deployment / Execution**:
    * The generated `.iso` file can be used with a virtual machine (like QEMU, VMware, VirtualBox) or written to a USB drive to boot on physical hardware.

## Build Automation

These steps are automated via MSBuild targets and custom tasks defined within the project's `.csproj` file and shared build components (`Cosmos.Asm.Build`, `Cosmos.Patcher.Analyzer`, `Cosmos.Ilc.Build`, `Cosmos.Common.Build`). This allows the entire kernel and bootable ISO to be built using standard `dotnet build` or `dotnet publish` commands with appropriate configurations. The goal is to make this process as nuget packages which can be integrated without needing to import .targets files from the `.csproj`.
