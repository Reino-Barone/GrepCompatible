#!/usr/bin/env pwsh
# GrepCompatible Windows Installer
# This script installs GrepCompatible and adds it to the PATH

param(
    [string]$InstallPath = "$env:LOCALAPPDATA\GrepCompatible",
    [string]$ExecutablePath = "",
    [switch]$Uninstall = $false,
    [switch]$ForAllUsers = $false
)

$ErrorActionPreference = "Stop"

$AppName = "GrepCompatible"
$ExeName = "grep.exe"

function Test-Administrator {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Add-ToPath {
    param([string]$PathToAdd, [bool]$AllUsers = $false)
    
    $target = if ($AllUsers) { "Machine" } else { "User" }
    $currentPath = [Environment]::GetEnvironmentVariable("PATH", $target)
    
    if ($currentPath -notlike "*$PathToAdd*") {
        $newPath = "$currentPath;$PathToAdd"
        [Environment]::SetEnvironmentVariable("PATH", $newPath, $target)
        Write-Host "✓ Added to PATH ($target scope)" -ForegroundColor Green
        return $true
    } else {
        Write-Host "✓ Already in PATH ($target scope)" -ForegroundColor Yellow
        return $false
    }
}

function Remove-FromPath {
    param([string]$PathToRemove, [bool]$AllUsers = $false)
    
    $target = if ($AllUsers) { "Machine" } else { "User" }
    $currentPath = [Environment]::GetEnvironmentVariable("PATH", $target)
    
    if ($currentPath -like "*$PathToRemove*") {
        $newPath = $currentPath -replace [regex]::Escape(";$PathToRemove"), ""
        $newPath = $newPath -replace [regex]::Escape("$PathToRemove;"), ""
        $newPath = $newPath -replace [regex]::Escape("$PathToRemove"), ""
        [Environment]::SetEnvironmentVariable("PATH", $newPath, $target)
        Write-Host "✓ Removed from PATH ($target scope)" -ForegroundColor Green
        return $true
    } else {
        Write-Host "✓ Not in PATH ($target scope)" -ForegroundColor Yellow
        return $false
    }
}

if ($Uninstall) {
    Write-Host "Uninstalling $AppName..." -ForegroundColor Red
    
    # Remove from PATH
    Remove-FromPath -PathToRemove $InstallPath -AllUsers:$ForAllUsers
    
    # Remove installation directory
    if (Test-Path $InstallPath) {
        Remove-Item -Recurse -Force $InstallPath
        Write-Host "✓ Removed installation directory: $InstallPath" -ForegroundColor Green
    }
    
    Write-Host "✓ $AppName has been uninstalled successfully!" -ForegroundColor Green
    Write-Host "Note: Please restart your terminal/command prompt to update PATH." -ForegroundColor Yellow
    exit 0
}

# Installation process
Write-Host "Installing $AppName..." -ForegroundColor Green

if ($ForAllUsers -and -not (Test-Administrator)) {
    Write-Error "Administrator privileges required for system-wide installation. Please run as administrator or remove -ForAllUsers flag."
    exit 1
}

if ($ForAllUsers) {
    $InstallPath = "$env:ProgramFiles\$AppName"
}

# Detect executable path if not provided
if ([string]::IsNullOrEmpty($ExecutablePath)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $possiblePaths = @(
        Join-Path $scriptDir "..\dist\win-x64\$ExeName",
        Join-Path $scriptDir "..\dist\win-x86\$ExeName",
        Join-Path $scriptDir "..\dist\win-arm64\$ExeName",
        Join-Path $scriptDir "$ExeName"
    )
    
    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            $ExecutablePath = $path
            break
        }
    }
    
    if ([string]::IsNullOrEmpty($ExecutablePath)) {
        Write-Error "Cannot find $ExeName. Please specify the path using -ExecutablePath parameter."
        exit 1
    }
}

if (-not (Test-Path $ExecutablePath)) {
    Write-Error "Executable not found at: $ExecutablePath"
    exit 1
}

Write-Host "Installing from: $ExecutablePath" -ForegroundColor Cyan
Write-Host "Installing to: $InstallPath" -ForegroundColor Cyan

# Create installation directory
if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Force -Path $InstallPath | Out-Null
    Write-Host "✓ Created installation directory: $InstallPath" -ForegroundColor Green
}

# Copy executable
$destinationPath = Join-Path $InstallPath $ExeName
Copy-Item -Path $ExecutablePath -Destination $destinationPath -Force
Write-Host "✓ Copied executable to: $destinationPath" -ForegroundColor Green

# Verify the executable works
try {
    $version = & $destinationPath --help | Select-Object -First 1
    Write-Host "✓ Executable verified: $version" -ForegroundColor Green
} catch {
    Write-Warning "Could not verify executable, but installation completed."
}

# Add to PATH
$pathAdded = Add-ToPath -PathToAdd $InstallPath -AllUsers:$ForAllUsers

Write-Host "`n✓ $AppName has been installed successfully!" -ForegroundColor Green
Write-Host "Installation path: $InstallPath" -ForegroundColor Cyan

if ($pathAdded) {
    Write-Host "`nNote: Please restart your terminal/command prompt to use 'grep' command." -ForegroundColor Yellow
} else {
    Write-Host "`nYou can now use 'grep' command in any terminal." -ForegroundColor Green
}

Write-Host "`nTo uninstall, run:" -ForegroundColor Cyan
if ($ForAllUsers) {
    Write-Host "  .\install-windows.ps1 -Uninstall -ForAllUsers" -ForegroundColor White
} else {
    Write-Host "  .\install-windows.ps1 -Uninstall" -ForegroundColor White
}

Write-Host "`nTo test the installation, run:" -ForegroundColor Cyan
Write-Host "  grep --help" -ForegroundColor White