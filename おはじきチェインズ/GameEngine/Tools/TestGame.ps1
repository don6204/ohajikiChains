param([switch]$Graphics)
$ErrorActionPreference = 'Stop'
$engineRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testArgs = '--game-test'
if ($Graphics) { $testArgs += ' --graphics' }
$test = Start-Process -FilePath (Join-Path $engineRoot 'bin\OhajikiEngine.exe') -ArgumentList $testArgs -WorkingDirectory $engineRoot -WindowStyle Hidden -PassThru
if (!$test.WaitForExit(120000)) {
    Stop-Process -Id $test.Id -Force
    throw '移植テストが120秒以内に完了しませんでした。'
}
$report = if ($Graphics) { 'Review\Migration\game-gpu-tests.txt' } else { 'Review\Migration\game-tests.txt' }
Get-Content -LiteralPath (Join-Path $engineRoot $report) -Encoding UTF8
if ($test.ExitCode -ne 0) { throw "移植テストに失敗しました（$($test.ExitCode)）。" }
