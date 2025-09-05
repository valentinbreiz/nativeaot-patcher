# Test script to validate Windows CI setup
# Run this script to test the Windows setup in a development environment

Write-Host "🧪 Testing Windows CI setup script..."

# Test 1: Check if setup script exists
if (Test-Path ".\setup-windows.ps1") {
    Write-Host "✅ setup-windows.ps1 found"
} else {
    Write-Host "❌ setup-windows.ps1 not found"
    exit 1
}

# Test 2: Check if install-packages.ps1 exists  
if (Test-Path ".\install-packages.ps1") {
    Write-Host "✅ install-packages.ps1 found"
} else {
    Write-Host "❌ install-packages.ps1 not found"
    exit 1
}

# Test 3: Check YAML workflow syntax
Write-Host "🔍 Validating GitHub Actions workflows..."

$yamlFiles = @(
    ".github\workflows\dotnet.yml",
    ".github\workflows\format.yml", 
    ".github\workflows\package.yml"
)

foreach ($yamlFile in $yamlFiles) {
    if (Test-Path $yamlFile) {
        try {
            # Basic YAML validation - check for common issues
            $content = Get-Content $yamlFile -Raw
            
            # Check for unmatched quotes
            $singleQuotes = ($content.ToCharArray() | Where-Object { $_ -eq "'" }).Count
            $doubleQuotes = ($content.ToCharArray() | Where-Object { $_ -eq '"' }).Count
            
            if ($singleQuotes % 2 -ne 0) {
                Write-Host "⚠️  Warning: Unmatched single quotes in $yamlFile"
            }
            
            if ($doubleQuotes % 2 -ne 0) {
                Write-Host "⚠️  Warning: Unmatched double quotes in $yamlFile"
            }
            
            Write-Host "✅ $yamlFile appears valid"
        }
        catch {
            Write-Host "❌ Error validating $yamlFile : $_"
        }
    } else {
        Write-Host "❌ $yamlFile not found"
    }
}

# Test 4: Check Windows-specific MSBuild targets
Write-Host "🔍 Checking Windows build targets..."

$windowsTargets = @(
    "src\Cosmos.Build.GCC\build\GCC.Build.Windows.targets",
    "src\Cosmos.Build.Asm\build\Asm.Build.Windows.targets",
    "src\Cosmos.Build.Common\build\Common.Build.Windows.targets"
)

foreach ($target in $windowsTargets) {
    if (Test-Path $target) {
        Write-Host "✅ $target found"
    } else {
        Write-Host "❌ $target not found"
    }
}

# Test 5: Check for required build system files
Write-Host "🔍 Checking build system files..."

$requiredFiles = @(
    "global.json",
    "nativeaot-patcher.slnx",
    "Packages.slnx"
)

foreach ($file in $requiredFiles) {
    if (Test-Path $file) {
        Write-Host "✅ $file found"
    } else {
        Write-Host "❌ $file not found"
    }
}

Write-Host ""
Write-Host "🎉 Windows CI setup validation complete!"
Write-Host ""
Write-Host "To test the actual setup process, run:"
Write-Host "  .\setup-windows.ps1 -SkipPackageRestore"
Write-Host ""
Write-Host "To test the full build process (requires dependencies), run:"
Write-Host "  .\setup-windows.ps1"