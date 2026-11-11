# 把 tools/logs/wow-blp.txt 里列出的贴图（每行一个 MPQ 内路径）从本机 WoW 数据转成 PNG 到 tools/logs/wow-blp/（只供本地查看，不入库）。
. (Join-Path $PSScriptRoot '_common.ps1')
Start-ExileLog 'wow-blp'
try {
    $data = Get-WowData
    $exe = Join-Path $RepoRoot 'client\target\play\benilla-extract.exe'
    if (-not (Test-Path $exe)) { throw '找不到 benilla-extract.exe，请先运行 run-wow-dbc' }
    $out = Join-Path $LogDir 'wow-blp'
    New-Item -ItemType Directory -Force $out | Out-Null
    foreach ($p in Get-Content (Join-Path $LogDir 'wow-blp.txt')) {
        if (-not $p.Trim()) { continue }
        $name = [IO.Path]::GetFileNameWithoutExtension($p) + '.png'
        try { Invoke-Exile $exe @($data, 'blp', $p.Trim(), (Join-Path $out $name)) } catch { Write-ExileLog "跳过 $p：$_" }
    }
    Complete-ExileLog $true ''
}
catch { Write-ExileLog "错误: $_"; Complete-ExileLog $false '' }
