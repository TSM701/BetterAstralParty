param(
    [string]$GameRoot,
    [switch]$RestartSteam,
    [switch]$UpdateOnly,
    [string]$RecoveryPlan,
    [uint32]$AccountId = 0
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'install-log.ps1')
. (Join-Path $PSScriptRoot 'steam-session.ps1')
$steamSession = @{ Restart=$false }
$installStage = 'Steam registration lookup'
$transaction = $null
$registered = $false
$installLock = $null
$updaterMaintenance = $null
$installBundle = $null
$rollbackExpected = @{}
$diagnosticOwner=$null
$diagnosticPhase='BootstrapGate'
try {
$steamRoot = $null
if ($UpdateOnly) {
    if (!$GameRoot -or $RestartSteam -or $AccountId) { throw 'UpdateOnly requires GameRoot and cannot register/restart Steam.' }
} else {
    $steamRoot = (Get-ItemProperty 'HKCU:/Software/Valve/Steam').SteamPath
    $accountId = & (Join-Path $PSScriptRoot 'steam-shortcut.ps1') -Mode ResolveAccount -SteamRoot $steamRoot -AccountId $AccountId
}
. (Join-Path $PSScriptRoot 'find-game.ps1')
$installStage = 'INT installation discovery'
if (!$GameRoot) {
    $candidates = @(Find-IntGameDirectories $steamRoot $root)
    $installStage = "INT installation discovery; candidates=$($candidates.Count)"
    if ($candidates.Count -eq 1) { $GameRoot = $candidates[0] }
    else {
        Write-Host "Found $($candidates.Count) INT installations. Select a number or paste the game folder path."
        for ($i = 0; $i -lt $candidates.Count; $i++) { Write-Host "$($i + 1): $($candidates[$i])" }
        $choice = Read-Host 'INT game folder (AstralParty_INT.exe), or Astral Party folder; blank cancels'
        if ([string]::IsNullOrWhiteSpace($choice)) { throw 'Installation cancelled; no game files changed.' }
        $index = 0
        if ([int]::TryParse($choice, [ref]$index) -and $index -ge 1 -and $index -le $candidates.Count) {
            $GameRoot = $candidates[$index - 1]
        } else { $GameRoot = $choice }
    }
}
$resolvedGame = Resolve-IntGameDirectory $GameRoot
if (!$resolvedGame) { throw 'INT game executable not found. Select the folder containing AstralParty_INT.exe; CN is unsupported.' }
$GameRoot = $resolvedGame
Write-Host "INT installation: $GameRoot"
$exe = Join-Path $GameRoot 'AstralParty_INT.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Astral Party 국제판 실행 파일을 찾지 못했습니다: $exe"
}
. (Join-Path $PSScriptRoot 'install-backup.ps1')
. (Join-Path $PSScriptRoot 'install-policy.ps1')
. (Join-Path $PSScriptRoot 'install-helper.ps1')
. (Join-Path $PSScriptRoot 'install-reinstall.ps1')
$installStage = 'Incoming installation bundle validation'
$installBundle = Open-BapInstallBundle -PackageRoot $root -GameRoot $GameRoot -RecoveryPlan $RecoveryPlan
$diagnosticOwner=Start-BapInstallerDiagnostics $installBundle
Write-BapInstallerStage $installBundle 'BootstrapGate'

