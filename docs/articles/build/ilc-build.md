## Overview

`Cosmos.ilc.Build` integrates the native AOT [ILCompiler](https://github.com/dotnet/runtime/tree/main/src/mono/ilc) into the MSBuild pipeline. It consumes patched assemblies emitted by [`Cosmos.Build.Patcher`](../../../src/Cosmos.Build.Patcher) and transforms them into native object files later linked with platform libraries for CosmosOS.

---

## Flow chart

```mermaid
flowchart TD
    A[ResolveIlcPath] --> B[WriteIlcRsp]
    B --> C[CompileWithIlc]
    C --> D[BuildGCC]
    D --> E[Native binary]
```

---

## Parameters

| Name | Description | Default |
| --- | --- | --- |
| `IlcToolsPath` | Path to the `ilc` executable resolved from the `Microsoft.DotNet.ILCompiler` runtime pack. | auto-resolved |
| `IlcIntermediateOutputPath` | Directory where `.ilc.rsp` and `.o` files are written. | `$(IntermediateOutputPath)/cosmos/native/` |
| `IlcSystemModule` | System module used as ILCompiler entry point. | `System.Private.CoreLib` |
| `UnmanagedEntryPointsAssembly` | Assemblies whose methods are exported via `--generateunmanagedentrypoints`. | none |

---

## Tasks

| Task | Description | Depends On |
| --- | --- | --- |
| `ResolveIlcPath` | Downloads and locates ILCompiler, setting `IlcToolsPath`. | `Build` |
| `WriteIlcRsp` | Produces the ILCompiler response file listing inputs, references, and options. | `ResolveIlcPath` |
| `CompileWithIlc` | Runs `ilc` using the response file to emit a native object file. | `WriteIlcRsp` |
| `BuildGCC` | Links ILCompiler output with native libraries into the final binary. | `CompileWithIlc` |

---

## Detailed workflow

1. **ResolveIlcPath** uses `GetPackageDirectory` to find the `runtime.<RID>.Microsoft.DotNet.ILCompiler` package and sets `IlcToolsPath`.
2. **WriteIlcRsp** creates `$(IlcIntermediateOutputPath)$(AssemblyName).ilc.rsp`, gathering patched assemblies from `$(IntermediateOutputPath)/cosmos`, references from `cosmos/ref`, and ILCompiler options such as `--runtimeknob` and `--feature` flags.
3. **CompileWithIlc** executes `ilc` with the generated response file, producing `$(AssemblyName).o` in `$(IlcIntermediateOutputPath)`.
4. **BuildGCC** from [`Cosmos.Build.GCC`](../../../src/Cosmos.Build.GCC) links the ILCompiler object file with C runtime objects to emit `$(NativeOutputPath)$(AssemblyName)`.
5. The native binary is ready for further packaging, such as bootloader integration.

---

## Related components

- [`Cosmos.Build.Patcher`](../../../src/Cosmos.Build.Patcher)
- [`Cosmos.Build.GCC`](../../../src/Cosmos.Build.GCC)
- [`Cosmos.Patcher`](../../../src/Cosmos.Patcher)
