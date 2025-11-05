# NativeAOT-Patcher Quick Reference

## 🚨 MANDATORY WORKFLOW RULES 🚨

**BEFORE doing ANY work, you MUST:**

1. **Check Task Master for current task**: Use `tm next` or `tm show <id>` to see what you should be working on
2. **Update task with findings**: Use `tm update-subtask --id=<id> --prompt="your findings"` to log what you learn
3. **Use task system for decisions**: If you need to explore or debug, update the task with your plan FIRST
4. **NO random bash exploration**: Every command should serve the current task's objective

**Task-Driven Debugging Workflow:**
- ❌ DON'T: Run random bash commands trying different things
- ✅ DO: `tm show <id>` → understand what's needed → `tm update-subtask` with plan → execute plan → `tm update-subtask` with results
- ❌ DON'T: Chase problems without tracking progress
- ✅ DO: Update task with each finding, then decide next step based on task objective

**If stuck:**
1. `tm update-subtask --id=<id> --prompt="Stuck on: [problem]. Findings: [what you learned]. Need: [what's missing]"`
2. Ask user for guidance
3. DON'T keep trying random approaches

## Project Overview
NativeAOT patcher for Cosmos OS - ports the Cosmos plug system and assembly loading to NativeAOT.

## Complete Test Workflow

To test the entire workflow from setup to kernel execution:

### 1. Setup Framework (REQUIRED for First Time & Build System Changes)

**⚠️ CRITICAL:** ALWAYS run `postCreateCommand.sh` if you made changes to:
- Build system (`src/Cosmos.Build.*`)
- Patcher (`src/Cosmos.Patcher`)
- **Kernel core libraries** (`src/Cosmos.Kernel.*`) - especially for multi-arch work
- HAL implementations (`src/Cosmos.Kernel.HAL.*`)
- **First time setup or after pulling major changes**

**Skip to step 3 ONLY if:**
- You only changed kernel examples (`examples/DevKernel/`) application code
- No changes to src/ directories at all

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

#### x64 Build
```bash
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet publish -c Debug -r linux-x64 -p:DefineConstants="ARCH_X64" \
  ./examples/DevKernel/DevKernel.csproj -o ./output-x64
```

#### ARM64 Build
```bash
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet publish -c Debug -r linux-arm64 -p:DefineConstants="ARCH_ARM64" -p:CosmosArch=arm64 \
  ./examples/DevKernel/DevKernel.csproj -o ./output-arm64
```

### 4. Test with QEMU (with UART logs)

#### x64 Testing
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

#### ARM64 Testing
```bash
# Run QEMU with serial output for ARM64 (requires UEFI firmware)
qemu-system-aarch64 -M virt -cpu cortex-a72 -m 512M \
  -bios /usr/share/qemu-efi-aarch64/QEMU_EFI.fd \
  -cdrom ./output-arm64/DevKernel.iso \
  -serial file:uart-arm64.log -nographic &
QEMU_PID=$!
sleep 10
head -50 uart-arm64.log
kill $QEMU_PID 2>/dev/null || pkill -9 -f qemu-system
```

Expected output in uart-arm64.log:
```
UART started.
CosmosOS gen3 v0.*.* booted.
Architecture: ARM64.
```

**Note:**
- ARM64 requires UEFI firmware (`-bios` flag) to boot from CD-ROM, unlike x86 which can boot directly
- ARM64 is fully functional with heap initialization, memory management, and PL011 UART driver working correctly
- Both x64 and ARM64 architectures produce UART output for debugging

## Project Structure
- `src/` - Core patcher, build tools, kernel components
- `src/Cosmos.Kernel.*` - Kernel components
- `examples/DevKernel/` - Development example kernel project (recommended for testing)
- `tests/` - Test projects
- `dotnet/runtime/` - Submodule with .NET runtime sources (required for DevKernel)
- `.github/workflows/dotnet.yml` - CI pipeline reference

## Test with QEMU

For detailed testing with UART log capture, see section 4 above. For quick visual testing:

### x64
```bash
qemu-system-x86_64 -cdrom ./output-x64/DevKernel.iso -m 512M
```

### ARM64
```bash
qemu-system-aarch64 -M virt -cpu cortex-a72 -m 512M \
  -bios /usr/share/qemu-efi-aarch64/QEMU_EFI.fd \
  -cdrom ./output-arm64/DevKernel.iso
```

**Important:** ARM64 requires the `-bios` flag with UEFI firmware to boot from CD-ROM.

**Recommended:** Use the UART log commands from section 4 to capture serial output for verification.

## Documentation
See `docs/index.md` for full documentation links.

## Task Master AI Instructions
**Import Task Master's development workflow commands and guidelines, treat as if import is in the main CLAUDE.md file.**
@./.taskmaster/CLAUDE.md