Write-BapInstallerStage $installBundle 'BootstrapGate' 'Completed'
$diagnosticPhase='HelperInstall'
Write-BapInstallerStage $installBundle $diagnosticPhase
$installStage = 'Signed automatic update helper preflight'
Assert-BapHelperBootstrap $installBundle
Write-BapInstallerStage $installBundle $diagnosticPhase 'Completed'
# Authenticate the incoming bundle/root first. A partially copied ordinary installation
# must restore its complete reviewed journal before current owned8/receipt observation.
$pending=Get-InstallJournalPath $GameRoot
$null=New-Item -ItemType Directory -Force -Path (Split-Path -Parent $pending)
$installLock=[IO.File]::Open(($pending+'.lock'),'OpenOrCreate','ReadWrite','None')
if(Test-Path -LiteralPath $pending){
    $installStage='Interrupted installation recovery'
    $recoveryTargets=Get-BapInstallRecoveryTargets $installBundle
    $journal=Read-InstallRecoveryJournal $GameRoot $recoveryTargets
    Write-Warning "Unfinished installation found: $pending"
    if((Read-Host 'Restore the saved pre-install files? Later edits to those files will be replaced. [y/N]') -cne 'y'){
        throw 'Recovery cancelled; no files changed. Keep the backup for support.'
    }
    Stop-BapGame $GameRoot
    $recoveryLease=Invoke-BapWholeBridge $installBundle 'GuardWholeRecovery'
    try {
        $null=Assert-BapInstallRoot $installBundle
        Restore-InstallTransaction $GameRoot -AllowedTargets $recoveryTargets -ExpectedJournalHash $journal.JournalSha256 -VerifiedBundle $installBundle
    } finally {$recoveryLease.Dispose()}
} else {Stop-BapGame $GameRoot}
# Reuse the ordinary install lock; only complete removal may start a new generation.
$installStage='Completed removal / new installation generation validation'
$reinstallPlan=Get-BapCleanReinstallPlan $installBundle
if ($reinstallPlan) {
    if ($UpdateOnly -or $RecoveryPlan) { throw 'Clean reinstall requires normal installation, not UpdateOnly or recovery.' }
    Write-Host ("완전 제거 상태를 확인했습니다. 이전 업데이트 이력을 보존하고 새로 설치합니다. / Complete removal verified; archive previous history and install: "+$reinstallPlan.Version+' -> '+$installBundle.Version.Text)
    if ((Read-Host '새 설치를 진행할까요? / Start a new installation generation? [y/N]') -cne 'y') { throw 'Reinstall cancelled; history retained.' }
    Move-BapReinstallHistory $installBundle $reinstallPlan -HeldBackupLock $installLock
}
# A durable whole intent can now either finish the exact target or retire an exact restored origin.
$null=Invoke-BapWholeBridge $installBundle 'RecoverWholeHelper'
Assert-BapWholeBridge $installBundle
Assert-BapInstallTransition $installBundle -ConfirmRecovery
$installStage = 'Compatibility validation'
$manifest = Join-Path $root 'artifacts/BetterAstralParty.compatibility.json'
$report = & (Join-Path $PSScriptRoot 'check-compatibility.ps1') -GameRoot $GameRoot -Manifest $manifest -LoaderRoot (Join-Path $root '.deps/bepinex') -CheckOnly
if (!$report.Allowed) { throw ('호환성 검사 실패: ' + ($report.Issues -join '; ')) }
foreach ($warning in $report.Warnings) { Write-Warning $warning }

$installStage = 'Package file validation'
$artifact = Join-Path $root 'artifacts\BetterAstralParty.dll'
if (-not (Test-Path -LiteralPath $artifact)) {
    throw 'Mod DLL missing. Extract the complete release ZIP again.'
}

