#!/usr/bin/env pwsh
# PowerShell script to build Cosmos packages (Windows equivalent of postCreateCommand.sh)

$ErrorActionPreference = "Stop"

Write-Host "=== Starting postCreate setup (multi-arch) ===" -ForegroundColor Cyan

# Clear Cosmos packages from NuGet cache
Write-Host "Clearing Cosmos packages from NuGet cache..."
Remove-Item -Path "$env:USERPROFILE\.nuget\packages\cosmos.*" -Recurse -Force -ErrorAction SilentlyContinue

# Remove all build artifacts for clean build
Write-Host "Cleaning all build artifacts..."
Remove-Item -Path "artifacts" -Recurse -Force -ErrorAction SilentlyContinue
# Also clean obj folders in src (in case they exist outside artifacts)
Get-ChildItem -Path "src" -Directory -Recurse -Filter "obj" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# Remove local source if it exists
dotnet nuget remove source local-packages 2>$null

# Create artifacts directories
New-Item -ItemType Directory -Force -Path "artifacts/package/release" | Out-Null
New-Item -ItemType Directory -Force -Path "artifacts/multiarch" | Out-Null

# Add local source
dotnet nuget add source "$PWD/artifacts/package/release" --name local-packages

# Build and pack each project in dependency order
Write-Host "Building and packing base projects..." -ForegroundColor Cyan
dotnet build src/Cosmos.Build.API/Cosmos.Build.API.csproj -c Release --no-incremental
dotnet build src/Cosmos.Build.Common/Cosmos.Build.Common.csproj -c Release --no-incremental

Write-Host "Building and packing build tools..." -ForegroundColor Cyan
dotnet build src/Cosmos.Build.Asm/Cosmos.Build.Asm.csproj -c Release --no-incremental
dotnet build src/Cosmos.Build.GCC/Cosmos.Build.GCC.csproj -c Release --no-incremental
dotnet build src/Cosmos.Build.Ilc/Cosmos.Build.Ilc.csproj -c Release --no-incremental
dotnet build src/Cosmos.Build.Patcher/Cosmos.Build.Patcher.csproj -c Release --no-incremental
dotnet build src/Cosmos.Patcher/Cosmos.Patcher.csproj -c Release --no-incremental
dotnet build src/Cosmos.Tools/Cosmos.Tools.csproj -c Release --no-incremental

# Native packages (content-only)
Write-Host "Packing native packages..." -ForegroundColor Cyan
dotnet pack src/Cosmos.Kernel.Native.X64/Cosmos.Kernel.Native.X64.csproj -c Release -o artifacts/package/release
dotnet pack src/Cosmos.Kernel.Native.ARM64/Cosmos.Kernel.Native.ARM64.csproj -c Release -o artifacts/package/release

Write-Host "Verifying native packages..." -ForegroundColor Yellow
Get-ChildItem -Path "artifacts/package/release/Cosmos.Kernel.Native.*.nupkg" | ForEach-Object { Write-Host $_.Name }

# Architecture-independent kernel packages (build first, then pack)
Write-Host "Building and packing architecture-independent kernel packages..." -ForegroundColor Cyan
dotnet build src/Cosmos.Kernel.HAL.Interfaces/Cosmos.Kernel.HAL.Interfaces.csproj -c Release -p:GeneratePackageOnBuild=false
dotnet pack src/Cosmos.Kernel.HAL.Interfaces/Cosmos.Kernel.HAL.Interfaces.csproj -c Release --no-build -o artifacts/package/release
dotnet build src/Cosmos.Kernel.Debug/Cosmos.Kernel.Debug.csproj -c Release -p:GeneratePackageOnBuild=false
dotnet pack src/Cosmos.Kernel.Debug/Cosmos.Kernel.Debug.csproj -c Release --no-build -o artifacts/package/release
dotnet build src/Cosmos.Kernel.Boot.Limine/Cosmos.Kernel.Boot.Limine.csproj -c Release -p:GeneratePackageOnBuild=false
dotnet pack src/Cosmos.Kernel.Boot.Limine/Cosmos.Kernel.Boot.Limine.csproj -c Release --no-build -o artifacts/package/release

Write-Host "Verifying arch-independent packages..." -ForegroundColor Yellow
Get-ChildItem -Path "artifacts/package/release/Cosmos.Kernel.HAL.Interfaces.*.nupkg" | ForEach-Object { Write-Host $_.Name }
Get-ChildItem -Path "artifacts/package/release/Cosmos.Kernel.Debug.*.nupkg" | ForEach-Object { Write-Host $_.Name }
Get-ChildItem -Path "artifacts/package/release/Cosmos.Kernel.Boot.*.nupkg" | ForEach-Object { Write-Host $_.Name }

# Architecture-specific HAL packages (build first, then pack)
Write-Host "Building and packing architecture-specific HAL packages..." -ForegroundColor Cyan
dotnet build src/Cosmos.Kernel.HAL.X64/Cosmos.Kernel.HAL.X64.csproj -c Release -p:GeneratePackageOnBuild=false
dotnet pack src/Cosmos.Kernel.HAL.X64/Cosmos.Kernel.HAL.X64.csproj -c Release --no-build -o artifacts/package/release
dotnet build src/Cosmos.Kernel.HAL.ARM64/Cosmos.Kernel.HAL.ARM64.csproj -c Release -p:GeneratePackageOnBuild=false
dotnet pack src/Cosmos.Kernel.HAL.ARM64/Cosmos.Kernel.HAL.ARM64.csproj -c Release --no-build -o artifacts/package/release

