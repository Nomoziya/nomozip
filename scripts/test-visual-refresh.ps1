param([string]$ApplicationDirectory = (Join-Path $PSScriptRoot '../artifacts/app'))
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
foreach ($taskLanguage in @('zh-CN', 'en')) {
    $taskProcess = Start-Process (Join-Path $ApplicationDirectory 'ArchivePassword.exe') -ArgumentList @('--validate-visual-refresh', $taskRoot, $taskLanguage) -WindowStyle Hidden -Wait -PassThru
    if ($taskProcess.ExitCode -ne 0) { throw "Visual review failed: $taskLanguage" }
    $taskReport = Get-Content (Join-Path $taskRoot "artifacts/visual-refresh/$taskLanguage/report.json") -Raw | ConvertFrom-Json
    if (-not $taskReport.passed -or @($taskReport.checks.PSObject.Properties).Count -ne 90) { throw "Visual evidence is incomplete: $taskLanguage" }
}
