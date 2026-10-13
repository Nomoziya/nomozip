!define SHELL_HERE "{c708b15d-0d3e-47ab-8fc8-c22d21ff1171}"
!define SHELL_FOLDER "{c708b15d-0d3e-47ab-8fc8-c22d21ff1172}"
!define SHELL_COMPRESS "{c708b15d-0d3e-47ab-8fc8-c22d21ff1173}"

!macro SetShellOwners VALUE
  WriteRegStr HKCU "${ASSOCIATION_ROOT}\CLSID\${SHELL_HERE}" "NomozipOwner" "${VALUE}"
  WriteRegStr HKCU "${ASSOCIATION_ROOT}\CLSID\${SHELL_FOLDER}" "NomozipOwner" "${VALUE}"
  WriteRegStr HKCU "${ASSOCIATION_ROOT}\CLSID\${SHELL_COMPRESS}" "NomozipOwner" "${VALUE}"
  WriteRegStr HKCU "${ASSOCIATION_ROOT}\SystemFileAssociations\.7z\shell\nomozip" "NomozipOwner" "${VALUE}"
  WriteRegStr HKCU "${ASSOCIATION_ROOT}\SystemFileAssociations\.zip\shell\nomozip" "NomozipOwner" "${VALUE}"
  WriteRegStr HKCU "${ASSOCIATION_ROOT}\SystemFileAssociations\.rar\shell\nomozip" "NomozipOwner" "${VALUE}"
  WriteRegStr HKCU "${ASSOCIATION_ROOT}\*\shell\nomozip" "NomozipOwner" "${VALUE}"
  WriteRegStr HKCU "${ASSOCIATION_ROOT}\Directory\shell\nomozip" "NomozipOwner" "${VALUE}"
!macroend

!macro CheckShellOwner PATH
  ReadRegStr $5 HKCU "${ASSOCIATION_ROOT}\${PATH}" "NomozipOwner"
  ${If} $5 != "$INSTDIR\ArchivePassword.exe"
    StrCpy $PreserveShellMenu 0
  ${EndIf}
!macroend

!macro PrepareShellUpgrade
  StrCpy $PreserveShellMenu 1
  !insertmacro CheckShellOwner "CLSID\${SHELL_HERE}"
  !insertmacro CheckShellOwner "CLSID\${SHELL_FOLDER}"
  !insertmacro CheckShellOwner "CLSID\${SHELL_COMPRESS}"
  !insertmacro CheckShellOwner "SystemFileAssociations\.7z\shell\nomozip"
  !insertmacro CheckShellOwner "SystemFileAssociations\.zip\shell\nomozip"
  !insertmacro CheckShellOwner "SystemFileAssociations\.rar\shell\nomozip"
  !insertmacro CheckShellOwner "*\shell\nomozip"
  !insertmacro CheckShellOwner "Directory\shell\nomozip"
  ${If} $PreserveShellMenu == 1
    !insertmacro SetShellOwners "$INSTDIR\ArchivePassword.exe "
  ${EndIf}
!macroend

!macro RemoveOwnedShellKey PATH
  ReadRegStr $5 HKCU "${ASSOCIATION_ROOT}\${PATH}" "NomozipOwner"
  ${If} $5 == "$INSTDIR\ArchivePassword.exe"
    DeleteRegKey HKCU "${ASSOCIATION_ROOT}\${PATH}"
  ${EndIf}
!macroend

!macro RemoveOwnedShellMenu
  !insertmacro RemoveOwnedShellKey "CLSID\${SHELL_HERE}"
  !insertmacro RemoveOwnedShellKey "CLSID\${SHELL_FOLDER}"
  !insertmacro RemoveOwnedShellKey "CLSID\${SHELL_COMPRESS}"
  !insertmacro RemoveOwnedShellKey "SystemFileAssociations\.7z\shell\nomozip"
  !insertmacro RemoveOwnedShellKey "SystemFileAssociations\.zip\shell\nomozip"
  !insertmacro RemoveOwnedShellKey "SystemFileAssociations\.rar\shell\nomozip"
  !insertmacro RemoveOwnedShellKey "*\shell\nomozip"
  !insertmacro RemoveOwnedShellKey "Directory\shell\nomozip"
!macroend
