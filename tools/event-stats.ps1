# 编译 Exile.Runtime 并后台重放 replays/ 里的录像，统计技能事件（PLAN-0006 D）：tools/logs/event-stats.md。不需要客户端。
. (Join-Path $PSScriptRoot '_common.ps1')
Start-ExileLog 'event-stats'
try {
    $proj = Join-Path $RepoRoot 'src\Exile.Runtime\Exile.Runtime.csproj'
    Invoke-Exile 'dotnet' @('build', $proj, '-c', 'Release', '--nologo', '-v', 'q')
    $exe = Join-Path $RepoRoot 'src\Exile.Runtime\bin\Release\net8.0\Exile.Runtime.exe'
    Invoke-Exile $exe @('--event-stats', '--replays', (Join-Path $RepoRoot 'replays'))
    Complete-ExileLog $true ''
}
catch { Write-ExileLog "错误: $_"; Complete-ExileLog $false '' }
