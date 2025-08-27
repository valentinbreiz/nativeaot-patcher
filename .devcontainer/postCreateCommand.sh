#!/usr/bin/env bash

set -e

# Pack all projects
dotnet build ./Packages.slnx --configuration Release

# Add output folder as a local NuGet source if it doesn't already exist
if ! dotnet nuget list source | grep -q "local-packages"; then
  dotnet nuget add source "$PWD/artifacts/package/release" --name local-packages
else
  echo "[DEBUG] NuGet source 'local-packages' already exists."
fi

# Clear all NuGet locals cache
dotnet nuget locals all --clear

dotnet restore ./nativeaot-patcher.slnx

dotnet tool install -g ilc
dotnet tool install -g Cosmos.Patcher
