**NativeAOT Patcher — Agents Guide**

- Purpose: High‑signal reference for contributors and coding agents working in this repo.
- Scope: Repo layout, build/test/dev workflows, key components, and safe contribution rules.

**Project Overview**
- Goal: Bring CosmosOS‑style plugs and assembly loading to .NET NativeAOT, enabling a kernel building and supporting libs to compile to native and boot via Limine.
- Core Idea: Rewrite IL in target assemblies by applying plug classes/members, then feed patched IL to ILCompiler (NativeAOT) and link with native bits (asm/C).

**Repository Layout**
- `src/Cosmos.Patcher`: CLI and engine that scans plug assemblies and patches target IL using Mono.Cecil.
- `src/Cosmos.Build.Patcher`: MSBuild integration for Cosmos.Patcher (targets, tasks), orchestrates which assemblies are patched and where outputs go.
- `src/Cosmos.Build.Analyzer.Patcher`: Roslyn analyzer that validates plug usage at compile time (early errors vs. runtime patch failures).
- `src/Cosmos.Build.Ilc`: MSBuild targets to resolve ILCompiler and generate `.ilc.rsp` + compile to native object (`.o`).
- `src/Cosmos.Build.Asm`: MSBuild tasks to compile `.asm` to objects (Yasm).
- `src/Cosmos.Build.GCC`: MSBuild tasks to compile C sources to objects (GCC).
- `src/Cosmos.Sdk`: Lightweight MSBuild SDK that wires the above build steps together for end‑user projects.
- `src/Cosmos.Kernel.*`: Kernel runtime, HAL, boot, plugs, etc. used by examples and as references during AOT.
- `examples/KernelExample`: Minimal kernel entrypoint demonstrating the full pipeline (plugs + ILC + asm + GCC + Limine linker script).
- `tests/`: Unit tests for scanner, patcher integration points, analyzer, and asm tasks.
- `docs/`: DocFX sources and “articles” describing build flows (patcher, ILC, asm).

**Key Concepts**
- Plug: A class annotated with `Cosmos.Build.API.Attributes.PlugAttribute` targeting a type by name or `Type`.
- Plug Member: A method/field/property annotated with `PlugMemberAttribute` that replaces or augments the matching target member.
- Instance plugs: Methods can take a synthetic first parameter named `aThis` to express instance context.
- Constructors: Special names `Ctor`/`CCtor` map to instance/static constructors; parameter matching rules apply.

**Architecture Flow**
- Patcher CLI: `src/Cosmos.Patcher`
  - Entry: `Program.cs` uses Spectre.Console.Cli to expose `patch` command.
  - Command: `PatchCommand` loads target + plug assemblies, runs `PlugPatcher`.
  - Engine: `PlugScanner` finds plug types; `PlugPatcher` patches methods/properties/fields; `MonoCecilExtensions` hosts utilities for safe cloning/importing IL.
- MSBuild Integration: `src/Cosmos.Build.Patcher`
  - Target `SetupPatcher` collects candidate assemblies, resolves plug refs, filters candidates via `FindPluggedAssembliesTask`, copies unmodified refs to `cosmos/ref`, and schedules patching.
  - Platform targets run `Cosmos.Patcher` via `PatcherTask` and place patched outputs under `$(IntermediateOutputPath)/cosmos`.
- AOT Compilation: `src/Cosmos.Build.Ilc`
  - `ResolveIlcPath` locates the `Microsoft.DotNet.ILCompiler` toolset from the runtime pack.
  - `WriteIlcRsp` writes the ILCompiler response file with inputs, references, features, and knobs.
  - `CompileWithIlc` runs `ilc` to produce native `.o` files.
- Native Bits: `src/Cosmos.Build.Asm` and `src/Cosmos.Build.GCC` compile assembly and C code into objects consumed by the final link step (example linker script in `examples/KernelExample/Linker`).
- SDK Composition: `src/Cosmos.Sdk/Sdk.props` ties targets together: `RunPatcher` → `CompileWithIlc` → `BuildYasm`/`BuildGCC`.

**Build And Test**
- Prereqs: .NET 9 SDK, GCC and Yasm installed for native pieces (platform‑specific paths configurable in props/targets), typical dev tools for your OS.
- Build all: `dotnet build nativeaot-patcher.slnx -c Debug`
- Run tests: `dotnet test -c Debug`
- Pack MSBuild task packages (when enabled by csproj): `dotnet pack -c Release`
- Example kernel build: open `examples/KernelExample/KernelExample.csproj` and run `dotnet publish -c Debug -r linux-x64 --verbosity detailed`. This exercises patcher + ILC + asm + GCC pipelines.

