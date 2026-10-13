param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskVersion = '10.0.26100.7705'
$taskDirectory = Join-Path $taskRoot ".tools/windows-sdk-buildtools/$taskVersion"
$taskArchive = Join-Path $taskRoot ".tools/microsoft.windows.sdk.buildtools.$taskVersion.nupkg"
$taskUri = "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/$taskVersion/microsoft.windows.sdk.buildtools.$taskVersion.nupkg"
if (-not (Test-Path -LiteralPath $taskArchive)) { Invoke-WebRequest -UseBasicParsing -Uri $taskUri -OutFile $taskArchive }
# Pinned SHA-512 from the official NuGet catalog entry for this exact version.
$taskExpected = 'BIV+AD93Eh3Yv3xE55a9AD/o5f+wmW1NOoSu2DajGsnC324bnshnjr2vhRcyzC8fCY0tIRFX0nZg9yQQ/9+l5w=='
$taskHasher = [Security.Cryptography.SHA512]::Create()
try {
    $taskStream = [IO.File]::OpenRead($taskArchive)
    try { $taskDigest = [Convert]::ToBase64String($taskHasher.ComputeHash($taskStream)) } finally { $taskStream.Dispose() }
} finally { $taskHasher.Dispose() }
if ($taskDigest -ne $taskExpected) { throw 'SDK tool package does not match the NuGet SHA-512.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not (Test-Path -LiteralPath $taskDirectory)) { [IO.Compression.ZipFile]::ExtractToDirectory($taskArchive, $taskDirectory) }
$taskTools = [ordered]@{}
foreach ($taskName in @('makeappx.exe','signtool.exe','mt.exe')) {
    $taskPath = Get-ChildItem -LiteralPath $taskDirectory -Recurse -File -Filter $taskName | Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1
    if (-not $taskPath) { throw "SDK does not contain x64 $taskName." }
    $taskTools[$taskName] = $taskPath.FullName
}
$taskTools['version'] = $taskVersion
$taskTools['archiveSha256'] = (Get-FileHash -LiteralPath $taskArchive).Hash.ToLowerInvariant()
$taskTools
