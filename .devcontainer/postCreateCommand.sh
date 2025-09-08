#!/usr/bin/env bash

set -e

# Parse architecture argument (default to x64)
ARCH="${1:-x64}"
echo "=== Starting postCreate setup for architecture: $ARCH ==="

# Set architecture-specific defines
if [ "$ARCH" = "arm64" ]; then
    ARCH_DEFINE="ARCH_ARM64"
    RUNTIME_ID="linux-arm64"
else
    ARCH_DEFINE="ARCH_X64"
    RUNTIME_ID="linux-x64"
fi

echo "Using define: $ARCH_DEFINE"
echo "Using runtime: $RUNTIME_ID"

# Clear all NuGet locals cache first
echo "Clearing NuGet cache..."
dotnet nuget locals all --clear

# Remove local source if it exists (to avoid path issues)
dotnet nuget remove source local-packages 2>/dev/null || true

# Create artifacts directory
mkdir -p artifacts/package/release

# Build and pack each project individually in dependency order
echo "Building and packing projects individually..."

# First build the base projects without dependencies
dotnet build src/Cosmos.Build.API/Cosmos.Build.API.csproj -c Release
dotnet pack src/Cosmos.Build.API/Cosmos.Build.API.csproj -c Release -o artifacts/package/release --no-build

dotnet build src/Cosmos.Build.Common/Cosmos.Build.Common.csproj -c Release
dotnet pack src/Cosmos.Build.Common/Cosmos.Build.Common.csproj -c Release -o artifacts/package/release --no-build

# Add local source now that we have some packages
dotnet nuget add source "$PWD/artifacts/package/release" --name local-packages

# Build remaining projects
dotnet build src/Cosmos.Build.Asm/Cosmos.Build.Asm.csproj -c Release
dotnet pack src/Cosmos.Build.Asm/Cosmos.Build.Asm.csproj -c Release -o artifacts/package/release --no-build

dotnet build src/Cosmos.Build.GCC/Cosmos.Build.GCC.csproj -c Release
dotnet pack src/Cosmos.Build.GCC/Cosmos.Build.GCC.csproj -c Release -o artifacts/package/release --no-build

dotnet build src/Cosmos.Build.Ilc/Cosmos.Build.Ilc.csproj -c Release
dotnet pack src/Cosmos.Build.Ilc/Cosmos.Build.Ilc.csproj -c Release -o artifacts/package/release --no-build

dotnet build src/Cosmos.Build.Patcher/Cosmos.Build.Patcher.csproj -c Release
dotnet pack src/Cosmos.Build.Patcher/Cosmos.Build.Patcher.csproj -c Release -o artifacts/package/release --no-build

dotnet build src/Cosmos.Patcher/Cosmos.Patcher.csproj -c Release
dotnet pack src/Cosmos.Patcher/Cosmos.Patcher.csproj -c Release -o artifacts/package/release --no-build

# Build kernel projects with architecture-specific defines
echo "Building kernel projects with $ARCH_DEFINE..."
dotnet build src/Cosmos.Kernel.Core/Cosmos.Kernel.Core.csproj -c Release -r $RUNTIME_ID -p:DefineConstants="$ARCH_DEFINE"
dotnet pack src/Cosmos.Kernel.Core/Cosmos.Kernel.Core.csproj -c Release -o artifacts/package/release --no-build

dotnet build src/Cosmos.Kernel.Boot.Limine/Cosmos.Kernel.Boot.Limine.csproj -c Release -r $RUNTIME_ID -p:DefineConstants="$ARCH_DEFINE"
dotnet pack src/Cosmos.Kernel.Boot.Limine/Cosmos.Kernel.Boot.Limine.csproj -c Release -o artifacts/package/release --no-build

dotnet build src/Cosmos.Kernel.HAL/Cosmos.Kernel.HAL.csproj -c Release -r $RUNTIME_ID -p:DefineConstants="$ARCH_DEFINE"
dotnet pack src/Cosmos.Kernel.HAL/Cosmos.Kernel.HAL.csproj -c Release -o artifacts/package/release --no-build

dotnet build src/Cosmos.Kernel.Runtime/Cosmos.Kernel.Runtime.csproj -c Release -r $RUNTIME_ID -p:DefineConstants="$ARCH_DEFINE"
dotnet pack src/Cosmos.Kernel.Runtime/Cosmos.Kernel.Runtime.csproj -c Release -o artifacts/package/release --no-build

dotnet build src/Cosmos.Kernel.Plugs/Cosmos.Kernel.Plugs.csproj -c Release -r $RUNTIME_ID -p:DefineConstants="$ARCH_DEFINE"
dotnet pack src/Cosmos.Kernel.Plugs/Cosmos.Kernel.Plugs.csproj -c Release -o artifacts/package/release --no-build

dotnet build src/Cosmos.Kernel.System/Cosmos.Kernel.System.csproj -c Release -r $RUNTIME_ID -p:DefineConstants="$ARCH_DEFINE"
dotnet pack src/Cosmos.Kernel.System/Cosmos.Kernel.System.csproj -c Release -o artifacts/package/release --no-build

dotnet build src/Cosmos.Kernel.System.Graphics/Cosmos.Kernel.System.Graphics.csproj -c Release -r $RUNTIME_ID -p:DefineConstants="$ARCH_DEFINE"
dotnet pack src/Cosmos.Kernel.System.Graphics/Cosmos.Kernel.System.Graphics.csproj -c Release -o artifacts/package/release --no-build

dotnet build src/Cosmos.Kernel/Cosmos.Kernel.csproj -c Release -r $RUNTIME_ID -p:DefineConstants="$ARCH_DEFINE"
dotnet pack src/Cosmos.Kernel/Cosmos.Kernel.csproj -c Release -o artifacts/package/release --no-build

# Build native packages
dotnet build src/Cosmos.Kernel.Native.x64/Cosmos.Kernel.Native.x64.csproj -c Release
dotnet pack src/Cosmos.Kernel.Native.x64/Cosmos.Kernel.Native.x64.csproj -c Release -o artifacts/package/release --no-build

dotnet build src/Cosmos.Kernel.Native.ARM64/Cosmos.Kernel.Native.ARM64.csproj -c Release
dotnet pack src/Cosmos.Kernel.Native.ARM64/Cosmos.Kernel.Native.ARM64.csproj -c Release -o artifacts/package/release --no-build

# Build SDK
dotnet pack src/Cosmos.Sdk/Cosmos.Sdk.csproj -c Release -o artifacts/package/release

# Restore main solution
echo "Restoring main solution..."
dotnet restore ./nativeaot-patcher.slnx

# Install global tools
echo "Installing global tools..."
dotnet tool install -g ilc --add-source artifacts/package/release || true
dotnet tool install -g Cosmos.Patcher --add-source artifacts/package/release || true

echo "=== PostCreate setup completed ==="