Write-Host "Verifying HAL packages..." -ForegroundColor Yellow
Get-ChildItem -Path "artifacts/package/release/Cosmos.Kernel.HAL.X64.*.nupkg" | ForEach-Object { Write-Host $_.Name }
Get-ChildItem -Path "artifacts/package/release/Cosmos.Kernel.HAL.ARM64.*.nupkg" | ForEach-Object { Write-Host $_.Name }

# Multi-arch packages list
$MultiArchProjects = @(
    "Cosmos.Kernel.Core",
    "Cosmos.Kernel.HAL",
    "Cosmos.Kernel.Graphics",
    "Cosmos.Kernel.System",
    "Cosmos.Kernel.Plugs",
    "Cosmos.Kernel"
)

# Build all multi-arch packages for x64
Write-Host "Building all multi-arch packages for x64..." -ForegroundColor Cyan
dotnet build src/Cosmos.Kernel/Cosmos.Kernel.csproj -c Release -r linux-x64 -p:DefineConstants="ARCH_X64" --no-incremental

# Stage x64 builds
Write-Host "Staging x64 builds..." -ForegroundColor Cyan
foreach ($proj in $MultiArchProjects) {
    New-Item -ItemType Directory -Force -Path "artifacts/multiarch/$proj/x64" | Out-Null
    $sourcePath1 = "artifacts/bin/$proj/release_linux-x64/$proj.dll"
    $sourcePath2 = "artifacts/bin/$proj/release/$proj.dll"
    $destPath = "artifacts/multiarch/$proj/x64/"

    if (Test-Path $sourcePath1) {
        Copy-Item $sourcePath1 $destPath -ErrorAction SilentlyContinue
    } elseif (Test-Path $sourcePath2) {
        Copy-Item $sourcePath2 $destPath -ErrorAction SilentlyContinue
    }
}

# Build all multi-arch packages for arm64
Write-Host "Building all multi-arch packages for arm64..." -ForegroundColor Cyan
dotnet build src/Cosmos.Kernel/Cosmos.Kernel.csproj -c Release -r linux-arm64 -p:DefineConstants="ARCH_ARM64" --no-incremental

# Stage arm64 builds
Write-Host "Staging arm64 builds..." -ForegroundColor Cyan
foreach ($proj in $MultiArchProjects) {
    New-Item -ItemType Directory -Force -Path "artifacts/multiarch/$proj/arm64" | Out-Null
    $sourcePath1 = "artifacts/bin/$proj/release_linux-arm64/$proj.dll"
    $sourcePath2 = "artifacts/bin/$proj/release/$proj.dll"
    $destPath = "artifacts/multiarch/$proj/arm64/"

    if (Test-Path $sourcePath1) {
        Copy-Item $sourcePath1 $destPath -ErrorAction SilentlyContinue
    } elseif (Test-Path $sourcePath2) {
        Copy-Item $sourcePath2 $destPath -ErrorAction SilentlyContinue
    }
}

# No ref assembly needed - NuGet will select the correct RID-specific assembly
Write-Host "Multi-arch staging complete (no ref assembly - NuGet selects by RID)" -ForegroundColor Cyan

# Pack multi-arch packages (these use pre-staged DLLs via Directory.MultiArch.targets)
Write-Host "Packing multi-arch packages..." -ForegroundColor Cyan
foreach ($proj in $MultiArchProjects) {
    Write-Host "Packing $proj..." -ForegroundColor Yellow
    Get-ChildItem -Path "artifacts/obj/$proj" -Filter "*.nuspec" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force
    # Only delete exact package name (not prefix matches like Cosmos.Kernel.* which would delete Native, HAL, etc)
    Remove-Item -Path "artifacts/package/release/$proj.3.0.*.nupkg" -Force -ErrorAction SilentlyContinue
    dotnet pack "src/$proj/$proj.csproj" -c Release -o artifacts/package/release -p:NoBuild=true
}

# SDK and Templates
Write-Host "Building SDK and Templates..." -ForegroundColor Cyan
dotnet build src/Cosmos.Sdk/Cosmos.Sdk.csproj -c Release --no-incremental
dotnet build src/Cosmos.Build.Templates/Cosmos.Build.Templates.csproj -c Release --no-incremental

# List all created packages
Write-Host "=== Created packages ===" -ForegroundColor Green
Get-ChildItem -Path "artifacts/package/release/*.nupkg" | ForEach-Object { Write-Host $_.Name }

# Clear Cosmos packages again to force fresh restore
Write-Host "Clearing Cosmos packages to force fresh restore..." -ForegroundColor Cyan
Remove-Item -Path "$env:USERPROFILE\.nuget\packages\cosmos.*" -Recurse -Force -ErrorAction SilentlyContinue

# Restore main solution
Write-Host "Restoring main solution..." -ForegroundColor Cyan
dotnet restore ./nativeaot-patcher.slnx

# Install global tools
Write-Host "Installing global tools..." -ForegroundColor Cyan
dotnet tool install -g ilc --add-source artifacts/package/release 2>$null
dotnet tool update -g ilc --add-source artifacts/package/release 2>$null
dotnet tool install -g Cosmos.Patcher --add-source artifacts/package/release 2>$null
dotnet tool update -g Cosmos.Patcher --add-source artifacts/package/release 2>$null
dotnet tool install -g Cosmos.Tools --add-source artifacts/package/release 2>$null
dotnet tool update -g Cosmos.Tools --add-source artifacts/package/release 2>$null

Write-Host "=== PostCreate setup completed (multi-arch) ===" -ForegroundColor Green
