# NativeAOT Patcher Repository - Comprehensive Analysis

## 1. OVERALL PROJECT STRUCTURE

### Directory Tree
```
nativeaot-patcher/
├── .devcontainer/           # Dev environment configuration
├── .github/                 # CI/CD workflows and automation
├── src/                     # Main source projects (24 projects, ~12,895 LOC)
├── examples/                # Example kernel projects
├── tests/                   # Test projects (6 projects, ~903 LOC)
├── docs/                    # Documentation
├── dotnet/                  # Git submodules
├── .taskmaster/             # Task management
├── CLAUDE.md               # Project instructions
├── Directory.Build.props    # MSBuild global properties
├── Directory.Build.targets  # MSBuild global targets
├── Directory.Packages.props # Central package version management
├── Packages.slnx           # Package solution
└── nativeaot-patcher.slnx  # Main solution
```

### Purpose
The NativeAOT patcher is a build system and runtime framework that enables the Cosmos OS plug system to work with .NET NativeAOT. It ports Cosmos' IL-based plug architecture (historically used with IL2CPU) to work with modern .NET NativeAOT compilation.

## 2. CORE COMPONENTS

### Build System Projects (src/)

#### Core Framework
- **Cosmos.Build.API** (256 LOC)
  - Defines plug-related attributes: `[Plug]`, `[PlugMember]`, `[Expose]`, `[FieldAccess]`
  - Label maker utility for generated symbols
  - Foundational types for the entire system

- **Cosmos.Build.Common** (netstandard2.0)
  - Architecture picker (x64/ARM64)
  - Common MSBuild props and targets
  - Linker script management
  - Windows/Unix-specific build targets

#### IL Patching
- **Cosmos.Patcher** (Executable, ~89KB PlugPatcher.cs)
  - Main CLI tool: `cosmos.patcher`
  - Rewrites IL based on plug definitions
  - Components:
    - `PlugScanner.cs` - Discovers plugs in assemblies
    - `PlugPatcher.cs` - Core patching logic (89KB)
    - `MonoCecilExtension.cs` - Mono.Cecil extensions
    - Uses Mono.Cecil for IL manipulation

- **Cosmos.Build.Patcher** (MSBuild integration)
  - Wires patcher into MSBuild pipeline
  - Tasks: SetupPatcher, RunPatcher, CleanPatcher, FindPluggedAssembliesTask, PatcherTask
  - Handles platform-specific patcher invocation (Windows/Unix)

- **Cosmos.Build.Analyzer.Patcher** (Roslyn analyzer)
  - Code analyzer for plug validation
  - Detects plug errors during C# compilation
  - CodeFixes and analyzer packages

#### Native Compilation
- **Cosmos.Build.Ilc** (ILC/NativeAOT integration)
  - Integrates Microsoft's ILCompiler (NativeAOT)
  - Tasks: ResolveIlcPath, WriteIlcRsp, CompileWithIlc
  - Generates response files for ilc compiler

- **Cosmos.Build.Asm** (Assembly compilation)
  - YASM compiler integration
  - Platform-specific assembly build targets (Unix/Windows)
  - Transforms .asm files to object files

- **Cosmos.Build.GCC** (C code compilation)
  - GCC cross-compiler integration
  - Compiles C source files for kernel
  - Platform-specific targets

#### SDK and Templates
- **Cosmos.Sdk** (NuGet SDK)
  - MSBuild SDK for kernel projects
  - Imports all build tools
  - Orchestrates entire compilation pipeline

- **Cosmos.Build.Templates** (Linker scripts and templates)
  - Linker script (.ld) templates
  - Build configuration templates

### Kernel Projects (src/)

#### Core Kernel
- **Cosmos.Kernel** (Aggregator, architecture-aware)
  - Brings together boot, core, HAL, plugs, and services
  - Architecture-conditional references (x64/ARM64)
  - Enforces dependency rules via build targets

- **Cosmos.Kernel.Core** (2142 LOC)
  - Runtime fundamentals
  - Memory management
  - IO operations
  - Extension utilities
  - Runtime services

- **Cosmos.Kernel.Boot.Limine** (Bootloader integration)
  - Limine bootloader support
  - Framebuffer structures
  - Memory map handling

#### Hardware Abstraction Layer (HAL)
- **Cosmos.Kernel.HAL** (Abstract interfaces)
  - CPU abstraction
  - PCI abstraction
  - Platform-independent HAL

- **Cosmos.Kernel.HAL.Interfaces** (Interface definitions)

- **Cosmos.Kernel.HAL.X64** (x86-64 implementation)
  - x86-64 specific CPU handling
  - Port IO

