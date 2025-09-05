# Windows setup script for nativeaot-patcher GitHub Actions CI
# This script installs required dependencies for Windows builds in CI environment

param(
    [switch]$SkipPackageRestore = $false
)

Write-Host "🪟 Setting up Windows CI environment for nativeaot-patcher..."

# Function to check if a command exists
function Test-Command {
    param($CommandName)
    try {
        if (Get-Command $CommandName -ErrorAction SilentlyContinue) {
            return $true
        }
    }
    catch {
        return $false
    }
    return $false
}

# Function to safely add to PATH in GitHub Actions
function Add-ToGitHubPath {
    param($Path)
    if (Test-Path $Path) {
        $env:PATH = "$Path;$env:PATH"
        echo $Path | Out-File -FilePath $env:GITHUB_PATH -Encoding utf8 -Append
        Write-Host "✅ Added to PATH: $Path"
    } else {
        Write-Warning "⚠️ Path does not exist: $Path"
    }
}

# Check for required tools after setup
Write-Host "🔍 Checking for required tools after setup..."

# Check for yasm
if (Test-Command "yasm") {
    Write-Host "✅ YASM found: $(yasm --version | Select-Object -First 1)"
} else {
    Write-Host "❌ YASM not found after installation."
    Write-Host "   Trying to find YASM in common locations..."
    $yasmPaths = @(
        "C:\ProgramData\chocolatey\lib\yasm\tools\yasm.exe",
        "C:\tools\yasm\yasm.exe"
    )
    
    foreach ($yasmPath in $yasmPaths) {
        if (Test-Path $yasmPath) {
            $yasmDir = Split-Path $yasmPath -Parent
            Add-ToGitHubPath $yasmDir
            Write-Host "✅ Found YASM at: $yasmPath"
            break
        }
    }
    
    # Final check
    if (!(Test-Command "yasm")) {
        Write-Host "❌ YASM still not found. The build may fail."
    }
}

# Check for GCC cross-compiler
if (Test-Command "x86_64-elf-gcc") {
    Write-Host "✅ x86_64-elf-gcc found: $(x86_64-elf-gcc --version | Select-Object -First 1)"
} elseif (Test-Command "x86_64-w64-mingw32-gcc") {
    Write-Host "✅ x86_64-w64-mingw32-gcc found (MinGW): $(x86_64-w64-mingw32-gcc --version | Select-Object -First 1)"
    Write-Host "   Note: Using MinGW as x86_64-elf-gcc via wrapper"
} else {
    Write-Host "❌ x86_64-elf-gcc not found. The build may fail."
    Write-Host "   Checking for standard gcc..."
    if (Test-Command "gcc") {
        Write-Host "✅ Standard gcc found: $(gcc --version | Select-Object -First 1)"
    }
}

# Check for .NET SDK
Write-Host "🔍 Checking .NET SDK..."
try {
    $dotnetVersion = dotnet --version
    Write-Host "✅ .NET SDK found: $dotnetVersion"
} catch {
    Write-Host "❌ .NET SDK not found. Please check the setup-dotnet action."
}

if (-not $SkipPackageRestore) {
    Write-Host "📦 Building and configuring packages..."
    
    # Run the existing package setup
    & "$PSScriptRoot\install-packages.ps1"
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Package setup failed with exit code: $LASTEXITCODE"
        exit $LASTEXITCODE
    }
    
    Write-Host "✅ Package setup completed successfully"
}

Write-Host "🎉 Windows CI environment setup complete!"
Write-Host ""
Write-Host "Environment Summary:"
Write-Host "  - YASM: $(if (Test-Command 'yasm') { '✅ Available' } else { '❌ Missing' })"
Write-Host "  - GCC/Cross-compiler: $(if (Test-Command 'x86_64-elf-gcc') { '✅ Available' } elseif (Test-Command 'x86_64-w64-mingw32-gcc') { '✅ Available (MinGW)' } else { '❌ Missing' })"
Write-Host "  - .NET SDK: $(if (Test-Command 'dotnet') { "✅ $(dotnet --version)" } else { '❌ Missing' })"