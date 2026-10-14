param([ValidateSet('x64','arm64')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path $taskRoot 'artifacts/worker'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
if ($Architecture -eq 'arm64') {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $taskVs = & $taskVsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.ARM64 -property installationPath
    if (-not $taskVs) { throw 'Visual Studio ARM64 C++ tools are required.' }
    $taskModule = Join-Path $taskVs 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll'
    Import-Module $taskModule
    $taskHost = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'amd64' }
    Enter-VsDevShell -VsInstallPath $taskVs -SkipAutomaticLocation -DevCmdArguments "-arch=arm64 -host_arch=$taskHost" | Out-Null
    & cl.exe /nologo /utf-8 /std:c++17 /EHsc /O2 /MT /W3 "/I$(Join-Path $taskRoot '.tools/7zip-src/CPP')" "/I$(Join-Path $taskRoot '.tools')" "/Fo$(Join-Path $taskOutput 'worker.obj')" "/Fe$(Join-Path $taskOutput 'ArchiveEngineWorker.exe')" (Join-Path $taskRoot 'src/EngineWorker/main.cpp') /link ole32.lib oleaut32.lib uuid.lib
} else {
    $taskCompiler = (Get-Command g++ -ErrorAction Stop).Source
    & $taskCompiler -std=c++17 -O2 -Wall -Wextra -municode -static -static-libgcc -static-libstdc++ "-I$(Join-Path $taskRoot '.tools/7zip-src/CPP')" "-I$(Join-Path $taskRoot '.tools')" (Join-Path $taskRoot 'src/EngineWorker/main.cpp') -lole32 -loleaut32 -luuid -o (Join-Path $taskOutput 'ArchiveEngineWorker.exe')
}
if ($LASTEXITCODE -ne 0) { throw 'Worker compilation failed.' }
Write-Output "Built $(Join-Path $taskOutput 'ArchiveEngineWorker.exe')"
