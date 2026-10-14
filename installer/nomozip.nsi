# -*- coding: utf-8 -*-
Unicode true
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
BrandingText "nomozip · 解压与密码管理"
VIProductVersion "${VERSION}"
VIAddVersionKey /LANG=2052 "ProductName" "nomozip"
VIAddVersionKey /LANG=2052 "FileDescription" "nomozip 当前用户安装程序"
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
!include "shell-menu.nsh"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "安装 nomozip"
!ifdef FRAMEWORK_DEPENDENT
  !define MUI_WELCOMEPAGE_TEXT "保存解压密码，下次自动尝试。$\r$\n$\r$\n这是 ${ARCHITECTURE} 轻量版，使用系统共享的 .NET 10 Windows Desktop Runtime ${ARCHITECTURE}（10.0.12 或更高的 10.0 补丁版），无需 SDK。未安装运行库时，请先从 dotnet.microsoft.com 下载并安装。$\r$\n$\r$\n升级保留密码库、设置和下载的引擎，并清理上一安装的程序文件。安装前请关闭 nomozip。"
!else
  !define MUI_WELCOMEPAGE_TEXT "保存解压密码，下次自动尝试。$\r$\n$\r$\n仅为当前 Windows 用户安装，已包含 .NET 运行组件。升级会保留密码库、设置和已下载的引擎。$\r$\n$\r$\n软件仍为开发预览。安装前请关闭正在运行的 nomozip。"
!endif
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TEXT "nomozip 已安装。可从开始菜单打开软件，或查看安装目录中的使用说明。"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_WELCOME
UninstPage custom un.DataPage un.DataPageLeave
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "SimpChinese"

!macro CheckNotRunning PREFIX
  IfFileExists "$INSTDIR\ArchivePassword.exe" 0 ${PREFIX}end
  ClearErrors
  FileOpen $0 "$INSTDIR\ArchivePassword.exe" a
  IfErrors ${PREFIX}locked
  FileClose $0
  Goto ${PREFIX}end
${PREFIX}locked:
  IfSilent ${PREFIX}abort
  MessageBox MB_OK|MB_ICONEXCLAMATION "请先关闭 nomozip，再继续安装或卸载。文件暂未更改。"
${PREFIX}abort:
  SetErrorLevel 2
  Abort
${PREFIX}end:
!macroend

Function .onInit
  SetShellVarContext current
!if "${ARCHITECTURE}" == "arm64"
  ${IfNot} ${IsNativeARM64}
    IfSilent +2
    MessageBox MB_OK|MB_ICONSTOP "此版本需要 ARM64 Windows。"
    SetErrorLevel 2
    Abort
  ${EndIf}
!else
  ${IfNot} ${RunningX64}
  ${AndIfNot} ${IsNativeARM64}
    IfSilent +2
    MessageBox MB_OK|MB_ICONSTOP "此版本需要 64 位 Windows。"
    SetErrorLevel 2
    Abort
  ${EndIf}
!endif
FunctionEnd

Function un.onInit
  SetShellVarContext current
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
  !insertmacro MUI_HEADER_TEXT "卸载与本机数据" "默认保留密码库和设置，便于重新安装后继续使用。"
  nsDialogs::Create 1018
  Pop $Dialog
  ${If} $Dialog == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 42u "卸载将移除程序和开始菜单入口。$\r$\n保存的密码、设置及下载的引擎默认保留在当前用户的本机目录中。"
  Pop $0
  ${NSD_CreateCheckbox} 0 58u 100% 30u "同时删除我的密码库、设置和已下载的引擎（无法恢复）"
  Pop $DeleteCheckbox
  ${NSD_Uncheck} $DeleteCheckbox
  nsDialogs::Show
FunctionEnd

Function un.DataPageLeave
  ${NSD_GetState} $DeleteCheckbox $DeleteData
  ${If} $DeleteData == ${BST_CHECKED}
    MessageBox MB_YESNO|MB_DEFBUTTON2|MB_ICONEXCLAMATION "确定删除本机保存的全部 nomozip 数据？$\r$\n密码库、设置和下载的引擎将无法恢复。" IDYES confirmed
    Abort
confirmed:
  ${EndIf}
FunctionEnd

Section "安装"
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
    MessageBox MB_OK|MB_ICONSTOP "上一安装未能完成清理。请关闭 nomozip 后重试；本机密码库和设置保持不变。"
    SetErrorLevel 2
    Abort
  ${EndIf}
cleanupdone:
  SetOutPath "$INSTDIR"
  File /r "${APP_DIR}\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "${SHORTCUT_DIR}"
  CreateShortcut "${SHORTCUT_DIR}\nomozip.lnk" "$INSTDIR\ArchivePassword.exe"
  CreateShortcut "${SHORTCUT_DIR}\卸载 nomozip.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${REG_KEY}" "DisplayName" "nomozip 开发预览"
  WriteRegStr HKCU "${REG_KEY}" "DisplayVersion" "${DISPLAY_VERSION}"
  WriteRegStr HKCU "${REG_KEY}" "Publisher" "Nomoziya"
  WriteRegStr HKCU "${REG_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${REG_KEY}" "DisplayIcon" "$INSTDIR\ArchivePassword.exe"
  WriteRegStr HKCU "${REG_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${REG_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${REG_KEY}" "NoRepair" 1
SectionEnd

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
  RMDir "${SHORTCUT_DIR}"
  DeleteRegKey HKCU "${REG_KEY}"
  ${If} $DeleteData == 1
    ; Fixed product data directory, never inferred from /D or arbitrary runtime arguments.
    RMDir /r "${DATA_DIR}"
  ${EndIf}
SectionEnd
