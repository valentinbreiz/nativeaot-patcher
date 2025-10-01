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
- `tests/Cosmos.Tests.Kernel/` - Automated kernel runtime tests
- `dotnet/runtime/` - Submodule with .NET runtime sources (required for DevKernel)
- `.github/workflows/dotnet.yml` - CI pipeline reference

## Automated Kernel Tests

### Running Tests
The `tests/Cosmos.Tests.Kernel/` project contains automated runtime tests for the kernel. Use the test runner script:

```bash
# Run tests with x64 architecture (default)
./tests/Cosmos.Tests.Kernel/run-tests.sh

# Run tests with ARM64 architecture
./tests/Cosmos.Tests.Kernel/run-tests.sh arm64

# Specify custom timeout (default 10s)
./tests/Cosmos.Tests.Kernel/run-tests.sh x64 15
```

The script will:
1. Build the test kernel
2. Run it in QEMU with serial output capture
3. Parse test results and display colored output
4. Exit with status 0 if all tests pass, 1 if any fail

### Test Coverage (34 tests total: 33 pass, 1 intentional failure)

**Test Categories:**
- **Validation** (2 tests) - Framework validation (intentional failures), Intentional failure test (demonstrates failure detection)
- **Serial I/O** (2 tests) - Serial write, number output
- **Memory Management** (5 tests) - Small/large allocations, arrays, byte arrays
- **String Operations** (4 tests) - Creation, concatenation, length, allocation
- **GC/Heap** (5 tests) - Allocation patterns, large objects, mixed sizes, fragmentation, zero-init
- **Array Operations** (6 tests) - Empty, single element, copy, typed arrays, boundaries, fill patterns
- **Numeric Operations** (10 tests) - Arithmetic, overflow, unsigned/long integers, bitwise, shifts, comparisons

**Test Validation:**
- All tests use UART protocol assertions (`[ASSERT_PASS]`/`[ASSERT_FAIL]`)
- Each assertion verified via serial output - tests actually validate kernel functionality
- Framework includes intentional failing test to prove failure detection works
- Expected result: `TEST_END: 33/34 passed, 1 failed` (the failing test is intentional)

**Known Limitations:**
- Multi-dimensional arrays (`int[,]`) not supported - cause kernel crashes
- Jagged arrays (`int[][]`) not supported - cause kernel crashes
- Action delegates not supported - tests use direct method calls

### Manual Testing
To manually run and inspect test output:

```bash
# Build test kernel
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet publish -c Debug -r linux-x64 -p:DefineConstants="ARCH_X64" \
  ./tests/Cosmos.Tests.Kernel/Cosmos.Tests.Kernel.csproj -o ./output-tests

# Run in QEMU with serial logging
qemu-system-x86_64 -cdrom ./output-tests/Cosmos.Tests.Kernel.iso -m 512M \
  -serial file:tests.log -nographic &
QEMU_PID=$!
sleep 5
cat tests.log
kill $QEMU_PID 2>/dev/null || pkill -9 -f qemu-system
```

## Test with QEMU (DevKernel)

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
