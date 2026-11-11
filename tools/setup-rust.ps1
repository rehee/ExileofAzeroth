# 在本项目内安装 Rust 工具链（.toolchain\rust，不入库；用户 2026-11-08：编译工具放在本项目内，不借用其他项目）。
# 下载官方 rustup-init，装到 .toolchain\rust\cargo 与 .toolchain\rust\rustup，不改系统 PATH；
# 客户端要的版本由 client\rust-toolchain.toml 指定，第一次编译时 rustup 自动下载。
. (Join-Path $PSScriptRoot '_common.ps1')
Start-ExileLog 'setup-rust'
try {
    $rustHome = Get-ExileRustHome
    $env:CARGO_HOME = Join-Path $rustHome 'cargo'
    $env:RUSTUP_HOME = Join-Path $rustHome 'rustup'
    New-Item -ItemType Directory -Force -Path $env:CARGO_HOME, $env:RUSTUP_HOME | Out-Null
    $init = Join-Path $rustHome 'rustup-init.exe'
    if (-not (Test-Path $init)) {
        Write-ExileLog '下载 rustup-init.exe（https://static.rust-lang.org）'
        Invoke-WebRequest -Uri 'https://static.rust-lang.org/rustup/dist/x86_64-pc-windows-msvc/rustup-init.exe' -OutFile $init -UseBasicParsing
    }
    Invoke-Exile $init @('-y', '--no-modify-path', '--profile', 'minimal', '--default-toolchain', 'none')
    $rustup = Join-Path $env:CARGO_HOME 'bin\rustup.exe'
    # 按客户端的 rust-toolchain.toml 装好对应版本。
    Set-Location (Join-Path $RepoRoot 'client')
    $channel = (Select-String -Path (Join-Path $RepoRoot 'client\rust-toolchain.toml') -Pattern 'channel\s*=\s*"([^"]+)"').Matches[0].Groups[1].Value
    Write-ExileLog "客户端要求的 Rust：$channel"
    Invoke-Exile $rustup @('toolchain', 'install', $channel, '--profile', 'minimal', '-c', 'clippy', '-c', 'rustfmt')
    Invoke-Exile (Join-Path $env:CARGO_HOME 'bin\cargo.exe') @('--version')
    Complete-ExileLog $true "工具链在 $rustHome"
}
catch { Write-ExileLog "错误: $_"; Complete-ExileLog $false '' }
