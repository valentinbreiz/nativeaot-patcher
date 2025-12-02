<context>
# Project Conventions

## GitHub Integration
**Tasks MUST be linked to GitHub issues.** This project uses GitHub as the source of truth for work items.

### Repositories
- Main repository: `valentinbreiz/nativeaot-patcher`
- Upstream Cosmos: `CosmosOS/Cosmos` (issue #3088 tracks Gen3)

### Workflow
1. **Before creating tasks**: Check existing GitHub issues using `gh issue list --repo valentinbreiz/nativeaot-patcher`
2. **Task titles**: Should reference GitHub issue numbers (e.g., "Implement feature X (fixes #42)")
3. **New work**: Create GitHub issue first with `gh issue create`, then create Task Master task linked to it
4. **Priority board**: https://github.com/users/valentinbreiz/projects/2/views/2

### Commands
```bash
# List open issues
gh issue list --repo valentinbreiz/nativeaot-patcher

# View specific issue
gh issue view <number> --repo valentinbreiz/nativeaot-patcher

# Create new issue
gh issue create --repo valentinbreiz/nativeaot-patcher --title "Title" --body "Description"

# API calls
gh api repos/valentinbreiz/nativeaot-patcher/issues
```

# Overview

**NativeAOT-Patcher** is a toolchain that ports the Cosmos OS plug system and assembly loading to .NET NativeAOT, enabling the development of operating system kernels in C# that compile to native code. This project represents **Cosmos Gen3** - a complete rewrite of the Cosmos OS development framework.

## Problem Statement

The current Cosmos OS (Gen2) suffers from critical limitations:
- **IL2CPU compiler issues**: No optimizations, odd bugs, poor debugging support, performance problems
- **Architecture lock-in**: Only supports x86 (32-bit), not x86-64 or ARM64
- **Lack of stability**: High amount of unintended behavior originating from Cosmos itself
- **Poor performance**: IL2CPU does not perform any kind of optimizations
- **Poor Linux support**: Debugging on non-Windows platforms is extremely difficult
- **Monolithic design**: No modularization - every kernel includes everything (network stack, etc.)
- **No defined ABI**: Difficult interoperability with native libraries

## Solution

Replace IL2CPU with .NET NativeAOT's ILC (IL Compiler) and implement a plug system that patches assemblies at build time. This enables:
- Native code compilation with optimizations
- Multi-architecture support (x86-64, ARM64)
- System V ABI compliance for Unix library interoperability
- GDB debugging support (breakpoints, backtracing, local variables)
- Modular NuGet-based architecture
- IDE-agnostic tooling

## Target Users

1. **Systems programming learners**: Developers wanting to learn OS development using C# instead of C
2. **Domain-specific kernel developers**: Experienced developers creating specialized kernels (embedded, IoT, research)
3. **Cosmos ecosystem users**: Existing Cosmos Gen2 users migrating to Gen3

## Current State

The project is functional with:
- Working x64 and ARM64 kernel builds producing bootable ISOs
- Complete build pipeline (Roslyn -> Patcher -> ILC -> GCC/ASM -> Linker -> ISO)
- Limine bootloader integration
- Memory management (heap, GC integration)
- UART/Serial output for debugging
- Hardware Abstraction Layer (HAL) for both architectures
- Interrupt handling (x64)
- Console output with graphics support
- Test framework for kernel testing
- CI/CD pipeline for Linux and Windows

# Core Features

## 1. Plug System
The plug system is the core innovation that enables C# kernel development by replacing .NET BCL methods with kernel-compatible implementations.

**What it does:**
- Replaces existing .NET methods, fields, or types with custom implementations
- Uses attributes (`[Plug]`, `[PlugMember]`, `[Expose]`, `[FieldAccess]`) to define replacements
- Patches IL at build time using Mono.Cecil before NativeAOT compilation

**Why it's important:**
- Allows using standard C# constructs (Console.WriteLine, exceptions) in bare-metal environment
- Enables gradual BCL support expansion without runtime modifications
- Provides clean separation between kernel code and plug implementations

## 2. Multi-Architecture Support
Support for multiple CPU architectures from a single codebase.

**What it does:**
- Compile-time architecture selection via MSBuild properties (`CosmosArch`)
- Conditional compilation using `ARCH_X64` and `ARCH_ARM64` defines
- Architecture-specific HAL implementations
- Separate native code packages per architecture

**Current status:**
- x64: Fully functional with interrupt handling, ACPI support
- ARM64: Functional with PL011 UART, memory management

## 3. Build Pipeline
Complete MSBuild integration for kernel compilation.

**Components:**
- `Cosmos.Sdk`: Custom MSBuild SDK for kernel projects
- `Cosmos.Build.Patcher`: IL patching MSBuild task
- `Cosmos.Build.Ilc`: NativeAOT ILC integration
- `Cosmos.Build.Asm`: Assembly file compilation (YASM)
- `Cosmos.Build.GCC`: C file compilation
- `Cosmos.Build.Common`: Linking and ISO creation

**Output:** Bootable ISO images with Limine bootloader

## 4. Kernel Core Components
Fundamental kernel functionality implemented in managed C#.

**Components:**
- `Cosmos.Kernel.Core`: Memory management, heap allocation, runtime internals
- `Cosmos.Kernel.Boot.Limine`: Bootloader protocol support
- `Cosmos.Kernel.Services`: Math, IO utilities, Base64
- `Cosmos.Kernel.Graphics`: Canvas rendering, PSF font support
- `Cosmos.Kernel.Plugs`: BCL plug implementations

## 5. Hardware Abstraction Layer (HAL)
Platform-independent hardware interface with architecture-specific implementations.

**Structure:**
- `Cosmos.Kernel.HAL.Interfaces`: Abstract interfaces
- `Cosmos.Kernel.HAL`: Bridge layer with compile-time selection
- `Cosmos.Kernel.HAL.X64`: x86-64 specific (ACPI, Port I/O, CPU ops)
- `Cosmos.Kernel.HAL.ARM64`: ARM64 specific

## 6. Native Code Integration
Assembly and C code for low-level operations.

**Components:**
- `Cosmos.Kernel.Native.X64`: x86-64 assembly (Port I/O, interrupts, SSE) and C (ACPI, LAI interpreter)
- `Cosmos.Kernel.Native.ARM64`: ARM64 assembly and C implementations

## 7. Testing Infrastructure
Kernel testing framework with QEMU integration.

**Components:**
- `Cosmos.TestRunner.Framework`: Base framework for kernel tests
- `Cosmos.TestRunner.Protocol`: IPC message definitions
- `Cosmos.TestRunner.Engine`: Host-side test orchestration with QEMU
- Test kernels: HelloWorld, Memory, TypeCasting

# User Experience

## Developer Workflow

1. **Project Creation**: Create new kernel project using `Cosmos.Sdk`
2. **Development**: Write C# kernel code with standard IDE support
3. **Build**: `dotnet publish` produces bootable ISO
4. **Test**: Run in QEMU with UART output capture
5. **Debug**: GDB integration via QEMU for breakpoints and inspection

## Build Commands

```bash
# x64 build
dotnet publish -c Debug -r linux-x64 -p:DefineConstants="ARCH_X64" ./examples/DevKernel/DevKernel.csproj -o ./output-x64

# ARM64 build
dotnet publish -c Debug -r linux-arm64 -p:DefineConstants="ARCH_ARM64" -p:CosmosArch=arm64 ./examples/DevKernel/DevKernel.csproj -o ./output-arm64
```

## Testing with QEMU

```bash
# x64
qemu-system-x86_64 -cdrom ./output-x64/DevKernel.iso -m 512M -serial file:uart.log

# ARM64 (requires UEFI firmware)
qemu-system-aarch64 -M virt -cpu cortex-a72 -m 512M -bios /usr/share/qemu-efi-aarch64/QEMU_EFI.fd -cdrom ./output-arm64/DevKernel.iso -serial file:uart.log
```
</context>
<PRD>
# Technical Architecture

## System Components

### Build System Layer
```
Kernel Project (.csproj with Cosmos.Sdk)
    |
    v
[Roslyn Compiler] --> IL Assembly
    |
    v
[Cosmos.Build.Analyzer.Patcher] --> Validates plug rules at compile time
    |
    v
[Cosmos.Build.Patcher / cosmos.patcher CLI]
    |-- Uses Mono.Cecil for IL manipulation
    |-- Applies [Plug], [PlugMember], [Expose], [FieldAccess] attributes
    |-- Outputs to $(IntermediateOutputPath)/cosmos/
    v
[Cosmos.Build.Ilc / ilc]
    |-- NativeAOT IL Compiler
    |-- Outputs native .o files to $(IntermediateOutputPath)/cosmos/native/
    v
[Cosmos.Build.Asm / yasm] --> Assembly .obj files
[Cosmos.Build.GCC / gcc] --> C .obj files
    |
    v
[ld.lld Linker] --> ELF binary
    |
    v
[xorriso + Limine] --> Bootable ISO
```

### Kernel Component Hierarchy
```
Cosmos.Kernel (Aggregator Package)
├── Cosmos.Kernel.Core
│   ├── Memory/ (Heap, allocation, GC integration)
│   ├── Runtime/ (Exception handling, method tables)
│   ├── IO/ (Serial, UART)
│   └── Cosmos.Kernel.Boot.Limine
├── Cosmos.Kernel.HAL
│   ├── Cosmos.Kernel.HAL.Interfaces
│   ├── Cosmos.Kernel.HAL.X64 (conditional)
│   └── Cosmos.Kernel.HAL.ARM64 (conditional)
├── Cosmos.Kernel.Plugs
├── Cosmos.Kernel.Services
├── Cosmos.Kernel.Native.X64 (conditional)
└── Cosmos.Kernel.Native.ARM64 (conditional)
```

### Package Dependencies
All components are packaged as NuGet packages (version 3.0.0):
- Build tools: MSBuild tasks in `tasks/` folder
- Content packages: Linker scripts, bootloader configs
- Platform-specific: Native assemblies (X64, ARM64)

## Data Models

### Plug Attributes (Cosmos.Build.API)
- `[Plug(typeof(TargetType))]`: Marks class as replacement for target type
- `[PlugMember]`: Replaces specific member on target type
- `[Expose]`: Adds new private members to target type
- `[FieldAccess(Name = "fieldName")]`: Access private fields from plug method

### Build Configuration Properties
- `CosmosArch`: Architecture selection (x64, arm64)
- `EnablePatching`: Enable/disable patcher (default: true)
- `PlugReference`: Names of plug assemblies to include

## APIs and Integrations

### External Dependencies
- **Mono.Cecil**: IL manipulation library for patcher
- **MonoMod.Utils**: Additional Cecil utilities
- **.NET NativeAOT ILC**: Microsoft.DotNet.ILCompiler runtime pack
- **Limine**: Bootloader protocol and binaries
- **LAI**: ACPI interpreter library (embedded in Native.X64)

### Toolchain Requirements
| Tool | Purpose | Platform |
|------|---------|----------|
| .NET SDK 10.0+ | Core compilation | All |
| YASM | x64 assembly compilation | All |
| GCC/x86_64-elf-gcc | C compilation | Windows needs cross-compiler |
| gcc-aarch64-linux-gnu | ARM64 cross-compilation | Linux |
| ld.lld | ELF linking | All |
| xorriso | ISO creation | All |

## Infrastructure Requirements

### Development Environment
- .NET 10.0 SDK
- Git with submodule support (dotnet/runtime submodule required)
- QEMU for testing (qemu-system-x86_64, qemu-system-aarch64)
- UEFI firmware for ARM64 testing

### CI/CD (GitHub Actions)
- Patcher tests, Scanner tests, Analyzer tests, ASM tests
- ISO build tests for Linux (x64, ARM64) and Windows (x64, ARM64)
- Artifact upload for ISO images

# Development Roadmap

## Phase 1: Core Stability (Current Focus)
**Goal:** Ensure reliable foundation for kernel development

### 1.1 Build System Hardening
- Fix any remaining cross-platform build issues
- Improve error messages from patcher and build tools
- Add build validation for common mistakes
- Document all MSBuild properties and their effects

### 1.2 Memory Management Improvements
- Implement proper garbage collection integration
- Add memory debugging utilities
- Implement memory protection (if hardware supports)
- Optimize heap allocation performance

### 1.3 Exception Handling
- Complete exception handling support
- Stack unwinding implementation
- Exception message formatting
- Kernel panic handling

## Phase 2: BCL Expansion
**Goal:** Increase the surface area of supported .NET APIs

### 2.1 Core Types
- String manipulation (more methods)
- Collections (List, Dictionary basics)
- LINQ basics (Where, Select, FirstOrDefault)
- Math operations (complete System.Math)

### 2.2 I/O Abstractions
- Stream base class support
- TextReader/TextWriter
- BinaryReader/BinaryWriter
- Memory streams

### 2.3 Threading Primitives
- Thread-safe collections basics
- Interlocked operations
- SpinLock implementation
- Basic synchronization primitives

## Phase 3: Hardware Support Expansion
**Goal:** Broader hardware support for real-world use

### 3.1 x64 Hardware
- Complete ACPI support (power management, device enumeration)
- PCI/PCIe enumeration
- Basic storage drivers (AHCI/NVMe stubs)
- USB host controller interface

### 3.2 ARM64 Hardware
- GIC (Generic Interrupt Controller) support
- ARM timer support
- Device tree parsing
- Virtio device support

### 3.3 Common Drivers
- Block device abstraction
- Network device abstraction
- Framebuffer/display abstraction

## Phase 4: Developer Experience
**Goal:** Make kernel development accessible and productive

### 4.1 Debugging Improvements
- Enhanced GDB integration
- DWARF debug info optimization
- Source-level debugging improvements
- Kernel debug logging framework

### 4.2 Documentation
- API documentation generation
- Tutorial series for kernel development
- Architecture guides
- Migration guide from Cosmos Gen2

### 4.3 Templates and Tooling
- `dotnet new` template for kernel projects
- VS Code extension for debugging
- Performance profiling tools

## Phase 5: Advanced Features
**Goal:** Enable production-quality kernel development

### 5.1 Kernel Module System
- Dynamic module loading
- Module dependency resolution
- Kernel module API definition

### 5.2 Filesystem Abstraction
- VFS (Virtual File System) layer
- FAT32 implementation (optional package)
- Ext2/4 implementation (optional package)

### 5.3 Networking (Optional Package)
- Network stack architecture
- TCP/IP implementation
- Socket API

### 5.4 Process/Thread Management (Optional Package)
- Process abstraction
- Scheduler interface
- User-space support

# Logical Dependency Chain

## Foundation Layer (Must be stable first)
1. **Build System** - Everything depends on reliable builds
   - Cosmos.Patcher must correctly apply plugs
   - ILC integration must produce valid native code
   - Linker must produce bootable binaries

2. **Memory Management** - All code needs memory
   - Heap allocation working
   - GC integration (or manual management)
   - Stack management

3. **Exception Handling** - Error recovery
   - Try/catch must work
   - Stack traces useful for debugging

## Core Services Layer
4. **Serial/UART Output** - Primary debugging tool
   - Must work before any complex features
   - Architecture-specific but simple interface

5. **Console Output** - Developer feedback
   - Depends on graphics or serial backend
   - String formatting

6. **HAL Basics** - Hardware access
   - CPU operations (interrupts enable/disable)
   - Port I/O (x64) or MMIO (ARM64)

## Hardware Layer
7. **Interrupt Handling** - Event-driven kernel
   - Depends on HAL basics
   - Architecture-specific IDT/GIC setup

8. **Timer** - Scheduling, delays
   - Depends on interrupts
   - PIT/APIC timer (x64) or ARM timer

9. **ACPI/Device Enumeration** - Hardware discovery
   - Depends on memory, interrupts
   - Complex but enables driver loading

## Application Layer
10. **BCL Expansion** - Developer productivity
    - Can proceed in parallel with hardware
    - Each type independent

11. **Filesystem** - Persistence
    - Depends on block device drivers
    - Optional for many kernels

12. **Networking** - Connectivity
    - Depends on device drivers
    - Optional package

## Quick Wins (Can be done early for visible progress)
- Improved console output formatting
- More diagnostic messages during boot
- Better error messages from build system
- Additional plug implementations for common types

# Risks and Mitigations

## Technical Challenges

### Risk: NativeAOT Limitations
.NET NativeAOT has documented limitations that may impact kernel development.

**Known limitations:**
- No runtime code generation (Reflection.Emit)
- Limited reflection support
- No C++/CLI support
- Assembly.LoadFile not supported

**Mitigation:**
- Document unsupported patterns
- Provide plug-based alternatives
- Use source generators where possible

### Risk: Architecture-Specific Bugs
Different architectures may have subtle differences in behavior.

**Mitigation:**
- Comprehensive test suite per architecture
- CI/CD testing on both x64 and ARM64
- Clear separation of architecture-specific code

### Risk: Patcher Complexity
The IL patcher is complex and may have edge cases.

**Mitigation:**
- Roslyn analyzer for compile-time validation
- Extensive unit tests for patcher
- Clear error messages for patcher failures

## MVP Definition

The **Minimum Viable Product** is a kernel that can:
1. Boot on x64 and ARM64 (via QEMU)
2. Output text to console/serial
3. Handle basic exceptions
4. Allocate memory
5. Run a simple "Hello World" program

**Current status:** MVP is achieved. Focus is now on stability and expansion.

## Resource Constraints

### Risk: Limited Contributors
OS development is niche; finding contributors is difficult.

**Mitigation:**
- Excellent documentation
- Simple contribution process
- Clear task breakdown (Task Master integration)
- Focus on developer experience

### Risk: Dependency on .NET Runtime Changes
NativeAOT evolves with .NET releases.

**Mitigation:**
- Track .NET preview releases
- Maintain compatibility with multiple .NET versions if needed
- dotnet/runtime submodule for specific version pinning

# Appendix

## Research References

### Official Documentation
- [NativeAOT Developer Workflow](https://github.com/dotnet/runtime/blob/main/docs/workflow/building/coreclr/nativeaot.md)
- [NativeAOT Limitations](https://github.com/dotnet/runtime/blob/main/src/coreclr/nativeaot/docs/limitations.md)
- [ILC Architecture](https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/ilc-architecture.md)
- [Cosmos OS Plugs Documentation](https://cosmosos.github.io/articles/Kernel/Plugs.html)

### Project References
- [Cosmos OS Gen3 Issue](https://github.com/CosmosOS/Cosmos/issues/3088)
- [Cosmos Gen3 Blog Post](https://valentin.bzh/posts/3)
- [Zarlo's Original NativeAOT Patcher](https://gitlab.com/liquip/nativeaot-patcher)

## Technical Specifications

### Supported Configurations
| Platform | Architecture | Build Host | Status |
|----------|--------------|------------|--------|
| Linux | x64 | Linux | Passing |
| Linux | ARM64 | Linux | Passing |
| Windows | x64 | Windows | Passing |
| Windows | ARM64 | Windows | Failing |

### Output Artifacts
| Stage | Output Path | Description |
|-------|-------------|-------------|
| Patching | `$(IntermediateOutputPath)/cosmos/` | Main patched assembly |
| Patching | `$(IntermediateOutputPath)/cosmos/ref/` | Reference assemblies |
| NativeAOT | `$(IntermediateOutputPath)/cosmos/native/` | Native object files |
| Assembly | `$(IntermediateOutputPath)/cosmos/asm/` | YASM objects |
| C Code | `$(IntermediateOutputPath)/cosmos/cobj/` | GCC objects |
| Linking | `$(OutputPath)/$(AssemblyName).elf` | Linked ELF kernel |
| ISO | `$(PublishDir)/$(AssemblyName).iso` | Bootable ISO |

### Version Information
- Project Version: 3.0.0
- .NET SDK: 10.0.x
- Target Framework: net10.0

## Project Structure Summary

```
nativeaot-patcher/
├── src/                          # 27 components
│   ├── Cosmos.Patcher/           # CLI patcher tool
│   ├── Cosmos.Build.*/           # Build system (6 packages)
│   ├── Cosmos.Kernel.*/          # Kernel components (8 packages)
│   ├── Cosmos.Kernel.HAL.*/      # HAL implementations (4 packages)
│   └── Cosmos.Kernel.Native.*/   # Native code (2 packages)
├── examples/
│   └── DevKernel/                # Reference kernel implementation
├── tests/                        # 10+ test projects
│   ├── Cosmos.TestRunner.*/      # Test infrastructure
│   ├── Cosmos.Tests.*/           # Unit tests
│   └── Kernels/                  # Test kernels
├── docs/                         # Documentation
├── dotnet/runtime/               # .NET runtime submodule
└── .github/workflows/            # CI/CD
```
</PRD>
