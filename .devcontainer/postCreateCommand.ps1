#!/usr/bin/env pwsh
# PowerShell script to build Cosmos packages (Windows equivalent of postCreateCommand.sh)
# Builds both x64 and arm64 for multi-arch NuGet packages

$ErrorActionPreference = "Stop"

Write-Host "=== Starting postCreate setup (multi-arch) ===" -ForegroundColor Cyan

# Clear Cosmos packages from NuGet cache
Write-Host "Clearing Cosmos packages from NuGet cache..."
Remove-Item -Path "$env:USERPROFILE\.nuget\packages\cosmos.*" -Recurse -Force -ErrorAction SilentlyContinue

# Remove build artifacts
Remove-Item -Path "artifacts" -Recurse -Force -ErrorAction SilentlyContinue

# Remove local source if it exists
dotnet nuget remove source local-packages 2>$null

# Create artifacts directories
New-Item -ItemType Directory -Force -Path "artifacts/package/release" | Out-Null
New-Item -ItemType Directory -Force -Path "artifacts/multiarch" | Out-Null

# Add local source
dotnet nuget add source "$PWD/artifacts/package/release" --name local-packages

# Build and pack each project in dependency order
Write-Host "Building and packing projects..." -ForegroundColor Cyan

# Base projects
dotnet build src/Cosmos.Build.API/Cosmos.Build.API.csproj -c Release
dotnet build src/Cosmos.Build.Common/Cosmos.Build.Common.csproj -c Release

# Build tools
dotnet build src/Cosmos.Build.Asm/Cosmos.Build.Asm.csproj -c Release
dotnet build src/Cosmos.Build.GCC/Cosmos.Build.GCC.csproj -c Release
dotnet build src/Cosmos.Build.Ilc/Cosmos.Build.Ilc.csproj -c Release
dotnet build src/Cosmos.Build.Patcher/Cosmos.Build.Patcher.csproj -c Release
dotnet build src/Cosmos.Patcher/Cosmos.Patcher.csproj -c Release
dotnet build src/Cosmos.Tools/Cosmos.Tools.csproj -c Release

# Native packages
Write-Host "Building native packages..." -ForegroundColor Cyan
dotnet build src/Cosmos.Kernel.Native.X64/Cosmos.Kernel.Native.X64.csproj -c Release
dotnet build src/Cosmos.Kernel.Native.ARM64/Cosmos.Kernel.Native.ARM64.csproj -c Release

# Architecture-independent kernel packages
Write-Host "Building architecture-independent kernel packages..." -ForegroundColor Cyan
dotnet build src/Cosmos.Kernel.HAL.Interfaces/Cosmos.Kernel.HAL.Interfaces.csproj -c Release
dotnet build src/Cosmos.Kernel.Debug/Cosmos.Kernel.Debug.csproj -c Release
dotnet build src/Cosmos.Kernel.Boot.Limine/Cosmos.Kernel.Boot.Limine.csproj -c Release

# Architecture-specific HAL packages
Write-Host "Building architecture-specific HAL packages..." -ForegroundColor Cyan
dotnet build src/Cosmos.Kernel.HAL.X64/Cosmos.Kernel.HAL.X64.csproj -c Release
dotnet build src/Cosmos.Kernel.HAL.ARM64/Cosmos.Kernel.HAL.ARM64.csproj -c Release

# Multi-arch packages list
$MultiArchProjects = @(
    "Cosmos.Kernel.Core",
    "Cosmos.Kernel.HAL",
    "Cosmos.Kernel.Graphics",
    "Cosmos.Kernel.System",
    "Cosmos.Kernel.Plugs",
    "Cosmos.Kernel"
)

# Clean all kernel project bin/obj directories
Write-Host "Cleaning all kernel project bin/obj..." -ForegroundColor Cyan
Get-ChildItem -Path "src" -Directory -Filter "Cosmos.Kernel*" | ForEach-Object {
    Remove-Item -Path "$($_.FullName)/bin" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path "$($_.FullName)/obj" -Recurse -Force -ErrorAction SilentlyContinue
}

