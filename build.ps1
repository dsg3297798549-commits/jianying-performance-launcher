$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $projectDir 'JianyingPerformanceLauncher.cs'
$dist = Join-Path $projectDir 'dist'
$output = Join-Path $dist '剪映性能启动器.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $compiler)) {
    throw '未找到 .NET Framework C# 编译器。'
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null
& $compiler /nologo /target:winexe /optimize+ "/out:$output" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码：$LASTEXITCODE"
}

$hash = Get-FileHash $output -Algorithm SHA256
Write-Host "构建完成：$output"
Write-Host "SHA-256：$($hash.Hash)"
