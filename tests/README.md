# Cosmos Test Runner

NativeAOT-compatible test runner for Cosmos OS kernels with QEMU execution and CI integration.

## Test Suites

| Suite | Tests | Description |
|-------|-------|-------------|
| **HelloWorld** | 3 | Basic arithmetic, boolean logic, integer comparison |
| **Memory** | 18 (3 skipped) | Boxing/unboxing, memory allocation, generic collections |

### HelloWorld Tests
- `Test_BasicArithmetic` - Addition (2+2=4)
- `Test_BooleanLogic` - True/False assertions
- `Test_IntegerComparison` - Equality and comparison operators

### Memory Tests

**Boxing/Unboxing (8 tests):**
- `Boxing_Char`, `Boxing_Int32`, `Boxing_Byte`, `Boxing_Long`
- `Boxing_Nullable`, `Boxing_Interface`, `Boxing_CustomStruct`
- `Boxing_ArrayCopy` - Array.Copy with automatic boxing

**Memory Allocation (3 tests, 2 skipped):**
- `Memory_CharArray`, `Memory_StringAllocation`, `Memory_IntArray`
- ~~`Memory_StringConcat`~~ - Skipped: triggers #UD exception
- ~~`Memory_StringBuilder`~~ - Skipped: triggers #UD exception

**Generic Collections (7 tests, 1 skipped):**
- `Collections_ListInt`, `Collections_ListString`, `Collections_ListByte`
- `Collections_ListLong`, `Collections_ListStruct`
- `Collections_ListContains`, `Collections_ListIndexOf`
- ~~`Collections_ListRemoveAt`~~ - Skipped: Array.Copy triggers #UD exception

## Quick Start

### Run Tests from VS Code

**Using Tasks (Recommended):**
1. Press `Ctrl+Shift+P` and type "Tasks: Run Task"
2. Select one of:
   - **Run Test: HelloWorld (x64)** - Run with console + XML output
   - **Run Test: HelloWorld (x64, Console Only)** - Console output only
   - **Run Test: HelloWorld (ARM64)** - ARM64 test with XML output
   - **Dev Test: HelloWorld (x64)** - Developer mode with verbose output

**Using Test Menu:**
- Press `Ctrl+Shift+P` -> "Tasks: Run Test Task"
- Default test task: HelloWorld (x64)

**Debug Test Runner:**
1. Go to Run & Debug panel (`Ctrl+Shift+D`)
2. Select configuration:
   - **Debug Test Runner (HelloWorld x64)**
   - **Debug Test Runner (HelloWorld ARM64)**
3. Press `F5` to start debugging

### Run Tests from Command Line

```bash
# Run test with XML output
dotnet run --project tests/Cosmos.TestRunner.Engine/Cosmos.TestRunner.Engine.csproj -- \
  tests/Kernels/Cosmos.Kernel.Tests.HelloWorld \
  x64 \
  60 \
  test-results.xml

# Run test with console only
dotnet run --project tests/Cosmos.TestRunner.Engine/Cosmos.TestRunner.Engine.csproj -- \
  tests/Kernels/Cosmos.Kernel.Tests.HelloWorld \
  x64 \
  60
```

**Arguments:**
1. Kernel project path (absolute or relative)
2. Architecture: `x64` or `arm64`
3. Timeout in seconds
4. (Optional) XML output path for JUnit format
5. (Optional) Mode: `ci` or `dev`

**Recommended Timeouts:**
| Suite | x64 | ARM64 |
|-------|-----|-------|
| HelloWorld | 60s | 90s |
| Memory | 120s | 180s |

## Output Formats

### Console Output (Colored)
```
================================================================================
Starting test suite: HelloWorld Basic Tests
Architecture: x64
Time: 2025-11-05 04:06:48
================================================================================
...
[1] Test_BasicArithmetic: PASSED (15ms)
[2] Test_BooleanLogic: PASSED (12ms)
[3] Test_IntegerComparison: PASSED (10ms)
================================================================================
Suite: HelloWorld Basic Tests
Total tests: 3
Passed: 3
Failed: 0
Skipped: 0
Duration: 0.04s
================================================================================
ALL TESTS PASSED
================================================================================
```

