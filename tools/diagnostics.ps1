param(
    [ValidateSet('Menu','Collect','Open')][string]$Action = 'Menu',
    [string]$GameRoot = '',
    [ValidateSet('ko','en')][string]$Language = 'ko',
    [switch]$NoOpen
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Say([string]$Ko, [string]$En) { if ($Language -eq 'ko') { Write-Host $Ko } else { Write-Host $En } }
try {
    $source = Join-Path (Split-Path -Parent $PSScriptRoot) 'diagnostics/DiagnosticBundle.cs'
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw 'BAP-DIAGNOSTICS-TOOL-MISSING' }
    $projection = Join-Path (Split-Path -Parent $PSScriptRoot) 'diagnostics/MinimalDiagnosticProjection.cs'
    if (!(Test-Path -LiteralPath $projection -PathType Leaf)) { throw 'BAP-DIAGNOSTICS-PROJECTION-MISSING' }
    if (!('BetterAstralParty.Diagnostics.DiagnosticBundle' -as [type])) {
        if ($PSVersionTable.PSVersion.Major -lt 6) {
            Add-Type -Path @($source,$projection) -ReferencedAssemblies 'System.dll','System.Core.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll'
        } else { Add-Type -Path @($source,$projection) }
    }
    $local = [Environment]::GetFolderPath('LocalApplicationData')
    $profile = [Environment]::GetFolderPath('UserProfile')
    $temp = [IO.Path]::GetTempPath()
    $primaryHub = Join-Path $local 'BetterAstralParty/Diagnostics'
    $fallbackHub = Join-Path $temp 'BetterAstralParty/Diagnostics'
    if ($Action -eq 'Menu') {
        Say '1: 진단 ZIP 만들기  2: 진단 폴더 열기  Enter: 종료' '1: Create diagnostics ZIP  2: Open diagnostics folder  Enter: Exit'
        $choice = Read-Host
        if ($choice -eq '1') { $Action = 'Collect' } elseif ($choice -eq '2') { $Action = 'Open' } else { exit 0 }
    }
    if ($Action -eq 'Open') {
        $lease = [BetterAstralParty.Diagnostics.DiagnosticBundle]::AcquireHub($primaryHub, $fallbackHub)
        try {
            $hub = $lease.Path
            Say "진단 폴더: $hub" "Diagnostics folder: $hub"
            Say 'Create-Diagnostics.cmd로 ZIP을 만든 뒤 ZIP 안의 로그를 볼 수 있습니다.' 'Create a ZIP with Create-Diagnostics.cmd, then open its logs.'
            if (!$NoOpen) { $lease.Open([Action[string]]{ param($folder) Start-Process -FilePath $folder }) }
        } finally { $lease.Dispose() }
        exit 0
    }
    if (!$GameRoot) {
        # Probe only adjacent deployment roots; no registry, account, Steam profile or recursive search.
        $deployment = Split-Path -Parent $PSScriptRoot
        foreach ($candidate in @($deployment, (Split-Path -Parent $deployment))) {
            if ((Test-Path -LiteralPath (Join-Path $candidate 'AstralParty_INT.exe') -PathType Leaf) -and
                (Test-Path -LiteralPath (Join-Path $candidate 'AstralParty_INT_Data') -PathType Container)) { $GameRoot = $candidate; break }
        }
        if (!$GameRoot) {
            Say 'Steam의 설치 폴더 경로를 붙여 넣으세요. 비워 두면 설치·런처·Unity 로그만 수집합니다.' 'Paste the INT installation folder from Steam. Leave blank for install/launcher/Unity logs only.'
            $GameRoot = Read-Host
        }
    }
    Say '최근 설치·초기 실행·런처·호환성 오류 본문과 게임 진단 요약을 ZIP에 포함합니다. 토큰·개인 경로는 가리며 카드·손패·채팅·덤프·전체 설정은 제외합니다.' 'Including recent install/bootstrap/launcher/compatibility error text and game diagnostic summaries. Tokens and private paths are redacted; card/hand/chat payload, dumps and full settings are excluded.'
    $bundle = [BetterAstralParty.Diagnostics.DiagnosticBundle]::Collect($GameRoot,
        (Join-Path $local 'BetterAstralParty/Logs'), (Join-Path $temp 'BetterAstralParty'),
        (Join-Path $profile 'AppData/LocalLow/feimo/AstralParty_INT'), $primaryHub, $fallbackHub,
        $PSVersionTable.PSVersion.ToString())
    Say "ZIP 생성됨: $($bundle.ArchivePath)" "ZIP created: $($bundle.ArchivePath)"
    Say "포함 로그 $($bundle.Included)개, 제외·누락 항목 $($bundle.Omitted)개. summary.json에서 이유를 확인하세요." "Included $($bundle.Included) logs, omitted/missing $($bundle.Omitted) items. See summary.json for reasons."
    Say '개인정보가 남을 수 있습니다. ZIP 내용을 확인한 뒤 개발자에게 직접 첨부하세요.' 'Unrecognised personal data may remain. Review the ZIP, then attach it to the developer yourself.'
    if (!$NoOpen) {
        try { $bundle.OpenFolder([Action[string]]{ param($folder) Start-Process -FilePath $folder }) }
        catch { Say 'ZIP은 생성됐지만 폴더를 열지 못했습니다. 위 경로로 직접 열어 주세요.' 'The ZIP was created, but the folder could not open. Use the path above.' }
    }
    exit 0
} catch {
    # Exception messages/paths may themselves contain private information. Print fixed codes only.
    Say 'BAP-DIAGNOSTICS-FAILED: 진단 도구 또는 쓰기 권한을 확인하고 다시 실행하세요. 기존 로그는 보존됩니다.' 'BAP-DIAGNOSTICS-FAILED: Check the diagnostics tool and write permissions, then retry. Existing logs are preserved.'
    exit 1
}
