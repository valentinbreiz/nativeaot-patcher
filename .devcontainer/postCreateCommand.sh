#!/usr/bin/env bash

set -e

# Pack all projects
dotnet build ./src/Cosmos.Build.API/Cosmos.Build.API.csproj --configuration Release
dotnet build ./src/Cosmos.Build.Patcher/Cosmos.Build.Patcher.csproj --configuration Release
dotnet build ./src/Cosmos.Patcher/Cosmos.Patcher.csproj --configuration Release
dotnet build ./src/Cosmos.Patcher/Cosmos.Patcher.csproj --configuration Release
dotnet build ./src/Cosmos.Build.Common/Cosmos.Build.Common.csproj --configuration Release
dotnet build ./src/Cosmos.Build.Ilc/Cosmos.Build.Ilc.csproj --configuration Release
dotnet build ./src/Cosmos.Build.Asm/Cosmos.Build.Asm.csproj --configuration Release
dotnet build ./src/Cosmos.Build.Analyzer.Patcher.Package/Cosmos.Build.Analyzer.Patcher.Package.csproj --configuration Release
dotnet build ./src/Cosmos.Kernel.Native.x86/Cosmos.Kernel.Native.x86.csproj --configuration Release

# Add output folder as a local NuGet source if it doesn't already exist
if ! dotnet nuget list source | grep -q "local-packages"; then
  dotnet nuget add source "$PWD/artifacts/package/release" --name local-packages
else
  echo "[DEBUG] NuGet source 'local-packages' already exists."
fi

# Clear all NuGet locals cache
dotnet nuget locals all --clear

dotnet restore

dotnet tool install -g ilc
dotnet tool install -g Cosmos.Patcher
