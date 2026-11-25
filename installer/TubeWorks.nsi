; TubeWorks Designer installer (NSIS 3). Build with installer/build-installer.sh or build-installer.ps1.
; Defines passed by the build script: VERSION, STAGE (folder with the files to install), OUTDIR.

Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"

!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!define APPNAME "TubeWorks Designer"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\TubeWorksDesigner"
!define ADDINGUID "{6478F06E-71AB-4E73-9A87-8A6503767FF1}"
!define REGASM "$WINDIR\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

Name "${APPNAME} ${VERSION}"
OutFile "${OUTDIR}\TubeWorksSetup-${VERSION}.exe"
InstallDir "$PROGRAMFILES64\TubeWorks Designer"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
BrandingText "${APPNAME} ${VERSION}"
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileDescription" "${APPNAME} add-in for SOLIDWORKS - setup"
VIAddVersionKey "LegalCopyright" ""

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "This installs the ${APPNAME} add-in for SOLIDWORKS (64-bit).$\r$\n$\r$\nClose SOLIDWORKS before you continue.$\r$\n$\r$\nThe add-in has not yet been certified on every SOLIDWORKS version. Read 'Test plan' in the documentation folder before production use."
!define MUI_FINISHPAGE_TEXT "${APPNAME} is installed.$\r$\n$\r$\nStart SOLIDWORKS and open a part: the 'Tube Design' tab appears. If it does not, enable the add-in in Tools > Add-Ins."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "${APPNAME} needs 64-bit Windows." /SD IDOK
    Abort
  ${EndIf}
  SetRegView 64
  ; .NET Framework 4.8 = Release 528040 or later.
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
  ${If} $0 < 528040
    MessageBox MB_ICONSTOP ".NET Framework 4.8 is required. Install it from Microsoft, then run this setup again." /SD IDOK
    Abort
  ${EndIf}
  IfFileExists "${REGASM}" +3 0
    MessageBox MB_ICONSTOP "64-bit RegAsm.exe was not found (${REGASM})." /SD IDOK
    Abort
FunctionEnd

Function un.onInit
  SetRegView 64
FunctionEnd

!macro CheckSolidWorksClosed
  check_sw:
  nsExec::ExecToStack 'cmd /c tasklist /FI "IMAGENAME eq SLDWORKS.exe" /NH | find /I "SLDWORKS.exe"'
  Pop $0
  Pop $1
  ${If} $0 == 0
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "SOLIDWORKS is running. Close it, then click Retry." /SD IDCANCEL IDRETRY check_sw
    Abort
  ${EndIf}
!macroend

Section "Install"
  !insertmacro CheckSolidWorksClosed
  SetOutPath "$INSTDIR"
  ; Unregister an earlier version first, so that stale COM entries never remain.
  IfFileExists "$INSTDIR\TubeWorks.AddIn.dll" 0 +2
    nsExec::ExecToLog '"${REGASM}" /u "$INSTDIR\TubeWorks.AddIn.dll"'
  File "${STAGE}\TubeWorks.AddIn.dll"
  File "${STAGE}\TubeWorks.Geometry.dll"
  SetOutPath "$INSTDIR\Icons"
  File "${STAGE}\Icons\*.*"
  SetOutPath "$INSTDIR\Documentation"
  File "${STAGE}\Documentation\*.*"
  SetOutPath "$INSTDIR"

  DetailPrint "Registering the add-in with SOLIDWORKS..."
  nsExec::ExecToLog '"${REGASM}" /codebase "$INSTDIR\TubeWorks.AddIn.dll"'
  Pop $0
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Registration failed (RegAsm exit code $0). The add-in is copied but not registered." /SD IDOK
    Abort
  ${EndIf}

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTKEY}" "Publisher" "TubeWorks"
  WriteRegStr HKLM "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "${UNINSTKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\Uninstall.exe"
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoRepair" 1

  CreateDirectory "$SMPROGRAMS\TubeWorks Designer"
  CreateShortcut "$SMPROGRAMS\TubeWorks Designer\Documentation.lnk" "$INSTDIR\Documentation"
  CreateShortcut "$SMPROGRAMS\TubeWorks Designer\Uninstall TubeWorks Designer.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd

Section "Uninstall"
  !insertmacro CheckSolidWorksClosed
  IfFileExists "$INSTDIR\TubeWorks.AddIn.dll" 0 +2
    nsExec::ExecToLog '"${REGASM}" /u "$INSTDIR\TubeWorks.AddIn.dll"'
  ; Remove the per-user start-up flag of the user who uninstalls (the HKLM entry is removed by RegAsm /u).
  DeleteRegKey HKCU "Software\SolidWorks\AddInsStartup\${ADDINGUID}"
  Delete "$INSTDIR\TubeWorks.AddIn.dll"
  Delete "$INSTDIR\TubeWorks.Geometry.dll"
  RMDir /r "$INSTDIR\Icons"
  RMDir /r "$INSTDIR\Documentation"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  RMDir /r "$SMPROGRAMS\TubeWorks Designer"
  DeleteRegKey HKLM "${UNINSTKEY}"
  ; User settings and the tube library in %APPDATA%\TubeWorks are kept.
SectionEnd