- **Cosmos.Kernel.HAL.ARM64** (ARM64 implementation)
  - ARM64 specific HAL

#### Native Code
- **Cosmos.Kernel.Native.X64** (Complex architecture-specific)
  - Assembly files (.asm): CPU ops, interrupts, SSE, debug
  - C code: ACPI support (LAI submodule), interrupts
  - ACPI support with LAI (ACPI interpreter)
  - Debug stubs

- **Cosmos.Kernel.Native.ARM64** (ARM64 native code)
  - ARM64-specific assembly and C implementations

#### Plugs and Services
- **Cosmos.Kernel.Plugs** (IL patches for .NET framework)
  - AppContextPlug
  - ConsolePlug  
  - Threading/MonitorPlug
  - Internal runtime helpers

- **Cosmos.Kernel.Services** (Kernel services)

- **Cosmos.Kernel.Debug** (Debug functionality)

- **Cosmos.Kernel.Graphics** (Graphics subsystem)

### Test Projects

1. **Cosmos.Tests.Patcher** (XUnit)
   - Tests plug patcher functionality
   - PlugPatcherTest_ObjectPlugs.cs
   - PlugPatcherTest_StaticPlugs.cs
   - PlugPatcherTest_SkipUnpluggedAssembly.cs

2. **Cosmos.Tests.Scanner**
   - Tests plug discovery logic

3. **Cosmos.Tests.Build.Analyzer.Patcher**
   - Tests Roslyn analyzer for plugs

4. **Cosmos.Tests.Build.Asm**
   - Tests assembly compilation

5. **Cosmos.Tests.NativeLibrary** & **Cosmos.Tests.NativeWrapper**
   - Tests native interop

## 3. BUILD SYSTEM ARCHITECTURE

### Build Pipeline Flow
```
C# Source Code
    ↓
Roslyn Compiler → IL Assembly
    ↓
[Cosmos.Build.Analyzer.Patcher validates plugs]
    ↓
Cosmos.Build.Patcher
    ├── SetupPatcher (collects assemblies)
    ├── FindPluggedAssembliesTask (identifies targets)
    └── PatcherTask (runs cosmos.patcher CLI)
    ↓
Patched Assemblies
    ↓
Cosmos.Build.Ilc (NativeAOT)
    ├── ResolveIlcPath
    ├── WriteIlcRsp
    └── CompileWithIlc
    ↓
Native Object Files (.o)
    ↓
Cosmos.Build.Asm (YASM) + Cosmos.Build.GCC (GCC)
    ├── Compile assembly files (.asm → .obj)
    └── Compile C code (.c → .o)
    ↓
All Object Files
    ↓
Cosmos.Build.Common (Linker)
    ├── Link with ld.lld
    └── Create ELF binary
    ↓
BuildISO Target
    ├── Use xorriso
    ├── Use Limine bootloader
    └── Generate ISO
    ↓
Bootable ISO Image
```

### Key Build Files and Targets
- `Directory.Build.props` - Global version (3.0.0) and settings
- `Cosmos.Build.Common.props` - Runtime identifier defaults
- `Cosmos.Sdk/Sdk.props` - SDK entry point
- Platform-specific targets: `Common.Build.Windows.targets`, `Common.Build.Unix.targets`
- Patcher targets: `Cosmos.Build.Patcher.props`, `Patcher.Build.Windows.targets`, `Patcher.Build.Unix.targets`

### Architecture Support
- **x64/x86-64**: Full support (YASM assembler, x86_64-elf-gcc)
- **ARM64**: Full support (gcc-aarch64-linux-gnu)
- Defined via `DefineConstants`: `ARCH_X64`, `ARCH_ARM64`
- Conditional project references based on architecture

## 4. CONFIGURATION & VERSION MANAGEMENT

### Central Package Management
**Directory.Packages.props** specifies versions for:
- Build components: Cosmos.Build.* (3.0.0)
- Native packages: Cosmos.Kernel.Native.* (3.0.0)
- Code analysis: Microsoft.CodeAnalysis 4.14.0
- Testing: xunit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1
- IL manipulation: Mono.Cecil 0.11.6
- Build utilities: Microsoft.Build.Utilities.Core 17.14.8

### .NET Version
- Target Framework: **net9.0** (with support for netstandard2.0 for build packages)
- C# Language: Latest (enable latest features)
- Implicit usings: Enabled

### Code Features
- Unsafe blocks: Enabled (for kernel code)
- Nullable: Enabled (strict null checking)
- Invariant globalization: Enabled (kernel requirement)
- Self-contained: True
- Trimmed: True (for NativeAOT)

