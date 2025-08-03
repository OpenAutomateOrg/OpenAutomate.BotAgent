;======================================================================
; OpenAutomate Bot Agent Installer
; Generated using NSIS (Nullsoft Scriptable Install System)
;======================================================================

; Modern UI
!include "MUI2.nsh"

;======================================================================
; Installer Attributes
;======================================================================

; General
Name "OpenAutomate Agent"
OutFile "OpenAutomate-Agent-Setup.exe"
Unicode true

; Default installation folder
InstallDir "$PROGRAMFILES64\OpenAutomateAgent"

; Registry key to check for directory (for uninstall)
InstallDirRegKey HKLM "Software\OpenAutomate Agent" "Install_Dir"

; Request administrator privileges
RequestExecutionLevel admin

; Compressor
SetCompressor lzma

; Branding
BrandingText "OpenAutomate Agent Installer"

; Icon
Icon "agent.ico"
UninstallIcon "agent.ico"

;======================================================================
; Version Information
;======================================================================
VIProductVersion "1.0.0.0"
VIAddVersionKey "ProductName" "OpenAutomate Agent"
VIAddVersionKey "CompanyName" "OpenAutomate"
VIAddVersionKey "FileDescription" "OpenAutomate Agent Setup"
VIAddVersionKey "FileVersion" "1.0.0.0"
VIAddVersionKey "ProductVersion" "1.0.0.0"
VIAddVersionKey "LegalCopyright" "© OpenAutomate"

;======================================================================
; Interface Settings
;======================================================================
!define MUI_ABORTWARNING
!define MUI_ICON "agent.ico"
!define MUI_UNICON "agent.ico"

;======================================================================
; Pages
;======================================================================

; Installer pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LicenseAgreement.rtf"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

; Uninstaller pages
!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

;======================================================================
; Languages
;======================================================================
!insertmacro MUI_LANGUAGE "English"

;======================================================================
; Installer Sections
;======================================================================

Section "Core Files" SecCore
    SectionIn RO  ; Read-only section (always installed)
    
    ; Set output path to installation directory
    SetOutPath "$INSTDIR"
    
    ; Recursively copy all files from the UI release directory
    File /r "..\OpenAutomate.BotAgent.UI\bin\Release\net8.0-windows\*.*"
    
    ; Copy the agent icon for shortcuts
    File "agent.ico"
    
    ; Store installation folder
    WriteRegStr HKLM "SOFTWARE\OpenAutomate Agent" "Install_Dir" "$INSTDIR"
    
SectionEnd

Section "Windows Service" SecService
    SectionIn RO  ; Read-only section (always installed)
    
    DetailPrint "Installing Windows Service..."
    
    ; Stop service if it exists
    nsExec::Exec '"$SYSDIR\sc.exe" stop "OpenAutomateBotAgent"'
    
    ; Wait a moment
    Sleep 2000
    
    ; Delete service if it exists
    nsExec::Exec '"$SYSDIR\sc.exe" delete "OpenAutomateBotAgent"'
    
    ; Wait a moment
    Sleep 1000
    
    ; Create the service
    nsExec::Exec '"$SYSDIR\sc.exe" create "OpenAutomateBotAgent" binPath= "$INSTDIR\OpenAutomate.BotAgent.Service.exe" start= auto DisplayName= "OpenAutomate Bot Agent"'
    Pop $0
    
    ${If} $0 == 0
        DetailPrint "Service installed successfully"
        
        ; Start the service
        DetailPrint "Starting service..."
        nsExec::Exec '"$SYSDIR\sc.exe" start "OpenAutomateBotAgent"'
        Pop $0
        
        ${If} $0 == 0
            DetailPrint "Service started successfully"
        ${Else}
            DetailPrint "Warning: Service installed but failed to start. You can start it manually from Services.msc"
        ${EndIf}
    ${Else}
        MessageBox MB_ICONEXCLAMATION "Failed to install Windows Service. Error code: $0"
    ${EndIf}
    
SectionEnd

Section "Start Menu Shortcuts" SecShortcuts
    
    CreateDirectory "$SMPROGRAMS\OpenAutomate Agent"
    CreateShortcut "$SMPROGRAMS\OpenAutomate Agent\OpenAutomate Agent.lnk" "$INSTDIR\OpenAutomate.BotAgent.UI.exe" "" "$INSTDIR\agent.ico" 0
    CreateShortcut "$SMPROGRAMS\OpenAutomate Agent\Uninstall.lnk" "$INSTDIR\uninstall.exe" "" "$INSTDIR\agent.ico" 0
    
