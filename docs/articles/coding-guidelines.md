# Coding Guidelines

This document establishes the coding style and architecture patterns for Cosmos Gen3. These guidelines ensure a clean, modern, and consistent codebase across all contributors.

## Table of Contents

- [1. Project Architecture](#1-project-architecture)
- [2. Naming Conventions](#2-naming-conventions)
- [3. File Organization](#3-file-organization)
- [4. Class & Type Design](#4-class--type-design)
- [5. Kernel Lifecycle](#5-kernel-lifecycle)
- [6. HAL Architecture](#6-hal-architecture)
- [7. Plug System](#7-plug-system)
- [8. Native Interop](#8-native-interop)
- [9. Architecture-Specific Code](#9-architecture-specific-code)
- [10. Memory & Safety](#10-memory--safety)
- [11. Error Handling](#11-error-handling)
- [12. Modern C# 14 / .NET 10 Idioms](#12-modern-c-14--net-10-idioms)
- [13. AOT Constraints](#13-aot-constraints)
- [14. Documentation](#14-documentation)
- [15. Testing](#15-testing)

---

## 1. Project Architecture

### Layer Dependency Rules

The project is split into strict layers. Dependencies flow **downward only**. These rules are **enforced at compile time** by the `LayerAnalyzer` Roslyn analyzer in `Cosmos.Build.Analyzer.Patcher`.

```
User Kernel (DevKernel, test kernels)
    └── Cosmos.Kernel.System        ← high-level OS APIs (Console, Graphics, Network)
         └── Cosmos.Kernel.HAL      ← hardware abstraction (shared logic)
              ├── Cosmos.Kernel.HAL.X64        ← x64-specific HAL implementations
              ├── Cosmos.Kernel.HAL.ARM64      ← ARM64-specific HAL implementations
              └── Cosmos.Kernel.HAL.Interfaces ← pure interfaces, no implementations
                   └── Cosmos.Kernel.Core   ← low-level runtime (memory, scheduler, serial)
                        ├── Cosmos.Kernel.Native.X64       ← x64 assembly (.asm)
                        ├── Cosmos.Kernel.Native.ARM64     ← ARM64 assembly (.S)
                        └── Cosmos.Kernel.Native.MultiArch ← cross-platform native C code (ACPI, libc stubs)
```

For the full dependency graph, project descriptions, and rules, see [Kernel Project Layout](kernel-project-layout.md).

### When to Create a New Project

- New hardware device category → new interface in `Cosmos.Kernel.HAL.Interfaces`, implementations in `Cosmos.Kernel.HAL.X64`/`Cosmos.Kernel.HAL.ARM64`. Cross-platform HAL devices go to `Cosmos.Kernel.HAL`.
- New OS-level feature, user API exposed → in `Cosmos.Kernel.System`.
- New low-level runtime concern → in `Cosmos.Kernel.Core`.

---

## 2. Naming Conventions

All naming rules are enforced by `.editorconfig`. Summary:

| Element | Convention | Example |
|---------|-----------|---------|
| Namespace | `Cosmos.Kernel.{Layer}.{Category}` | `Cosmos.Kernel.Core.Scheduler` |
| Public class/struct | PascalCase | `GarbageCollector`, `Thread` |
| Interface | `I` + PascalCase | `IScheduler`, `IPlatformInitializer` |
| Public method/property | PascalCase | `InitializeStack()`, `StackPointer` |
| Private/internal field | `_camelCase` | `_currentScheduler`, `_kernel` |
| Private/internal static field | `s_camelCase` | `s_cursorPattern`, `s_nextThreadId` |
| Constant | PascalCase | `DefaultStackSize`, `MaxThreadCount` |
| Private constant | PascalCase | `COM1_BASE` (hardware regs use UPPER_SNAKE for readability) |
| Local variable | camelCase | `stackTop`, `entryPoint` |
| Parameter | camelCase | `cpuState`, `threadId` |
| Type parameter | `T` + PascalCase | `TValue`, `TKey` |
| Enum member | PascalCase | `ThreadState.Running` |

### Avoid

- Hungarian notation (`m_`, `p_`, `g_`),use `_` prefix for private fields only.
- Legacy `mStopped` style, use `_stopped`.
- Abbreviations unless universally understood (`GC`, `CPU`, `HAL`, `IO`, `IP`, `MAC`).

---

## 3. File Organization

### One Type Per File

Each public type gets its own file named after the type:

```
Cosmos.Kernel.Core/
  Scheduler/
    Thread.cs
    ThreadState.cs
    IScheduler.cs
    SchedulerManager.cs
    PerCpuState.cs
```

### Partial Classes for Large Types

Split large classes across multiple files using `partial`. Name the files `{ClassName}.{Aspect}.cs`:

```
Memory/
  GarbageCollector.cs              ← core structure, fields, initialization
  GarbageCollector.Alloc.cs        ← allocation methods
  GarbageCollector.Mark.cs         ← mark phase
  GarbageCollector.Sweep.cs        ← sweep phase
  GarbageCollector.GCHandler.cs    ← GC handle table
  GarbageCollector.Frozen.cs       ← frozen segment support
```

### Architecture-Specific Files

For types that need per-architecture implementations within the same project, use a suffix:

```
Scheduler/
  ThreadContext.X64.cs             ← x64-specific context layout
  ThreadContext.ARM64.cs           ← ARM64-specific context layout
```

These are conditionally compiled via `.csproj`:

```xml
<ItemGroup Condition="'$(CosmosArch)' != 'arm64'">
    <Compile Remove="Scheduler\ThreadContext.ARM64.cs" />
</ItemGroup>
```

This is only allowed now in `Cosmos.Kernel.Core` but this may change in the future.

### File Structure Order

Use `// --- Section Name ---` comments to separate groups. The ordering differs between static and instance classes.

#### Static Classes

```csharp
public static unsafe partial class StaticClassName
{
    // --- Nested types ---

    // --- Constants ---

    // --- Private fields ---

    // --- Public properties ---

    // --- Public methods ---

    // --- Internal methods ---

    // --- Private methods ---
}
```

#### Instance Classes 

```csharp
public unsafe class Canvas
{
    // --- Nested types ---

    // --- Constants ---

    // --- Private fields ---

    // --- Public properties ---

    // --- Constructors ---

    // --- Static methods ---

    // --- Public methods ---

    // --- Internal methods ---

    // --- Protected methods ---

    // --- Private methods ---
}
```

**Key principles:**
- **One separator style everywhere:** `// --- Section Name ---`
- Data-oriented classes: group fields/properties by domain concern (`// --- Identity ---`, `// --- State ---`)
- Behavior-oriented classes: group by member type, then sub-group methods by category
- Constructors always come after fields/properties, before methods
- Static factory methods come right after constructors

### Using Directives

- Place `using` directives **outside** the namespace
- Sort `System` namespaces first (enforced by `dotnet_sort_system_directives_first`)
- Use file-scoped namespaces (eg. `namespace Cosmos.Kernel.Core.Scheduler;`)

---

## 4. Class & Type Design

### Static Utility Classes

Use `static class` for stateless kernel utilities that have no per-instance state.

### Managers

Use a `static class` with an `Initialize()` method and an `IsInitialized` property. Managers coordinate subsystem state without requiring an instance:

```csharp
// Simple manager — no underlying instance to expose
public static class TimerManager
{
    private static ITimerDevice? _timer;
    private static bool _initialized;

    public static bool IsInitialized => _initialized;

    public static void Initialize() { ... }
    public static void Wait(int ms) { ... }
}

// Manager wrapping a pluggable implementation — expose via Current
public static class SchedulerManager
{
    private static IScheduler? _currentScheduler;

    public static IScheduler Current => _currentScheduler
        ?? throw new InvalidOperationException("Scheduler not initialized");

    public static void Initialize(IScheduler scheduler) { ... }
}
```

Only add a `Current` property when the manager wraps a pluggable implementation (e.g., `IScheduler` for multiple scheduling algorithms). Most managers don't need one.

### Structs for Low-Level Data

Use `struct` with `[StructLayout(LayoutKind.Sequential)]` for data that maps to hardware or memory layouts:

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct FreeBlock
{
    public unsafe MethodTable* MethodTable;
    public nuint Size;
    public unsafe FreeBlock* Next;
}
```

### Enums

Use `enum` for state machines and flags. Apply `[Flags]` where appropriate:

```csharp
public enum ThreadState
{
    Ready,
    Running,
    Blocked,
    Sleeping,
    Dead
}

[Flags]
public enum ThreadFlags
{
    None = 0,
    IsIdle = 1 << 0,
    IsKernel = 1 << 1
}
```

---

## 5. Kernel Lifecycle

### User Kernel Pattern

User kernels inherit from `Cosmos.Kernel.System.Kernel` and override lifecycle methods:

```csharp
using Cosmos.Kernel.System;

namespace MyKernel;

public class Kernel : Cosmos.Kernel.System.Kernel
{
    protected override void BeforeRun()
    {
        // One-time setup (after boot, before main loop)
        Console.WriteLine("Booted!");
    }

    protected override void Run()
    {
        // Called in a loop until Stop() is called
        string? input = Console.ReadLine();
        ProcessCommand(input);
    }

    protected override void AfterRun()
    {
        // Cleanup (after main loop exits)
    }
}
```

**Rules:**
- Override `OnBoot()` only to customize boot (default calls `Global.Init()`)
- Override `BeforeRun()` for one-time setup after the system is ready
- `Run()` is the main loop body — keep it focused
- Call `Stop()` to exit the main loop cleanly
- **Never** return from the kernel — the base class halts the CPU after `AfterRun()`

---

## 6. HAL Architecture


### Interface-Driven HAL

All hardware interaction goes through interfaces. Implementations are registered at boot:

```csharp
// Interface (Cosmos.Kernel.HAL.Interfaces)
public interface ICpuOps
{
    void Halt();
    void DisableInterrupts();
    void EnableInterrupts();
}

// Implementation (Cosmos.Kernel.HAL.X64)
public class X64CpuOps : ICpuOps
{
    public void Halt() => Native.Cpu.Halt();
    // ...
}
```

### Platform Initializer Pattern

Each architecture provides a factory that creates all platform-specific components:

```csharp
public class X64PlatformInitializer : IPlatformInitializer
{
    public string PlatformName => "x86-64";
    public PlatformArchitecture Architecture => PlatformArchitecture.X64;

    public IPortIO CreatePortIO() => new X64PortIO();
    public ICpuOps CreateCpuOps() => new X64CpuOps();
    public IInterruptController CreateInterruptController() => new X64InterruptController();
    public ITimerDevice CreateTimer() => new X64Timer();
    public IKeyboardDevice[] GetKeyboardDevices() => [new PS2Keyboard()];
    public IMouseDevice[] GetMouseDevices() => [new PS2Mouse()];
    public INetworkDevice? GetNetworkDevice() => /* PCI probe */ null;
    public uint GetCpuCount() => /* ACPI/MADT */ 1;

    public void InitializeHardware()
    {
        // PCI, ACPI, APIC initialization
    }

    public void StartSchedulerTimer(uint quantumMs)
    {
        // Configure PIT/APIC timer for preemptive scheduling
    }
}
```

### Adding a New Device

1. Define the interface in `Cosmos.Kernel.HAL.Interfaces/Devices/`
2. Implement in `Cosmos.Kernel.HAL.X64/` and `Cosmos.Kernel.HAL.ARM64/`
3. Add factory method to `IPlatformInitializer`
4. Register during `InitializeHardware()` or via the platform initializer

### HAL Registration

```csharp
// At boot (in Global.Init or OnBoot):
PlatformHAL.Initialize(new X64PlatformInitializer());
```

---

## 7. Plug System

Plugs replace BCL methods at the IL level. The patcher rewires calls at build time. For full documentation on plug attributes (`[Plug]`, `[PlugMember]`, `[Expose]`, `[FieldAccess]`) and the plug template, see [Plugs](plugs.md).

### When to Use Plugs vs. Other Approaches

| Scenario | Approach |
|----------|----------|
| Replace a BCL method | Plug |
| Add kernel-level API | New class in `Cosmos.Kernel.System` |
| Provide runtime stub | `[RuntimeExport]` in Core |
| Call native code | `[LibraryImport]` in Core |

---

## 8. Native Interop

### RuntimeExport — C# callable from native

Used for runtime stubs that the NativeAOT runtime expects:

```csharp
[RuntimeExport("RhNewArray")]
internal static unsafe void* RhNewArray(MethodTable* pEEType, int length)
{
    return GarbageCollector.AllocArray(pEEType, length);
}
```

### LibraryImport — native callable from C#

Modern P/Invoke pattern for calling assembly routines:

```csharp
[LibraryImport("*", EntryPoint = "_native_cpu_rdtsc")]
[SuppressGCTransition]
private static partial ulong NativeReadTSC();
```

**Rules:**
- Use `LibraryImport` (not `DllImport`) — it's source-generated and AOT-compatible
- Use `"*"` as the library name (links to the kernel binary itself)
- Add `[SuppressGCTransition]` for hot-path calls that don't trigger GC
- Keep native interop methods `private` and wrap them in a clean public API
- Assembly entry point names use `_snake_case` with a category prefix: `_native_cpu_rdtsc`, `_native_io_write_byte`

---

## 9. Architecture-Specific Code

### Conditional Compilation

Use `#if ARCH_X64` / `#if ARCH_ARM64` for code that differs by architecture. For now this is only tolerated in `Cosmos.Kernel.Core` but this aims to disappear.

```csharp
public static void ComWrite(byte value)
{
#if ARCH_ARM64
    while ((Native.MMIO.Read32(PL011_BASE + PL011_FR) & FR_TXFF) != 0) ;
    Native.MMIO.Write8(PL011_BASE + PL011_DR, value);
#else
    while ((Native.IO.Read8(COM1_BASE + REG_LSR) & LSR_TX_EMPTY) == 0) ;
    Native.IO.Write8(COM1_BASE + REG_DATA, value);
#endif
}
```

### When to Use What

| Pattern | When |
|---------|------|
| `#if ARCH_X64` | Same class, small code differences (eg. serial driver) |
| Separate files (`*.X64.cs`) | Same class, large per-arch blocks (eg.  ThreadContext) |
| Separate HAL projects | Different implementations behind a shared interface |
| Separate Native projects | Assembly code (`.asm` / `.S`) |

### Guard Rules

- The default `#else` path should be x64 (most common development target)
- Always have both paths, no dangling `#if` without the other arch
- Consider whether the code belongs in a HAL implementation instead of `#if`

---

## 10. Memory & Safety

### Unsafe Code

Unsafe code is allowed (`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`) but should be contained in `Cosmos.Kernel.Core`:

```csharp
// Good: unsafe scoped to where needed
public static unsafe void WriteString(string str)
{
    fixed (char* ptr = str)
    {
        for (int i = 0; i < str.Length; i++)
        {
            ComWrite((byte)ptr[i]);
        }
    }
}

// Good: unsafe class for types that inherently work with pointers
public unsafe class Thread : SchedulerExtensible
{
    public ThreadContext* GetContext() => (ThreadContext*)StackPointer;
}
```

### Memory Allocation

```csharp
// Kernel heap allocation (manual, non-GC)
void* ptr = MemoryOp.Alloc(size);
MemoryOp.Free(ptr);

// GC-managed allocation (via RuntimeExport stubs)
// Happens automatically through normal C# object creation
List<int> list = new();  // uses RhAllocateNewArray under the hood
```

### Pointer Safety

- Always check pointers before dereferencing in GC/scanning code
- Use `nuint` for addresses (not `uint`, which avoids 64-bit truncation)
- Use `nint`/`nuint` for pointer arithmetic, not `int`/`uint`
- Use `stackalloc` for small, short-lived buffers instead of heap allocation

### Critical Sections

```csharp
// Preferred: scoped interrupt disable
using (InternalCpu.DisableInterruptsScope())
{
    // Interrupts disabled here
    // Re-enabled automatically at scope exit
}
```

---

## 11. Error Handling

### Kernel Panic

For unrecoverable errors, use `Panic.Halt()`:

```csharp
if (ptr == null)
    Panic.Halt("Memory allocation failed");

// With caller info (auto-filled by compiler)
Panic.Halt("Invalid thread state");
```

### Exceptions

Exceptions work, but use them judiciously:

```csharp
// Good: validate at API boundaries
public static void ConfigIP(INetworkDevice device, Address ip)
{
    if (device == null)
        throw new ArgumentNullException(nameof(device));
    // ...
}

// Good: feature guards
private static void ThrowIfKeyboardDisabled()
{
    if (!CosmosFeatures.KeyboardEnabled)
        throw new InvalidOperationException("Console input requires keyboard feature.");
}

// Bad: exceptions in interrupt handlers, GC, or scheduler hot paths
// These paths cannot allocate (exception objects are heap-allocated)
```

### When to Panic vs. Throw

| Situation | Action |
|-----------|--------|
| Hardware failure, corrupted state | `Panic.Halt()` |
| GC/allocator internal error | `Panic.Halt()` |
| Invalid API usage | `throw` appropriate exception |
| Missing feature at runtime | `throw InvalidOperationException` |
| User-facing error in kernel shell | `try/catch` + print error message |

---

## 12. Modern C# 14 / .NET 10 Idioms

The project targets `<LangVersion>latest</LangVersion>` (C# 14) and .NET 10. Use modern language features throughout.

### Established Modern Patterns

These patterns from C# 10–13 should be used consistently:

```csharp
// File-scoped namespaces
namespace Cosmos.Kernel.Core.Scheduler;

// Expression-bodied members for trivial implementations
public string PlatformName => "x86-64";
public void Halt() => Native.Cpu.Halt();

// Pattern matching
switch (args[i])
{
    case null: WriteString("null"); break;
    case string s: WriteString(s); break;
    case int n: WriteNumber(n); break;
}

// Null-conditional and coalescing
INetworkDevice? device = NetworkManager.PrimaryDevice;
if (device?.Ready != true) return;

Address ip = config?.IPAddress ?? defaultAddress;

// Collection expressions
public IKeyboardDevice[] GetKeyboardDevices() => [new PS2Keyboard()];

// Target-typed new
Thread thread = new();
List<int> items = new();

// Numeric separators for readability
public const nuint DefaultStackSize = 64 * 1024;
public static long TscFrequency { get; set; } = 1_000_000_000;

// stackalloc for small buffers (no heap allocation)
Span<byte> buffer = stackalloc byte[64];
```

### Avoid

```csharp
// Don't use var when type isn't obvious
var x = GetValue();           // Bad: what type is x?
int count = GetValue();       // Good: explicit type

// Don't use var for built-in types
var i = 0;                    // Bad
int i = 0;                    // Good

// Don't use this. qualification
this._field = value;          // Bad
_field = value;               // Good

// Don't allocate arrays in hot paths when Span works
byte[] temp = new byte[16];       // Bad in hot path: heap allocation
Span<byte> temp = stackalloc byte[16]; // Good: stack allocation
```

### Feature Switches

Use `[FeatureSwitchDefinition]` for compile-time feature toggling (trimmed by NativeAOT linker). All feature flags live in `Cosmos.Kernel.Core.CosmosFeatures`:

```csharp
// In CosmosFeatures.cs — one property per feature
[FeatureSwitchDefinition("Cosmos.Kernel.HAL.Interrupts.Enabled")]
public static bool InterruptsEnabled =>
    AppContext.TryGetSwitch("Cosmos.Kernel.HAL.Interrupts.Enabled", out bool enabled)
        ? enabled : true;

// Usage — the ILC linker trims the dead branch entirely
if (CosmosFeatures.KeyboardEnabled)
{
    // This code is removed from the binary when keyboard is disabled
}
```

---

## 13. AOT Constraints

NativeAOT imposes strict limitations. **All kernel code must be AOT-compatible.**

.NET 10 has improved NativeAOT with smaller binaries and broader platform support, but the fundamental constraints remain.

### Forbidden

- `System.Reflection.Emit` (no runtime code generation)
- `dynamic` keyword
- `Assembly.Load` at runtime
- `Type.GetType("...")` by string at runtime (types must be statically reachable)
- `MakeGenericType` / `MakeGenericMethod` at runtime (generic instantiations must be known at compile time)

### Use with Caution

- **Generic virtual methods** — NativeAOT must generate all possible instantiations at compile time. Avoid deeply nested or open-ended generic virtual dispatch.
- **LINQ** — many LINQ operators allocate iterators and closures. Avoid in hot paths (GC, scheduler, interrupt handlers).

---

## 14. Documentation

### License Header

Every `.cs` file must start with the license header (enforced by `.editorconfig`):

```csharp
// This code is licensed under MIT license (see LICENSE for details)
```

### XML Documentation

Document all **public** APIs with `<summary>`. Keep it concise:

```csharp
/// <summary>
/// Triggers a kernel panic with the specified message.
/// Disables interrupts and halts the CPU.
/// </summary>
/// <param name="message">The panic message describing the error.</param>
public static void Halt(string message) { ... }
```

**Rules:**
- Document public classes, interfaces, methods, properties
- Skip documentation on obvious members (`get`/`set` for self-documenting property names)
- Use `<param>` for non-obvious parameters
- Use `<returns>` when return value needs explanation
- Use `<see cref="..."/>` to reference related types
- Don't document private implementation details unless the logic is complex

### Inline Comments

Use sparingly — only when the code isn't self-explanatory:

```csharp
// Stack layout (growing downward from top):
// [StackBase + stackSize] = Top of usable stack
// [contextAddr] = Start of ThreadContext (where StackPointer points)
// [StackBase] = Bottom of stack

// Align context to 16 bytes (required for XMM operations)
contextAddr = (contextAddr + 0xF) & ~(nuint)0xF;
```

## 15. Testing

For the full testing guide (unit tests, kernel integration tests, UART protocol, CI, writing test kernels), see [Testing](testing.md).

**Code coverage:** Add the `run-coverage` label to a PR to trigger the coverage CI. It runs the kernel test suites and outputs which code paths are covered by the integration tests.

---
