param([string]$ApplicationDirectory, [string]$OutputFile,
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version, [string]$ValidationRoot, [switch]$FrameworkDependent = $true,
    [string]$DisplayVersion)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (-not $FrameworkDependent -and -not $ValidationRoot) { throw 'Bundled runtime installers are only allowed for isolated legacy-upgrade validation.' }
if (-not $ApplicationDirectory) { $ApplicationDirectory = Join-Path $taskRoot $(if ($FrameworkDependent) { 'artifacts/light-app' } else { 'artifacts/self-contained-app' }) }
$ApplicationDirectory = [IO.Path]::GetFullPath($ApplicationDirectory)
if (-not (Test-Path (Join-Path $ApplicationDirectory 'ArchivePassword.exe'))) { throw 'Build the application first.' }
$taskApplicationVersion = (Get-Item -LiteralPath (Join-Path $ApplicationDirectory 'ArchivePassword.exe')).VersionInfo
if (-not $Version) { $Version = $taskApplicationVersion.FileVersion }
if (-not $DisplayVersion) {
    $DisplayVersion = if ($ValidationRoot) { $Version } else { $taskApplicationVersion.ProductVersion.Split('+')[0] }
}
if (-not $ValidationRoot -and ($Version -ne $taskApplicationVersion.FileVersion -or $DisplayVersion -ne $taskApplicationVersion.ProductVersion.Split('+')[0])) { throw 'Installer version must match the application; version overrides are reserved for isolated upgrade fixtures.' }
if (-not $OutputFile) { $OutputFile = Join-Path $taskRoot $(if ($FrameworkDependent) { "artifacts/nomozip-$DisplayVersion-light-setup-win-x64.exe" } else { 'artifacts/nomozip-legacy-fixture-setup-win-x64.exe' }) }
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
$taskArguments = @('/V2', "/DAPP_DIR=$ApplicationDirectory", "/DOUTPUT_FILE=$OutputFile", "/DOWNED_FILES=$taskOwned", "/DVERSION=$Version", "/DDISPLAY_VERSION=$DisplayVersion")
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
