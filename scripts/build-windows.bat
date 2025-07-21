@echo off
REM Build script for Windows self-contained executables (Batch version)

setlocal EnableDelayedExpansion

set "CONFIGURATION=Release"
set "OUTPUT_DIR=.\dist"

if "%1" neq "" set "CONFIGURATION=%1"
if "%2" neq "" set "OUTPUT_DIR=%2"

echo Building GrepCompatible for Windows...

REM Clean output directory
if exist "%OUTPUT_DIR%" (
    echo Cleaning output directory...
    rmdir /s /q "%OUTPUT_DIR%"
)
mkdir "%OUTPUT_DIR%" 2>nul

set "RUNTIMES=win-x64 win-x86 win-arm64"

for %%r in (%RUNTIMES%) do (
    echo Building for %%r...
    
    set "OUTPUT_PATH=%OUTPUT_DIR%\%%r"
    mkdir "!OUTPUT_PATH!" 2>nul
    
    dotnet publish src -c %CONFIGURATION% -r %%r --self-contained true -o "!OUTPUT_PATH!" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
    
    if !errorlevel! equ 0 (
        echo ^✓ Successfully built for %%r
        
        REM Show file size
        if exist "!OUTPUT_PATH!\grep.exe" (
            for %%f in ("!OUTPUT_PATH!\grep.exe") do (
                set /a "size=%%~zf / 1048576"
                echo   Executable size: !size! MB
            )
        )
    ) else (
        echo Failed to build for %%r
        exit /b 1
    )
)

echo.
echo Build completed successfully!
echo Output directory: %OUTPUT_DIR%

echo.
echo Generated files:
for /r "%OUTPUT_DIR%" %%f in (*.exe) do (
    for %%s in ("%%f") do (
        set /a "size=%%~zs / 1048576"
        echo   %%f (!size! MB^)
    )
)

pause