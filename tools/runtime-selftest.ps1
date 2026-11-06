# 编译 Exile.Runtime 并运行自检（登录 DQQ → 角色列表 → 跑战斗），不需要客户端。
# 用法：右键 -> 使用 PowerShell 运行。结果：tools/logs/runtime-selftest.log 与 selftest.json。
. (Join-Path $PSScriptRoot '_common.ps1')
Start-ExileLog 'runtime-selftest'
try {
    $proj = Join-Path $RepoRoot 'src\Exile.Runtime\Exile.Runtime.csproj'
    if (-not (Test-Path (Join-Path $RepoRoot 'source\dqq\DQQ.Core.dll'))) { throw 'source\dqq 里没有 DQQ.Core.dll，请先运行 tools/update-dqq.ps1' }
    Invoke-Exile 'dotnet' @('build', $proj, '-c', 'Release', '--nologo', '-v', 'q')
    $exe = Join-Path $RepoRoot 'src\Exile.Runtime\bin\Release\net8.0\Exile.Runtime.exe'
    Invoke-Exile $exe @('--selftest', '--config', (Join-Path $RepoRoot 'config\dev.local.json'))
    $report = Join-Path $RepoRoot 'src\Exile.Runtime\bin\Release\net8.0\logs\selftest.json'
    if (Test-Path $report) { Copy-Item $report (Join-Path $LogDir 'selftest.json') -Force }
    Complete-ExileLog $true ''
}
catch {
    Write-ExileLog "错误: $_"
    $report = Join-Path $RepoRoot 'src\Exile.Runtime\bin\Release\net8.0\logs\selftest.json'
    if (Test-Path $report) { Copy-Item $report (Join-Path $LogDir 'selftest.json') -Force }
    Complete-ExileLog $false ''
}
