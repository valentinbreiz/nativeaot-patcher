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

## Updating

Update everything in one step — the CLI, the patcher, the build tools, the project templates, and the VS Code extension:

```bash
cosmos update
```

Run it inside a kernel project directory to also move the project's Cosmos version pins (the `Sdk="Cosmos.Sdk/..."` attribute and `Cosmos.*` package references) to the latest release. Additional options:

| Option | Effect |
|--------|--------|
| `cosmos update --check` | Report available updates without installing anything |
| `cosmos update --no-project` | Update the tools but leave project files untouched |
| `cosmos update --version <VERSION>` | Move the CLI, patcher, templates, and project pins to a specific version (system tools always follow the `tools-latest` bundles) |

Projects pinned to a version newer than the latest release (for example a locally built date-stamped version) are left untouched unless `--version` is passed explicitly.

This works on Windows too — existing installations update in place without re-running the installer. `cosmos build`, `cosmos run`, and `cosmos check` also print a one-line notice when a newer release exists (checked at most once per day; set `COSMOS_NO_UPDATE_NOTIFIER=1` to disable).

## Quick Start

Once installed, see [Kernel Startup](startup.md) to create your first kernel and learn the boot flow.
