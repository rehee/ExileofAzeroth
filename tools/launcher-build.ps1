# 生成演示版启动器 dist\EoALauncher.exe（用户 2026-11-08）：单个 exe，玩家放进 WoW 1.12.1 目录代替 WoW.exe 运行。
# 步骤：编译客户端（项目内 Rust）→ runtime 自带 .NET 发布 → 客户端、runtime（含对应表）、插件打成 zip
#      → 启动器单文件发布 → zip 接到 exe 末尾。不包含任何暴雪文件。
# 用法：双击 run-launcher-build.cmd。日志 tools\logs\launcher-build.log。
. (Join-Path $PSScriptRoot '_common.ps1')
Start-ExileLog 'launcher-build'
try {
    $work = Join-Path $RepoRoot 'build\launcher'
    $payload = Join-Path $work 'payload'
    $dist = Join-Path $RepoRoot 'dist'
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
    New-Item -ItemType Directory -Force -Path $payload, $dist | Out-Null

    # 1. 客户端
    $cargo = Use-ExileRust
    Push-Location (Join-Path $RepoRoot 'client')
    try { Invoke-Exile $cargo @('build', '--profile', 'play', '-p', 'benilla') } finally { Pop-Location }
    $clientDir = Join-Path $payload 'client'
    New-Item -ItemType Directory -Force -Path $clientDir | Out-Null
    Copy-Item (Join-Path $RepoRoot 'client\target\play\benilla.exe') $clientDir
    foreach ($lic in 'LICENSE-MIT', 'LICENSE-APACHE') { Copy-Item (Join-Path $RepoRoot "client\$lic") $clientDir }

    # 2. runtime：自带 .NET，玩家不用另装（对应表随发布复制到 runtime\mapping\）
    $proj = Join-Path $RepoRoot 'src\Exile.Runtime\Exile.Runtime.csproj'
    Invoke-Exile 'dotnet' @('publish', $proj, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', (Join-Path $payload 'runtime'), '--nologo', '-v', 'q')
    $mapping = Join-Path $payload 'runtime\mapping'
    New-Item -ItemType Directory -Force -Path $mapping | Out-Null
    Copy-Item (Join-Path $RepoRoot 'config\mapping\*.json') $mapping -Force

    # 3. 插件：装到客户端状态目录 benilla-config\AddOns\
    $addons = Join-Path $payload 'benilla-config\AddOns'
    New-Item -ItemType Directory -Force -Path $addons | Out-Null
    Get-ChildItem -Path (Join-Path $RepoRoot 'addons') -Directory | ForEach-Object { Copy-Item -Recurse $_.FullName (Join-Path $addons $_.Name) }

    Set-Content -Path (Join-Path $payload 'README.txt') -Encoding UTF8 -Value @(
        'Exile of Azeroth 演示版（非官方粉丝项目）。',
        '需要自备魔兽世界 1.12.1 英文版客户端；本包不包含任何暴雪的文件。',
        '运行 WoW 目录里的 EoALauncher.exe，在登录界面输入 DQQ 账号密码。日志在 Exile\logs\。')

    # 4. 打包
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = Join-Path $work 'payload.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $id = (Get-FileHash -Algorithm SHA256 $zip).Hash.Substring(0, 32).ToLowerInvariant()
    Write-ExileLog "包 $id $([math]::Round((Get-Item $zip).Length/1MB,1)) MB"

    # 5. 启动器本体
    $launcherOut = Join-Path $work 'launcher'
    Invoke-Exile 'dotnet' @('publish', (Join-Path $RepoRoot 'src\Exile.Launcher\Exile.Launcher.csproj'), '-c', 'Release', '-o', $launcherOut, '--nologo', '-v', 'q')

    # 6. exe + zip + [32 字节包标识][8 字节 zip 长度][魔数 EOAPAYL1]
    $target = Join-Path $dist 'EoALauncher.exe'
    $out = [IO.File]::Create($target)
    try {
        foreach ($part in (Join-Path $launcherOut 'EoALauncher.exe'), $zip) {
            $in = [IO.File]::OpenRead($part)
            try { $in.CopyTo($out) } finally { $in.Dispose() }
        }
        $tail = [Text.Encoding]::ASCII.GetBytes($id)
        $out.Write($tail, 0, $tail.Length)
        $len = [BitConverter]::GetBytes([long](Get-Item $zip).Length)
        $out.Write($len, 0, 8)
        $magic = [Text.Encoding]::ASCII.GetBytes('EOAPAYL1')
        $out.Write($magic, 0, 8)
    } finally { $out.Dispose() }
    Write-ExileLog "产物: $target $([math]::Round((Get-Item $target).Length/1MB,1)) MB"
    Complete-ExileLog $true ''
}
catch { Write-ExileLog "错误: $_"; Complete-ExileLog $false '' }
