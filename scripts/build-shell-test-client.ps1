param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Output, [ValidateSet('x64','arm64')][string]$Architecture = 'x64', [switch]$Library)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if ($Architecture -eq 'arm64') {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $taskVs = & $taskVsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.ARM64 -property installationPath
    if (-not $taskVs) { throw 'Visual Studio ARM64 tools are required for the native Shell probe.' }
    Import-Module (Join-Path $taskVs 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll')
    $taskHost = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'amd64' }
    Enter-VsDevShell -VsInstallPath $taskVs -SkipAutomaticLocation -DevCmdArguments "-arch=arm64 -host_arch=$taskHost" | Out-Null
    [string[]]$taskKind = if ($Library) { @('/LD') } else { @() }
    & cl.exe /nologo /utf-8 /std:c++17 /EHsc /O2 /MT /W3 @taskKind "/I$(Join-Path $taskRoot '.tools')" "/Fo$Output.obj" "/Fe$Output" $Source /link shell32.lib ole32.lib uuid.lib user32.lib
} else {
    [string[]]$taskKind = if ($Library) { @('-shared') } else { @('-municode') }
    & g++ -std=c++17 -O2 -Wall -Wextra @taskKind -static -static-libgcc -static-libstdc++ ("-I" + (Join-Path $taskRoot '.tools')) $Source -lshell32 -lole32 -luuid -o $Output
}
if ($LASTEXITCODE -ne 0) { throw 'Native Shell test client build failed.' }
