# Installation Guide

## Prerequisites

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) or later
- [Visual Studio Code](https://code.visualstudio.com/)

## Windows

Download and run the latest installer from the [Releases](https://github.com/valentinbreiz/nativeaot-patcher/releases) page:

```
CosmosSetup-<version>-windows.exe
```

The installer will:
- Install all required build tools (cross-compilers, linker, assembler, xorriso, QEMU)
- Register NuGet packages
- Install the Cosmos CLI (`cosmos`) and patcher
- Install project templates
- Install the VS Code extension
- Add tools to your PATH

After installation, open a new terminal and verify:

```powershell
cosmos check
```

To uninstall, use **Add or Remove Programs** in Windows Settings.

## Linux / macOS

### 1. Install Cosmos CLI

```bash
dotnet tool install -g Cosmos.Tools
```

### 2. Install tools and dependencies

```bash
cosmos install
```

This will install all required tools via your package manager (`apt`, `dnf`, `pacman`, or `brew`), the Cosmos patcher, project templates, and the VS Code extension.

### 3. Verify installation

```bash
cosmos check
```

### Uninstall

```bash
cosmos uninstall
dotnet tool uninstall -g Cosmos.Tools
```

## Quick Start

Once installed, create and build your first kernel:

```bash
cosmos new MyKernel
cd MyKernel
cosmos build
```

Or open VS Code and use the Cosmos extension to create, build, and run bare-metal C# kernels directly from the editor.
