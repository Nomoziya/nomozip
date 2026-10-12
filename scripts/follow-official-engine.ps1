param([switch]$Update)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskRelease = Invoke-RestMethod 'https://api.github.com/repos/ip7z/7zip/releases/latest'
$taskVersion = $taskRelease.tag_name
if ($taskVersion -notmatch '^\d{2}\.\d{2}$' -or $taskRelease.prerelease) { throw 'Unrecognized official stable release.' }
Write-Output "Official stable engine: $taskVersion"
$taskEngine = Join-Path $taskRoot "engines/$taskVersion"
$taskManifestPath = Join-Path $taskEngine 'manifest.json'
if (Test-Path $taskManifestPath) {
    $taskExisting = Get-Content -Raw $taskManifestPath | ConvertFrom-Json
    if ($taskExisting.protocol -eq 1) { Write-Output 'This version is already available locally. No update needed.'; return }
}
if (-not $Update) { Write-Output 'A new engine is available. Run with -Update to download, validate and rebuild the local product.'; return }
$taskAsset = $taskRelease.assets | Where-Object name -Match '^7z\d+-x64\.exe$' | Select-Object -First 1
$taskUri = [uri]$taskAsset.browser_download_url
if ($taskUri.Host -ne 'github.com' -or -not $taskUri.AbsolutePath.StartsWith('/ip7z/7zip/releases/download/')) { throw 'Unexpected upstream asset source.' }
$taskDownload = Join-Path $taskRoot ".tools/$($taskAsset.name)"
Invoke-WebRequest $taskUri.AbsoluteUri -OutFile $taskDownload
$taskDigest = (Get-FileHash $taskDownload -Algorithm SHA256).Hash.ToLowerInvariant()
if ($taskAsset.digest -and $taskAsset.digest -ne "sha256:$taskDigest") { throw 'Upstream download checksum mismatch.' }
New-Item -ItemType Directory -Force $taskEngine | Out-Null
& (Get-Command 7z -ErrorAction Stop).Source x $taskDownload "-o$taskEngine" -y 7z.dll 7z.exe License.txt | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Official engine extraction failed.' }
$taskManifest = [ordered]@{
    version = $taskVersion; architecture = 'x64'; protocol = 0
    source = $taskUri.AbsoluteUri
    dllSha256 = (Get-FileHash (Join-Path $taskEngine '7z.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
}
# Protocol 0 keeps a candidate out of the desktop catalog until its tests pass.
$taskManifest | ConvertTo-Json | Set-Content $taskManifestPath -Encoding utf8
$taskPython = (Get-Command python -ErrorAction SilentlyContinue).Source
if (-not $taskPython -or $taskPython -like '*WindowsApps*') { $taskPython = 'C:\Users\Nomozi\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' }
& $taskPython (Join-Path $taskRoot 'tests/engine_validation.py') --versions "$taskVersion,26.04,26.03"
if ($LASTEXITCODE -ne 0) { throw 'Compatibility checks failed. The candidate remains disabled; the existing app is unchanged.' }
$taskManifest.protocol = 1
$taskManifest | ConvertTo-Json | Set-Content $taskManifestPath -Encoding utf8
& (Join-Path $PSScriptRoot 'build.ps1') -SkipWorker
Write-Output 'The validated engine is now in the rebuilt local product. Select it in Settings.'
