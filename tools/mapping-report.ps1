# 编译 Exile.Runtime 并输出 DQQ ↔ WoW 对应表的现状（PLAN-0006）：tools/logs/mapping-report.md。不需要客户端，也不登录 DQQ。
. (Join-Path $PSScriptRoot '_common.ps1')
Start-ExileLog 'mapping-report'
try {
    $proj = Join-Path $RepoRoot 'src\Exile.Runtime\Exile.Runtime.csproj'
    Invoke-Exile 'dotnet' @('build', $proj, '-c', 'Release', '--nologo', '-v', 'q')
    $exe = Join-Path $RepoRoot 'src\Exile.Runtime\bin\Release\net8.0\Exile.Runtime.exe'
    Invoke-Exile $exe @('--mapping-report')
    Complete-ExileLog $true ''
}
catch { Write-ExileLog "错误: $_"; Complete-ExileLog $false '' }
