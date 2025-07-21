@echo off
REM Quick build script for GrepCompatible Windows executable

echo Building GrepCompatible for Windows...

dotnet publish src -c Release -r win-x64 --self-contained true -o .\dist\win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

if %errorlevel% equ 0 (
    echo.
    echo ✓ Build successful!
    echo Executable: .\dist\win-x64\grep.exe
    
    if exist ".\dist\win-x64\grep.exe" (
        for %%f in (".\dist\win-x64\grep.exe") do (
            set /a "size=%%~zf / 1048576"
            echo Size: !size! MB
        )
    )
    
    echo.
    echo To test: .\dist\win-x64\grep.exe --help
) else (
    echo Build failed!
)

pause