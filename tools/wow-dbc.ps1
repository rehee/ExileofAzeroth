# 从本机 WoW 1.12.1 数据导出 DBC 原始文件到 tools/logs/wow-dbc/（仅供本地分析选模型/法术，不入库、不分发）。
. (Join-Path $PSScriptRoot '_common.ps1')
Start-ExileLog 'wow-dbc'
try {
    $cargo = (Get-Command cargo -ErrorAction SilentlyContinue).Source
    if (-not $cargo) {
        $rustHome = if ($env:EXILE_RUST_HOME) { $env:EXILE_RUST_HOME } else { 'C:\wow_survival\tools' }
        $env:CARGO_HOME = Join-Path $rustHome 'cargo'
        $env:RUSTUP_HOME = Join-Path $rustHome 'rustup'
        $env:PATH = (Join-Path $env:CARGO_HOME 'bin') + ';' + $env:PATH
        $cargo = Join-Path $env:CARGO_HOME 'bin\cargo.exe'
    }
    $data = if ($env:WOW_DATA) { $env:WOW_DATA } else { 'C:\wow_survival\wow-data\Data' }
    Set-Location (Join-Path $RepoRoot 'client')
    Invoke-Exile $cargo @('build', '--profile', 'play', '-p', 'benilla-formats', '--bin', 'benilla-extract')
    $exe = Join-Path $RepoRoot 'client\target\play\benilla-extract.exe'
    $out = Join-Path $PSScriptRoot 'logs\wow-dbc'
    New-Item -ItemType Directory -Force $out | Out-Null
    $names = 'CreatureDisplayInfo','CreatureDisplayInfoExtra','CreatureModelData','CreatureFamily','CreatureType',
             'Spell','SpellChainEffects','SpellVisual','SpellVisualKit','SpellVisualEffectName','SpellIcon','SpellCastTimes','SpellMissile',
             'AnimationData','ItemDisplayInfo','CharSections','CharHairGeosets','ChrRaces','ChrClasses','AreaTable','Map','Lock'
    foreach ($n in $names) {
        try { Invoke-Exile $exe @($data, 'extract', "DBFilesClient\$n.dbc", (Join-Path $out "$n.dbc")) } catch { Write-ExileLog "跳过 $n：$_" }
    }
    $ErrorActionPreference = 'Continue'
    & $exe $data list 2>$null | Out-File -Encoding utf8 (Join-Path $out 'files.txt')
    $ErrorActionPreference = 'Stop'
    Write-ExileLog ("文件数: " + (Get-ChildItem $out).Count)
    Complete-ExileLog $true ''
}
catch { Write-ExileLog "错误: $_"; Complete-ExileLog $false '' }
