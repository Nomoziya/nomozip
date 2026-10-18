param([ValidateSet('x64','arm64')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'Stable upgrade acceptance requires disposable CI.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskExpected = if ($Architecture -eq 'arm64') { '34a62555f3f4061e4a49a373f9974dd64a16790f900b6cd6a8ed567ffc79a657' } else { 'b293603731f0e8621b4232b537de7e2eea4b0ba02b9b7702f8d36e7bc4d02e10' }
$taskPrevious = Join-Path $taskRoot "artifacts/stable-0.2.1-$Architecture"
if (Test-Path -LiteralPath $taskPrevious) { throw 'Keep prior upgrade fixtures immutable.' }
$taskZip = "$taskPrevious.zip"
Invoke-WebRequest "https://github.com/Nomoziya/nomozip/releases/download/v0.2.1/nomozip-0.2.1-light-win-$Architecture.zip" -OutFile $taskZip
if ((Get-FileHash $taskZip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskExpected) { throw 'Published 0.2.1 fixture checksum changed.' }
Expand-Archive -LiteralPath $taskZip -DestinationPath $taskPrevious
if (Test-Path "$taskRoot/artifacts/light-installer-validation.json") { Copy-Item "$taskRoot/artifacts/light-installer-validation.json" "$taskRoot/artifacts/installer-legacy-validation.json" }
& "$taskRoot/scripts/test-installer.ps1" -Architecture $Architecture -PreviousArchitecture $Architecture -PreviousApplicationDirectory $taskPrevious -PreviousFrameworkDependent -UsePayloadVersions
Copy-Item "$taskRoot/artifacts/light-installer-validation.json" "$taskRoot/artifacts/installer-0.2.1-upgrade-validation.json"