SectionEnd

Section "Registry Entries" SecRegistry
    
    ; Write the installation path to registry
    WriteRegStr HKLM "SOFTWARE\OpenAutomate Agent" "Install_Dir" "$INSTDIR"
    
    ; Write the uninstall keys for Windows Add/Remove Programs
    WriteRegStr HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OpenAutomate Agent" "DisplayName" "OpenAutomate Agent"
    WriteRegStr HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OpenAutomate Agent" "UninstallString" '"$INSTDIR\uninstall.exe"'
    WriteRegStr HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OpenAutomate Agent" "InstallLocation" "$INSTDIR"
    WriteRegStr HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OpenAutomate Agent" "Publisher" "OpenAutomate"
    WriteRegStr HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OpenAutomate Agent" "DisplayVersion" "1.0.0"
    WriteRegDWORD HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OpenAutomate Agent" "NoModify" 1
    WriteRegDWORD HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OpenAutomate Agent" "NoRepair" 1
    
    ; Create uninstaller
    WriteUninstaller "$INSTDIR\uninstall.exe"
    
SectionEnd

;======================================================================
; Section Descriptions (removed - not using components page)
;======================================================================

;======================================================================
; Uninstaller Section
;======================================================================

Section "Uninstall"
    
    ; Remove service first
    DetailPrint "Stopping and removing Windows Service..."
    
    ; Stop the service
    nsExec::Exec '"$SYSDIR\sc.exe" stop "OpenAutomateBotAgent"'
    
    ; Wait for service to stop
    Sleep 3000
    
    ; Delete the service
    nsExec::Exec '"$SYSDIR\sc.exe" delete "OpenAutomateBotAgent"'
    Pop $0
    
    ${If} $0 == 0
        DetailPrint "Service removed successfully"
    ${Else}
        DetailPrint "Warning: Failed to remove service (it may not have been installed)"
    ${EndIf}
    
    ; Remove registry keys
    DeleteRegKey HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OpenAutomate Agent"
    DeleteRegKey HKLM "SOFTWARE\OpenAutomate Agent"
    
    ; Remove Start Menu shortcuts
    Delete "$SMPROGRAMS\OpenAutomate Agent\*.*"
    RMDir "$SMPROGRAMS\OpenAutomate Agent"
    
    ; Remove files and directories
    Delete "$INSTDIR\*.exe"
    Delete "$INSTDIR\*.dll"
    Delete "$INSTDIR\*.json"
    Delete "$INSTDIR\*.bat"
    Delete "$INSTDIR\*.pdb"
    Delete "$INSTDIR\*.deps.json"
    Delete "$INSTDIR\*.runtimeconfig.json"
    
    ; Remove subdirectories
    RMDir /r "$INSTDIR\runtimes"
    RMDir /r "$INSTDIR\win"
    
    ; Remove the installation directory if it's empty
    RMDir "$INSTDIR"
    
    ; Remove the uninstaller
    Delete "$INSTDIR\uninstall.exe"
    
SectionEnd

;======================================================================
; Functions
;======================================================================

Function .onInit
    
    ; Check if we're running as administrator
    UserInfo::GetAccountType
    Pop $R0
    ${If} $R0 != "admin"
        MessageBox MB_ICONSTOP "Administrator privileges required!"
        SetErrorLevel 740 ;ERROR_ELEVATION_REQUIRED
        Quit
    ${EndIf}
    
    ; Check if already installed
    ReadRegStr $R0 HKLM "SOFTWARE\OpenAutomate Agent" "Install_Dir"
    ${If} $R0 != ""
        MessageBox MB_YESNO|MB_ICONQUESTION "OpenAutomate Agent appears to be already installed at:$\n$\n$R0$\n$\nDo you want to continue with the installation?" IDYES continue
        Abort
        continue:
    ${EndIf}
    
FunctionEnd

Function un.onInit
    
    ; Check if we're running as administrator
    UserInfo::GetAccountType
    Pop $R0
    ${If} $R0 != "admin"
        MessageBox MB_ICONSTOP "Administrator privileges required for uninstallation!"
        SetErrorLevel 740 ;ERROR_ELEVATION_REQUIRED
        Quit
    ${EndIf}
    
    MessageBox MB_YESNO|MB_ICONQUESTION "This will completely remove OpenAutomate Agent and all of its components.$\n$\nDo you want to continue?" IDYES continue
    Abort
    continue:
    
FunctionEnd