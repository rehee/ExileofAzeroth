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

# ---- 本机路径与工具链（config/dev.local.json 的 paths，用户 2026-11-08） ----
# 本项目只读取本机配置里写明的目录：DQQ 源码（私有项目）、WoW 客户端数据（暴雪资源，不入库）；
# Rust 工具链装在本项目内（.toolchain\rust，不入库），不借用其他项目的工具。

function Get-ExileConfig {
    $path = Join-Path $script:RepoRoot 'config\dev.local.json'
    if (-not (Test-Path $path)) { throw "缺少 config\dev.local.json：复制 config\dev.example.json 并填写 paths。" }
    return Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json
}

# 本机目录：环境变量优先（临时覆盖），否则 dev.local.json 的 paths.<key>；都没有就报错并说明怎么配。
function Get-ExilePath([string]$key, [string]$envName, [string]$what) {
    $v = if ($envName) { [Environment]::GetEnvironmentVariable($envName) } else { $null }
    if (-not $v) {
        $cfg = Get-ExileConfig
        if ($cfg.paths -and $cfg.paths.$key) { $v = $cfg.paths.$key }
    }
    if (-not $v) { throw "没有配置$what：在 config\dev.local.json 的 paths.$key 写上目录。" }
    if (-not (Test-Path $v)) { throw "$what 不存在：$v（config\dev.local.json 的 paths.$key）" }
    return $v
}

function Get-DqqRoot { Get-ExilePath 'dqqRoot' 'DQQ_ROOT' ' DQQ 源码目录' }
function Get-WowData { Get-ExilePath 'wowData' 'WOW_DATA' ' WoW 客户端数据目录（Data）' }

# 项目内的 Rust 工具链（tools\run-setup-rust.cmd 安装）：.toolchain\rust\cargo 与 rustup。
function Get-ExileRustHome { Join-Path $script:RepoRoot '.toolchain\rust' }

function Use-ExileRust {
    $rustHome = Get-ExileRustHome
    $env:CARGO_HOME = Join-Path $rustHome 'cargo'
    $env:RUSTUP_HOME = Join-Path $rustHome 'rustup'
    $env:PATH = (Join-Path $env:CARGO_HOME 'bin') + ';' + $env:PATH
    $cargo = Join-Path $env:CARGO_HOME 'bin\cargo.exe'
    if (-not (Test-Path $cargo)) { throw "项目内还没有 Rust 工具链：先运行 tools\run-setup-rust.cmd（装到 $rustHome）。" }
    Write-ExileLog "Rust 工具链（项目内）：$rustHome"
    return $cargo
}
