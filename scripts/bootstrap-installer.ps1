$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskTools = Join-Path $taskRoot '.tools'
$taskVersion = '3.13-2'
$taskArchive = Join-Path $taskTools "mingw-w64-ucrt-x86_64-nsis-$taskVersion-any.pkg.tar.zst"
if (-not (Test-Path $taskArchive)) {
    Invoke-WebRequest "https://repo.msys2.org/mingw/ucrt64/mingw-w64-ucrt-x86_64-nsis-$taskVersion-any.pkg.tar.zst" -OutFile $taskArchive
}
if ((Get-FileHash $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne '262c2453ee69924e4995a6f3c685edb96e57e57e749d38f8f89732be6ffccff7') { throw 'NSIS checksum does not match official MSYS2 package metadata.' }
$taskCompiler = Join-Path $taskTools 'nsis/ucrt64/bin/makensis.exe'
if (-not (Test-Path $taskCompiler)) {
    & (Get-Command 7z -ErrorAction Stop).Source x $taskArchive "-o$taskTools" -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'NSIS decompression failed.' }
    & (Get-Command 7z -ErrorAction Stop).Source x ($taskArchive -replace '\.zst$','') "-o$(Join-Path $taskTools 'nsis')" -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'NSIS extraction failed.' }
}
$taskRuntimeVersion = (Get-ChildItem (Join-Path $taskTools 'dotnet/shared/Microsoft.NETCore.App') -Directory | Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1).Name
if (-not $taskRuntimeVersion) { throw 'Bootstrap the pinned SDK first.' }
foreach ($taskPackageId in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64','microsoft.aspnetcore.app.runtime.win-x64')) {
    $taskPackage = Join-Path $taskTools "nuget-source/$taskPackageId.$taskRuntimeVersion.nupkg"
    if (-not (Test-Path $taskPackage)) {
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$taskPackageId/$taskRuntimeVersion/$taskPackageId.$taskRuntimeVersion.nupkg" -OutFile $taskPackage
    }
}
$taskPriorNsis = $env:NSISDIR
try { $env:NSISDIR = Join-Path $taskTools 'nsis/ucrt64/share/nsis'; & $taskCompiler /VERSION }
finally { $env:NSISDIR = $taskPriorNsis }
if ($LASTEXITCODE -ne 0) { throw 'NSIS compiler could not run.' }
