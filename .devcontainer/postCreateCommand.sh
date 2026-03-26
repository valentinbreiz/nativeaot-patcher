#!/usr/bin/env bash

set -e

echo "=== Starting postCreate setup (multi-arch) ==="

# Resolve version: override env var > git tag > fallback
if [[ -n "${VERSION_OVERRIDE:-}" ]]; then
    VERSION="$VERSION_OVERRIDE"
    echo "Package version: $VERSION (from VERSION_OVERRIDE)"
else
    VERSION=$(git describe --tags --abbrev=0 2>/dev/null | sed 's/^v//' || echo "0.0.0")
    echo "Package version: $VERSION (from git tag)"
fi
VERSION_PROP="-p:VersionPrefix=$VERSION"

# Update global.json msbuild-sdks so Cosmos.Sdk NuGet resolves correctly
if command -v jq &>/dev/null; then
    jq --arg v "$VERSION" '.["msbuild-sdks"]["Cosmos.Sdk"] = $v' global.json > global.json.tmp && mv global.json.tmp global.json
else
    sed -i "s/\"Cosmos.Sdk\": \"[^\"]*\"/\"Cosmos.Sdk\": \"$VERSION\"/" global.json
fi

# Clear Cosmos packages from NuGet cache
echo "Clearing Cosmos packages from NuGet cache..."
rm -rf ~/.nuget/packages/cosmos.* 2>/dev/null || true

# Remove all build artifacts for clean build
echo "Cleaning all build artifacts..."
rm -rf artifacts/ 2>/dev/null || true
# Also clean obj folders in src (in case they exist outside artifacts)
find src -name "obj" -type d -exec rm -rf {} + 2>/dev/null || true

# Remove local source if it exists (to avoid duplicates)
dotnet nuget remove source local-packages >/dev/null 2>&1 || true

# Create artifacts directories
mkdir -p artifacts/package/release
mkdir -p artifacts/multiarch

# Add local source
dotnet nuget add source "$PWD/artifacts/package/release" --name local-packages

# Download Limine bootloader (bundled in Cosmos.Build.Common NuGet package)
echo "Downloading Limine bootloader..."
rm -rf artifacts/limine
git clone https://github.com/Limine-Bootloader/Limine.git --branch=v10.x-binary --depth=1 artifacts/limine
rm -rf artifacts/limine/.git

# Build and pack each project individually in dependency order
# Note: GeneratePackageOnBuild=true in Directory.Build.props means build also packs
echo "Building and packing base projects..."
dotnet build src/Cosmos.Build.API/Cosmos.Build.API.csproj -c Release --no-incremental $VERSION_PROP
dotnet build src/Cosmos.Build.Common/Cosmos.Build.Common.csproj -c Release --no-incremental $VERSION_PROP

echo "Building and packing build tools..."
dotnet build src/Cosmos.Build.Asm/Cosmos.Build.Asm.csproj -c Release --no-incremental $VERSION_PROP
dotnet build src/Cosmos.Build.GCC/Cosmos.Build.GCC.csproj -c Release --no-incremental $VERSION_PROP
dotnet build src/Cosmos.Build.Ilc/Cosmos.Build.Ilc.csproj -c Release --no-incremental $VERSION_PROP
dotnet build src/Cosmos.Build.Patcher/Cosmos.Build.Patcher.csproj -c Release --no-incremental $VERSION_PROP
dotnet build src/Cosmos.Build.Analyzer.Patcher.Package/Cosmos.Build.Analyzer.Patcher.Package.csproj -c Release --no-incremental $VERSION_PROP
dotnet build src/Cosmos.Patcher/Cosmos.Patcher.csproj -c Release --no-incremental $VERSION_PROP
dotnet build src/Cosmos.Tools/Cosmos.Tools.csproj -c Release --no-incremental $VERSION_PROP

# Native packages (content-only)
echo "Packing native packages..."
dotnet pack src/Cosmos.Kernel.Native.X64/Cosmos.Kernel.Native.X64.csproj -c Release -o artifacts/package/release $VERSION_PROP
dotnet pack src/Cosmos.Kernel.Native.ARM64/Cosmos.Kernel.Native.ARM64.csproj -c Release -o artifacts/package/release $VERSION_PROP
dotnet pack src/Cosmos.Kernel.Native.MultiArch/Cosmos.Kernel.Native.MultiArch.csproj -c Release -o artifacts/package/release $VERSION_PROP

echo "Verifying native packages..."
ls -la artifacts/package/release/Cosmos.Kernel.Native.*.nupkg

# Architecture-independent kernel packages (build first, then pack)
echo "Building and packing architecture-independent kernel packages..."
dotnet build src/Cosmos.Kernel.HAL.Interfaces/Cosmos.Kernel.HAL.Interfaces.csproj -c Release -p:GeneratePackageOnBuild=false $VERSION_PROP
dotnet pack src/Cosmos.Kernel.HAL.Interfaces/Cosmos.Kernel.HAL.Interfaces.csproj -c Release --no-build -o artifacts/package/release $VERSION_PROP
dotnet build src/Cosmos.Kernel.Debug/Cosmos.Kernel.Debug.csproj -c Release -p:GeneratePackageOnBuild=false $VERSION_PROP
dotnet pack src/Cosmos.Kernel.Debug/Cosmos.Kernel.Debug.csproj -c Release --no-build -o artifacts/package/release $VERSION_PROP
dotnet build src/Cosmos.Kernel.Boot.Limine/Cosmos.Kernel.Boot.Limine.csproj -c Release -p:GeneratePackageOnBuild=false $VERSION_PROP
dotnet pack src/Cosmos.Kernel.Boot.Limine/Cosmos.Kernel.Boot.Limine.csproj -c Release --no-build -o artifacts/package/release $VERSION_PROP