### XML Output (JUnit Format)
```xml
<?xml version="1.0" encoding="utf-16"?>
<testsuites name="HelloWorld Basic Tests" tests="3" failures="0" skipped="0" time="0.037">
  <testsuite name="HelloWorld Basic Tests" tests="3" failures="0" skipped="0" time="0.037">
    <properties>
      <property name="architecture" value="x64" />
    </properties>
    <testcase name="Test_BasicArithmetic" classname="HelloWorld Basic Tests" time="0.015" />
    <testcase name="Test_BooleanLogic" classname="HelloWorld Basic Tests" time="0.012" />
    <testcase name="Test_IntegerComparison" classname="HelloWorld Basic Tests" time="0.010" />
  </testsuite>
</testsuites>
```

## Project Structure

```
tests/
├── Cosmos.TestRunner.Engine/       # Test runner execution engine
│   ├── Engine.cs                   # Main orchestration
│   ├── Engine.Build.cs             # NativeAOT build pipeline
│   ├── Program.cs                  # CLI entry point
│   ├── TestConfiguration.cs        # Configuration handling
│   ├── TestResults.cs              # Result model
│   ├── Hosts/                      # QEMU host implementations
│   │   ├── IQemuHost.cs            # Host interface
│   │   ├── QemuX64Host.cs          # x86-64 QEMU runner
│   │   └── QemuARM64Host.cs        # ARM64 QEMU runner
│   ├── OutputHandlers/             # Result output formats
│   │   ├── OutputHandlerBase.cs    # Abstract base class
│   │   ├── OutputHandlerConsole.cs # Colored terminal output
│   │   ├── OutputHandlerXml.cs     # JUnit XML output
│   │   └── MultiplexingOutputHandler.cs  # Multi-output support
│   └── Protocol/                   # UART protocol parser
│       └── UartMessageParser.cs    # Binary message parsing
├── Cosmos.TestRunner.Framework/    # In-kernel test framework
│   ├── TestRunner.cs               # Test execution (Start/Run/Skip/Finish)
│   └── Assert.cs                   # Assertion methods
├── Cosmos.TestRunner.Protocol/     # Protocol definitions
│   ├── Consts.cs                   # Protocol constants
│   └── Messages.cs                 # Binary message definitions
└── Kernels/                        # Test kernel projects
    ├── Cosmos.Kernel.Tests.HelloWorld/
    │   ├── Kernel.cs               # 3 basic tests
    │   └── Bootloader/limine.conf
    └── Cosmos.Kernel.Tests.Memory/
        ├── Kernel.cs               # 18 memory/collection tests
        └── Bootloader/limine.conf
```

## Writing Test Kernels

### Basic Test Kernel

```csharp
using Cosmos.TestRunner.Framework;
using static Cosmos.TestRunner.Framework.TestRunner;
using static Cosmos.TestRunner.Framework.Assert;

internal unsafe static partial class Program
{
    [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
    private static void KernelMain() => Main();

    private static void Main()
    {
        // Initialize test suite
        Start("My Test Suite");

        // Run tests
        Run("Test_Addition", () =>
        {
            int result = 2 + 2;
            Equal(4, result);
        });

        Run("Test_StringOps", () =>
        {
            string str = "Hello";
            Equal("Hello", str);
            NotNull(str);
        });

        // Skip tests that crash
        Skip("Test_Unsupported", "Feature not implemented");

        // Finish and send results
        Finish();

        while (true) ;  // Halt
    }
}
```

### Available Assertions

```csharp
// Equality
Assert.Equal(expected, actual);
Assert.Equal<T>(expected, actual);  // Generic

// Null checks
Assert.Null(obj);
Assert.NotNull(obj);

// Boolean
Assert.True(condition);
Assert.True(condition, "message");
Assert.False(condition);

// Exception handling
Assert.Throws<TException>(() => { /* code */ });

// Manual failure
Assert.Fail("Custom error message");
```

### Test Status

- **Passed** - Test completed without assertion failures
- **Failed** - Assertion failed or exception thrown
- **Skipped** - Test explicitly skipped via `Skip(name, reason)`

