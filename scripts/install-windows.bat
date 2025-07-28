@echo off
REM GrepCompatible Windows Installer (Batch version)
REM This script installs GrepCompatible and adds it to the PATH

setlocal EnableDelayedExpansion

set "APP_NAME=GrepCompatible"
set "EXE_NAME=grep.exe"
set "INSTALL_PATH=%LOCALAPPDATA%\GrepCompatible"
set "UNINSTALL=false"
set "FOR_ALL_USERS=false"

REM Parse arguments
:parse_args
if "%1"=="/uninstall" (
    set "UNINSTALL=true"
    shift
    goto parse_args
)
if "%1"=="/allusers" (
    set "FOR_ALL_USERS=true"
    shift
    goto parse_args
)
if "%1"=="/path" (
    set "INSTALL_PATH=%2"
    shift
    shift
    goto parse_args
)
if "%1" neq "" (
    shift
    goto parse_args
)

if "%FOR_ALL_USERS%"=="true" (
    set "INSTALL_PATH=%ProgramFiles%\%APP_NAME%"
    
    REM Check for administrator privileges
    net session >nul 2>&1
    if !errorlevel! neq 0 (
        echo Administrator privileges required for system-wide installation.
        echo Please run as administrator or remove /allusers flag.
        pause
        exit /b 1
    )
)

if "%UNINSTALL%"=="true" goto uninstall

REM Installation process
echo Installing %APP_NAME%...

REM Find executable
set "EXE_PATH="
set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"

if exist "%SCRIPT_DIR%\..\dist\win-x64\%EXE_NAME%" (
    set "EXE_PATH=%SCRIPT_DIR%..\dist\win-x64\%EXE_NAME%"
) else if exist "%SCRIPT_DIR%..\dist\win-x86\%EXE_NAME%" (
    set "EXE_PATH=%SCRIPT_DIR%..\dist\win-x86\%EXE_NAME%"
) else if exist "%SCRIPT_DIR%..\dist\win-arm64\%EXE_NAME%" (
    set "EXE_PATH=%SCRIPT_DIR%..\dist\win-arm64\%EXE_NAME%"
) else if exist "%SCRIPT_DIR%\%EXE_NAME%" (
    set "EXE_PATH=%SCRIPT_DIR%\%EXE_NAME%"
)

if "%EXE_PATH%"=="" (
    echo Cannot find %EXE_NAME%. Please ensure it's in the script directory or build output.
    pause
    exit /b 1
)

echo Installing from: %EXE_PATH%
echo Installing to: %INSTALL_PATH%

REM Create installation directory
if not exist "%INSTALL_PATH%" (
    mkdir "%INSTALL_PATH%" 2>nul
    echo ✓ Created installation directory: %INSTALL_PATH%
)

REM Copy executable
copy "%EXE_PATH%" "%INSTALL_PATH%\%EXE_NAME%" >nul
if !errorlevel! equ 0 (
    echo ✓ Copied executable to: %INSTALL_PATH%\%EXE_NAME%
) else (
    echo Failed to copy executable
    pause
    exit /b 1
)

REM Add to PATH
call :add_to_path "%INSTALL_PATH%"

echo.
echo ✓ %APP_NAME% has been installed successfully!
echo Installation path: %INSTALL_PATH%
echo.
echo Note: Please restart your terminal/command prompt to use 'grep' command.
echo.
echo To uninstall, run:
if "%FOR_ALL_USERS%"=="true" (
    echo   %~nx0 /uninstall /allusers
) else (
    echo   %~nx0 /uninstall
)
echo.
echo To test the installation, run:
echo   grep --help

pause
exit /b 0

:uninstall
echo Uninstalling %APP_NAME%...

REM Remove from PATH
call :remove_from_path "%INSTALL_PATH%"

REM Remove installation directory
if exist "%INSTALL_PATH%" (
    rmdir /s /q "%INSTALL_PATH%"
    echo ✓ Removed installation directory: %INSTALL_PATH%
)

echo ✓ %APP_NAME% has been uninstalled successfully!
echo Note: Please restart your terminal/command prompt to update PATH.

pause
exit /b 0

:add_to_path
set "PATH_TO_ADD=%~1"
set "REG_KEY=HKCU\Environment"
if "%FOR_ALL_USERS%"=="true" set "REG_KEY=HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment"

REM Get current PATH
for /f "tokens=2*" %%i in ('reg query "%REG_KEY%" /v PATH 2^>nul') do set "CURRENT_PATH=%%j"
if "%CURRENT_PATH%"=="" set "CURRENT_PATH="

REM Check if already in PATH
echo %CURRENT_PATH% | find /i "%PATH_TO_ADD%" >nul
if !errorlevel! equ 0 (
    echo ✓ Already in PATH
    exit /b 0
)

REM Add to PATH
if "%CURRENT_PATH%"=="" (
    set "NEW_PATH=%PATH_TO_ADD%"
) else (
    set "NEW_PATH=%CURRENT_PATH%;%PATH_TO_ADD%"
)

reg add "%REG_KEY%" /v PATH /t REG_EXPAND_SZ /d "!NEW_PATH!" /f >nul
if !errorlevel! equ 0 (
    echo ✓ Added to PATH
) else (
    echo Failed to add to PATH
)
exit /b 0

:remove_from_path
set "PATH_TO_REMOVE=%~1"
set "REG_KEY=HKCU\Environment"
if "%FOR_ALL_USERS%"=="true" set "REG_KEY=HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment"

REM Get current PATH
for /f "tokens=2*" %%i in ('reg query "%REG_KEY%" /v PATH 2^>nul') do set "CURRENT_PATH=%%j"
if "%CURRENT_PATH%"=="" (
    echo ✓ Not in PATH
    exit /b 0
)

REM Remove from PATH
set "NEW_PATH=!CURRENT_PATH!"
set "NEW_PATH=!NEW_PATH:;%PATH_TO_REMOVE%=!"
set "NEW_PATH=!NEW_PATH:%PATH_TO_REMOVE%;=!"
set "NEW_PATH=!NEW_PATH:%PATH_TO_REMOVE%=!"

reg add "%REG_KEY%" /v PATH /t REG_EXPAND_SZ /d "!NEW_PATH!" /f >nul
if !errorlevel! equ 0 (
    echo ✓ Removed from PATH
) else (
    echo Failed to remove from PATH
)
exit /b 0