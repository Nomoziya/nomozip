param([string]$ApplicationDirectory = 'artifacts/app')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskDirectory = Join-Path $taskRoot ('artifacts/shell-modern/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $taskDirectory | Out-Null
$taskLibrary = Join-Path $taskDirectory 'NomoZipExplorerPrototype.dll'
& g++ -std=c++17 -O2 -s -Wall -Wextra -shared -static -static-libgcc -static-libstdc++ (Join-Path $taskRoot 'src/ShellPrototype/ExplorerCommand.cpp') -lshlwapi -lshell32 -lole32 -luuid -o $taskLibrary
if ($LASTEXITCODE -ne 0) { throw 'Modern menu prototype compilation failed.' }
$taskClient = Join-Path $taskDirectory 'ShellClient.exe'
& g++ -std=c++17 -O2 -municode -static -static-libgcc -static-libstdc++ ("-I" + (Join-Path $taskRoot '.tools')) (Join-Path $taskRoot 'tests/ShellValidation/client.cpp') -lshell32 -lole32 -luuid -o $taskClient
if ($LASTEXITCODE -ne 0) { throw 'Modern menu client compilation failed.' }
& (Join-Path $PSScriptRoot 'test-shell-command-state.ps1') -LibraryPath $taskLibrary -ClientPath $taskClient
$taskApp = Join-Path (Join-Path $taskRoot $ApplicationDirectory) 'ArchivePassword.exe'
$taskArchive = Join-Path $taskDirectory '原生接口验证.7z'
Copy-Item -LiteralPath (Join-Path $taskRoot 'tests/fixtures/generated/encrypted-header.7z') -Destination $taskArchive
$taskServer = Start-Process -FilePath $taskApp -ArgumentList @('--shell-server','--validate-shell-server',('"' + $taskRoot + '"')) -WindowStyle Hidden -PassThru
try {
    & $taskClient '{c708b15d-0d3e-47ab-8fc8-c22d21ff1173}' (Join-Path $taskDirectory 'client.json') --modern-dll $taskLibrary $taskArchive
    if ($LASTEXITCODE -ne 0) { throw 'Modern interface could not delegate to the existing application.' }
    if (-not $taskServer.WaitForExit(30000) -or $taskServer.ExitCode -ne 0) { throw 'Modern prototype desktop endpoint failed.' }
    $taskDesktop = Get-Content -Raw (Join-Path $taskRoot 'artifacts/shell-server-Compress-validation.json') | ConvertFrom-Json
    if (-not $taskDesktop.passed) { throw 'Modern prototype lost selected items.' }
} finally { if (-not $taskServer.HasExited) { Stop-Process -Id $taskServer.Id } }
[xml]$taskManifest = Get-Content -Raw (Join-Path $taskRoot 'src/ShellPrototype/AppxManifest.xml')
$taskNamespaces = [Xml.XmlNamespaceManager]::new($taskManifest.NameTable)
$taskNamespaces.AddNamespace('com','http://schemas.microsoft.com/appx/manifest/com/windows10')
$taskNamespaces.AddNamespace('d5','http://schemas.microsoft.com/appx/manifest/desktop/windows10/5')
$taskNativeClass = $taskManifest.SelectSingleNode('//com:SurrogateServer/com:Class',$taskNamespaces).Id
$taskManifestMatches = @($taskManifest.SelectNodes('//d5:Verb',$taskNamespaces) | Where-Object { $_.Clsid -ne $taskNativeClass }).Count -eq 0
$taskReport = [ordered]@{passed=$taskManifestMatches;nativeInterfaceDelegation=$true;manifestIdentityMatches=$taskManifestMatches;prototypeDllBytes=(Get-Item $taskLibrary).Length;includedInProduct=$false;packageRegistrationTested=$false;actualWindows11MenuTested=$false;directory=$taskDirectory}
$taskReport | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $taskRoot 'artifacts/shell-modern-prototype-validation.json') -Encoding utf8
if (-not $taskManifestMatches) { throw 'Modern manifest CLSIDs do not match.' }
Write-Output 'Modern COM/manifest prototype passed; package identity registration and Windows 11 display remain separate acceptance.'
