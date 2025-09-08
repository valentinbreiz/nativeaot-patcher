#!/usr/bin/env bash

set -e

echo "=== Starting postCreate setup ==="

# Pack all projects
echo "Building packages..."
dotnet build ./Packages.slnx --configuration Release

# Add output folder as a local NuGet source if it doesn't already exist
if ! dotnet nuget list source | grep -q "local-packages"; then
  dotnet nuget add source "$PWD/artifacts/package/release" --name local-packages
else
  echo "[DEBUG] NuGet source 'local-packages' already exists."
fi

# Clear all NuGet locals cache
echo "Clearing NuGet cache..."
dotnet nuget locals all --clear

# Restore main solution
echo "Restoring main solution..."
dotnet restore ./nativeaot-patcher.slnx

# Install global tools
echo "Installing global tools..."
dotnet tool install -g ilc || true
dotnet tool install -g Cosmos.Patcher || true

echo "=== PostCreate setup completed ==="
