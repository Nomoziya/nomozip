param(
    [ValidateSet('x64','arm64')][string]$Architecture = 'x64',
    [ValidateSet('0.2.1','0.3.0','0.3.1')][string]$PreviousVersion = '0.2.1'
)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'Stable upgrade acceptance requires disposable CI.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskExpected = if ($PreviousVersion -eq '0.3.1') {
    if ($Architecture -eq 'arm64') { '94b639839e6fc182e892a19452b0ea5f94759e84ffbd25039b3b4f147ce783df' } else { '1a9509a2638e64dceacaaea4911356236c73dc5eb4ffe0b67d07a29810a71d57' }
} elseif ($PreviousVersion -eq '0.3.0') {
    if ($Architecture -eq 'arm64') { '50f2ae87cef09276b81539caa2c5a2a6374521cb6be522e838078bb1f5b8f7e6' } else { '228cab91ff0f789ca4baf961d46c73bf123a3429a3da0c57f08822518981ff66' }
} else {
    if ($Architecture -eq 'arm64') { '34a62555f3f4061e4a49a373f9974dd64a16790f900b6cd6a8ed567ffc79a657' } else { 'b293603731f0e8621b4232b537de7e2eea4b0ba02b9b7702f8d36e7bc4d02e10' }
}
$taskPrevious = Join-Path $taskRoot "artifacts/stable-$PreviousVersion-$Architecture"
if (Test-Path -LiteralPath $taskPrevious) { throw 'Keep prior upgrade fixtures immutable.' }
$taskZip = "$taskPrevious.zip"
Invoke-WebRequest "https://github.com/Nomoziya/nomozip/releases/download/v$PreviousVersion/nomozip-$PreviousVersion-light-win-$Architecture.zip" -OutFile $taskZip
if ((Get-FileHash $taskZip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskExpected) { throw "Published $PreviousVersion fixture checksum changed." }
Expand-Archive -LiteralPath $taskZip -DestinationPath $taskPrevious
if (Test-Path "$taskRoot/artifacts/light-installer-validation.json") { Copy-Item "$taskRoot/artifacts/light-installer-validation.json" "$taskRoot/artifacts/installer-legacy-validation.json" }
& "$taskRoot/scripts/test-installer.ps1" -Architecture $Architecture -PreviousArchitecture $Architecture -PreviousApplicationDirectory $taskPrevious -PreviousFrameworkDependent -UsePayloadVersions
Copy-Item "$taskRoot/artifacts/light-installer-validation.json" "$taskRoot/artifacts/installer-$PreviousVersion-upgrade-validation.json"
