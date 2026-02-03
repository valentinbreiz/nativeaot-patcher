# Contributing to NativeAOT-Patcher

Thank you for your interest in contributing to NativeAOT-Patcher! This guide will help you get started.

## Reporting Issues

When reporting issues, please provide:

### For Bug Reports
- **Clear description**: Describe what went wrong
- **Expected behavior**: What you expected to happen
- **Actual behavior**: What actually happened
- **Steps to reproduce**: Detailed steps to recreate the issue
- **Environment**: OS, .NET version, architecture (x64/ARM64)
- **Build configuration**: Debug/Release, compile flags used
- **Error messages**: Full error output, stack traces, or log files
- **Code sample**: Minimal reproducible code if applicable

### For Feature Requests
- **Use case**: Why you need this feature
- **Proposed solution**: How you envision it working
- **Alternatives**: Other approaches you've considered

### Common Issues

Before filing an issue, check these common problems:

1. **Build failures**: Did you run `./.devcontainer/postCreateCommand.sh [x64|arm64]` after cloning?
2. **QEMU errors**: Is the architecture correct in your command?
3. **NativeAOT limitations**: Some reflection/dynamic features aren't supported - see [NativeAOT Limitations](https://github.com/dotnet/runtime/blob/main/src/coreclr/nativeaot/docs/limitations.md)

## Development Setup

See [CLAUDE.md](CLAUDE.md) for detailed build commands and project structure.

## Code Style

- Use C# 12 features, target .NET 9
- Kernel code must be AOT-compatible (no reflection, dynamic code)
- Use `[RuntimeExport("name")]` for functions callable from native code
- Use `[LibraryImport("*")]` for native imports
- Architecture-specific code uses `#if ARCH_X64` / `#if ARCH_ARM64`

## Testing

Before submitting a PR:
1. Build both x64 and ARM64 configurations if your changes affect both
2. Run relevant tests from `tests/` directory
3. Test with QEMU if kernel code was modified
4. Ensure code follows existing patterns

## Pull Requests

- Reference the issue number in your PR description
- Keep changes focused and minimal
- Update documentation if behavior changes
- Add tests for new functionality

## Questions?

For questions about usage, please:
- Check the [documentation](docs/index.md)
- Review existing [issues](https://github.com/valentinbreiz/nativeaot-patcher/issues)
- Search [Cosmos OS discussions](https://github.com/CosmosOS/Cosmos/discussions)

Thank you for contributing!
