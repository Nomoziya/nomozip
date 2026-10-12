param([switch]$SkipSdk)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskTools = Join-Path $taskRoot '.tools'
New-Item -ItemType Directory -Force -Path $taskTools | Out-Null
$taskSevenZip = (Get-Command 7z -ErrorAction Stop).Source
foreach ($taskVersion in @('26.04', '26.03')) {
    $taskDigits = $taskVersion.Replace('.', '')
    $taskEngine = Join-Path $taskRoot "engines/$taskVersion"
    $taskInstaller = Join-Path $taskTools "7z$taskDigits-x64.exe"
    if (-not (Test-Path $taskInstaller)) {
        Invoke-WebRequest "https://github.com/ip7z/7zip/releases/download/$taskVersion/7z$taskDigits-x64.exe" -OutFile $taskInstaller
    }
    New-Item -ItemType Directory -Force -Path $taskEngine | Out-Null
    & $taskSevenZip x $taskInstaller "-o$taskEngine" -y 7z.dll 7z.exe License.txt | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Engine extraction failed.' }
    $taskManifest = [ordered]@{
        version = $taskVersion; architecture = 'x64'; protocol = 1
        source = "https://github.com/ip7z/7zip/releases/download/$taskVersion/7z$taskDigits-x64.exe"
        dllSha256 = (Get-FileHash (Join-Path $taskEngine '7z.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $taskManifest | ConvertTo-Json | Set-Content (Join-Path $taskEngine 'manifest.json') -Encoding utf8
    Write-Output "Prepared official engine $taskVersion"
}
$taskSourceArchive = Join-Path $taskTools '7z2604-src.7z'
if (-not (Test-Path $taskSourceArchive)) {
    Invoke-WebRequest 'https://github.com/ip7z/7zip/releases/download/26.04/7z2604-src.7z' -OutFile $taskSourceArchive
}
$taskSource = Join-Path $taskTools '7zip-src'
& $taskSevenZip x $taskSourceArchive "-o$taskSource" -y | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Source extraction failed.' }
$taskJson = Join-Path $taskTools 'json.hpp'
if (-not (Test-Path $taskJson)) {
    Invoke-WebRequest 'https://raw.githubusercontent.com/nlohmann/json/v3.12.0/single_include/nlohmann/json.hpp' -OutFile $taskJson
    Invoke-WebRequest 'https://raw.githubusercontent.com/nlohmann/json/v3.12.0/LICENSE.MIT' -OutFile (Join-Path $taskTools 'json-LICENSE.MIT')
}
$taskNugetSource = Join-Path $taskTools 'nuget-source'
New-Item -ItemType Directory -Force -Path $taskNugetSource | Out-Null
$taskDpapiPackage = Join-Path $taskNugetSource 'system.security.cryptography.protecteddata.10.0.12.nupkg'
if (-not (Test-Path $taskDpapiPackage)) {
    Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/system.security.cryptography.protecteddata/10.0.12/system.security.cryptography.protecteddata.10.0.12.nupkg' -OutFile $taskDpapiPackage
}
if (-not $SkipSdk) {
    $taskDotnet = Join-Path $taskTools 'dotnet/dotnet.exe'
    if (-not (Test-Path $taskDotnet)) {
        $taskRelease = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
        $taskPinnedSdk = (Get-Content -Raw (Join-Path $taskRoot 'global.json') | ConvertFrom-Json).sdk.version
        $taskSdk = $taskRelease.releases | ForEach-Object { $_.sdk; $_.sdks } | Where-Object { $_.version -eq $taskPinnedSdk } | Select-Object -First 1
        if (-not $taskSdk) { throw 'Pinned SDK was not found in official release metadata.' }
        $taskPackage = $taskSdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name.EndsWith('.zip') } | Select-Object -First 1
        $taskZip = Join-Path $taskTools 'dotnet-sdk.zip'
        Write-Output "Downloading local .NET SDK $($taskSdk.version)"
        Invoke-WebRequest $taskPackage.url -OutFile $taskZip
        if ((Get-FileHash $taskZip -Algorithm SHA512).Hash -ne $taskPackage.hash) { throw 'SDK checksum mismatch.' }
        & $taskSevenZip x $taskZip "-o$(Join-Path $taskTools 'dotnet')" -y | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'SDK extraction failed.' }
        $taskSdk.version | Set-Content (Join-Path $taskTools 'sdk-version.txt')
    }
    & $taskDotnet --version
}
