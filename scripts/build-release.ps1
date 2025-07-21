#!/usr/bin/env pwsh
# Complete build and packaging script for GrepCompatible

param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "./release",
    [switch]$CreateZips = $true,
    [switch]$SkipTests = $false
)

$ErrorActionPreference = "Stop"

Write-Host "=== GrepCompatible Release Builder ===" -ForegroundColor Magenta
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "Output Directory: $OutputDir" -ForegroundColor Cyan

# Clean output directory
if (Test-Path $OutputDir) {
    Write-Host "Cleaning output directory..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# Run tests first (unless skipped)
if (-not $SkipTests) {
    Write-Host "`n=== Running Tests ===" -ForegroundColor Magenta
    dotnet test
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed! Aborting release build."
        exit 1
    }
    Write-Host "✓ All tests passed!" -ForegroundColor Green
}

# Build for each Windows architecture
$runtimes = @("win-x64", "win-x86", "win-arm64")

Write-Host "`n=== Building Packages ===" -ForegroundColor Magenta

foreach ($runtime in $runtimes) {
    Write-Host "`nBuilding package for $runtime..." -ForegroundColor Yellow
    
    $packageDir = "$OutputDir/$runtime"
    
    # Create package using the create-package script
    & "./scripts/create-package.ps1" -Runtime $runtime -OutputDir $packageDir -Configuration $Configuration
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create package for $runtime"
        exit 1
    }
    
    # Create ZIP archive if requested
    if ($CreateZips) {
        Write-Host "Creating ZIP archive for $runtime..." -ForegroundColor Cyan
        $zipPath = "$OutputDir/GrepCompatible-$runtime.zip"
        Compress-Archive -Path "$packageDir/*" -DestinationPath $zipPath -CompressionLevel Optimal
        Write-Host "✓ Created: $zipPath" -ForegroundColor Green
    }
}

# Create a combined package with all architectures
Write-Host "`nCreating combined package..." -ForegroundColor Yellow
& "./scripts/create-package.ps1" -IncludeAllRuntimes -OutputDir "$OutputDir/combined" -Configuration $Configuration

if ($CreateZips) {
    $combinedZipPath = "$OutputDir/GrepCompatible-windows-all.zip"
    Compress-Archive -Path "$OutputDir/combined/*" -DestinationPath $combinedZipPath -CompressionLevel Optimal
    Write-Host "✓ Created combined package: $combinedZipPath" -ForegroundColor Green
}

# Create release summary
$releaseInfo = @"
GrepCompatible Release Package
==============================

Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Configuration: $Configuration

Packages Created:
$(foreach ($runtime in $runtimes) {
"- $runtime ($(if (Test-Path "$OutputDir/$runtime/grep.exe") { [Math]::Round((Get-Item "$OutputDir/$runtime/grep.exe").Length / 1MB, 2) } else { "N/A" }) MB)"
})
- Combined (all architectures)

Installation:
1. Download the appropriate package for your system architecture
2. Extract the ZIP file
3. Run install-windows.ps1 (PowerShell) or install-windows.bat (Command Prompt)
4. Follow the installation prompts

For manual installation, simply copy grep.exe to a directory in your PATH.

Support: https://github.com/Reino-Barone/GrepCompatible
"@

Set-Content -Path "$OutputDir/RELEASE-NOTES.txt" -Value $releaseInfo -Encoding UTF8

Write-Host "`n=== Release Build Completed ===" -ForegroundColor Magenta
Write-Host "✓ All packages created successfully!" -ForegroundColor Green
Write-Host "Output directory: $OutputDir" -ForegroundColor Cyan

# Show summary
Write-Host "`nRelease Summary:" -ForegroundColor Yellow
Get-ChildItem -Path $OutputDir -File | ForEach-Object {
    $size = [Math]::Round($_.Length / 1MB, 2)
    Write-Host "  📦 $($_.Name) ($size MB)" -ForegroundColor White
}

Get-ChildItem -Path $OutputDir -Directory | ForEach-Object {
    Write-Host "  📁 $($_.Name)/" -ForegroundColor Blue
    $exePath = Join-Path $_.FullName "grep.exe"
    if (Test-Path $exePath) {
        $size = [Math]::Round((Get-Item $exePath).Length / 1MB, 2)
        Write-Host "      └─ grep.exe ($size MB)" -ForegroundColor White
    }
}

Write-Host "`nReady for distribution! 🚀" -ForegroundColor Green