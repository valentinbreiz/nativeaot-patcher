# Build all projects in Release configuration
$projects = @(
    'src\Cosmos.Build.API\Cosmos.Build.API.csproj',
    'src\Cosmos.Build.Patcher\Cosmos.Build.Patcher.csproj',
    'src\Cosmos.Patcher\Cosmos.Patcher.csproj',
    'src\Cosmos.Build.Common\Cosmos.Build.Common.csproj',
    'src\Cosmos.Build.Ilc\Cosmos.Build.Ilc.csproj',
    'src\Cosmos.Build.Asm\Cosmos.Build.Asm.csproj',
    'src\Cosmos.Analyzer.Patcher.Package\Cosmos.Analyzer.Patcher.Package.csproj'
    'src\Cosmos.Sdk\Cosmos.Sdk.csproj'
)
foreach ($proj in $projects) {
    dotnet build "$PSScriptRoot\$proj" -c Release
}

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

# Restore project dependencies
dotnet restore

# Uninstall old global Cosmos.Patcher tool if it exists
if (dotnet tool list -g | Select-String '^Cosmos\.Patcher') {
    Write-Host "➖ Uninstalling existing global Cosmos.Patcher tool"
    dotnet tool uninstall -g Cosmos.Patcher
}

# Install the latest global Cosmos.Patcher tool
Write-Host "➕ Installing global Cosmos.Patcher tool"
dotnet tool install -g Cosmos.Patcher --version 1.0.0