# NativeAOT-Patcher

NativeAOT patcher for Cosmos OS - ports the Cosmos plug system to NativeAOT for building bare-metal C# kernels.

## Build Commands

```bash
# Setup (first time or after src/ changes)
./.devcontainer/postCreateCommand.sh [x64|arm64]

# Build x64 kernel
dotnet publish -c Debug -r linux-x64 -p:DefineConstants="ARCH_X64" \
  ./examples/DevKernel/DevKernel.csproj -o ./output-x64

# Build ARM64 kernel
dotnet publish -c Debug -r linux-arm64 -p:DefineConstants="ARCH_ARM64" -p:CosmosArch=arm64 \
  ./examples/DevKernel/DevKernel.csproj -o ./output-arm64

# Test x64 with QEMU
qemu-system-x86_64 -cdrom ./output-x64/DevKernel.iso -m 512M -serial file:uart.log -nographic

# Test ARM64 with QEMU (requires UEFI)
qemu-system-aarch64 -M virt -cpu cortex-a72 -m 512M \
  -bios /usr/share/qemu-efi-aarch64/QEMU_EFI.fd \
  -cdrom ./output-arm64/DevKernel.iso -serial file:uart.log -nographic
```

## Project Structure

- `src/Cosmos.Kernel.*` - Kernel core libraries
- `src/Cosmos.Build.*` - Build system
- `src/Cosmos.Patcher` - IL patcher
- `examples/DevKernel/` - Test kernel (use for development)
- `tests/Kernels/` - Test runner kernels
- `dotnet/runtime/` - .NET runtime submodule

## Task Master (Hamster)

**IMPORTANT:** Use Task Master for all work. Tasks sync with Hamster cloud.

```bash
task-master list                              # List all tasks
task-master next                              # Get next task
task-master show VAL-35                       # Show task details
task-master set-status --id=VAL-35 --status=in-progress
task-master update-task --id=VAL-42 --prompt="Findings..."
task-master add-task --prompt="New task description"
```

Task IDs use format `VAL-XX`. Update tasks with progress/findings as you work.

## Code Style

- Use C# 12 features, target .NET 9
- Kernel code must be AOT-compatible (no reflection, dynamic code)
- Use `[RuntimeExport("name")]` for functions callable from native code
- Use `[LibraryImport("*")]` for native imports
- Architecture-specific code uses `#if ARCH_X64` / `#if ARCH_ARM64`

## Key Files

- `src/Cosmos.Kernel.Core/Runtime/Stdllib.cs` - Runtime helper stubs (RhpThrowEx, etc.)
- `src/Cosmos.Kernel.Core/Memory/Memory.cs` - Memory allocation
- `src/Cosmos.Kernel.Native.X64/` - x64 assembly files (.asm)

## GitHub

```bash
gh issue list --repo valentinbreiz/nativeaot-patcher
gh issue view <number> --repo valentinbreiz/nativeaot-patcher
```
