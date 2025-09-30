# NativeAOT-Patcher Quick Reference

## Project Overview
NativeAOT patcher for Cosmos OS - ports the Cosmos plug system and assembly loading to NativeAOT.

## Complete Test Workflow

To test the entire workflow from setup to kernel execution:

### 1. Setup Framework (Only if Build System Changed)

**IMPORTANT:** Only run `postCreateCommand.sh` if you made changes to:
- Build system (`src/Cosmos.Build.*`)
- Patcher (`src/Cosmos.Patcher`)

If you only changed the **kernel examples** (`examples/DevKernel/`) or **Kernel core libraries** (`src/Cosmos.Kernel.*`), skip to step 3.

```bash
./.devcontainer/postCreateCommand.sh [x64|arm64]
```
Default is x64. This script:
- Clears NuGet cache
- Builds and packs all projects in dependency order
- Creates packages in `artifacts/package/release/`
- Installs global tools (ilc, Cosmos.Patcher)
- Restores main solution

**When to run:**
- First time setup
- After modifying build tools
- After pulling changes that affect `src/` directories

**When to skip:**
- Only modifying kernel examples (`examples/`)
- Only changing kernel application code

### 2. Initialize Submodules (Required for DevKernel)

**When to run:**
- First time setup
  
```bash
git submodule update --init --recursive
```

### 3. Build Kernel
```bash
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet publish -c Debug -r linux-x64 -p:DefineConstants="ARCH_X64" \
  ./examples/DevKernel/DevKernel.csproj -o ./output-x64
```

### 4. Test with QEMU (with UART logs)
```bash
# Run QEMU with serial output, wait for boot, check logs, and kill
qemu-system-x86_64 -cdrom ./output-x64/DevKernel.iso -m 512M \
  -serial file:uart.log -nographic &
QEMU_PID=$!
sleep 3
head -20 uart.log
kill $QEMU_PID 2>/dev/null || pkill -9 -f qemu-system
```

Expected output in uart.log:
```
UART started.
CosmosOS gen3 v0.*.* booted.
Architecture: x86-64.
```

## Project Structure
- `src/` - Core patcher, build tools, kernel components
- `src/Cosmos.Kernel.*` - Kernel components
- `examples/DevKernel/` - Development example kernel project (recommended for testing)
- `tests/` - Test projects
- `dotnet/runtime/` - Submodule with .NET runtime sources (required for DevKernel)
- `.github/workflows/dotnet.yml` - CI pipeline reference

## Test with QEMU

### x64
```bash
qemu-system-x86_64 -cdrom ./output-x64/DevKernel.iso -m 512M
```

### ARM64
```bash
qemu-system-aarch64 -M virt -cpu cortex-a72 -cdrom ./output-arm64/DevKernel.iso -m 512M
```

## Documentation
See `docs/index.md` for full documentation links.
