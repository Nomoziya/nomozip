param([string]$ApplicationDirectory, [string]$OutputFile,
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version = '0.1.0.3', [string]$ValidationRoot)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (-not $ApplicationDirectory) { $ApplicationDirectory = Join-Path $taskRoot 'artifacts/self-contained-app' }
if (-not $OutputFile) { $OutputFile = Join-Path $taskRoot 'artifacts/nomozip-0.1.0-preview.3-setup-win-x64.exe' }
$ApplicationDirectory = [IO.Path]::GetFullPath($ApplicationDirectory)
$OutputFile = [IO.Path]::GetFullPath($OutputFile)
if (-not (Test-Path (Join-Path $ApplicationDirectory 'coreclr.dll'))) { throw 'Build the self-contained application first.' }
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
$taskArguments = @('/V2', "/DAPP_DIR=$ApplicationDirectory", "/DOUTPUT_FILE=$OutputFile", "/DOWNED_FILES=$taskOwned", "/DVERSION=$Version")
if ($ValidationRoot) { $taskArguments += "/DVALIDATION_ROOT=$ValidationRoot" }
$taskArguments += (Join-Path $taskRoot 'installer/nomozip.nsi')
$taskPriorNsis = $env:NSISDIR
try {
    $env:NSISDIR = Join-Path $taskRoot '.tools/nsis/ucrt64/share/nsis'
    & (Join-Path $taskRoot '.tools/nsis/ucrt64/bin/makensis.exe') @taskArguments
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
} finally { $env:NSISDIR = $taskPriorNsis }
Write-Output "Installer created: $OutputFile"
