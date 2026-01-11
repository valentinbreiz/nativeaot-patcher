#!/usr/bin/env bash

set -e

echo "=== Starting postCreate setup (multi-arch) ==="

# Only clear Cosmos packages from NuGet cache (not everything)
echo "Clearing Cosmos packages from NuGet cache..."
rm -rf ~/.nuget/packages/cosmos.* 2>/dev/null || true

# Remove build artifacts
rm -rf artifacts/ 2>/dev/null || true

# Remove local source if it exists (to avoid duplicates)
dotnet nuget remove source local-packages 2>/dev/null || true

# Create artifacts directories
mkdir -p artifacts/package/release
mkdir -p artifacts/multiarch

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
dotnet build src/Cosmos.Tools/Cosmos.Tools.csproj -c Release

# Build native packages for both architectures
echo "Building native packages..."
dotnet build src/Cosmos.Kernel.Native.X64/Cosmos.Kernel.Native.X64.csproj -c Release
dotnet build src/Cosmos.Kernel.Native.ARM64/Cosmos.Kernel.Native.ARM64.csproj -c Release

# Build architecture-independent kernel packages
echo "Building architecture-independent kernel packages..."
dotnet build src/Cosmos.Kernel.HAL.Interfaces/Cosmos.Kernel.HAL.Interfaces.csproj -c Release
dotnet build src/Cosmos.Kernel.Debug/Cosmos.Kernel.Debug.csproj -c Release
dotnet build src/Cosmos.Kernel.Boot.Limine/Cosmos.Kernel.Boot.Limine.csproj -c Release

# Build architecture-specific HAL packages (needed before multi-arch packages like Plugs)
echo "Building architecture-specific HAL packages..."
dotnet build src/Cosmos.Kernel.HAL.X64/Cosmos.Kernel.HAL.X64.csproj -c Release
dotnet build src/Cosmos.Kernel.HAL.ARM64/Cosmos.Kernel.HAL.ARM64.csproj -c Release

# Multi-arch packages (have #if ARCH_* conditional code or depend on multi-arch)
# Build order matters - dependencies first
MULTIARCH_PROJECTS=(
    "Cosmos.Kernel.Core"
    "Cosmos.Kernel.HAL"
    "Cosmos.Kernel.Graphics"
    "Cosmos.Kernel.System"
    "Cosmos.Kernel.Plugs"
    "Cosmos.Kernel"
)

# Clean all bin/obj directories for clean multi-arch builds
echo "Cleaning all kernel project bin/obj..."
for dir in src/Cosmos.Kernel*/; do
    rm -rf "${dir}bin" "${dir}obj" 2>/dev/null || true
done

# Build all multi-arch packages for x64 using top-level package (pulls in all deps)
echo "Building all multi-arch packages for x64..."
dotnet build src/Cosmos.Kernel/Cosmos.Kernel.csproj -c Release -r linux-x64 -p:DefineConstants="ARCH_X64"

# Stage x64 builds
echo "Staging x64 builds..."
for proj in "${MULTIARCH_PROJECTS[@]}"; do
    mkdir -p "artifacts/multiarch/$proj/x64"
    # Try both possible output paths
    cp "src/$proj/bin/Release/net10.0/linux-x64/$proj.dll" "artifacts/multiarch/$proj/x64/" 2>/dev/null || \
    cp "src/$proj/bin/Release/net10.0/$proj.dll" "artifacts/multiarch/$proj/x64/" 2>/dev/null || true
done

# Clean bin/obj again before arm64 builds
echo "Cleaning all kernel project bin/obj before arm64 build..."
for dir in src/Cosmos.Kernel*/; do
    rm -rf "${dir}bin" "${dir}obj" 2>/dev/null || true
done

# Build all multi-arch packages for arm64 using top-level package
echo "Building all multi-arch packages for arm64..."
dotnet build src/Cosmos.Kernel/Cosmos.Kernel.csproj -c Release -r linux-arm64 -p:DefineConstants="ARCH_ARM64"

# Stage arm64 builds
echo "Staging arm64 builds..."
for proj in "${MULTIARCH_PROJECTS[@]}"; do
    mkdir -p "artifacts/multiarch/$proj/arm64"
    # Try both possible output paths
    cp "src/$proj/bin/Release/net10.0/linux-arm64/$proj.dll" "artifacts/multiarch/$proj/arm64/" 2>/dev/null || \
    cp "src/$proj/bin/Release/net10.0/$proj.dll" "artifacts/multiarch/$proj/arm64/" 2>/dev/null || true
done

# Use x64 as reference assembly
echo "Setting up reference assemblies..."
for proj in "${MULTIARCH_PROJECTS[@]}"; do
    mkdir -p "artifacts/multiarch/$proj/ref"
    cp "artifacts/multiarch/$proj/x64/$proj.dll" "artifacts/multiarch/$proj/ref/" 2>/dev/null || true
done

# Pack multi-arch packages
echo "Packing multi-arch packages..."
for proj in "${MULTIARCH_PROJECTS[@]}"; do
    echo "Packing $proj..."
    dotnet pack "src/$proj/$proj.csproj" -c Release --no-build
done

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
dotnet tool install -g Cosmos.Tools --add-source artifacts/package/release || dotnet tool update -g Cosmos.Tools --add-source artifacts/package/release || true

echo "=== PostCreate setup completed (multi-arch) ==="
