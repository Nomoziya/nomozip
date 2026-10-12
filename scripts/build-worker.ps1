$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path $taskRoot 'artifacts/worker'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskCompiler = (Get-Command g++ -ErrorAction Stop).Source
& $taskCompiler -std=c++17 -O2 -Wall -Wextra -municode -static -static-libgcc -static-libstdc++ "-I$(Join-Path $taskRoot '.tools/7zip-src/CPP')" "-I$(Join-Path $taskRoot '.tools')" (Join-Path $taskRoot 'src/EngineWorker/main.cpp') -lole32 -loleaut32 -luuid -o (Join-Path $taskOutput 'ArchiveEngineWorker.exe')
if ($LASTEXITCODE -ne 0) { throw 'Worker compilation failed.' }
Write-Output "Built $(Join-Path $taskOutput 'ArchiveEngineWorker.exe')"
