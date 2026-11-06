# Exile of Azeroth - 开发环境安装脚本（Windows）
# 用法：右键本文件 -> "使用 PowerShell 运行"。安装 VS 生成工具时可能弹出管理员确认，请点"是"。
$ErrorActionPreference = 'Continue'
$log = Join-Path $PSScriptRoot 'setup-dev.log'
Start-Transcript -Path $log -Append | Out-Null

function Install-Pkg($id, $extra) {
    Write-Host "==== 安装 $id ====" -ForegroundColor Cyan
    $args = @('install', '--id', $id, '-e', '--accept-source-agreements', '--accept-package-agreements')
    if ($extra) { $args += $extra }
    & winget @args
    Write-Host "winget 退出码: $LASTEXITCODE"
}

Install-Pkg 'Git.Git' $null
Install-Pkg 'Microsoft.DotNet.SDK.8' $null
Install-Pkg 'Microsoft.VisualStudio.2022.BuildTools' @('--override', '--quiet --wait --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended')
Install-Pkg 'Rustlang.Rustup' $null

# 刷新 PATH 后设置 Rust 默认工具链
$env:Path = [System.Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path','User')
$rustup = Join-Path $env:USERPROFILE '.cargo\bin\rustup.exe'
if (Test-Path $rustup) { & $rustup default stable-x86_64-pc-windows-msvc }

Write-Host "==== 版本检查 ====" -ForegroundColor Cyan
foreach ($c in @('git --version', 'dotnet --list-sdks', "$env:USERPROFILE\.cargo\bin\rustc.exe --version", "$env:USERPROFILE\.cargo\bin\cargo.exe --version")) {
    Write-Host "> $c"; try { Invoke-Expression $c } catch { Write-Host "  未找到" -ForegroundColor Yellow }
}
Stop-Transcript | Out-Null
Write-Host "`n完成。日志：$log  按回车关闭。" -ForegroundColor Green
Read-Host
