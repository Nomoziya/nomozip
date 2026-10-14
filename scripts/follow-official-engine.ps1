param([switch]$Update, [ValidateSet('x64','arm64')][string]$Architecture = 'x64', [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskRelease = Invoke-RestMethod 'https://api.github.com/repos/ip7z/7zip/releases/latest'
$taskVersion = $taskRelease.tag_name
if ($taskVersion -notmatch '^\d{2}\.\d{2}$' -or $taskRelease.prerelease) { throw 'Unrecognized official stable release.' }
Write-Output "Official stable engine: $taskVersion ($Architecture)"
$taskEngine = Join-Path $taskRoot $(if ($Architecture -eq 'arm64') { "engines/arm64/$taskVersion" } else { "engines/$taskVersion" })
if (Test-Path (Join-Path $taskEngine 'manifest.json')) {
    $taskExisting = Get-Content (Join-Path $taskEngine 'manifest.json') -Raw | ConvertFrom-Json
    if ($taskExisting.protocol -eq 1 -and $taskExisting.architecture -eq $Architecture) { Write-Output 'This native version is already available. No update needed.'; return }
}
if (-not $Update) { Write-Output 'A new engine is available. Run with -Update on the matching native Windows environment to validate it.'; return }
$taskPython = (Get-Command python -ErrorAction SilentlyContinue).Source
if (-not $taskPython -or $taskPython -like '*WindowsApps*') { $taskPython = (Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe') }
$taskCandidate = Join-Path $taskRoot ('artifacts/upstream-' + $Architecture + '-' + [Guid]::NewGuid().ToString('N'))
# Check the official digest and all ten native matrices without signing or publishing.
& (Join-Path $PSScriptRoot 'maintain-engine-release.ps1') -Mode Prepare -CandidateDirectory $taskCandidate -Version $taskVersion -Architecture $Architecture -Python $taskPython
if (Test-Path $taskEngine) { throw 'A prior disabled engine exists; review it before replacing it. The isolated validated candidate was retained.' }
New-Item -ItemType Directory -Force (Split-Path $taskEngine -Parent) | Out-Null
Copy-Item -LiteralPath (Join-Path $taskCandidate "workspace/engines/$taskVersion") -Destination $taskEngine -Recurse
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $taskCandidate 'product' }
& (Join-Path $PSScriptRoot 'build.ps1') -SkipWorker -Architecture $Architecture -OutputDirectory $OutputDirectory
Write-Output "Native engine validated; rebuilt candidate product: $OutputDirectory"
