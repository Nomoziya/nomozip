param([string]$ApplicationDirectory = (Join-Path $PSScriptRoot '../artifacts/app'))
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Product UI validation requires isolated CI.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskFailed = $false
foreach ($taskLanguage in @('zh-CN','en')) {
    $taskProcess = Start-Process (Join-Path $ApplicationDirectory 'ArchivePassword.exe') -ArgumentList @('--validate-redesign',$taskRoot,$taskLanguage) -WindowStyle Hidden -Wait -PassThru
    $taskReport = Get-Content (Join-Path $taskRoot "artifacts/ui-redesign/$taskLanguage/report.json") -Raw | ConvertFrom-Json
    if ($taskProcess.ExitCode -ne 0 -or -not $taskReport.passed) { $taskFailed = $true }
    $taskReport.checks.PSObject.Properties | Where-Object { -not $_.Value } | ForEach-Object { Write-Warning "${taskLanguage}: $($_.Name)" }
    if ($taskReport.error) { Write-Warning $taskReport.error }
}
if ($taskFailed) { throw 'Product UI/flow validation failed; original reports retained.' }
