# Bootstrap uses only an approved incoming launcher and production public trust.
function Assert-BapHelperBootstrap($Bundle) {
    $names=@('helper-bootstrap/BetterAstralParty-UpdateHelper.exe','helper-bootstrap/helper.descriptor','helper-bootstrap/helper.signature')
    foreach($name in $names){if(!$Bundle.SourceHashes.ContainsKey($name)){throw '자동 업데이트 준비 파일이 없는 설치 묶음입니다. 전체 ZIP을 다시 받으세요. / Complete signed helper bootstrap required.'}}
    $null = Assert-BapInstallRoot $Bundle
    $assembly=Get-BapInstallVerifier $Bundle
    $method=$assembly.GetType('SteamLauncher').GetMethod('ValidateHelperBootstrap',[Reflection.BindingFlags]'Public,Static')
    if(!$method){throw '최초 설치 helper 검증 API가 없습니다. 최신 전체 묶음을 사용하세요. / Helper bootstrap API missing.'}
    $bootstrapRoot=Join-Path $Bundle.PackageRoot 'helper-bootstrap'
    $hash=$Bundle.SourceHashes['helper-bootstrap/BetterAstralParty-UpdateHelper.exe']
    try{$verified=$method.Invoke($null,@([string]$bootstrapRoot,[string]$Bundle.Version.Text,[string]$hash))}
    catch{throw ('자동 업데이트 helper 서명을 확인하지 못했습니다. / Helper bootstrap signature validation failed. '+$_.Exception.GetBaseException().Message)}
    if($verified -isnot [string] -or $verified -cne $hash){throw 'Helper bootstrap hash response mismatch.'}
}
function Install-BapHelperBootstrap($Bundle) {
    Assert-BapHelperBootstrap $Bundle
    $assembly=Get-BapInstallVerifier $Bundle
    $method=$assembly.GetType('SteamLauncher').GetMethod('InstallHelperBootstrap',[Reflection.BindingFlags]'Public,Static')
    if(!$method){throw 'Helper installation API missing.'}
    $bootstrapRoot=Join-Path $Bundle.PackageRoot 'helper-bootstrap'
    $hash=$Bundle.SourceHashes['helper-bootstrap/BetterAstralParty-UpdateHelper.exe']
    try{$result=$method.Invoke($null,@([string]$Bundle.GameRoot,[string]$bootstrapRoot,[string]$Bundle.Version.Text,[string]$hash))}
    catch{throw ('모드 파일 설치는 완료됐지만 자동 업데이트 준비가 완료되지 않았습니다. 게임을 닫고 기록·백업을 보존하세요. / Mod files committed; updater bootstrap incomplete. Keep the game closed and retain recovery files. '+$_.Exception.GetBaseException().Message)}
    if($result -isnot [int]){throw 'Invalid helper bootstrap exit response.'}
    if($result -eq 4){throw '모드 파일 설치는 완료됐지만 기존 helper의 수동 migration이 필요합니다. 기존 helper·설정·기록을 보존했습니다. / Mod files committed; existing helper needs manual migration. Existing helper, settings and records preserved.'}
    if($result -ne 0){throw ('모드 파일 설치는 완료됐지만 자동 업데이트 준비가 완료되지 않았습니다. 기록을 보존하고 복구하세요. / Mod files committed; updater bootstrap incomplete. Retain records for recovery. Exit: '+$result)}
    # Actual committed fingerprint, not merely a successful process start, defines completion.
    $owned=$assembly.GetType('SteamLauncher').GetMethod('CheckOwnedHelper',[Reflection.BindingFlags]'Public,Static').Invoke($null,@([string]$Bundle.GameRoot))
    if($owned -isnot [string] -or !$owned.StartsWith($hash+':',[StringComparison]::Ordinal)){throw 'Installed helper ownership could not be verified; retain files and recovery records.'}
    Write-Host '자동 업데이트 helper 준비 완료. / Automatic update helper ready.'
}

function Invoke-BapWholeBridge($Bundle, [string]$Method, [string[]]$Additional=@()) {
    $assembly=Get-BapInstallVerifier $Bundle
    $api=$assembly.GetType('SteamLauncher').GetMethod($Method,[Reflection.BindingFlags]'Public,Static')
    if (!$api) { throw 'Verified whole bridge API missing.' }
    [object[]]$arguments=@([string]$Bundle.GameRoot,[string]$Bundle.PackageRoot,[string]$Bundle.Version.Text)+@($Additional)
    try { return $api.Invoke($null,$arguments) }
    catch { throw ('Whole migration blocked; retain files/settings/recovery evidence. '+$_.Exception.GetBaseException().Message) }
}
function Assert-BapWholeBridge($Bundle) {
    $Bundle.WholeObservation=Invoke-BapWholeBridge $Bundle 'ValidateWholeBridge'
    if ($Bundle.WholeObservation -isnot [string]) { throw 'Invalid whole observation response.' }
}

function Start-BapInstallerDiagnostics($Bundle) {
    try { return (Get-BapInstallVerifier $Bundle).GetType('SteamLauncher').GetMethod('BeginInstallerDiagnostics',[Reflection.BindingFlags]'Public,Static').Invoke($null,@([string]$Bundle.GameRoot,[string]$Bundle.Version.Text)) }
    catch { Write-Warning 'Minimal local diagnostic setup unavailable.'; return $null }
}
function Write-BapInstallerStage($Bundle,[string]$Phase,[string]$Outcome='Begin') {
    if (!$Bundle) { return }
    try { $null=(Get-BapInstallVerifier $Bundle).GetType('SteamLauncher').GetMethod('RecordInstallerStage',[Reflection.BindingFlags]'Public,Static').Invoke($null,@($Phase,$Outcome)) } catch {}
}
function Write-BapInstallerError($Bundle,[string]$Phase,[Exception]$Error) {
    if (!$Bundle) { return }
    try { $null=(Get-BapInstallVerifier $Bundle).GetType('SteamLauncher').GetMethod('RecordInstallerFailure',[Reflection.BindingFlags]'Public,Static').Invoke($null,@($Phase,$Error)) } catch {}
}