## 5. DOCUMENTATION

### Comprehensive Docs Structure
```
docs/
├── index.md - Landing page with links
├── api/ - API documentation
└── articles/
    ├── debugging.md - VSCode/QEMU debugging guide
    ├── plugs.md - Plug system guide with templates
    ├── testing.md - Testing documentation
    ├── kernel-project-layout.md - Kernel architecture diagram
    └── build/
        ├── kernel-compilation-steps.md - Complete build pipeline (detailed)
        ├── patcher-build.md - Patcher architecture with flow diagram
        ├── ilc-build.md - NativeAOT integration details
        ├── asm-build.md - Assembly compilation
        └── gcc-build.md - C compilation
```

### Key Documentation
1. **kernel-compilation-steps.md** - Full pipeline with mermaid flowchart
2. **patcher-build.md** - Detailed plug patching flow
3. **kernel-project-layout.md** - Dependency graph for kernel projects
4. **plugs.md** - Complete plug system guide with examples

## 6. CI/CD AUTOMATION

### GitHub Actions Workflows

#### `.github/workflows/dotnet.yml` (Main test pipeline)
- **patcher-tests**: Tests Cosmos.Tests.Patcher (Ubuntu, .NET 9.0.x)
- **scanner-tests**: Tests Cosmos.Tests.Scanner
- **analyzer-tests**: Tests Cosmos.Tests.Build.Analyzer.Patcher
- **asm-tests**: Tests Cosmos.Tests.Build.Asm (requires yasm)
- **unix-iso-tests**: Full ISO build tests (Ubuntu, x64/arm64)
  - Installs cross-compilation tools
  - Builds DevKernel.iso
  - Uploads artifacts
- **windows-iso-tests**: Full ISO build tests (Windows, x64/arm64)
  - Installs LLVM, YASM via Chocolatey
  - Special toolchain for Windows

#### `.github/workflows/package.yml` (Packaging)
- Builds and packs all projects
- Creates NuGet packages
- Version: 3.0.0-build.YYYYMMDDHHMM
- Uploads to artifacts

#### `.github/workflows/format.yml`
- Code formatting checks

#### `.github/dependabot.yml`
- Automated dependency updates

### Build Status
- Linux x64: PASSING
- Linux ARM64: PASSING  
- Windows x64: PASSING
- Windows ARM64: FAILING (known issue)

## 7. DEPENDENCIES

### Direct Dependencies
- **Mono.Cecil** (0.11.6) - IL manipulation library
- **MonoMod.Utils** (25.0.8) - Reflection utilities
- **Microsoft.CodeAnalysis** (4.14.0) - Roslyn compiler APIs
- **Microsoft.Build.Utilities.Core** (17.14.8) - MSBuild integration
- **Spectre.Console.Cli** (0.51.1) - CLI framework
- **PolySharp** (1.15.0) - Polymorph source generator
- **log4net** (3.0.3) - Logging
- **Moq** (4.20.72) - Mocking framework
- **xunit** (2.9.3) - Testing framework

### External Tools (Required)
- **.NET SDK 9.0+**
- **YASM** (assembly compiler, x64)
- **gcc-aarch64-linux-gnu** (ARM64 cross-compiler)
- **ld.lld** (LLVM linker)
- **xorriso** (ISO creator)
- **Limine** (bootloader)

### Git Submodules
```
dotnet/runtime → https://github.com/dotnet/runtime (release/9.0)
src/Cosmos.Kernel.Native.X64/lai → https://github.com/managarm/lai.git
```

## 8. TESTING STRATEGY

### Test Projects
- **Unit tests**: PlugPatcher tests, Analyzer tests
- **Integration tests**: Build tests for Asm compilation
- **System tests**: Full ISO builds on Linux and Windows
- **Coverage**: XUnit with coverlet collector

### Test Framework
- **XUnit** with code coverage
- **Moq** for mocking
- **Roslyn CodeFix testing** for analyzer

### CI Test Matrix
- OS: Ubuntu-latest, Windows-latest
- .NET: 9.0.x
- Architecture: x64, arm64 (where supported)
- Configuration: Debug (for tests), Release (for packages)

## 9. CODE PATTERNS & ARCHITECTURE

### Architectural Patterns

1. **Plug System**
   - Type-level: `[Plug(typeof(TargetType))]` - Replace entire type
   - Member-level: `[PlugMember]` - Replace specific members
   - Field exposure: `[Expose]` - Add private fields to target
   - Field access mapping: `[FieldAccess(Name="fieldName")]` - Access private fields

