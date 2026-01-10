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

# Only clear Cosmos packages from NuGet cache (not everything)
echo "Clearing Cosmos packages from NuGet cache..."
rm -rf ~/.nuget/packages/cosmos.* 2>/dev/null || true

# Remove build artifacts
rm -rf artifacts/ 2>/dev/null || true

# Remove local source if it exists (to avoid duplicates)
dotnet nuget remove source local-packages 2>/dev/null || true

# Create artifacts directory
mkdir -p artifacts/package/release

# Add local source FIRST with higher priority
# The order matters - local-packages will be checked before nuget.org
dotnet nuget add source "$PWD/artifacts/package/release" --name local-packages

# Build and pack each project individually in dependency order
# Note: GeneratePackageOnBuild=true in Directory.Build.props means build also packs
echo "Building and packing projects individually..."

# First build the base projects without dependencies
dotnet build src/Cosmos.Build.API/Cosmos.Build.API.csproj -c Release
dotnet build src/Cosmos.Build.Common/Cosmos.Build.Common.csproj -c Release

# Build remaining build tools
dotnet build src/Cosmos.Build.Asm/Cosmos.Build.Asm.csproj -c Release
dotnet build src/Cosmos.Build.GCC/Cosmos.Build.GCC.csproj -c Release
dotnet build src/Cosmos.Build.Ilc/Cosmos.Build.Ilc.csproj -c Release
dotnet build src/Cosmos.Build.Patcher/Cosmos.Build.Patcher.csproj -c Release
dotnet build src/Cosmos.Patcher/Cosmos.Patcher.csproj -c Release

# Build native packages for both architectures
echo "Building native packages..."
dotnet build src/Cosmos.Kernel.Native.X64/Cosmos.Kernel.Native.X64.csproj -c Release
dotnet build src/Cosmos.Kernel.Native.ARM64/Cosmos.Kernel.Native.ARM64.csproj -c Release

# Build kernel projects with architecture-specific defines
echo "Building kernel projects with $ARCH_DEFINE..."

# Build interfaces first (no arch dependencies)
dotnet build src/Cosmos.Kernel.HAL.Interfaces/Cosmos.Kernel.HAL.Interfaces.csproj -c Release
dotnet build src/Cosmos.Kernel.Debug/Cosmos.Kernel.Debug.csproj -c Release

dotnet build src/Cosmos.Kernel.Core/Cosmos.Kernel.Core.csproj -c Release -r $RUNTIME_ID -p:DefineConstants="$ARCH_DEFINE"
dotnet build src/Cosmos.Kernel.Boot.Limine/Cosmos.Kernel.Boot.Limine.csproj -c Release -r $RUNTIME_ID -p:DefineConstants="$ARCH_DEFINE"

dotnet build src/Cosmos.Kernel.HAL/Cosmos.Kernel.HAL.csproj -c Release -r $RUNTIME_ID -p:DefineConstants="$ARCH_DEFINE" -p:CosmosArch=$ARCH

# Build architecture-specific HAL packages
dotnet build src/Cosmos.Kernel.HAL.X64/Cosmos.Kernel.HAL.X64.csproj -c Release
dotnet build src/Cosmos.Kernel.HAL.ARM64/Cosmos.Kernel.HAL.ARM64.csproj -c Release

dotnet build src/Cosmos.Kernel.Plugs/Cosmos.Kernel.Plugs.csproj -c Release -r $RUNTIME_ID -p:DefineConstants="$ARCH_DEFINE"
dotnet build src/Cosmos.Kernel.System/Cosmos.Kernel.System.csproj -c Release -r $RUNTIME_ID -p:DefineConstants="$ARCH_DEFINE"
dotnet build src/Cosmos.Kernel.Graphics/Cosmos.Kernel.Graphics.csproj -c Release -r $RUNTIME_ID -p:DefineConstants="$ARCH_DEFINE"
dotnet build src/Cosmos.Kernel/Cosmos.Kernel.csproj -c Release -r $RUNTIME_ID -p:DefineConstants="$ARCH_DEFINE" -p:CosmosArch=$ARCH

# Build SDK - must be built fresh to include updated Sdk.props
dotnet build src/Cosmos.Sdk/Cosmos.Sdk.csproj -c Release

# Build Templates (includes dotnet new project template)
dotnet build src/Cosmos.Build.Templates/Cosmos.Build.Templates.csproj -c Release

# Clear Cosmos packages again to ensure fresh restore from local source
echo "Clearing Cosmos packages to force fresh restore..."
rm -rf ~/.nuget/packages/cosmos.* 2>/dev/null || true

# Restore main solution - will now use local packages since they're the only source for cosmos.*
echo "Restoring main solution..."
dotnet restore ./nativeaot-patcher.slnx

# Install global tools
echo "Installing global tools..."
dotnet tool install -g ilc --add-source artifacts/package/release || dotnet tool update -g ilc --add-source artifacts/package/release || true
dotnet tool install -g Cosmos.Patcher --add-source artifacts/package/release || dotnet tool update -g Cosmos.Patcher --add-source artifacts/package/release || true

echo "=== PostCreate setup completed ==="
