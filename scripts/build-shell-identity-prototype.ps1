param([string]$OutputDirectory = ('artifacts/shell-identity/' + [Guid]::NewGuid().ToString('N')),
    [string]$PackageName = 'Nomoziya.NomozipShellPrototype',
    [string]$Publisher = 'CN=Nomoziya.NomozipShellPrototype',
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version = '0.2.0.2',
    [switch]$IncludeValidationProbe)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory))
if (Test-Path -LiteralPath $taskOutput) { throw 'Keep identity prototypes immutable; choose a fresh directory.' }
$taskTools = & (Join-Path $PSScriptRoot 'bootstrap-shell-package-tools.ps1')
$taskPackageDirectory = Join-Path $taskOutput 'identity'
New-Item -ItemType Directory -Path $taskPackageDirectory -Force | Out-Null
$taskLibrary = Join-Path $taskOutput 'NomoZipExplorerPrototype.dll'
& g++ -std=c++17 -O2 -s -Wall -Wextra -shared -static -static-libgcc -static-libstdc++ (Join-Path $taskRoot 'src/ShellPrototype/ExplorerCommand.cpp') -lshlwapi -lshell32 -lole32 -luuid -o $taskLibrary
if ($LASTEXITCODE -ne 0) { throw 'Modern command compilation failed.' }
$taskImports = @(& objdump -p $taskLibrary | ForEach-Object { if ($_ -match 'DLL Name:\s*(.+)$') { $Matches[1].Trim() } })
if ($LASTEXITCODE -ne 0 -or $taskImports.Count -eq 0) { throw 'Cannot inspect native command dependencies.' }
if (@($taskImports | Where-Object { $_ -match '^lib(winpthread|stdc\+\+|gcc)' }).Count) { throw 'Menu DLL still depends on a compiler runtime DLL.' }
[xml]$taskManifest = Get-Content -LiteralPath (Join-Path $taskRoot 'src/ShellPrototype/AppxManifest.xml') -Raw
$taskManifest.Package.Identity.Name = $PackageName; $taskManifest.Package.Identity.Publisher = $Publisher; $taskManifest.Package.Identity.Version = $Version
if ($IncludeValidationProbe) {
    $taskProbeApplication = $taskManifest.Package.Applications.Application.CloneNode($true)
    $taskProbeApplication.Id = 'Probe'; $taskProbeApplication.Executable = 'IdentityProbe.exe'
    $taskProbeApplication.RemoveChild($taskProbeApplication.Extensions) | Out-Null
    $taskManifest.Package.Applications.AppendChild($taskProbeApplication) | Out-Null
}
$taskManifest.Save((Join-Path $taskPackageDirectory 'AppxManifest.xml'))
# Sparse packages contain identity metadata, not the WPF app, engine, or DLL.
$taskMsix = Join-Path $taskOutput 'nomozip.identity.msix'
& $taskTools['makeappx.exe'] pack /d $taskPackageDirectory /p $taskMsix /nv | Set-Content -LiteralPath (Join-Path $taskOutput 'makeappx.log')
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $taskOutput 'makeappx.log'); throw 'Sparse identity packaging failed.' }
$taskReport = [ordered]@{packageName=$PackageName;publisher=$Publisher;version=$Version;identityBytes=(Get-Item -LiteralPath $taskMsix).Length;nativeDllBytes=(Get-Item -LiteralPath $taskLibrary).Length;additionalRuntime=$false;imports=$taskImports;signed=$false;registered=$false;sdk=$taskTools['version'];sdkArchiveSha256=$taskTools['archiveSha256']}
$taskReport | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'build.json') -Encoding utf8
$taskOutput
