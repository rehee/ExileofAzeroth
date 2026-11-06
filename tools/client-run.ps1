# 运行客户端（未改动的 benilla 先用来确认能启动；之后由 Exile 模式接管）。
# 游戏数据：$env:WOW_DATA，默认 C:\wow_survival\wow-data\Data。窗口关闭后脚本结束。
. (Join-Path $PSScriptRoot '_common.ps1')
Start-ExileLog 'client-run'
try {
    if (-not $env:WOW_DATA) { $env:WOW_DATA = 'C:\wow_survival\wow-data\Data' }
    $exe = Join-Path $RepoRoot 'client\target\play\benilla.exe'
    if (-not (Test-Path $exe)) { throw "找不到 $exe，请先运行 client-build。" }
    Write-ExileLog "WOW_DATA=$($env:WOW_DATA)"
    Set-Location (Join-Path $RepoRoot 'client')
    Invoke-Exile $exe @()
    Complete-ExileLog $true ''
}
catch { Write-ExileLog "错误: $_"; Complete-ExileLog $false '' }
