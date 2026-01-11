#!/bin/pwsh
# Build Packages in Release configuration
dotnet build ./Packages.slnx -c Release

# Configure the local NuGet source
$sourceName = 'local-packages'
$packagePath = Join-Path $PSScriptRoot 'artifacts\package\release'

# Remove existing source if it already exists to avoid duplication
$existing = dotnet nuget list source | Where-Object { $_ -match $sourceName }
if ($existing) {
    dotnet nuget remove source $sourceName
}

# Add the local source
dotnet nuget add source $packagePath --name $sourceName

# Clear all NuGet caches (HTTP, global packages, temp, and plugins) in one go
dotnet nuget locals all --clear

# Restore Main Solution
dotnet restore ./Packages.slnx

# Uninstall old global Cosmos.Patcher tool if it exists
if (dotnet tool list -g | Select-String '^Cosmos\.Patcher') {
    Write-Host "➖ Uninstalling existing global Cosmos.Patcher tool"
    dotnet tool uninstall -g Cosmos.Patcher
}

# Install the latest global Cosmos.Patcher tool
Write-Host "➕ Installing global Cosmos.Patcher tool"
dotnet tool install -g Cosmos.Patcher --version 3.0.0
