# Installation Guide

## Prerequisites

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) or later
- [Visual Studio Code](https://code.visualstudio.com/)

## Windows

Download and run the latest installer from the [Releases](https://github.com/valentinbreiz/nativeaot-patcher/releases) page:

```
CosmosSetup-<version>-windows.exe
```

After installation, open a new terminal and verify:

```powershell
cosmos check
```

To uninstall, use **Add or Remove Programs** in Windows Settings.

## Linux / macOS

Install the Cosmos CLI and all dependencies:

```bash
dotnet tool install -g Cosmos.Tools
cosmos install
```

This will install all required tools via your package manager (`apt`, `dnf`, `pacman`, or `brew`), the Cosmos patcher, project templates, and the VS Code extension.

Verify installation:

```bash
cosmos check
```

To uninstall:

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