## CI Integration

### GitHub Actions Workflow

The CI workflow (`.github/workflows/kernel-tests.yml`) runs tests on both x64 and ARM64:

**Jobs:**
- `helloworld-tests` - Matrix build for x64/arm64
- `helloworld-results` - Combined PR comment
- `memory-tests` - Matrix build for x64/arm64
- `memory-results` - Combined PR comment
- `test-summary` - Final status summary

**Triggers:**
- Push to `main` branch
- Pull requests (any branch)
- Manual dispatch with architecture selection

**PR Comments:**
Each test suite posts a comment with:
- Separate rows for x64 and arm64 results
- Test counts (total, passed, failed, skipped)
- Duration
- Links to artifacts (XML results, UART log, kernel ISO)

**Artifacts (30-day retention):**
- `test-results-{suite}-{arch}.xml` - JUnit XML results
- `uart-log-{suite}-{arch}` - Full UART output
- `{Suite}-Test-ISO-{arch}` - Bootable kernel ISO + ELF

### Example CI Configuration

```yaml
- name: Run Cosmos Tests
  run: |
    dotnet run --project tests/Cosmos.TestRunner.Engine/Cosmos.TestRunner.Engine.csproj -- \
      tests/Kernels/Cosmos.Kernel.Tests.HelloWorld \
      x64 \
      120 \
      test-results.xml \
      ci

- name: Publish Test Results
  uses: dorny/test-reporter@v1
  if: always()
  with:
    name: Cosmos Tests
    path: test-results.xml
    reporter: java-junit
```

## Exit Codes

- **0**: All tests passed (skipped tests are acceptable)
- **1**: Tests failed or execution error
- **137**: Timeout (SIGKILL)

## UART Log

The test runner captures full UART output to `uart-output.log` for debugging:

```bash
# View UART log
cat uart-output.log

# Search for specific output
grep "ERROR" uart-output.log
```

## Troubleshooting

### Timeout Issues
- Increase timeout value (3rd argument)
- Check UART log for kernel boot issues
- Verify QEMU is installed: `qemu-system-x86_64 --version`

### Build Failures
- Run `.devcontainer/postCreateCommand.sh` to rebuild framework
- Check that NuGet packages are restored
- Verify .NET 9 SDK is installed

### ARM64 Issues
- Ensure UEFI firmware is installed: `/usr/share/qemu-efi-aarch64/QEMU_EFI.fd`
- Install on Ubuntu: `sudo apt install qemu-efi-aarch64`
- Use longer timeout (90s+ for HelloWorld, 180s+ for Memory)

### Test Crashes (#UD Exception)
Some operations trigger Invalid Opcode exceptions in the kernel. Use `Skip()` to mark these tests:
- String concatenation (`+` operator on strings)
- StringBuilder operations
- List.RemoveAt (uses Array.Copy internally)

## Architecture

The test runner uses a binary protocol over UART serial:

1. **Kernel Side**: `TestRunner.Framework` sends binary messages
2. **UART Capture**: QEMU redirects serial to file
3. **Protocol Parser**: Engine parses binary messages in real-time
4. **Output Handlers**: Console and XML outputs generated simultaneously
5. **Result Aggregation**: Final `TestResults` with all test outcomes
6. **Early Termination**: QEMU killed immediately when `TEST_SUITE_END` marker received

## Performance

| Stage | x64 | ARM64 |
|-------|-----|-------|
| Kernel Build | ~60s | ~70s |
| HelloWorld Execution | 2-5s | 5-10s |
| Memory Execution | 30-40s | 60-90s |

## Adding a New Test Suite

1. Create kernel project in `tests/Kernels/Cosmos.Kernel.Tests.{Name}/`
2. Copy `.csproj` and `Bootloader/limine.conf` from existing suite
3. Update `limine.conf` to point to correct ELF file
4. Implement tests using `TestRunner.Framework`
5. Add CI job in `.github/workflows/kernel-tests.yml`:
   - Copy `helloworld-tests` job and rename
   - Add `{name}-results` job for PR comments
   - Add to `test-summary` dependencies
6. Add VS Code tasks in `.vscode/tasks.json`
