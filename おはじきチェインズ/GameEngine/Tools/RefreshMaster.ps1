param([string]$Source)
$ErrorActionPreference = 'Stop'
$engineRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Source) { $Source = Join-Path (Split-Path $engineRoot -Parent) 'MasterData.xlsx' }
& dotnet (Join-Path $engineRoot 'bin\OhajikiEngine.dll') --import-master $Source
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath (Join-Path $engineRoot 'Review\startup-error.txt') -Encoding UTF8
    throw 'MasterDataの更新に失敗しました。以前のデータを保持しています。'
}
Get-Content -LiteralPath (Join-Path $engineRoot 'Review\master-import.txt') -Encoding UTF8
Write-Host '更新完了。起動中のゲームは再生を停止して再開するか、プレイヤーを再起動してください。'