echo "Verifying arch-independent packages..."
ls -la artifacts/package/release/Cosmos.Kernel.HAL.Interfaces.*.nupkg
ls -la artifacts/package/release/Cosmos.Kernel.Debug.*.nupkg
ls -la artifacts/package/release/Cosmos.Kernel.Boot.*.nupkg

# Architecture-specific HAL packages (build first, then pack)
echo "Building and packing architecture-specific HAL packages..."
dotnet build src/Cosmos.Kernel.HAL.X64/Cosmos.Kernel.HAL.X64.csproj -c Release -p:GeneratePackageOnBuild=false $VERSION_PROP
dotnet pack src/Cosmos.Kernel.HAL.X64/Cosmos.Kernel.HAL.X64.csproj -c Release --no-build -o artifacts/package/release $VERSION_PROP
dotnet build src/Cosmos.Kernel.HAL.ARM64/Cosmos.Kernel.HAL.ARM64.csproj -c Release -p:GeneratePackageOnBuild=false $VERSION_PROP
dotnet pack src/Cosmos.Kernel.HAL.ARM64/Cosmos.Kernel.HAL.ARM64.csproj -c Release --no-build -o artifacts/package/release $VERSION_PROP

echo "Verifying HAL packages..."
ls -la artifacts/package/release/Cosmos.Kernel.HAL.X64.*.nupkg
ls -la artifacts/package/release/Cosmos.Kernel.HAL.ARM64.*.nupkg

# Multi-arch packages (have #if ARCH_* conditional code or depend on multi-arch)
MULTIARCH_PROJECTS=(
    "Cosmos.Kernel.Core"
    "Cosmos.Kernel.HAL"
    "Cosmos.Kernel.System"
    "Cosmos.Kernel.Plugs"
    "Cosmos.Kernel"
)

# Build all multi-arch packages for x64
echo "Building all multi-arch packages for x64..."
dotnet build src/Cosmos.Kernel/Cosmos.Kernel.csproj -c Release -r linux-x64 -p:CosmosArch=x64 --no-incremental $VERSION_PROP

# Stage x64 builds
echo "Staging x64 builds..."
for proj in "${MULTIARCH_PROJECTS[@]}"; do
    mkdir -p "artifacts/multiarch/$proj/x64"
    cp "artifacts/bin/$proj/release_linux-x64/$proj.dll" "artifacts/multiarch/$proj/x64/" 2>/dev/null || \
    cp "artifacts/bin/$proj/release/$proj.dll" "artifacts/multiarch/$proj/x64/" 2>/dev/null || true
done

# Build all multi-arch packages for arm64
echo "Building all multi-arch packages for arm64..."
dotnet build src/Cosmos.Kernel/Cosmos.Kernel.csproj -c Release -r linux-arm64 -p:CosmosArch=arm64 --no-incremental $VERSION_PROP

# Stage arm64 builds
echo "Staging arm64 builds..."
for proj in "${MULTIARCH_PROJECTS[@]}"; do
    mkdir -p "artifacts/multiarch/$proj/arm64"
    cp "artifacts/bin/$proj/release_linux-arm64/$proj.dll" "artifacts/multiarch/$proj/arm64/" 2>/dev/null || \
    cp "artifacts/bin/$proj/release/$proj.dll" "artifacts/multiarch/$proj/arm64/" 2>/dev/null || true
done

# No ref assembly needed - NuGet will select the correct RID-specific assembly
echo "Multi-arch staging complete (no ref assembly - NuGet selects by RID)"

# Pack multi-arch packages (these use pre-staged DLLs via Directory.MultiArch.targets)
echo "Packing multi-arch packages..."
for proj in "${MULTIARCH_PROJECTS[@]}"; do
    echo "Packing $proj..."
    find "artifacts/obj/$proj" -name "*.nuspec" -delete 2>/dev/null || true
    # Only delete exact package name (not prefix matches like Cosmos.Kernel.* which would delete Native, HAL, etc)
    rm -f "artifacts/package/release/${proj}.${VERSION}."*.nupkg 2>/dev/null || true
    rm -f "artifacts/package/release/${proj}.${VERSION}.nupkg" 2>/dev/null || true
    dotnet pack "src/$proj/$proj.csproj" -c Release -o artifacts/package/release -p:NoBuild=true $VERSION_PROP
done

# SDK and Templates
echo "Building SDK and Templates..."
dotnet build src/Cosmos.Sdk/Cosmos.Sdk.csproj -c Release --no-incremental $VERSION_PROP
dotnet build src/Cosmos.Build.Templates/Cosmos.Build.Templates.csproj -c Release --no-incremental $VERSION_PROP

# List all created packages
echo "=== Created packages ==="
ls -la artifacts/package/release/*.nupkg

# Clear Cosmos packages again to ensure fresh restore from local source
echo "Clearing Cosmos packages to force fresh restore..."
rm -rf ~/.nuget/packages/cosmos.* 2>/dev/null || true

# Restore main solution
echo "Restoring main solution..."
dotnet restore ./nativeaot-patcher.slnx $VERSION_PROP

# Install global tools
echo "Installing global tools..."
dotnet tool uninstall -g Cosmos.Patcher 2>/dev/null || true
dotnet tool install -g Cosmos.Patcher --add-source artifacts/package/release
dotnet tool uninstall -g Cosmos.Tools 2>/dev/null || true
dotnet tool install -g Cosmos.Tools --add-source artifacts/package/release

echo "=== PostCreate setup completed (multi-arch) ==="
