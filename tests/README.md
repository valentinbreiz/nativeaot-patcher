# Cosmos Test Runner

NativeAOT-compatible test runner for Cosmos OS kernels with QEMU execution and CI integration.

## Quick Start

### Run Tests from VS Code

**Using Tasks (Recommended):**
1. Press `Ctrl+Shift+P` and type "Tasks: Run Task"
2. Select one of:
   - **Run Test: HelloWorld (x64)** - Run with console + XML output
   - **Run Test: HelloWorld (x64, Console Only)** - Console output only
   - **Run Test: HelloWorld (ARM64)** - ARM64 test with XML output

**Using Test Menu:**
- Press `Ctrl+Shift+P` → "Tasks: Run Test Task"
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
3. Timeout in seconds (e.g., `60`)
4. (Optional) XML output path for JUnit format

## Output Formats

### Console Output (Colored)
```
================================================================================
Starting test suite: HelloWorld Basic Tests
Architecture: x64
Time: 2025-11-05 04:06:48
================================================================================
.F.S..
[2] Test_StringEquality: Assertion failed: Expected "Hello" but got "World"
================================================================================
Suite: HelloWorld Basic Tests
Total tests: 6
Passed: 4
Failed: 1
Skipped: 1
Duration: 2.45s
================================================================================
TESTS FAILED (1 failures)
================================================================================
```

### XML Output (JUnit Format)
```xml
<?xml version="1.0" encoding="utf-16"?>
<testsuites name="HelloWorld Basic Tests" tests="6" failures="1" skipped="1" time="2.450">
  <testsuite name="HelloWorld Basic Tests" tests="6" failures="1" skipped="1" time="2.450">
    <properties>
      <property name="architecture" value="x64" />
    </properties>
    <testcase name="Test_BasicArithmetic" classname="HelloWorld Basic Tests" time="0.015" />
    <testcase name="Test_StringEquality" classname="HelloWorld Basic Tests" time="0.012">
      <failure message="Assertion failed">Expected "Hello" but got "World"</failure>
    </testcase>
    <!-- ... more test cases ... -->
  </testsuite>
</testsuites>
```

## Project Structure

```
tests/
├── Cosmos.TestRunner.Engine/       # Test runner execution engine
│   ├── Engine.cs                   # Main orchestration
│   ├── Engine.Build.cs            # NativeAOT build pipeline
│   ├── Hosts/                     # QEMU host implementations
│   │   ├── QemuX64Host.cs
│   │   └── QemuARM64Host.cs
│   ├── OutputHandlers/            # Result output formats
│   │   ├── OutputHandlerConsole.cs
│   │   └── OutputHandlerXml.cs
│   └── Protocol/                  # UART protocol parser
├── Cosmos.TestRunner.Framework/    # In-kernel test framework
│   ├── TestRunner.cs              # Test execution (Start/Run/Finish)
│   └── Assert.cs                  # Assertion methods
├── Cosmos.TestRunner.Protocol/     # Protocol definitions
│   └── Messages.cs                # Binary protocol constants
└── Kernels/                       # Test kernel projects
    └── Cosmos.Kernel.Tests.HelloWorld/
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
Assert.False(condition);

// Exception handling
Assert.Throws<TException>(() => { /* code */ });

// Manual failure
Assert.Fail("Custom error message");
```

## CI Integration

### GitHub Actions

```yaml
- name: Run Cosmos Tests
  run: |
    dotnet run --project tests/Cosmos.TestRunner.Engine/Cosmos.TestRunner.Engine.csproj -- \
      tests/Kernels/Cosmos.Kernel.Tests.HelloWorld \
      x64 \
      120 \
      test-results.xml

- name: Publish Test Results
  uses: dorny/test-reporter@v1
  if: always()
  with:
    name: Cosmos Tests
    path: test-results.xml
    reporter: java-junit
```

## Exit Codes

- **0**: All tests passed
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
- Use longer timeout (90s+) for ARM64

## Architecture

The test runner uses a binary protocol over UART serial:

1. **Kernel Side**: `TestRunner.Framework` sends binary messages
2. **UART Capture**: QEMU redirects serial to file
3. **Protocol Parser**: Engine parses binary messages in real-time
4. **Output Handlers**: Console and XML outputs generated
5. **Result Aggregation**: Final TestResults with all test outcomes

## Performance

- **x64 Build**: ~60s (kernel compilation)
- **x64 Execution**: 2-5s (typical test suite)
- **ARM64 Build**: ~70s (kernel compilation)
- **ARM64 Execution**: 5-10s (slower boot)
