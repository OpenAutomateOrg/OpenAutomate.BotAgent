@echo off
echo Uninstalling OpenAutomate Bot Agent Service...

REM Check if running as administrator
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo This script requires administrator privileges.
    exit /b 1
)

REM Stop service
echo Stopping service...
sc stop "OpenAutomateBotAgent" >nul 2>&1

REM Wait a moment for service to stop
timeout /t 3 /nobreak >nul 2>&1

REM Delete service
echo Removing service...
sc delete "OpenAutomateBotAgent" >nul 2>&1

if %errorLevel% equ 0 (
    echo Service uninstalled successfully.
) else (
    echo Service may not have been installed or already removed.
)

echo Service uninstallation completed.
exit /b 0