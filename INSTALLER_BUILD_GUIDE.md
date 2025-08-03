# OpenAutomate Agent - NSIS Installer Build Guide

This guide explains how to build the OpenAutomate Agent installer using NSIS (Nullsoft Scriptable Install System).

## Prerequisites

1. **NSIS Installed**: Download and install NSIS from https://nsis.sourceforge.io/Download
   - Default installation path: `C:\Program Files (x86)\NSIS\`
   - Make sure `makensis.exe` is accessible

2. **Release Build**: Ensure the application is built in Release mode
   - All files should be in: `OpenAutomate.BotAgent.UI\bin\Release\net8.0-windows\`

## File Structure

```
OpenAutomate.BotAgent/
├── Installer/
│   ├── installer.nsi          # Main NSIS script
│   ├── agent.ico             # Application icon
│   └── OpenAutomate-Agent-Setup.exe (generated)
├── LicenseAgreement.rtf      # License agreement file
├── build-installer.bat       # Automated build script
└── OpenAutomate.BotAgent.UI/
    └── bin/Release/net8.0-windows/  # Source files for installer
```

## Building the Installer

### Method 1: Using the Build Script (Recommended)

1. **Run the build script**:
   ```batch
   build-installer.bat
   ```

2. **Output**: The installer will be created as `Installer\OpenAutomate-Agent-Setup.exe`

### Method 2: Manual NSIS Command

1. **Open Command Prompt** as Administrator

2. **Navigate to the Installer directory**:
   ```batch
   cd /d "G:\CapstoneProject\OpenAutomate.BotAgent\Installer"
   ```

3. **Run NSIS compiler**:
   ```batch
   "C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi
   ```

### Method 3: Using NSIS GUI

1. **Open NSIS** from Start Menu
2. **Drag and drop** `installer.nsi` onto the NSIS window
3. **Click Compile** to build the installer

## Build Commands Reference

### Basic Build Command
```batch
"C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi
```

### Build with Verbose Output
```batch
"C:\Program Files (x86)\NSIS\makensis.exe" /V4 installer.nsi
```

### Build with Custom Output File
```batch
"C:\Program Files (x86)\NSIS\makensis.exe" /XOutFile "MyCustomInstaller.exe" installer.nsi
```

### Build with Preprocessor Defines
```batch
"C:\Program Files (x86)\NSIS\makensis.exe" /DVERSION="1.0.1" installer.nsi
```

## Common NSIS Command Line Options

| Option | Description |
|--------|-------------|
| `/V4` | Maximum verbosity (shows all details) |
| `/V3` | High verbosity (no script output) |
| `/V2` | Medium verbosity (no info messages) |
| `/V1` | Low verbosity (no warnings) |
| `/V0` | No output |
| `/WX` | Treat warnings as errors |
| `/PAUSE` | Pause after execution |
| `/NOCD` | Don't change to script directory |

## Troubleshooting

### Common Issues

1. **"makensis.exe not found"**
   - Ensure NSIS is installed
   - Check the path: `C:\Program Files (x86)\NSIS\makensis.exe`
   - Add NSIS to PATH environment variable if needed

2. **"File not found" errors**
   - Ensure Release build is completed
   - Check that all referenced files exist
   - Verify paths in the NSIS script

3. **"Icon file invalid"**
   - Ensure `agent.ico` is a valid ICO file
   - Try with a different icon if needed

4. **Permission errors**
   - Run Command Prompt as Administrator
   - Ensure no antivirus is blocking the process

### Rebuilding After Changes

If you modify the application:

1. **Rebuild the solution**:
   ```batch
   dotnet build "OpenAutomate.BotAgent.UI\OpenAutomate.BotAgent.UI.csproj" --configuration Release
   ```

2. **Rebuild the installer**:
   ```batch
   cd Installer
   "C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi
   ```

## Installer Features

The generated installer includes:

- ✅ **Modern UI** with wizard-style interface
- ✅ **License Agreement** page
- ✅ **Custom installation directory** selection
- ✅ **Windows Service** installation and startup
- ✅ **Start Menu shortcuts** creation
- ✅ **Add/Remove Programs** integration
- ✅ **Complete uninstaller** with service removal
- ✅ **Administrator privilege** checks
- ✅ **Professional branding** with icon

## Testing the Installer

1. **Test Installation**:
   - Run `OpenAutomate-Agent-Setup.exe` as Administrator
   - Follow the installation wizard
   - Verify service is installed and running
   - Check Start Menu shortcuts

2. **Test Uninstallation**:
   - Use Add/Remove Programs or run uninstaller directly
   - Verify complete removal of files, service, and shortcuts

## Advanced Customization

### Modifying the Script

The main NSIS script is located at `Installer\installer.nsi`. Key sections:

- **Lines 15-31**: Basic installer attributes
- **Lines 40-45**: Version information  
- **Lines 82-109**: Core files installation
- **Lines 111-140**: Windows Service installation
- **Lines 142-148**: Start Menu shortcuts
- **Lines 150-167**: Registry entries
- **Lines 171-218**: Uninstaller logic

### Adding Custom Pages

To add custom installer pages, modify the Pages section:

```nsis
; Add after existing pages
!insertmacro MUI_PAGE_COMPONENTS  ; For component selection
Page custom MyCustomPage          ; For custom functionality
```

### Changing Installation Directory

Modify line 18 in the NSIS script:

```nsis
InstallDir "$PROGRAMFILES64\Your Custom Directory"
```

## Build Automation

For CI/CD integration, use this command in your build pipeline:

```batch
# Build application first
dotnet build "OpenAutomate.BotAgent.UI\OpenAutomate.BotAgent.UI.csproj" --configuration Release

# Build installer
cd Installer
"C:\Program Files (x86)\NSIS\makensis.exe" /V2 installer.nsi

# Verify output
if exist "OpenAutomate-Agent-Setup.exe" (
    echo Installer built successfully
) else (
    echo Installer build failed
    exit /b 1
)
```

---

**Note**: Always test the installer on a clean system before distribution to ensure all dependencies and features work correctly.