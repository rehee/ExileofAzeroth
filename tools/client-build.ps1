# 编译客户端（client/ 子模块，benilla 分支），profile=play（release + 增量）。首次编译较久。
. (Join-Path $PSScriptRoot '_common.ps1')
Start-ExileLog 'client-build'
try {
    $cargo = Use-ExileRust
    Set-Location (Join-Path $RepoRoot 'client')
    Invoke-Exile $cargo @('--version')
    $package = if ($env:EXILE_CLIENT_PACKAGE) { $env:EXILE_CLIENT_PACKAGE } else { 'benilla' }
    Invoke-Exile $cargo @('build', '--profile', 'play', '-p', $package)
    Get-ChildItem (Join-Path $RepoRoot 'client\target\play') -Filter '*.exe' | ForEach-Object { Write-ExileLog "产物: $($_.FullName) $([math]::Round($_.Length/1MB,1)) MB" }
    Complete-ExileLog $true ''
}
catch { Write-ExileLog "错误: $_"; Complete-ExileLog $false '' }
