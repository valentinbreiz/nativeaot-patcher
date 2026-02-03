# nativeaot-patcher

 Zarlo's NativeAOT patcher. Main goal is to port Cosmos plug system, assembly loading for NativeAOT. See https://github.com/CosmosOS/Cosmos/issues/3088 for details.

## Build Status

[![.NET Tests](https://github.com/valentinbreiz/nativeaot-patcher/actions/workflows/dotnet.yml/badge.svg?branch=main&event=push)](https://github.com/valentinbreiz/nativeaot-patcher/actions/workflows/dotnet.yml)
[![Kernel Tests](https://github.com/valentinbreiz/nativeaot-patcher/actions/workflows/kernel-tests.yml/badge.svg)](https://github.com/valentinbreiz/nativeaot-patcher/actions/workflows/kernel-tests.yml)
[![Package](https://github.com/valentinbreiz/nativeaot-patcher/actions/workflows/package.yml/badge.svg)](https://github.com/valentinbreiz/nativeaot-patcher/actions/workflows/package.yml)

 ## Documentation
 - [Index](https://github.com/valentinbreiz/nativeaot-patcher/blob/main/docs/index.md)

## Getting Help

Having issues? Here's how to get help:

1. **Check the [Documentation](https://github.com/valentinbreiz/nativeaot-patcher/blob/main/docs/index.md)** - Build guides, debugging tips, and more
2. **Review [Existing Issues](https://github.com/valentinbreiz/nativeaot-patcher/issues)** - Your question might already be answered
3. **Read [CONTRIBUTING.md](CONTRIBUTING.md)** - Common issues and troubleshooting
4. **File a [New Issue](https://github.com/valentinbreiz/nativeaot-patcher/issues/new/choose)** - Use the issue templates for bug reports, features, or questions

### Common Issues

- **Build fails after cloning**: Run `./.devcontainer/postCreateCommand.sh [x64|arm64]` first
- **QEMU errors**: Check architecture matches your build (x64 vs ARM64)
- **Runtime errors**: NativeAOT has [limitations](https://github.com/dotnet/runtime/blob/main/src/coreclr/nativeaot/docs/limitations.md) - no reflection or dynamic code

## Priority
- [Priority Board](https://github.com/users/valentinbreiz/projects/2/views/2) 
   
 ## Credit:
 - [@zarlo](https://github.com/zarlo)
 - [@kumja1](https://github.com/kumja1)
 - [@Guillermo-Santos](https://github.com/Guillermo-Santos)
 - [@valentinbreiz](https://github.com/valentinbreiz)
 - [@ascpixi](https://github.com/ascpixi)
 - [@ilobilo](https://github.com/ilobilo)
