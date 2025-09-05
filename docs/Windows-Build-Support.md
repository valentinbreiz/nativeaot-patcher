# Windows Build Support

This document explains the Windows build support added to the nativeaot-patcher repository.

## Overview

The repository now supports building and testing on Windows in addition to Linux/Unix systems. This includes:

- ✅ All unit tests (patcher, scanner, analyzer, ASM)
- ✅ Package building and publishing
- ✅ Code formatting validation  
- ✅ Kernel ISO generation (cross-compilation to Linux targets)

## Windows Dependencies

The Windows build system requires the following tools:

### Core Dependencies

1. **YASM** - Assembly compiler
   - Installed via: `choco install yasm`
   - Used by: Assembly build tasks

2. **LLVM** - LLVM toolchain including linker
   - Installed via: `choco install llvm --version=18.1.8`
   - Provides: `ld.lld` linker for ELF generation
   - Used by: ISO/kernel linking

3. **MinGW-w64** - GCC cross-compiler
   - Installed via: `choco install mingw --version=11.2.0.07112021`
   - Provides: `x86_64-w64-mingw32-gcc` 
   - Used by: C code compilation (wrapped as `x86_64-elf-gcc`)

4. **.NET 9.0.200+ SDK**
   - Required for all .NET compilation
   - Installed via GitHub Actions `setup-dotnet` action

### Tool Mapping

| Tool Name | Windows Implementation | Linux Implementation |
|-----------|----------------------|---------------------|
| `yasm` | Direct install via Chocolatey | `apt-get install yasm` |
| `x86_64-elf-gcc` | Wrapper around `x86_64-w64-mingw32-gcc` | Direct `gcc` usage |
| `ld.lld` | From LLVM package | From `lld` package |
| `xorriso` | Downloaded from GitHub | `apt-get install xorriso` |

## GitHub Actions Integration

### Workflow Updates

All existing workflows have been updated to support Windows:

1. **`.github/workflows/dotnet.yml`**
   - Added `windows-latest` to build matrix
   - Windows dependency installation steps
   - Separate Windows ISO build job
   - Platform-specific package compilation

2. **`.github/workflows/package.yml`**  
   - Cross-platform package building
   - OS-specific artifact naming

3. **`.github/workflows/format.yml`**
   - Windows/Linux formatting validation
   - Platform-specific shell commands

### Windows CI Jobs

| Job Name | Purpose | Windows Support |
|----------|---------|----------------|
| `patcher-tests` | Core patcher tests | ✅ Full support |
| `scanner-tests` | IL scanner tests | ✅ Full support |  
| `analyzer-tests` | Roslyn analyzer tests | ✅ Full support |
| `asm-tests` | Assembly build tests | ✅ Full support |
| `windows-iso-tests` | Windows kernel ISO build | ✅ New job |
| `unix-iso-tests` | Linux kernel ISO build | ✅ Existing (renamed) |

## Build System Architecture

### MSBuild Target Selection

The build system automatically selects platform-specific targets:

```xml
<Import Project="GCC.Build.Unix.targets" Condition="'$(OS)' != 'Windows_NT'"/>
<Import Project="GCC.Build.Windows.targets" Condition="'$(OS)' == 'Windows_NT'"/>
```

### Windows-Specific Features

1. **Path Handling**: Automatic conversion to cygdrive format for compatibility
2. **Tool Detection**: Uses `where` instead of `which` for tool discovery  
3. **Cross-compilation**: All builds target Linux ELF format even on Windows
4. **Object Extensions**: Uses `.obj` extension instead of `.o`

## Setup Scripts

### `setup-windows.ps1`

Comprehensive Windows setup script for CI environments:

```powershell
.\setup-windows.ps1 [-SkipPackageRestore]
```

**Features**:
- Dependency validation and installation guidance
- GitHub Actions PATH integration
- Tool verification and diagnostics
- Error handling and reporting

### `install-packages.ps1`

Existing package setup script (unchanged):

```powershell
.\install-packages.ps1
```

**Features**:
- Builds packages in Release configuration
- Configures local NuGet sources
- Installs global tools

### `test-windows-setup.ps1`

Validation script for development environments:

```powershell
.\test-windows-setup.ps1
```

**Features**:  
- YAML syntax validation
- File existence checks
- Build system validation
- Setup guidance

## Cross-Platform Considerations

### Target Platform

- **Compilation Host**: Windows or Linux
- **Target Platform**: Linux x64 (for kernel/ISO)
- **Runtime**: .NET 9.0 (cross-platform)

### File Paths

- Windows builds use normalized paths
- Automatic cygdrive conversion for tools expecting Unix paths
- MSBuild handles cross-platform path differences

### Shell Commands

- **Windows**: PowerShell scripts with error handling
- **Linux**: Bash scripts with traditional Unix tools
- **Common**: .NET CLI commands work identically

## Development Workflow

### Local Windows Development

1. Install dependencies:
   ```powershell
   # Install via Chocolatey
   choco install yasm llvm mingw
   ```

2. Run setup:
   ```powershell
   .\setup-windows.ps1
   ```

3. Build and test:
   ```powershell
   dotnet build nativeaot-patcher.slnx
   dotnet test -c Debug
   ```

### Cross-Platform Testing

The CI system automatically tests on both platforms:

- **Push to main**: Full test suite on Windows + Linux
- **Pull requests**: Full test suite on Windows + Linux  
- **Packages**: Built on both platforms with OS-specific naming

## Troubleshooting

### Common Issues

1. **Chocolatey not found**
   - Solution: The CI script auto-installs Chocolatey
   - Manual: Install from https://chocolatey.org/install

2. **x86_64-elf-gcc not found**
   - Solution: Script creates wrapper around MinGW GCC
   - Verify: Check that MinGW is installed and in PATH

3. **ld.lld not found**  
   - Solution: Install LLVM package which includes linker
   - Verify: `ld.lld --version` should work

4. **YASM not found**
   - Solution: Install via `choco install yasm`
   - Alternative: Download from https://yasm.tortall.net/

### Debug Commands

```powershell
# Check tool availability
yasm --version
x86_64-elf-gcc --version  
ld.lld --version
dotnet --version

# Validate setup
.\test-windows-setup.ps1

# Check PATH
$env:PATH -split ';'
```

## Future Enhancements

Potential improvements for Windows support:

1. **Native Windows Kernel**: Support for Windows kernel targets (not just Linux)
2. **MSVC Compiler**: Alternative to MinGW for C compilation
3. **Windows-specific Tools**: Replace Unix-style tools with native Windows alternatives
4. **Performance**: Optimize CI build times on Windows runners
5. **Caching**: Implement dependency caching for faster builds

## Contributing

When adding features that affect cross-platform builds:

1. Test on both Windows and Linux
2. Use conditional MSBuild targets when needed
3. Update CI workflows for new dependencies
4. Document platform-specific requirements
5. Validate YAML syntax before committing

For questions or issues with Windows builds, please open an issue with:
- OS version and environment details
- Tool versions (YASM, LLVM, MinGW, .NET)
- Complete error messages and logs
- Steps to reproduce the issue