$pluginDir = Join-Path $GameRoot 'BepInEx\plugins\BetterAstralParty'
$launcher = Join-Path $root 'artifacts/BetterAstralParty-Launcher.exe'
if (!(Test-Path -LiteralPath $launcher)) { throw 'Launcher missing. Extract the complete release ZIP again.' }
$installFiles = @(
    @{ Source = $launcher; Target = (Join-Path $GameRoot 'BetterAstralParty-Launcher.exe') }
    @{ Source = $artifact; Target = (Join-Path $pluginDir 'BetterAstralParty.dll') }
    @{ Source = $manifest; Target = (Join-Path $GameRoot 'BetterAstralParty.compatibility.json') }
    foreach ($name in @('BetterAstralParty-Mod.cmd','AstralParty-Vanilla.cmd','check-compatibility.ps1','BetterAstralParty-Steam.ps1','steam-shortcut.ps1')) {
        @{ Source = (Join-Path $PSScriptRoot $name); Target = (Join-Path $GameRoot $name) }
    }
)
$freshLoader = !(Test-Path -LiteralPath (Join-Path $GameRoot 'doorstop_config.ini'))
$ownedInstallFiles = @($installFiles)
if ($freshLoader) {
    $loaderRoot = Join-Path $root '.deps/bepinex'
    $installFiles += @(New-BapSharedLoaderPlan $installBundle $loaderRoot)
}
$bepinexConfig = Join-Path $GameRoot 'BepInEx/config/BepInEx.cfg'
$receiptPath = Join-Path $GameRoot 'BetterAstralParty.install.json'
$loaderBefore = $null
if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
    $oldReceipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($oldReceipt.owner -eq 'kr.betterastralparty.mod' -and $oldReceipt.loaderBefore -is [bool]) { $loaderBefore = $oldReceipt.loaderBefore }
} elseif (!(Test-Path -LiteralPath (Join-Path $pluginDir 'BetterAstralParty.dll'))) {
    if ($freshLoader) { $loaderBefore = $false }
    else {
        $matchesEnabled = [regex]::Matches((Get-Content -LiteralPath (Join-Path $GameRoot 'doorstop_config.ini') -Raw), '(?m)^enabled\s*=\s*(true|false)\s*$')
        if ($matchesEnabled.Count -eq 1) { $loaderBefore = $matchesEnabled[0].Groups[1].Value -eq 'true' }
    }
}
if ($UpdateOnly) {
    if ($freshLoader -or !(Test-Path -LiteralPath (Join-Path $pluginDir 'BetterAstralParty.dll') -PathType Leaf) -or
        !(Test-Path -LiteralPath $receiptPath -PathType Leaf)) { throw 'UpdateOnly requires an existing installation. Use normal installation first.' }
    Assert-InstallTarget $GameRoot $receiptPath
    $existingReceipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($existingReceipt.owner -ne 'kr.betterastralparty.mod' -or $existingReceipt.schema -ne 1) { throw 'Invalid existing installation receipt.' }
}
$baseline=if($null -eq $loaderBefore){'null'}elseif($loaderBefore){'true'}else{'false'}
$preparedReceipt=Invoke-BapWholeBridge $installBundle 'PrepareWholeReceipt' @([string]$installBundle.WholeObservation,[string]$baseline)
if ($preparedReceipt -isnot [string]) { throw 'Invalid signed channel receipt response.' }
$targets = @((@($installFiles | ForEach-Object { $_.Target }) + @($bepinexConfig, (Join-Path $GameRoot 'doorstop_config.ini'), $receiptPath)) | Sort-Object -Unique)
$installBytes = [long]0
foreach ($file in $installFiles) {
    if (!(Test-Path -LiteralPath $file.Source -PathType Leaf)) { throw "Package file missing: $($file.Source)" }
    $installBytes += (Get-Item -LiteralPath $file.Source).Length
}
$installStage = 'Write permission / file lock preflight'
foreach ($target in $targets) { Assert-InstallTarget $GameRoot $target }
Test-InstallWritable $targets
$needsShortcut = !$UpdateOnly -and (& (Join-Path $PSScriptRoot 'steam-shortcut.ps1') -Mode PlanRegister -GameRoot $GameRoot -SteamRoot $steamRoot -AccountId $accountId)
if ($needsShortcut) {
    $shortcutPath = Join-Path $steamRoot "userdata/$accountId/config/shortcuts.vdf"
    if (!(Test-Path -LiteralPath (Split-Path -Parent $shortcutPath) -PathType Container)) { throw 'Steam account configuration directory missing. Sign in first.' }
    Test-InstallWritable @($shortcutPath)
}
if (Test-Path -LiteralPath (Join-Path $pluginDir 'BetterAstralParty.dll') -PathType Leaf) {
    Write-Host '기존 모드 설치가 확인되었습니다. 사용자 설정을 유지하고 변경 대상 파일을 백업합니다. / Existing installation: settings preserved; target files backed up.'
}