# Build all multi-arch packages for x64
Write-Host "Building all multi-arch packages for x64..." -ForegroundColor Cyan
dotnet build src/Cosmos.Kernel/Cosmos.Kernel.csproj -c Release -r linux-x64 -p:DefineConstants="ARCH_X64"

# Stage x64 builds
Write-Host "Staging x64 builds..." -ForegroundColor Cyan
foreach ($proj in $MultiArchProjects) {
    New-Item -ItemType Directory -Force -Path "artifacts/multiarch/$proj/x64" | Out-Null
    $sourcePath1 = "src/$proj/bin/Release/net10.0/linux-x64/$proj.dll"
    $sourcePath2 = "src/$proj/bin/Release/net10.0/$proj.dll"
    $destPath = "artifacts/multiarch/$proj/x64/"

    if (Test-Path $sourcePath1) {
        Copy-Item $sourcePath1 $destPath -ErrorAction SilentlyContinue
    } elseif (Test-Path $sourcePath2) {
        Copy-Item $sourcePath2 $destPath -ErrorAction SilentlyContinue
    }
}

# Clean bin/obj again before arm64 builds
Write-Host "Cleaning all kernel project bin/obj before arm64 build..." -ForegroundColor Cyan
Get-ChildItem -Path "src" -Directory -Filter "Cosmos.Kernel*" | ForEach-Object {
    Remove-Item -Path "$($_.FullName)/bin" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path "$($_.FullName)/obj" -Recurse -Force -ErrorAction SilentlyContinue
}

# Build all multi-arch packages for arm64
Write-Host "Building all multi-arch packages for arm64..." -ForegroundColor Cyan
dotnet build src/Cosmos.Kernel/Cosmos.Kernel.csproj -c Release -r linux-arm64 -p:DefineConstants="ARCH_ARM64"

# Stage arm64 builds
Write-Host "Staging arm64 builds..." -ForegroundColor Cyan
foreach ($proj in $MultiArchProjects) {
    New-Item -ItemType Directory -Force -Path "artifacts/multiarch/$proj/arm64" | Out-Null
    $sourcePath1 = "src/$proj/bin/Release/net10.0/linux-arm64/$proj.dll"
    $sourcePath2 = "src/$proj/bin/Release/net10.0/$proj.dll"
    $destPath = "artifacts/multiarch/$proj/arm64/"

    if (Test-Path $sourcePath1) {
        Copy-Item $sourcePath1 $destPath -ErrorAction SilentlyContinue
    } elseif (Test-Path $sourcePath2) {
        Copy-Item $sourcePath2 $destPath -ErrorAction SilentlyContinue
    }
}

# Use x64 as reference assembly
Write-Host "Setting up reference assemblies..." -ForegroundColor Cyan
foreach ($proj in $MultiArchProjects) {
    New-Item -ItemType Directory -Force -Path "artifacts/multiarch/$proj/ref" | Out-Null
    $sourcePath = "artifacts/multiarch/$proj/x64/$proj.dll"
    $destPath = "artifacts/multiarch/$proj/ref/"
    if (Test-Path $sourcePath) {
        Copy-Item $sourcePath $destPath -ErrorAction SilentlyContinue
    }
}

# Pack multi-arch packages
Write-Host "Packing multi-arch packages..." -ForegroundColor Cyan
foreach ($proj in $MultiArchProjects) {
    Write-Host "Packing $proj..." -ForegroundColor Yellow
    dotnet pack "src/$proj/$proj.csproj" -c Release --no-build
}

# SDK and Templates
dotnet build src/Cosmos.Sdk/Cosmos.Sdk.csproj -c Release
dotnet build src/Cosmos.Build.Templates/Cosmos.Build.Templates.csproj -c Release

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
