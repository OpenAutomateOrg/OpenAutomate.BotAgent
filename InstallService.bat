@echo off
echo Installing OpenAutomate Bot Agent Service...

REM Check if running as administrator
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo This script requires administrator privileges.
    exit /b 1
)

REM Stop service if it exists
sc stop "OpenAutomateBotAgent" >nul 2>&1

REM Delete service if it exists  
sc delete "OpenAutomateBotAgent" >nul 2>&1

REM Install the service
sc create "OpenAutomateBotAgent" binPath= "%~dp0OpenAutomate.BotAgent.Service.exe" start= auto DisplayName= "OpenAutomate Bot Agent"

if %errorLevel% equ 0 (
    echo Service installed successfully.
    echo Starting service...
    sc start "OpenAutomateBotAgent"
    if %errorLevel% equ 0 (
        echo Service started successfully.
    ) else (
        echo Failed to start service. Check Windows Event Log for details.
        exit /b 1
    )
) else (
    echo Failed to install service.
    exit /b 1
)

echo Service installation completed successfully.
exit /b 0