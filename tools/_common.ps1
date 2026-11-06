# 脚本公用函数：日志写到 tools/logs/<名字>.log（UTF-8），状态写到 tools/logs/<名字>.status（running/ok/fail）。
$ErrorActionPreference = 'Stop'
$script:RepoRoot = Split-Path $PSScriptRoot -Parent
$script:LogDir = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Force -Path $script:LogDir | Out-Null

function Start-ExileLog([string]$name) {
    $script:LogName = $name
    $script:LogFile = Join-Path $script:LogDir "$name.log"
    $script:StatusFile = Join-Path $script:LogDir "$name.status"
    Set-Content -LiteralPath $script:LogFile -Value "[$(Get-Date -Format s)] $name 开始" -Encoding UTF8
    Set-Content -LiteralPath $script:StatusFile -Value 'running' -Encoding ASCII
}

function Write-ExileLog([string]$line) {
    Write-Host $line
    Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8
}

# 运行外部程序，输出同时写日志；退出码非 0 时抛错。
function Invoke-Exile([string]$exe, [string[]]$arguments) {
    Write-ExileLog "> $exe $($arguments -join ' ')"
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $exe @arguments 2>&1 | ForEach-Object { Write-ExileLog "$_" }
    $code = $LASTEXITCODE
    $ErrorActionPreference = $old
    if ($code -ne 0) { throw "$exe 退出码 $code" }
}

function Complete-ExileLog([bool]$ok, [string]$message) {
    Write-ExileLog "[$(Get-Date -Format s)] $($script:LogName) $(if ($ok) {'完成'} else {'失败'}) $message"
    Set-Content -LiteralPath $script:StatusFile -Value $(if ($ok) {'ok'} else {'fail'}) -Encoding ASCII
    Start-Sleep -Seconds 3
}
