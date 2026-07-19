# Contributing to Cosmos gen3 (nativeaot-patcher)

Thank you for your interest in contributing! This guide covers how to report issues so they can actually be diagnosed, and how to set up for development.

## Reporting Issues

Use the [issue templates](https://github.com/valentinbreiz/nativeaot-patcher/issues/new/choose) — they ask for everything below. The short version: a report we can act on names the **exact command you ran**, the **versions involved**, and includes the **full output** (build log or serial log), not a screenshot of the last line.

### For build/publish failures

- The exact command (`cosmos build`, a VSCode task, `dotnet publish ...`) and the directory you ran it from
- `dotnet --version`, your OS, and the Cosmos.Sdk version (the `Sdk="Cosmos.Sdk/X.Y.Z"` line and `PackageReference` versions from your `.csproj`)
- The full build output — run with `--verbosity normal` if the minimal log doesn't show the failing step
- Output of `cosmos check` if the failure looks toolchain-related (missing linker, assembler, QEMU...)

### For kernel crashes at runtime

A CPU exception dump alone ("CPU EXCEPTION: General Protection Fault") is rarely enough — but with the serial log and a symbolicated stack trace, most crashes can be diagnosed directly. See [Collecting diagnostics](#collecting-diagnostics) below, and attach:

- The **full serial log** from boot to crash (not just the exception block)
- The **stack trace addresses symbolicated** to function names, or simply your kernel's `.elf` file (zip it) so we can symbolicate ourselves
- A link to a **repository that reproduces** the crash, if you can share one — this is by far the most useful thing
- How you launched the kernel: `cosmos run`, a VSCode task, or a manual QEMU command (include the QEMU flags — attached disks, memory size, and input devices all matter)

### For feature requests

- **Use case**: why you need this
- **Proposed solution**: how you envision it working
- **Alternatives**: other approaches you've considered

## Collecting diagnostics

### Capturing the serial log

The kernel logs everything (boot progress, GC activity, exception dumps) to the serial port.

- `cosmos run` shows serial output in the terminal.
- Running QEMU yourself: add `-serial stdio` (print to terminal) or `-serial file:serial.log` (write to a file). The default VSCode tasks already pass `-serial stdio`.

Attach the whole log to your issue. Lines before the crash (GC collections, allocation messages, driver init) are often the actual clue.

### Symbolicating a crash stack trace

A CPU exception dump prints raw return addresses:

```
Stack trace (raw return addresses, symbolicate with nm):
  ip:  0xFFFFFFFF80050A1E
  [0]  0xFFFFFFFF80050947
  ...
```

Your built kernel ELF (at `bin/<Config>/net10.0/linux-<arch>/<KernelName>.elf`) contains the symbols to turn these into function names:

```bash
addr2line -e bin/Debug/net10.0/linux-x64/MyKernel.elf -f -C 0xFFFFFFFF80050A1E 0xFFFFFFFF80050947 ...
```

Paste the resulting function names into the issue (the `ip` address is where it crashed; the rest is the call chain). If you're not sure how, just attach the `.elf` file itself — we can symbolicate it.

### Common issues

1. **Build fails right after cloning the framework repo**: run `./.devcontainer/postCreateCommand.sh` (or `make setup`) first — it builds all packages and configures the local NuGet source.
2. **`undefined symbol: _native_*` / `Rhp*` at link time**: the architecture-native package wasn't restored. Fixed in versions after 3.0.68; on older SDKs pass `-p:CosmosArch=x64` (or `arm64`) to `dotnet publish`, or use `cosmos build`.
3. **QEMU shows nothing / boots to BIOS**: check the architecture matches your build (x64 vs ARM64) and force CD boot with `-boot d` when a disk is also attached.
4. **Runtime errors on APIs that work in a normal .NET app**: kernels are AOT-compiled — see [NativeAOT limitations](https://github.com/dotnet/runtime/blob/main/src/coreclr/nativeaot/docs/limitations.md) (no reflection emit, no dynamic code), and not all of the base library is plugged yet.

## Development Setup

Requires .NET SDK 10.0.100+ (see `global.json`). After cloning:

```bash
./.devcontainer/postCreateCommand.sh   # build all packages, install cosmos/cosmos-patcher tools
make run                                # build the DevKernel ISO and boot it in QEMU
make test KERNEL=Memory                 # run a kernel test suite
```

See the [Developer Docs](https://valentinbreiz.github.io/nativeaot-patcher/index.html) for architecture internals (plug system, GC, scheduler, build pipeline).

## Code Style

See the [Coding Guidelines](docs/articles/dev/coding-guidelines.md). Highlights:

- C# 14 / .NET 10, 4-space indentation, Allman braces (enforced by `.editorconfig`)
- Braces always required; avoid `var` — use explicit types
- Private fields `_camelCase`, statics `s_camelCase`, constants `PascalCase`
- Kernel code must be AOT-compatible: no reflection, no dynamic code generation
- Architecture-specific code goes behind `#if ARCH_X64` / `#if ARCH_ARM64`, and only in `Cosmos.Kernel.Core` or `Cosmos.Kernel.Plugs`
- Commit messages start with a [gitmoji](https://gitmoji.dev/) matching the change (🐛 fix, ✨ feature, 📝 docs, ♻️ refactor, ✅ tests)

## Testing

Before submitting a PR:

1. Run `dotnet test` for the build-tool test suites in `tests/`
2. If kernel code changed, run the relevant kernel suites: `make test KERNEL=<Name>` (and `ARCH=arm64` if the change affects both architectures)
3. CI runs both architectures — a PR needs both green

## Pull Requests

- Reference the issue number in the PR description
- Keep changes focused; split unrelated fixes into separate PRs
- Update documentation when behavior changes

## Questions?

- [Documentation site](https://valentinbreiz.github.io/nativeaot-patcher/index.html)
- [Discord](https://discord.com/invite/kwtBwv6jhD)
- [Existing issues](https://github.com/valentinbreiz/nativeaot-patcher/issues)

Thank you for contributing!
