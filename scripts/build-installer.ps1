param([string]$ApplicationDirectory, [string]$OutputFile,
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version, [string]$ValidationRoot, [switch]$FrameworkDependent = $true,
    [string]$DisplayVersion, [ValidateSet('x64','arm64')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (-not $FrameworkDependent -and -not $ValidationRoot) { throw 'Bundled runtime installers are only allowed for isolated legacy-upgrade validation.' }
if (-not $ApplicationDirectory) { $ApplicationDirectory = Join-Path $taskRoot $(if ($FrameworkDependent) { 'artifacts/light-app' } else { 'artifacts/self-contained-app' }) }
$ApplicationDirectory = [IO.Path]::GetFullPath($ApplicationDirectory)
if (-not (Test-Path (Join-Path $ApplicationDirectory 'ArchivePassword.exe'))) { throw 'Build the application first.' }
$taskMachine = if ($Architecture -eq 'arm64') { 0xaa64 } else { 0x8664 }
$taskImages = @((Join-Path $ApplicationDirectory 'ArchivePassword.exe'), (Join-Path $ApplicationDirectory 'ArchiveEngineWorker.exe'))
foreach ($taskEngine in Get-ChildItem (Join-Path $ApplicationDirectory 'engines') -Directory) {
    $taskEngineManifest = Get-Content (Join-Path $taskEngine.FullName 'manifest.json') -Raw | ConvertFrom-Json
    if ($taskEngineManifest.architecture -ne $Architecture) { throw 'Installer engine architecture differs from its label.' }
    $taskImages += @((Join-Path $taskEngine.FullName '7z.dll'), (Join-Path $taskEngine.FullName '7z.exe'))
}
foreach ($taskImage in $taskImages) {
    $taskImageBytes = [IO.File]::ReadAllBytes($taskImage)
    if ($taskImageBytes.Length -lt 88 -or $taskImageBytes[0] -ne 77 -or $taskImageBytes[1] -ne 90) { throw 'Installer payload contains an invalid native image.' }
    $taskOffset = [BitConverter]::ToInt32($taskImageBytes,60)
    if ($taskOffset -lt 64 -or $taskOffset -gt $taskImageBytes.Length-24 -or [BitConverter]::ToUInt32($taskImageBytes,$taskOffset) -ne 17744 -or [BitConverter]::ToUInt16($taskImageBytes,$taskOffset+4) -ne $taskMachine) { throw 'Installer payload architecture differs from its label.' }
}
$taskApplicationVersion = (Get-Item -LiteralPath (Join-Path $ApplicationDirectory 'ArchivePassword.exe')).VersionInfo
if (-not $Version) { $Version = $taskApplicationVersion.FileVersion }
if (-not $DisplayVersion) {
    $DisplayVersion = if ($ValidationRoot) { $Version } else { $taskApplicationVersion.ProductVersion.Split('+')[0] }
}
if (-not $ValidationRoot -and ($Version -ne $taskApplicationVersion.FileVersion -or $DisplayVersion -ne $taskApplicationVersion.ProductVersion.Split('+')[0])) { throw 'Installer version must match the application; version overrides are reserved for isolated upgrade fixtures.' }
if (-not $OutputFile) { $OutputFile = Join-Path $taskRoot $(if ($FrameworkDependent) { "artifacts/nomozip-$DisplayVersion-light-setup-win-$Architecture.exe" } else { 'artifacts/nomozip-legacy-fixture-setup-win-x64.exe' }) }
$OutputFile = [IO.Path]::GetFullPath($OutputFile)
if ((Test-Path (Join-Path $ApplicationDirectory 'coreclr.dll')) -eq $FrameworkDependent.IsPresent) { throw 'Application payload does not match the selected runtime mode.' }
if (Test-Path $OutputFile) { throw 'Keep installers immutable; choose a new output file.' }
if ($ValidationRoot) {
    $ValidationRoot = [IO.Path]::GetFullPath($ValidationRoot)
    $taskValidationParent = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts/installer-validation')).TrimEnd('\') + '\'
    if (-not $ValidationRoot.StartsWith($taskValidationParent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Installer validation must stay in the project validation directory.' }
}
New-Item -ItemType Directory -Force (Split-Path $OutputFile -Parent) | Out-Null
$taskOwned = Join-Path (Split-Path $OutputFile -Parent) 'owned-files.nsh'
$taskLines = [Collections.Generic.List[string]]::new()
foreach ($taskFile in Get-ChildItem -LiteralPath $ApplicationDirectory -Recurse -File) {
    $taskRelative = [IO.Path]::GetRelativePath($ApplicationDirectory, $taskFile.FullName)
    if ($taskRelative.Contains('$') -or $taskRelative.Contains('"') -or $taskRelative.Contains('..')) { throw 'Unsupported payload path.' }
    $taskLines.Add('Delete "$INSTDIR\' + $taskRelative + '"')
}
foreach ($taskDirectory in (Get-ChildItem -LiteralPath $ApplicationDirectory -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending)) {
    $taskLines.Add('RMDir "$INSTDIR\' + [IO.Path]::GetRelativePath($ApplicationDirectory, $taskDirectory.FullName) + '"')
}
[IO.File]::WriteAllLines($taskOwned, $taskLines, [Text.UTF8Encoding]::new($false))
$taskArguments = @('/V2', "/DAPP_DIR=$ApplicationDirectory", "/DOUTPUT_FILE=$OutputFile", "/DOWNED_FILES=$taskOwned", "/DVERSION=$Version", "/DDISPLAY_VERSION=$DisplayVersion", "/DARCHITECTURE=$Architecture")
if ($ValidationRoot) { $taskArguments += "/DVALIDATION_ROOT=$ValidationRoot" }
if ($FrameworkDependent) { $taskArguments += '/DFRAMEWORK_DEPENDENT' }
$taskArguments += (Join-Path $taskRoot 'installer/nomozip.nsi')
$taskPriorNsis = $env:NSISDIR
try {
    $env:NSISDIR = Join-Path $taskRoot '.tools/nsis/ucrt64/share/nsis'
    & (Join-Path $taskRoot '.tools/nsis/ucrt64/bin/makensis.exe') @taskArguments
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
} finally { $env:NSISDIR = $taskPriorNsis }
Write-Output "Installer created: $OutputFile"
