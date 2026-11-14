param([switch]$Graphics)
$ErrorActionPreference = 'Stop'
$engineRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testArguments = @('--save-test')
if ($Graphics) { $testArguments += '--graphics' }
& dotnet (Join-Path $engineRoot 'bin/OhajikiEngine.dll') @testArguments
$testExit = $LASTEXITCODE
Get-Content -LiteralPath (Join-Path $engineRoot 'Review/Save/test-results.txt')
if ($testExit -ne 0) { throw 'セーブ機能のテストに失敗しました。' }
