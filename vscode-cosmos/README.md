# Cosmos OS Development for VS Code

VS Code extension for Cosmos OS kernel development with NativeAOT.

## Features

### Sidebar Panel
The extension adds a **Cosmos OS** icon to the Activity Bar (left sidebar). Click it to access:

**When no project is open:**
- Welcome screen with "Create Kernel Project" button
- Link to open an existing project folder

**When a Cosmos project is open:**
- **Project** view with build/run/debug actions for x64 and ARM64
- **Tools** view showing installed development tools status

### Commands
All commands available via Command Palette (`Ctrl+Shift+P`):

| Command | Description |
|---------|-------------|
| `Cosmos: Create Kernel Project` | Create a new Cosmos kernel with project wizard |
| `Cosmos: Check Tools` | Verify development tools are installed |
| `Cosmos: Install Tools` | Install missing development tools |
| `Cosmos: Build Kernel` | Build for x64 or ARM64 |
| `Cosmos: Run in QEMU` | Run kernel in QEMU emulator |
| `Cosmos: Debug in QEMU` | Start GDB debugging session |
| `Cosmos: Clean Build` | Remove build output directories |

## Getting Started

1. Install the extension
2. Click the Cosmos icon in the Activity Bar
3. Click "Create Kernel Project"
4. Follow the wizard to set up your kernel

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [cosmos-tools](https://www.nuget.org/packages/Cosmos.Tools) CLI
- QEMU (for running/debugging)
- GDB (for debugging)

The extension will prompt to install missing tools automatically.

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `cosmos.defaultArchitecture` | `x64` | Default target architecture |
| `cosmos.qemuMemory` | `512M` | QEMU memory allocation |

## Installation

### From VSIX
```bash
code --install-extension cosmos-vscode-1.0.0.vsix
```

### From Marketplace (coming soon)
Search for "Cosmos OS Development" in Extensions.

## License

MIT
