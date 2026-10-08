param(
    [Parameter(Mandatory)]
    [ValidateSet('Mod','Vanilla')]
    [string]$Mode,
    [string]$GameRoot = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '8vJXnINT')
)

$ErrorActionPreference = 'Stop'
if ($Mode -eq 'Mod') {
    throw 'Steam 기본 실행은 Vanilla로 유지합니다. 게임 폴더의 BetterAstralParty-Mod.cmd로 실행하세요.'
}
$config = Join-Path $GameRoot 'doorstop_config.ini'
if (-not (Test-Path -LiteralPath $config)) {
    throw "BepInEx가 설치되지 않았습니다: $config"
}

$enabled = if ($Mode -eq 'Mod') { 'true' } else { 'false' }
$content = Get-Content -LiteralPath $config -Raw
$updated = [regex]::Replace($content, '(?m)^enabled\s*=\s*(true|false)\s*$', "enabled=$enabled", 1)
if ($updated -eq $content -and $content -notmatch '(?m)^enabled\s*=') {
    throw 'doorstop_config.ini에서 enabled 설정을 찾지 못했습니다.'
}

[IO.File]::WriteAllText($config, $updated, [Text.UTF8Encoding]::new($false))
Write-Host $(if ($Mode -eq 'Mod') { 'BetterAstralParty 모드 실행으로 설정됨' } else { '완전 바닐라 실행으로 설정됨' })
