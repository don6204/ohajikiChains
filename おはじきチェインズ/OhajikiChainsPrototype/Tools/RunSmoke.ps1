param(
    [ValidateSet('all','presentation','master','board','spec','feedback','title','hub','shop','gacha')]
    [string]$Mode = 'master',
    [int]$TimeoutSeconds = 900,
    [string]$ReviewDirectory,
    [string]$BuildDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildRoot = Join-Path $projectRoot 'Build\20261114_HpBarShakeDuration'
if ($BuildDirectory) { $buildRoot = [System.IO.Path]::GetFullPath($BuildDirectory) }
$playerPath = Join-Path $buildRoot 'OhajikiChains.exe'
$reviewRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot '..\Review\20261114_hp_bar_shake_duration'))
if ($ReviewDirectory) { $reviewRoot = [System.IO.Path]::GetFullPath($ReviewDirectory) }
New-Item -ItemType Directory -Path $reviewRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath $playerPath -PathType Leaf)) { throw "Build missing: $playerPath" }
$modes = if ($Mode -eq 'all') { @('master','board','spec','feedback','title','hub','shop','gacha') }
    elseif ($Mode -eq 'presentation') { @('master','board','feedback','title','hub') } else { @($Mode) }
foreach ($smokeMode in $modes) {
$logPath = Join-Path $reviewRoot ($smokeMode + '-smoke.log')
$arguments = '--' + $smokeMode + '-smoke-test -logFile "' + $logPath + '" -force-d3d11 -screen-width 540 -screen-height 960 -screen-fullscreen 0'
$playerProcess = Start-Process -FilePath $playerPath -WorkingDirectory $buildRoot -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $playerProcess.WaitForExit($TimeoutSeconds * 1000)) {
    $playerProcess.Kill()
    throw "$smokeMode smoke timed out after $TimeoutSeconds seconds. Log: $logPath"
}
$exitCode = $playerProcess.ExitCode
$evidence = Select-String -LiteralPath $logPath -Pattern 'SMOKE PASS|SMOKE FAIL|UI CHECK FAIL|UI OVERFLOW|Exception' | ForEach-Object { $_.Line }
Write-Output "$smokeMode smoke exit: $exitCode"
Write-Output $evidence
if ($exitCode -ne 0 -or -not (Select-String -LiteralPath $logPath -Pattern 'SMOKE PASS' -Quiet)) {
    throw "$smokeMode smoke did not pass. Log: $logPath"
}
}
