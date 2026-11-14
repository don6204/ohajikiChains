$ErrorActionPreference = 'Stop'
$engineRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$test = Start-Process -FilePath (Join-Path $engineRoot 'bin\OhajikiEngine.exe') -ArgumentList '--self-test' -WorkingDirectory $engineRoot -WindowStyle Hidden -PassThru
if (!$test.WaitForExit(120000)) {
    Stop-Process -Id $test.Id -Force
    throw 'エンジンテストが120秒以内に完了しませんでした。Review/test-results.txtを確認してください。'
}
Get-Content -LiteralPath (Join-Path $engineRoot 'Review\test-results.txt') -Encoding UTF8
if ($test.ExitCode -ne 0) { throw "エンジンテストに失敗しました（$($test.ExitCode)）。" }
