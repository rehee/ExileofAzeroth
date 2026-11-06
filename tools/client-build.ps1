# 编译客户端（client/ 子模块，benilla 分支），profile=play（release + 增量）。首次编译较久。
. (Join-Path $PSScriptRoot '_common.ps1')
Start-ExileLog 'client-build'
try {
    # Rust 工具链：先找系统 PATH，再用免安装版（默认 C:\wow_survival\tools，可用 EXILE_RUST_HOME 覆盖）。
    $cargo = (Get-Command cargo -ErrorAction SilentlyContinue).Source
    if (-not $cargo) {
        $rustHome = if ($env:EXILE_RUST_HOME) { $env:EXILE_RUST_HOME } else { 'C:\wow_survival\tools' }
        $env:CARGO_HOME = Join-Path $rustHome 'cargo'
        $env:RUSTUP_HOME = Join-Path $rustHome 'rustup'
        $env:PATH = (Join-Path $env:CARGO_HOME 'bin') + ';' + $env:PATH
        $cargo = Join-Path $env:CARGO_HOME 'bin\cargo.exe'
        Write-ExileLog "使用免安装 Rust：$rustHome"
    }
    if (-not (Test-Path $cargo)) { throw '找不到 cargo，请运行 tools/setup-dev.ps1 安装 Rust。' }
    Set-Location (Join-Path $RepoRoot 'client')
    Invoke-Exile $cargo @('--version')
    $package = if ($env:EXILE_CLIENT_PACKAGE) { $env:EXILE_CLIENT_PACKAGE } else { 'benilla' }
    Invoke-Exile $cargo @('build', '--profile', 'play', '-p', $package)
    Get-ChildItem (Join-Path $RepoRoot 'client\target\play') -Filter '*.exe' | ForEach-Object { Write-ExileLog "产物: $($_.FullName) $([math]::Round($_.Length/1MB,1)) MB" }
    Complete-ExileLog $true ''
}
catch { Write-ExileLog "错误: $_"; Complete-ExileLog $false '' }
