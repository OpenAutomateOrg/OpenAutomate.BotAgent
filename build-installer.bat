@echo off
echo Building OpenAutomate Agent Installer...

REM Navigate to installer directory
cd /d "%~dp0Installer"

REM Check if NSIS is installed
if not exist "C:\Program Files (x86)\NSIS\makensis.exe" (
    echo NSIS not found at default location. Please install NSIS from https://nsis.sourceforge.io/Download
    echo Or update this script with the correct path to makensis.exe
    pause
    exit /b 1
)

REM Check if icon exists
if not exist "agent.ico" (
    echo Warning: agent.ico not found.
    echo Please ensure the icon file is present before building.
    pause
)

REM Build the installer
echo Building installer with NSIS...
"C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi

if %errorlevel% equ 0 (
    echo.
    echo ========================================
    echo Installer built successfully!
    echo Output: OpenAutomate-Agent-Setup.exe
    echo ========================================
    echo.
    pause
) else (
    echo.
    echo Build failed. Check the output above for errors.
    pause
    exit /b 1
)