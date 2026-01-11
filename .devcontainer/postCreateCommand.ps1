#!/usr/bin/env pwsh
# PowerShell script to build Cosmos packages (Windows equivalent of postCreateCommand.sh)

param(
    [string]$Arch = "x64"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Starting package setup for architecture: $Arch ===" -ForegroundColor Cyan

# Set architecture-specific defines
if ($Arch -eq "arm64") {
    $ArchDefine = "ARCH_ARM64"
    $RuntimeId = "linux-arm64"
} else {
    $ArchDefine = "ARCH_X64"
    $RuntimeId = "linux-x64"
}

Write-Host "Using define: $ArchDefine"
Write-Host "Using runtime: $RuntimeId"

# Clear Cosmos packages from NuGet cache
Write-Host "Clearing Cosmos packages from NuGet cache..."
Remove-Item -Path "$env:USERPROFILE\.nuget\packages\cosmos.*" -Recurse -Force -ErrorAction SilentlyContinue

# Remove build artifacts
Remove-Item -Path "artifacts" -Recurse -Force -ErrorAction SilentlyContinue

# Remove local source if it exists
dotnet nuget remove source local-packages 2>$null

# Create artifacts directory
New-Item -ItemType Directory -Force -Path "artifacts/package/release" | Out-Null

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

# Kernel projects
Write-Host "Building kernel projects with $ArchDefine..." -ForegroundColor Cyan
dotnet build src/Cosmos.Kernel.HAL.Interfaces/Cosmos.Kernel.HAL.Interfaces.csproj -c Release
dotnet build src/Cosmos.Kernel.Debug/Cosmos.Kernel.Debug.csproj -c Release
dotnet build src/Cosmos.Kernel.Core/Cosmos.Kernel.Core.csproj -c Release -r $RuntimeId -p:DefineConstants="$ArchDefine"
dotnet build src/Cosmos.Kernel.Boot.Limine/Cosmos.Kernel.Boot.Limine.csproj -c Release -r $RuntimeId -p:DefineConstants="$ArchDefine"
dotnet build src/Cosmos.Kernel.HAL/Cosmos.Kernel.HAL.csproj -c Release -r $RuntimeId -p:DefineConstants="$ArchDefine" -p:CosmosArch=$Arch
dotnet build src/Cosmos.Kernel.HAL.X64/Cosmos.Kernel.HAL.X64.csproj -c Release
dotnet build src/Cosmos.Kernel.HAL.ARM64/Cosmos.Kernel.HAL.ARM64.csproj -c Release
dotnet build src/Cosmos.Kernel.Plugs/Cosmos.Kernel.Plugs.csproj -c Release -r $RuntimeId -p:DefineConstants="$ArchDefine"
dotnet build src/Cosmos.Kernel.System/Cosmos.Kernel.System.csproj -c Release -r $RuntimeId -p:DefineConstants="$ArchDefine"
dotnet build src/Cosmos.Kernel.Graphics/Cosmos.Kernel.Graphics.csproj -c Release -r $RuntimeId -p:DefineConstants="$ArchDefine"
dotnet build src/Cosmos.Kernel/Cosmos.Kernel.csproj -c Release -r $RuntimeId -p:DefineConstants="$ArchDefine" -p:CosmosArch=$Arch

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

Write-Host "=== Package setup completed ===" -ForegroundColor Green