# Never force-close Steam or an active game. Registration must happen offline.
$installStage = 'Steam shortcut planning / graceful shutdown'
if ($needsShortcut) { Stop-BapSteam $steamRoot $steamSession }
try {
if (@(Get-BapGameTargets $GameRoot).Count) { throw 'Game/launcher started; installation cancelled.' }
if ($needsShortcut -and (Get-Process steam -ErrorAction SilentlyContinue)) { throw 'Steam restarted; installation cancelled.' }
$diagnosticPhase='Apply'
Write-BapInstallerStage $installBundle $diagnosticPhase
$installStage = 'Previous installation backup'
Assert-BapInstallUnchanged $installBundle
# The signed incoming proof, exact old helper identities, origin backups and target receipt
# become durable under the native update lock before any owned file is replaced.
$updaterMaintenance=Invoke-BapWholeBridge $installBundle 'BeginWholeInstall' @([string]$installBundle.WholeObservation,[string]$preparedReceipt)
$transaction = Start-InstallTransaction $GameRoot $targets $installBytes
$installStage = 'Loader / mod / launcher copy'
foreach ($file in $installFiles) {
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $file.Target)
    $copyHash = (Get-FileHash -LiteralPath $file.Source).Hash
    if (!$file.SharedLoader) { $rollbackExpected[$file.Target] = $copyHash }
    if ($file.SharedLoader) {
        Assert-InstallTarget $GameRoot $file.Target
        # No-replace copy closes the absence-to-copy race without adopting unrelated bytes.
        Copy-BapSharedLoaderFile $GameRoot $file
        $rollbackExpected[$file.Target] = $copyHash
    } else { Copy-Item -LiteralPath $file.Source -Destination $file.Target -Force }
    if ((Get-FileHash -LiteralPath $file.Source).Hash -ne (Get-FileHash -LiteralPath $file.Target).Hash) { throw "Copied file verification failed: $($file.Target)" }
}