2. **MSBuild Pipeline Integration**
   - Props-based configuration
   - Targets-based tasks
   - Platform-specific builds (Windows/Unix)
   - Artifact output organization

3. **Conditional Compilation**
   - Architecture detection via `DefineConstants`
   - Conditional project references
   - Platform-specific HAL implementations

4. **Project Layering** (Kernel)
   - Boot layer (Limine-specific)
   - Core layer (Runtime fundamentals)
   - HAL layer (Hardware abstraction)
   - Service layer (High-level services)
   - Plug layer (IL replacements)

### Design Principles
- **Separation of Concerns**: Build, patcher, kernel, HAL clearly separated
- **Architecture Flexibility**: x64 and ARM64 support via conditional logic
- **IL-First Approach**: Plugs operate on IL, not source code
- **Tool Composition**: Multiple specialized tools (patcher, ilc, asm, gcc) composed via MSBuild

## 10. EXAMPLES & TESTING

### DevKernel Example Project
- **Path**: `examples/DevKernel/`
- **Purpose**: Reference implementation for kernel projects
- **Components**:
  - C code in `src/C/` for low-level operations
  - Limine bootloader configuration
  - Architecture-specific compiler flags
  - GCC compiler flags: `-O2 -fno-stack-protector -nostdinc -ffreestanding`

- **Architecture Support**:
  ```csproj
  <!-- X64 flags -->
  -m64 -mcmodel=kernel -fno-PIC
  
  <!-- ARM64 flags -->
  (ARM64-specific)
  ```

### Submodule Initialization
```bash
git submodule update --init --recursive
```

### Build Command
```bash
dotnet publish -c Debug -r linux-x64 -p:DefineConstants="ARCH_X64" \
  ./examples/DevKernel/DevKernel.csproj -o ./output-x64
```

### Testing with QEMU
```bash
qemu-system-x86_64 -cdrom ./output-x64/DevKernel.iso -m 512M -serial file:uart.log
```

## 11. PACKAGE MANAGEMENT

### NuGet Packages Generated
All projects generate NuGet packages (3.0.0 version):
- `Cosmos.Build.API`
- `Cosmos.Build.Common`
- `Cosmos.Build.Asm`
- `Cosmos.Build.GCC`
- `Cosmos.Build.Ilc`
- `Cosmos.Build.Patcher`
- `Cosmos.Patcher` (as tool)
- `Cosmos.Sdk` (as SDK)
- `Cosmos.Kernel` (aggregator)
- `Cosmos.Kernel.Native.X64`
- `Cosmos.Kernel.Native.ARM64`

### Package Locations
- Local: `artifacts/package/release/`
- Published: NuGet.org

## 12. DEVELOPMENT WORKFLOW

### Setup (from CLAUDE.md)
```bash
# Only if build system changed:
./.devcontainer/postCreateCommand.sh [x64|arm64]

# Initialize submodules:
git submodule update --init --recursive

# Build kernel:
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet publish -c Debug -r linux-x64 -p:DefineConstants="ARCH_X64" \
  ./examples/DevKernel/DevKernel.csproj -o ./output-x64

# Test with QEMU:
qemu-system-x86_64 -cdrom ./output-x64/DevKernel.iso -m 512M
```

### postCreateCommand.sh
- Clears NuGet cache
- Builds and packs projects in dependency order
- Installs global tools (ilc, Cosmos.Patcher)
- Supports x64 and arm64 architectures

## STATISTICS SUMMARY

| Metric | Value |
|--------|-------|
| Source Projects | 24 |
| Test Projects | 6 |
| Source Files (.cs) | 113 |
| Source Lines of Code | ~12,895 |
| Test Code Lines | ~903 |
| Build Configuration Files | 20+ |
| Documentation Pages | 8+ |
| Target Framework | .NET 9.0 |
| Version | 3.0.0 |
| Architectures Supported | x64, ARM64 |
| CI/CD Workflows | 4 |

## KEY INSIGHTS

1. **Modern NativeAOT-first Design**: Unlike IL2CPU, this uses .NET's built-in NativeAOT compiler
2. **Clean Separation**: Build tools, patcher, kernel, and HAL are independent projects
3. **Multi-architecture**: x64 and ARM64 support from the ground up
4. **Plug System**: IL-based replacement system allows framework overrides without recompilation
5. **Comprehensive Tooling**: Integrated YASM, GCC, ILC, and linker management via MSBuild
6. **Well-Documented**: Excellent documentation with flowcharts and examples
7. **Automated Testing**: CI/CD covers unit, integration, and full system tests
8. **Active Development**: Recent commits show ongoing enhancement (MCP integration, architecture support improvements)

