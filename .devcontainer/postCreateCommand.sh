#!/usr/bin/env bash

set -e

# Pack all projects
dotnet build ./src/Cosmos.API/Cosmos.API.csproj --configuration Release
dotnet build ./src/Cosmos.Patcher.Build/Cosmos.Patcher.Build.csproj --configuration Release
dotnet build ./src/Cosmos.Patcher/Cosmos.Patcher.csproj --configuration Release
dotnet build ./src/Cosmos.Common.Build/Cosmos.Common.Build.csproj --configuration Release
dotnet build ./src/Cosmos.Ilc.Build/Cosmos.Ilc.Build.csproj --configuration Release
dotnet build ./src/Cosmos.Asm.Build/Cosmos.Asm.Build.csproj --configuration Release
dotnet build ./src/Cosmos.Patcher.Analyzer.Package/Cosmos.Patcher.Analyzer.Package.csproj --configuration Release

# Add output folder as a local NuGet source
dotnet nuget add source "$PWD/artifacts/package/release" --name local-packages

# Clear all NuGet locals cache
dotnet nuget locals all --clear

dotnet restore

dotnet tool install -g ilc
dotnet tool install -g Cosmos.Patcher