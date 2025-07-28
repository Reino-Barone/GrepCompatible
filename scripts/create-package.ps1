#!/usr/bin/env pwsh
# GrepCompatible Windows Package Creator
# This script builds and creates a distribution package for easy installation

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "./package",
    [switch]$IncludeAllRuntimes = $false
)

$ErrorActionPreference = "Stop"

Write-Host "Creating GrepCompatible installation package..." -ForegroundColor Green

# Clean and create package directory
if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

if ($IncludeAllRuntimes) {
    $runtimes = @("win-x64", "win-x86", "win-arm64")
} else {
    $runtimes = @($Runtime)
}

# Build executables
Write-Host "Building executables..." -ForegroundColor Yellow
foreach ($rt in $runtimes) {
    Write-Host "  Building for $rt..." -ForegroundColor Cyan
    
    $buildDir = "$OutputDir/build/$rt"
    New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
    
    dotnet publish src -c $Configuration -r $rt --self-contained true -o $buildDir `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to build for $rt"
        exit 1
    }
}

# Create main package structure
Write-Host "Creating package structure..." -ForegroundColor Yellow

# Copy executables to package root
foreach ($rt in $runtimes) {
    $buildDir = "$OutputDir/build/$rt"
    $exePath = "$buildDir/grep.exe"
    
    if (Test-Path $exePath) {
        if ($runtimes.Count -eq 1) {
            # Single runtime - put exe in root
            Copy-Item $exePath "$OutputDir/grep.exe"
        } else {
            # Multiple runtimes - create subdirectories
            $rtDir = "$OutputDir/$rt"
            New-Item -ItemType Directory -Force -Path $rtDir | Out-Null
            Copy-Item $exePath "$rtDir/grep.exe"
        }
    }
}

# Copy scripts
Write-Host "Copying installation scripts..." -ForegroundColor Yellow
Copy-Item "scripts/install-windows.ps1" "$OutputDir/"
Copy-Item "scripts/install-windows.bat" "$OutputDir/"

# Create README for the package
$packageReadme = @"
# GrepCompatible Installation Package

This package contains the GrepCompatible executable and installation scripts.

## Quick Installation

### Option 1: PowerShell (Recommended)
Right-click and "Run with PowerShell":
``````
.\install-windows.ps1
``````

Or from PowerShell command line:
``````powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
.\install-windows.ps1
``````

### Option 2: Batch file
Double-click or run from command prompt:
``````
install-windows.bat
``````

## Installation Options

### User Installation (Default)
- Installs to: `%LOCALAPPDATA%\GrepCompatible`
- Adds to user PATH only
- No administrator privileges required

``````powershell
.\install-windows.ps1
``````

### System-wide Installation
- Installs to: `%ProgramFiles%\GrepCompatible`
- Adds to system PATH (all users)
- Requires administrator privileges

``````powershell
.\install-windows.ps1 -ForAllUsers
``````

### Custom Installation Path
``````powershell
.\install-windows.ps1 -InstallPath "C:\MyTools\GrepCompatible"
``````

## Manual Installation

If you prefer manual installation:

1. Copy `grep.exe` to a directory of your choice
2. Add that directory to your PATH environment variable
3. Restart your command prompt/terminal

## Architecture Detection

$(if ($runtimes.Count -eq 1) {
"This package contains the executable for $Runtime architecture."
} else {
"This package contains executables for multiple architectures:
$(foreach ($rt in $runtimes) { "- $rt" })"

"The installer will automatically detect and install the appropriate version for your system."
})

## Usage

After installation, you can use `grep` from any command prompt:

``````
grep --help
grep "pattern" file.txt
grep -r "pattern" directory/
``````

## Uninstallation

To uninstall GrepCompatible:

``````powershell
.\install-windows.ps1 -Uninstall
``````

Or with batch file:
``````
install-windows.bat /uninstall
``````

For system-wide installations, use:
``````powershell
.\install-windows.ps1 -Uninstall -ForAllUsers
``````

## Support

For issues and documentation, visit:
https://github.com/Reino-Barone/GrepCompatible
"@

Set-Content -Path "$OutputDir/README.txt" -Value $packageReadme -Encoding UTF8

# Create version info
$versionInfo = @"
GrepCompatible Installation Package
Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Configuration: $Configuration
$(if ($runtimes.Count -eq 1) {
"Runtime: $Runtime"
} else {
"Runtimes: $($runtimes -join ', ')"
})
"@

Set-Content -Path "$OutputDir/VERSION.txt" -Value $versionInfo -Encoding UTF8

# Clean up build directory
Remove-Item -Recurse -Force "$OutputDir/build"

Write-Host "`n✓ Package created successfully!" -ForegroundColor Green
Write-Host "Package location: $OutputDir" -ForegroundColor Cyan

# Show package contents
Write-Host "`nPackage contents:" -ForegroundColor Yellow
Get-ChildItem -Path $OutputDir -Recurse | ForEach-Object {
    $relativePath = $_.FullName.Substring($OutputDir.Length + 1)
    if ($_.PSIsContainer) {
        Write-Host "  📁 $relativePath/" -ForegroundColor Blue
    } else {
        $size = [Math]::Round($_.Length / 1KB, 1)
        Write-Host "  📄 $relativePath ($size KB)" -ForegroundColor White
    }
}

Write-Host "`nTo create a ZIP archive:" -ForegroundColor Cyan
if ($IncludeAllRuntimes) {
    Write-Host "Compress-Archive -Path '$OutputDir\*' -DestinationPath 'GrepCompatible-all-runtimes.zip'" -ForegroundColor White
} else {
    Write-Host "Compress-Archive -Path '$OutputDir\*' -DestinationPath 'GrepCompatible-$Runtime.zip'" -ForegroundColor White
}