# Unity 2022.3.62의 IL2CPP 타입 훅 오탐을 피하는 BepInEx 공식 우회 설정.
Write-BapInstallerStage $installBundle 'Apply' 'Completed'
$diagnosticPhase='Configuration'
Write-BapInstallerStage $installBundle $diagnosticPhase
$installStage = 'Loader configuration'
$bepinexConfigDir = Join-Path $GameRoot 'BepInEx\config'
$bepinexConfig = Join-Path $bepinexConfigDir 'BepInEx.cfg'
New-Item -ItemType Directory -Force -Path $bepinexConfigDir | Out-Null
if (!(Test-Path -LiteralPath $bepinexConfig)) {
    $loggingText = '[Logging]' + [char]13 + [char]10 + 'UnityLogListening = false' + [char]13 + [char]10
    $rollbackExpected[$bepinexConfig] = Get-BapInstallHash ([Text.UTF8Encoding]::new($false).GetBytes($loggingText))
}
if (Test-Path -LiteralPath $bepinexConfig) {
    $content = Get-Content -LiteralPath $bepinexConfig -Raw
    $updated = [regex]::Replace($content, '(?m)^([ \t]*UnityLogListening[ \t]*=[ \t]*)true([ \t]*)(?=\r?$)', '${1}false${2}', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($updated -cne $content) {
        $rollbackExpected[$bepinexConfig] = Get-BapInstallHash ([Text.UTF8Encoding]::new($false).GetBytes($updated))
        [IO.File]::WriteAllText($bepinexConfig, $updated, [Text.UTF8Encoding]::new($false))
    }
} else {
    [IO.File]::WriteAllText($bepinexConfig, "[Logging]`r`nUnityLogListening = false`r`n", [Text.UTF8Encoding]::new($false))
}

$doorstopPath = Join-Path $GameRoot 'doorstop_config.ini'
$doorstopText = [IO.File]::ReadAllText($doorstopPath)
$doorstopUpdated = [regex]::Replace($doorstopText, '(?m)^enabled\s*=\s*(true|false)\s*$', 'enabled=false', 1)
$rollbackExpected[$doorstopPath] = Get-BapInstallHash ([Text.UTF8Encoding]::new($false).GetBytes($doorstopUpdated))
& (Join-Path $PSScriptRoot 'set-loader-mode.ps1') -Mode Vanilla -GameRoot $GameRoot 6>$null
foreach ($p in $installBundle.Expected.Keys) {
    if ((Get-FileHash -LiteralPath (Join-Path $GameRoot $p)).Hash -cne $installBundle.Expected[$p]) { throw '복사 후 설치 파일이 검증된 묶음과 다릅니다. / Installed payload differs from the verified bundle.' }
}
$receiptText = $preparedReceipt
$rollbackExpected[$receiptPath] = Get-BapInstallHash ([Text.UTF8Encoding]::new($false).GetBytes($receiptText))
[IO.File]::WriteAllText($receiptPath, $receiptText, [Text.UTF8Encoding]::new($false))
Write-BapInstallerStage $installBundle 'Configuration' 'Completed'
$diagnosticPhase='Apply'
Write-BapInstallerStage $installBundle $diagnosticPhase
$installStage = 'Steam shortcut registration'
if ($needsShortcut) {
    & (Join-Path $PSScriptRoot 'steam-shortcut.ps1') -Mode Register -GameRoot $GameRoot -SteamRoot $steamRoot -AccountId $accountId 6>$null
    $registered = $true
}
Complete-InstallTransaction $GameRoot -AllowedTargets $targets -VerifiedBundle $installBundle
Write-BapInstallerStage $installBundle 'Apply' 'Completed'
$transaction = $null
# HelperDelivery uses the same native update lock. Finish the existing file/shortcut transaction,
# release its maintenance lease, then run the separately recoverable signed helper transaction
# using only the pinned incoming inspector and its verified descriptor/signature/image bytes.
$updaterMaintenance.Dispose()
$updaterMaintenance = $null
$diagnosticPhase='HelperInstall'
Write-BapInstallerStage $installBundle $diagnosticPhase
$installStage = 'Automatic update helper installation / ownership verification'
$receiptHash=Get-BapInstallHash ([Text.UTF8Encoding]::new($false).GetBytes($preparedReceipt))
$null=Invoke-BapWholeBridge $installBundle 'InstallWholeHelper' @([string]$installBundle.WholeObservation,[string]$receiptHash)
$owned=(Get-BapInstallVerifier $installBundle).GetType('SteamLauncher').GetMethod('CheckOwnedHelper',[Reflection.BindingFlags]'Public,Static').Invoke($null,@([string]$GameRoot))
if ($owned -isnot [string] -or !$owned.StartsWith($installBundle.SourceHashes['helper-bootstrap/BetterAstralParty-UpdateHelper.exe']+':',[StringComparison]::Ordinal)) { throw 'Whole helper commit ownership verification failed; retain records.' }
Write-BapInstallerStage $installBundle 'HelperInstall' 'Completed'
if (!$needsShortcut) { Write-Host '기존 설치본 업데이트 완료. Steam은 변경하지 않았습니다. / Updated existing installation; Steam untouched.' }
else { Write-Host '적용 완료! Steam 라이브러리에 모드가 등록되어 있습니다.' }
Write-Host "Steam에서 'Astral Party - Mod'를 실행하세요."
} catch {
    $originalFailure = $_
    if ($transaction -and !$registered) {
        try { Write-BapInstallerStage $installBundle 'Rollback'; Restore-InstallTransaction $GameRoot -ExpectedCurrent $rollbackExpected -AllowedTargets $targets -VerifiedBundle $installBundle; Write-BapInstallerStage $installBundle 'Rollback' 'Completed'; $transaction = $null }
        catch {
            Write-BapInstallerError $installBundle 'Rollback' $_.Exception
            $null = Write-InstallFailure $_ 'Rollback failed; retain backup and retry recovery' $root $GameRoot
            Write-Warning "복구가 끝나지 않았습니다. 게임을 실행하지 말고 현재 묶음·journal·백업을 보관하세요. / Recovery incomplete; retain the current package, journal and backups. Journal: $transaction. $($_.Exception.Message)"
        }
    }
    throw $originalFailure
}
} catch {
    $failure = $_
    if ($installBundle) { Write-BapInstallerError $installBundle $diagnosticPhase $failure.Exception }
    $null = Write-InstallFailure $failure $installStage $root $GameRoot
    throw $failure
} finally {
    if ($diagnosticOwner) { $diagnosticOwner.Dispose() }
    if ($installLock) { $installLock.Dispose() }
    if ($updaterMaintenance) { $updaterMaintenance.Dispose() }
    if ($installBundle) { foreach ($lease in $installBundle.Leases) { $lease.Dispose() } }
    Resume-BapSteam $steamSession
}
