param([switch]$Test)
$ErrorActionPreference = 'Stop'
$engineRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputPath = Join-Path $engineRoot 'bin'
$buildPath = Join-Path $engineRoot 'Build'
New-Item -ItemType Directory -Force -Path $outputPath, $buildPath | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio C++ Build Tools が必要です。' }
$vsPath = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vsPath) { throw 'Visual Studio の「C++ によるデスクトップ開発」をインストールしてください。' }
$nativeScript = Join-Path $buildPath 'native-build.cmd'
$nativeSource = Join-Path $engineRoot 'Native\Renderer.cpp'
$nativeDll = Join-Path $outputPath 'OhajikiD3D12.dll'
$nativeObj = Join-Path $buildPath 'Renderer.obj'
$nativeLib = Join-Path $buildPath 'OhajikiD3D12.lib'
$scriptText = "@echo off`r`ncall `"$vsPath\Common7\Tools\VsDevCmd.bat`" -arch=x64 -host_arch=x64 >nul`r`nif errorlevel 1 exit /b 1`r`ncl /nologo /std:c++17 /EHsc /O2 /MT /W4 /utf-8 /LD `"$nativeSource`" /Fo`"$nativeObj`" /link /OUT:`"$nativeDll`" /IMPLIB:`"$nativeLib`" d3d12.lib dxgi.lib d3dcompiler.lib user32.lib`r`nexit /b %errorlevel%`r`n"
# Switch cmd.exe explicitly so Japanese project paths work regardless of the host code page.
$scriptText = $scriptText.Replace('@echo off', "@echo off`r`nchcp 65001 >nul")
[IO.File]::WriteAllText($nativeScript, $scriptText, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c "`"$nativeScript`""
if ($LASTEXITCODE -ne 0) { throw 'DirectX 12 描画部のビルドに失敗しました。' }
$env:DOTNET_CLI_HOME = Join-Path $engineRoot '.local'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& dotnet build (Join-Path $engineRoot 'Engine\OhajikiEngine.csproj') -c Release -o $outputPath --configfile (Join-Path $engineRoot 'NuGet.Config') --nologo
if ($LASTEXITCODE -ne 0) { throw 'エディターのビルドに失敗しました。' }
Write-Host "ビルド完了: $outputPath\OhajikiEngine.exe"
if ($Test) {
  & (Join-Path $PSScriptRoot 'Test.ps1')
}
