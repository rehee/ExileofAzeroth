# 提交并推送本仓库的全部改动。提交信息读自 tools/commit-message.txt（已忽略，由 AI 每次写入）。
. (Join-Path $PSScriptRoot '_common.ps1')
Start-ExileLog 'commit-push'
try {
    Set-Location $RepoRoot
    $msgFile = Join-Path $PSScriptRoot 'commit-message.txt'
    if (-not (Test-Path $msgFile)) { throw '缺少 tools/commit-message.txt' }
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $ignored = & git check-ignore config/dev.local.json
    $ErrorActionPreference = $old
    if (-not $ignored) { throw 'config/dev.local.json 未被忽略，已中止。' }
    Invoke-Exile 'git' @('add', '-A')
    Invoke-Exile 'git' @('status', '--short')
    Invoke-Exile 'git' @('commit', '-F', $msgFile)
    Invoke-Exile 'git' @('push')
    Invoke-Exile 'git' @('log', '--oneline', '-3')
    Remove-Item $msgFile
    Complete-ExileLog $true ''
}
catch { Write-ExileLog "错误: $_"; Complete-ExileLog $false '' }