**Using The Patcher CLI**
- Build the CLI: `dotnet build src/Cosmos.Patcher -c Debug`
- Run patch: `dotnet run --project src/Cosmos.Patcher -- patch --target <path/to/target.dll> --plugs <plug1.dll;plug2.dll> --output <output.dll>`
- Notes: `--plugs` accepts `;` or `,` separated paths. Output defaults to `<target>_patched.dll` when `--output` is not specified.

**Common Contribution Tasks**
- Add a new plug:
  - Place plug in an appropriate project (e.g., `Cosmos.Kernel.Plugs` or a test project).
  - Annotate class with `[Plug(typeof(TargetType))]` or `[Plug("Namespace.TargetType")]`.
  - Annotate members with `[PlugMember]` matching target signatures. Use `TargetName` if the member name differs.
  - For instance members, add `object aThis` as the first parameter.
  - Add/extend unit tests under `tests/` validating `PlugScanner` and patch effects where possible.
- Extend patcher behavior:
  - Start in `src/Cosmos.Patcher/PlugPatcher.cs` and `MonoCecilExtension(s).cs`.
  - Update `PlugScanner` if discovery rules change.
  - Ensure parameter/type import logic via `Module.ImportReference` remains correct when cloning IL.
  - Log via `IBuildLogger` without excessive noise at `Info` level; use `Debug` for deep traces.
- Adjust MSBuild flows:
  - `src/Cosmos.Build.Patcher/build/*.targets` for patch orchestration.
  - `src/Cosmos.Build.Ilc/build/*.targets` for ILC inputs/features.
  - `src/Cosmos.Build.Asm` and `src/Cosmos.Build.GCC` to tune tool paths and flags.
- Analyzer rules:
  - Add descriptors in `DiagnosticMessages.cs`, implement checks in `PatcherAnalyzer.cs`, and cover with analyzer tests.

**Safe‑Change Guidelines (For Agents)**
- Keep scope tight: change only what the task requires; don’t reformat or refactor unrelated files.
- Preserve public APIs and MSBuild contract items unless explicitly requested; if changed, update docs and tests.
- Prefer small, composable patches; document rationale in PR descriptions or commit messages.
- Add/adjust tests near the code you change; run `dotnet test` before handoff ONLY IF NEEDED.
- Follow `.editorconfig` and existing style; prefer clear names over cleverness; avoid one‑letter identifiers.
- For patcher code, be careful with IL cloning/importing and constructor handling. Always import member references into the destination module.
- For targets, avoid destructive file ops; write to `$(IntermediateOutputPath)/cosmos/...` as established by current flows.

**Debugging Tips**
- Enable rich logs: patcher logs at Info/Debug via `ConsoleBuildLogger`. Use `Debug` sparingly in committed code; it’s available for tracing.
- Validate plugs in isolation: write small test fixtures like those in `tests/Cosmos.Tests.NativeWrapper` and assert on Mono.Cecil inspected IL if needed.
- When methods don’t match: check `aThis`, parameter counts, and `FullName` matches for parameter types; use `TargetName` to disambiguate overloads.

**Documentation**
- High‑level build articles live under `docs/articles/build/`:
  - Patcher: `docs/articles/build/patcher-build.md`
  - ILCompiler: `docs/articles/build/ilc-build.md`
  - Asm: `docs/articles/build/asm-build.md`
- DocFX config is in `docs/docfx.json`. If adding public APIs, update or extend docs accordingly.

**Release And Packaging**
- Several projects pack MSBuild tasks/SDK content. Use `dotnet pack -c Release` in CI or locally.
- Keep versioning aligned across `Cosmos.Build.*` and `Cosmos.Sdk` to avoid skew.

**Quick Commands**
- Build solution: `dotnet build nativeaot-patcher.slnx`
- Run tests: `dotnet test -c Debug`
- Example kernel: `dotnet publish -c Debug -r linux-x64 --verbosity detailed ./examples/KernelExample/KernelExample.csproj -o ./output`

This document is a living guide. If you are an agent adding features or refactors, update AGENTS.md with any new rules, flows, or gotchas you introduce.

