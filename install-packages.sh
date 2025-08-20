#!/usr/bin/env bash
set -euo pipefail

# Get the script's directory (equivalent to $PSScriptRoot)
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Build all projects in Release configuration
projects=(
  "src/Cosmos.Build.API/Cosmos.Build.API.csproj"
  "src/Cosmos.Build.Patcher/Cosmos.Build.Patcher.csproj"
  "src/Cosmos.Patcher/Cosmos.Patcher.csproj"
  "src/Cosmos.Build.Common/Cosmos.Build.Common.csproj"
  "src/Cosmos.Build.Ilc/Cosmos.Build.Ilc.csproj"
  "src/Cosmos.Build.Asm/Cosmos.Build.Asm.csproj"
  "src/Cosmos.Build.Analyzer.Patcher.Package/Cosmos.Build.Analyzer.Patcher.Package.csproj"
  "src/Cosmos.Build.GCC/Cosmos.Build.GCC.csproj"
  "src/Cosmos.Sdk/Cosmos.Sdk.csproj"
  "src/Cosmos.Kernel.Native.x86/Cosmos.Kernel.Native.x86.csproj"
)

for proj in "${projects[@]}"; do
  echo "----- $SCRIPT_DIR/$proj -----"
  dotnet build "$SCRIPT_DIR/$proj" -c Release
done

# Configure the local NuGet source
sourceName="cosmos-local-packages"
packagePath="$SCRIPT_DIR/artifacts/package/release"

# Remove existing source if it already exists
if dotnet nuget list source | grep -q "$sourceName"; then
  echo "removing current local nuget repo"
  dotnet nuget remove source "$sourceName"
fi

echo "adding nuget source"
# Add the local source
dotnet nuget add source "$packagePath" --name "$sourceName"

echo "clearing cache"
# Clear all NuGet caches
dotnet nuget locals all --clear

# Restore project dependencies
#dotnet restore

# Uninstall old global Cosmos.Patcher tool if it exists
if dotnet tool list -g | grep -q "^Cosmos\.Patcher"; then
  echo "➖ Uninstalling existing global Cosmos.Patcher tool"
  dotnet tool uninstall -g Cosmos.Patcher
fi

# Install the latest global Cosmos.Patcher tool
echo "➕ Installing global Cosmos.Patcher tool"
dotnet tool install -g Cosmos.Patcher --version 1.0.0
