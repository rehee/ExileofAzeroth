# 编译 DQQ 三个核心项目，把它们和全部依赖（含中文资源）复制到 source/dqq/（D23）。
# 跳过 DQQ 的本地化同步（它会改写 DQQ 目录里的生成文件，违反 DQQ 零改动）；只用 DQQ 现有资源。
# 用法：双击 run-update-dqq.cmd。DQQ 源码目录：config\dev.local.json 的 paths.dqqRoot（可用环境变量 DQQ_ROOT 临时覆盖）。
. (Join-Path $PSScriptRoot '_common.ps1')
Start-ExileLog 'update-dqq'
try {
    $dqqRoot = Get-DqqRoot
    $collector = Join-Path $PSScriptRoot 'dqq-collector\DqqCollector.csproj'
    $tmp = Join-Path $env:TEMP "exile-dqq-$(Get-Date -Format yyyyMMddHHmmss)"
    $dest = Join-Path $RepoRoot 'source\dqq'

    Invoke-Exile 'dotnet' @('--list-sdks')
    Invoke-Exile 'dotnet' @('publish', $collector, '-c', 'Release', '-o', $tmp, "-p:DqqRoot=$dqqRoot", '-p:SkipLocalizationSync=true', '--nologo')

    if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Get-ChildItem -Path $tmp -Recurse -File |
        Where-Object { $_.Name -notlike 'DqqCollector*' } |
        ForEach-Object {
            $rel = $_.FullName.Substring($tmp.Length).TrimStart('\')
            $target = Join-Path $dest $rel
            New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $target
        }

    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $commit = (& git -C $dqqRoot rev-parse HEAD 2>$null)
    $dirty = @(& git -C $dqqRoot status --porcelain 2>$null).Count
    $sdk = (& dotnet --version)
    $ErrorActionPreference = $old
    $version = [ordered]@{
        dqqRoot = $dqqRoot; dqqCommit = "$commit"; dqqUncommittedFiles = $dirty
        builtAt = (Get-Date).ToString('s'); sdk = "$sdk"
        files = @(Get-ChildItem -Path $dest -Recurse -File).Count
    }
    $version | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dest 'VERSION.json') -Encoding UTF8
    Write-ExileLog ($version | ConvertTo-Json)
    Get-ChildItem -Path $dest -Directory | ForEach-Object { Write-ExileLog "子目录: $($_.Name)" }
    Get-ChildItem -Path $dest -Filter 'DQQ*.dll' | ForEach-Object { Write-ExileLog "DQQ: $($_.Name)" }
    Remove-Item -Recurse -Force $tmp
    Complete-ExileLog $true ''
}
catch { Write-ExileLog "错误: $_"; Complete-ExileLog $false '' }
