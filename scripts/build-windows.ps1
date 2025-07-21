#!/usr/bin/env pwsh
# Build script for Windows self-contained executables

param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "./dist",
    [switch]$SingleFile = $true
)

Write-Host "Building GrepCompatible for Windows..." -ForegroundColor Green

# Ensure output directory exists
if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$runtimes = @("win-x64", "win-x86", "win-arm64")

foreach ($runtime in $runtimes) {
    Write-Host "Building for $runtime..." -ForegroundColor Yellow
    
    $outputPath = "$OutputDir/$runtime"
    
    $publishArgs = @(
        "publish", "src",
        "-c", $Configuration,
        "-r", $runtime,
        "--self-contained", "true",
        "-o", $outputPath
    )
    
    if ($SingleFile) {
        $publishArgs += "-p:PublishSingleFile=true"
        $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
    }
    
    & dotnet @publishArgs
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Successfully built for $runtime" -ForegroundColor Green
        
        # Show file size
        $exePath = "$outputPath/grep.exe"
        if (Test-Path $exePath) {
            $fileSize = [Math]::Round((Get-Item $exePath).Length / 1MB, 2)
            Write-Host "  Executable size: $fileSize MB" -ForegroundColor Cyan
        }
    } else {
        Write-Error "Failed to build for $runtime"
        exit 1
    }
}

Write-Host "`nBuild completed successfully!" -ForegroundColor Green
Write-Host "Output directory: $OutputDir" -ForegroundColor Cyan

# List all generated files
Write-Host "`nGenerated files:" -ForegroundColor Yellow
Get-ChildItem -Path $OutputDir -Recurse -Filter "*.exe" | ForEach-Object {
    $size = [Math]::Round($_.Length / 1MB, 2)
    Write-Host "  $($_.FullName) ($size MB)" -ForegroundColor White
}