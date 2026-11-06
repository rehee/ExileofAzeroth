# Exile of Azeroth - 首次提交、推送，并加入客户端子模块
# 用法：右键本文件 -> "使用 PowerShell 运行"。需要 Windows 上已安装 git 并能登录 GitHub。
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$log = Join-Path $PSScriptRoot 'init-repo.log'
Start-Transcript -Path $log -Append | Out-Null
try {
    Write-Host "仓库目录：$root" -ForegroundColor Cyan
    git --version

    # 确认凭据文件不会被提交
    if (-not (git check-ignore config/dev.local.json)) { throw "config/dev.local.json 未被忽略，已中止。" }

    $msg1 = @"
初始化项目协作结构与架构文档

- agents/: AI 协作规则、工作流、模板
- docs/: 总体架构（已定稿）与讨论存档
- plans/: 总进度与 PLAN-0001~0003
- config/: 本地配置模板（dev.local.json 已忽略）
- tools/: 开发环境与仓库初始化脚本

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E3kWziCrWRFb752Xw9SfZN
"@
    git add -A
    git status --short
    git commit -m $msg1
    git branch -M main
    git push -u origin main

    Write-Host "==== 加入客户端子模块 client/ ====" -ForegroundColor Cyan
    git submodule add https://github.com/rehee/benilla.git client
    $msg2 = @"
加入客户端子模块 client/（rehee/benilla，fork 自 samwhosung/benilla）

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E3kWziCrWRFb752Xw9SfZN
"@
    git commit -m $msg2
    git push
    Write-Host "`n完成。" -ForegroundColor Green
}
catch {
    Write-Host "`n出错：$_" -ForegroundColor Red
}
finally {
    Stop-Transcript | Out-Null
    Write-Host "日志：$log  按回车关闭。"
    Read-Host
}
