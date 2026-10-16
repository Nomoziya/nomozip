# -*- coding: utf-8 -*-
Unicode true
!define MUI_ICON "..\assets\nomozip.ico"
!define MUI_UNICON "..\assets\nomozip.ico"
!include MUI2.nsh
!include LogicLib.nsh
!include FileFunc.nsh
!include x64.nsh
!include nsDialogs.nsh
!ifndef ARCHITECTURE
  !define ARCHITECTURE "x64"
!endif

Name "nomozip"
OutFile "${OUTPUT_FILE}"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetOverwrite on
BrandingText "$(Branding)"
VIProductVersion "${VERSION}"
VIAddVersionKey /LANG=2052 "ProductName" "nomozip"
VIAddVersionKey /LANG=2052 "FileDescription" "nomozip 当前用户安装程序"
VIAddVersionKey /LANG=1033 "FileDescription" "nomozip per-user installer"
VIAddVersionKey /LANG=1033 "ProductName" "nomozip"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${DISPLAY_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Nomoziya"
VIAddVersionKey /LANG=2052 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=2052 "ProductVersion" "${DISPLAY_VERSION}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Nomoziya"

!ifdef VALIDATION_ROOT
  !define REG_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\nomozip-validation"
  !define DATA_DIR "${VALIDATION_ROOT}\data"
  !define SHORTCUT_DIR "${VALIDATION_ROOT}\shortcuts"
  !define ASSOCIATION_ROOT "Software\nomozip-validation\InstallerAssociations"
  InstallDir "${VALIDATION_ROOT}\app"
!else
  !define REG_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\nomozip"
  !define DATA_DIR "$LOCALAPPDATA\ArchivePassword"
  !define SHORTCUT_DIR "$SMPROGRAMS\nomozip"
  !define ASSOCIATION_ROOT "Software\Classes"
  InstallDir "$LOCALAPPDATA\Programs\nomozip"
  InstallDirRegKey HKCU "${REG_KEY}" "InstallLocation"
!endif

Var DeleteData
Var DeleteCheckbox
Var Dialog
Var PreserveAssociations
Var OriginalOpenCommand
Var PreserveShellMenu
Var UpdateMode
Var UpdateAttempts
!include "shell-menu.nsh"
!define MUI_LANGDLL_REGISTRY_ROOT HKCU
!define MUI_LANGDLL_REGISTRY_KEY "${REG_KEY}"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "$(WelcomeTitle)"
!define MUI_WELCOMEPAGE_TEXT "$(WelcomeBody)"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TEXT "$(FinishBody)"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_WELCOME
UninstPage custom un.DataPage un.DataPageLeave
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"
!include "nomozip-languages.nsh"

!macro CheckNotRunning PREFIX
  IfFileExists "$INSTDIR\ArchivePassword.exe" 0 ${PREFIX}end
  ClearErrors
  FileOpen $0 "$INSTDIR\ArchivePassword.exe" a
  IfErrors ${PREFIX}locked
  FileClose $0
  IfFileExists "$INSTDIR\nomozip-cli.exe" 0 ${PREFIX}end
  FileOpen $0 "$INSTDIR\nomozip-cli.exe" a
  IfErrors ${PREFIX}locked
  FileClose $0
  Goto ${PREFIX}end
${PREFIX}locked:
  IfSilent ${PREFIX}abort
  MessageBox MB_OK|MB_ICONEXCLAMATION "$(CloseFirst)"
${PREFIX}abort:
  SetErrorLevel 2
  Abort
${PREFIX}end:
!macroend

Function .onInit
  SetShellVarContext current
  StrCpy $UpdateMode 0
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/UPDATE" $1
  ${IfNot} ${Errors}
    StrCpy $UpdateMode 1
    ; Automatic updates may only replace this user's registered installation.
    ReadRegStr $0 HKCU "${REG_KEY}" "InstallLocation"
    StrCmp $0 $INSTDIR 0 updateabort
    IfFileExists "$INSTDIR\ArchivePassword.exe" 0 updateabort
    StrCpy $UpdateAttempts 200
updatewait:
    ClearErrors
    FileOpen $0 "$INSTDIR\ArchivePassword.exe" a
    IfErrors updatebusy
    FileClose $0
    IfFileExists "$INSTDIR\nomozip-cli.exe" 0 updateready
    FileOpen $0 "$INSTDIR\nomozip-cli.exe" a
    IfErrors updatebusy
    FileClose $0
    Goto updateready
updatebusy:
    Sleep 100
    IntOp $UpdateAttempts $UpdateAttempts - 1
    IntCmp $UpdateAttempts 0 updateabort updateabort updatewait
updateabort:
    !ifndef VALIDATION_ROOT
      MessageBox MB_OK|MB_ICONSTOP "$(UpdateBlocked)"
    !endif
    SetErrorLevel 2
    Abort
updateready:
  ${EndIf}
  !insertmacro MUI_LANGDLL_DISPLAY
!if "${ARCHITECTURE}" == "arm64"
  ${IfNot} ${IsNativeARM64}
    IfSilent +2
    MessageBox MB_OK|MB_ICONSTOP "$(NeedArm64)"
    SetErrorLevel 2
    Abort
  ${EndIf}
!else
  ${IfNot} ${RunningX64}
  ${AndIfNot} ${IsNativeARM64}
    IfSilent +2
    MessageBox MB_OK|MB_ICONSTOP "$(NeedX64)"
    SetErrorLevel 2
    Abort
  ${EndIf}
!endif
FunctionEnd

Function un.onInit
  SetShellVarContext current
  !insertmacro MUI_UNGETLANGUAGE
  StrCpy $DeleteData 0
  !insertmacro CheckNotRunning "uninit"
  !ifdef VALIDATION_ROOT
    ${GetParameters} $0
    ${GetOptions} $0 "/DELETEDATA=" $1
    ${If} $1 == "1"
      StrCpy $DeleteData 1
    ${EndIf}
  !endif
FunctionEnd

Function un.DataPage
  !insertmacro MUI_HEADER_TEXT "$(UninstallTitle)" "$(UninstallSubtitle)"
  nsDialogs::Create 1018
  Pop $Dialog
  ${If} $Dialog == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 42u "$(UninstallBody)"
  Pop $0
  ${NSD_CreateCheckbox} 0 58u 100% 30u "$(DeleteDataLabel)"
  Pop $DeleteCheckbox
  ${NSD_Uncheck} $DeleteCheckbox
  nsDialogs::Show
FunctionEnd

Function un.DataPageLeave
  ${NSD_GetState} $DeleteCheckbox $DeleteData
  ${If} $DeleteData == ${BST_CHECKED}
    MessageBox MB_YESNO|MB_DEFBUTTON2|MB_ICONEXCLAMATION "$(ConfirmDelete)" IDYES confirmed
    Abort
confirmed:
  ${EndIf}
FunctionEnd

Section "$(InstallSection)"
  !insertmacro CheckNotRunning "install"
  ; Run only a registered installation's own cleanup list. This removes old bundled runtime
  ; files during a full-to-light upgrade without deleting unrelated files or user data.
  ReadRegStr $0 HKCU "${REG_KEY}" "InstallLocation"
  StrCmp $0 $INSTDIR 0 cleanupdone
  IfFileExists "$INSTDIR\Uninstall.exe" 0 cleanupdone
  StrCpy $PreserveAssociations 0
  ReadRegStr $OriginalOpenCommand HKCU "${ASSOCIATION_ROOT}\nomozip.Archive\shell\open\command" ""
  StrCmp $OriginalOpenCommand '"$INSTDIR\ArchivePassword.exe" "%1"' 0 runoldcleanup
  ; Keep the ProgID present so Windows defaults survive legacy uninstallers. Trailing
  ; whitespace leaves command semantics unchanged but avoids their exact ownership match.
  StrCpy $PreserveAssociations 1
  WriteRegStr HKCU "${ASSOCIATION_ROOT}\nomozip.Archive\shell\open\command" "" "$OriginalOpenCommand "
runoldcleanup:
  !insertmacro PrepareShellUpgrade
  ExecWait '"$INSTDIR\Uninstall.exe" /S _?=$INSTDIR' $1
  ${If} $PreserveShellMenu == 1
    !insertmacro SetShellOwners "$INSTDIR\ArchivePassword.exe"
  ${EndIf}
  ${If} $PreserveAssociations == 1
    WriteRegStr HKCU "${ASSOCIATION_ROOT}\nomozip.Archive\shell\open\command" "" "$OriginalOpenCommand"
  ${EndIf}
  ${If} $1 != 0
    IfSilent +2
    MessageBox MB_OK|MB_ICONSTOP "$(CleanupFailed)"
    SetErrorLevel 2
    Abort
  ${EndIf}
cleanupdone:
  SetOutPath "$INSTDIR"
  File /r "${APP_DIR}\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "${SHORTCUT_DIR}"
  CreateShortcut "${SHORTCUT_DIR}\nomozip.lnk" "$INSTDIR\ArchivePassword.exe"
  Delete "${SHORTCUT_DIR}\卸载 nomozip.lnk"
  Delete "${SHORTCUT_DIR}\Uninstall nomozip.lnk"
  CreateShortcut "${SHORTCUT_DIR}\$(UninstallShortcut)" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${REG_KEY}" "DisplayName" "$(InstalledName)"
  WriteRegStr HKCU "${REG_KEY}" "InstallerLanguage" "$LANGUAGE"
  WriteRegStr HKCU "${REG_KEY}" "DisplayVersion" "${DISPLAY_VERSION}"
  WriteRegStr HKCU "${REG_KEY}" "Publisher" "Nomoziya"
  WriteRegStr HKCU "${REG_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${REG_KEY}" "DisplayIcon" "$INSTDIR\ArchivePassword.exe"
  WriteRegStr HKCU "${REG_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${REG_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${REG_KEY}" "NoRepair" 1
SectionEnd

Function .onInstSuccess
  ${If} $UpdateMode == 1
    !ifdef VALIDATION_ROOT
      ; Exercise real restart with the existing isolated installed-app validation.
      !ifdef FRAMEWORK_DEPENDENT
        ExecWait '"$INSTDIR\ArchivePassword.exe" --validate-installed "${VALIDATION_ROOT}\..\..\.." --data-dir "${VALIDATION_ROOT}\data-update-restart" --shared-runtime' $1
      !else
        ExecWait '"$INSTDIR\ArchivePassword.exe" --validate-installed "${VALIDATION_ROOT}\..\..\.." --data-dir "${VALIDATION_ROOT}\data-update-restart"' $1
      !endif
      ${If} $1 != 0
        SetErrorLevel 2
        Return
      ${EndIf}
      FileOpen $0 "${VALIDATION_ROOT}\update-completed.txt" w
      FileWrite $0 "$INSTDIR"
      FileClose $0
    !else
      ClearErrors
      Exec '"$INSTDIR\ArchivePassword.exe"'
      ${If} ${Errors}
        MessageBox MB_OK|MB_ICONEXCLAMATION "$(UpdateRestartFailed)"
      ${EndIf}
    !endif
  ${EndIf}
FunctionEnd

Function .onInstFailed
  !ifndef VALIDATION_ROOT
    ${If} $UpdateMode == 1
      MessageBox MB_OK|MB_ICONSTOP "$(UpdateFailed)"
    ${EndIf}
  !endif
FunctionEnd

Section "Uninstall"
  !insertmacro CheckNotRunning "uninstall"
  ReadRegStr $0 HKCU "${ASSOCIATION_ROOT}\nomozip.Archive\shell\open\command" ""
  StrCmp $0 '"$INSTDIR\ArchivePassword.exe" "%1"' 0 associationsdone
  DeleteRegValue HKCU "${ASSOCIATION_ROOT}\.7z\OpenWithProgids" "nomozip.Archive"
  DeleteRegValue HKCU "${ASSOCIATION_ROOT}\.zip\OpenWithProgids" "nomozip.Archive"
  DeleteRegValue HKCU "${ASSOCIATION_ROOT}\.rar\OpenWithProgids" "nomozip.Archive"
  DeleteRegKey HKCU "${ASSOCIATION_ROOT}\nomozip.Archive"
  !ifndef VALIDATION_ROOT
    System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
  !endif
associationsdone:
  !insertmacro RemoveOwnedShellMenu
  !ifndef VALIDATION_ROOT
    System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
  !endif
  ; Only remove files owned by this installer; preserve unrelated files in the chosen directory.
  !include "${OWNED_FILES}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "${SHORTCUT_DIR}\nomozip.lnk"
  Delete "${SHORTCUT_DIR}\卸载 nomozip.lnk"
  Delete "${SHORTCUT_DIR}\Uninstall nomozip.lnk"
  RMDir "${SHORTCUT_DIR}"
  DeleteRegKey HKCU "${REG_KEY}"
  ${If} $DeleteData == 1
    ; Fixed product data directory, never inferred from /D or arbitrary runtime arguments.
    RMDir /r "${DATA_DIR}"
  ${EndIf}
SectionEnd
