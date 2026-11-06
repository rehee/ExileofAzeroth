# 一键运行 Exile 模式：编译并后台启动 Exile Runtime，再以 Exile 模式启动客户端（不连 WoW 服务器）。
# 客户端关闭后 runtime 随之退出（D26）。日志：tools/logs/exile-play.log（客户端）、tools/logs/runtime.log。
. (Join-Path $PSScriptRoot '_common.ps1')
Start-ExileLog 'exile-play'
$runtime = $null
try {
    $port = 7710
    $config = Join-Path $RepoRoot 'config\dev.local.json'
    $proj = Join-Path $RepoRoot 'src\Exile.Runtime\Exile.Runtime.csproj'
    Invoke-Exile 'dotnet' @('build', $proj, '-c', 'Release', '--nologo', '-v', 'q')
    $runtimeExe = Join-Path $RepoRoot 'src\Exile.Runtime\bin\Release\net8.0\Exile.Runtime.exe'
    $runtimeLog = Join-Path $LogDir 'runtime.log'
    $runtime = Start-Process -FilePath $runtimeExe -ArgumentList @('--port', $port, '--config', "`"$config`"") `
        -RedirectStandardOutput $runtimeLog -RedirectStandardError (Join-Path $LogDir 'runtime.err.log') -PassThru -WindowStyle Hidden
    Write-ExileLog "runtime 已启动 pid=$($runtime.Id)"

    $settings = Get-Content $config -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $env:WOW_DATA) { $env:WOW_DATA = 'C:\wow_survival\wow-data\Data' }
    $env:EXILE_RUNTIME = "ws://127.0.0.1:$port/"
    $env:WOW_USER = 'exile'
    $env:WOW_PASS = 'exile'
    if ($settings.dqq.testCharacter) { $env:WOW_CHAR = $settings.dqq.testCharacter }
    # D27：进入世界为自由模式。聊天框输入 .replay 进入战斗回放。
    # 无人值守测试：dev.local.json 的 dqq.autoReplay=true 时，进入世界 10 秒后自动走一遍回放流程。
    if ($settings.dqq.autoReplay) { $env:EXILE_AUTOCOMBAT = '1' } else { Remove-Item Env:EXILE_AUTOCOMBAT -ErrorAction SilentlyContinue }
    # 诊断探针（开发版客户端）：dev.local.json 的 probe.key / probe.lua → WOW_PROBE_KEY / WOW_PROBE_LUA。
    foreach ($n in 'WOW_PROBE_KEY','WOW_PROBE_LUA') { Remove-Item "Env:$n" -ErrorAction SilentlyContinue }
    if ($settings.probe.key) { $env:WOW_PROBE_KEY = $settings.probe.key }
    if ($settings.probe.lua) { $env:WOW_PROBE_LUA = $settings.probe.lua }
    $exe = Join-Path $RepoRoot 'client\target\play\benilla.exe'
    if (-not (Test-Path $exe)) { throw "找不到 $exe，请先运行 client-build。" }
    Set-Location (Join-Path $RepoRoot 'client')
    Invoke-Exile $exe @()
    Complete-ExileLog $true ''
}
catch { Write-ExileLog "错误: $_"; Complete-ExileLog $false '' }
finally {
    if ($runtime -and -not $runtime.HasExited) {
        Start-Sleep -Seconds 2
        if (-not $runtime.HasExited) { Stop-Process -Id $runtime.Id -Force; Write-ExileLog 'runtime 已结束' }
    }
}
