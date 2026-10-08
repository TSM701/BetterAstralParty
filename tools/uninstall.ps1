[CmdletBinding(SupportsShouldProcess=$true, ConfirmImpact='High')]
param([string]$GameRoot, [ValidateSet('Recorded','Enabled','Disabled')][string]$RestoreLoader = 'Recorded', [uint32]$AccountId = 0)
$ErrorActionPreference = 'Stop'
$env:PSModulePath = Join-Path $PSHOME 'Modules'
$root = Split-Path -Parent $PSScriptRoot
if (!(Test-Path -LiteralPath (Join-Path $root 'package-manifest.json') -PathType Leaf)) {
    throw 'BAP-SOURCE-DEVELOPMENT: 개발 소스에서 제거할 수 없습니다. 검증된 전체 ZIP을 사용하세요. / Use a verified whole ZIP; see docs/BUILD.md.'
}
. (Join-Path $PSScriptRoot 'find-game.ps1')
. (Join-Path $PSScriptRoot 'install-backup.ps1')
. (Join-Path $PSScriptRoot 'install-log.ps1')
. (Join-Path $PSScriptRoot 'install-helper.ps1')
. (Join-Path $PSScriptRoot 'steam-session.ps1')
$steamSession = @{ Restart=$false }
$updaterMaintenance = $null
$lock = $null; $transaction = $null; $receipt = $null; $removedShortcut = $null
$rollbackExpected = @{}
$externalBundle = $null; $diagnosticOwner=$null; $diagnosticPhase='Uninstall'
function Get-UninstallHash([string]$Path) {
    # InputStream avoids PS 5.1's path pipeline inheriting -WhatIf and returning no hash.
    $stream = [IO.File]::OpenRead($Path)
    try { return (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash }
    finally { $stream.Dispose() }
}
try {
    if (!$GameRoot) {
        $steam = (Get-ItemProperty 'HKCU:/Software/Valve/Steam' -ErrorAction SilentlyContinue).SteamPath
        $candidates = @(if ($steam) { Find-IntGameDirectories $steam $root } else { Resolve-IntGameDirectory (Split-Path -Parent $root) })
        if ($candidates.Count -eq 1) { $GameRoot = $candidates[0] }
        else {
            $candidates | ForEach-Object { Write-Host $_ }
            $GameRoot = Read-Host 'INT 게임 폴더 경로 / INT game folder (blank cancels)'
        }
    }
    $GameRoot = Resolve-IntGameDirectory $GameRoot
    if (!$GameRoot) { throw 'INT 게임 경로를 확인하세요. / INT game folder not found; nothing removed.' }
    # Finish interactive preparation before reading file hashes or beginning a transaction.
    $removeShortcut = & (Join-Path $PSScriptRoot 'steam-shortcut.ps1') -Mode PlanRemove -GameRoot $GameRoot -AccountId $AccountId
    if ($removeShortcut -and !$AccountId) {
        $AccountId = & (Join-Path $PSScriptRoot 'steam-shortcut.ps1') -Mode ResolveAccount -GameRoot $GameRoot
    }
    if (!$WhatIfPreference) { Stop-BapGame $GameRoot }
    $installedLauncher = Join-Path $GameRoot 'BetterAstralParty-Launcher.exe'
    . (Join-Path $PSScriptRoot 'install-policy.ps1')
    $externalBundle = Open-BapInstallBundle -PackageRoot $root -GameRoot $GameRoot
    $receiptPath = Join-Path $GameRoot 'BetterAstralParty.install.json'
    Assert-InstallTarget $GameRoot $receiptPath
    $hashes = @{}
    if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
        try {
            $receiptBytes=Read-BapInstallFile $receiptPath
            $receipt = Read-BapInstallJson $receiptBytes
            $names = @($receipt.PSObject.Properties.Name)
            if (($receipt.schema -isnot [int] -and $receipt.schema -isnot [long]) -or $receipt.schema -ne 1 -or
                $receipt.owner -isnot [string] -or $receipt.owner -cne 'kr.betterastralparty.mod' -or
                ($names.Count -ne 4 -and $names.Count -ne 5) -or ($names.Count -eq 5 -and $names -cnotcontains 'channelState') -or @('schema','owner','loaderBefore','files' | Where-Object { $names -cnotcontains $_ }).Count -or
                ($null -ne $receipt.loaderBefore -and $receipt.loaderBefore -isnot [bool]) -or
                $receipt.files -isnot [Array] -or $receipt.files.Count -gt 8) { throw 'Invalid installation receipt shape' }
            if($names.Count -eq 5){
                $validator=(Get-BapInstallVerifier $externalBundle).GetType('SteamLauncher').GetMethod('ValidateInstalledReceipt',[Reflection.BindingFlags]'Public,Static')
                if(!$validator){throw 'Channel receipt validator required'}
                $validatorArguments=[object[]]::new(1);$validatorArguments[0]=[byte[]]$receiptBytes;$null=$validator.Invoke($null,$validatorArguments)
            }
            foreach ($entry in $receipt.files) {
                if (@($entry.PSObject.Properties).Count -ne 2 -or $entry.path -isnot [string] -or
                    !($externalBundle.Expected.Keys -ccontains $entry.path) -or $hashes.ContainsKey($entry.path) -or
                    $entry.sha256 -isnot [string] -or $entry.sha256 -cnotmatch '\A[A-F0-9]{64}\z') { throw 'Invalid installation receipt entry' }
                $hashes[$entry.path] = $entry.sha256
            }
        } catch { $receipt = $null; $hashes = @{}; Write-Warning '설치 기록을 확인할 수 없어 원본을 보존합니다. / Invalid receipt retained; checking individual files.' }
    }
    $declaredLauncherHash = $hashes['BetterAstralParty-Launcher.exe']
    if ($declaredLauncherHash -and (!(Test-Path -LiteralPath $installedLauncher -PathType Leaf) -or (Get-UninstallHash $installedLauncher) -cne $declaredLauncherHash)) {
        throw '런처가 없거나 설치 기록과 다릅니다. 검토된 복구가 필요합니다. / Missing or modified installed launcher; reviewed recovery required before removal.'
    }
    if (!$declaredLauncherHash -and (Test-Path -LiteralPath $installedLauncher -PathType Leaf) -and (Get-UninstallHash $installedLauncher) -cne $externalBundle.Expected['BetterAstralParty-Launcher.exe']) {
        throw '출처를 확인할 수 없는 런처를 보존하고 중단합니다. / Unrecognized installed launcher retained; recovery review required.'
    }
    $updaterMaintenance = Enter-BapUpdateMaintenance $GameRoot -ReadOnly:$WhatIfPreference -VerifiedBundle $externalBundle
    $null = Assert-BapInstallRoot $externalBundle
    # Fixed allowlist; never recursively remove a plugin directory or trust receipt paths.
    $files = @(
        @{ Path='BepInEx/plugins/BetterAstralParty/BetterAstralParty.dll'; Source='artifacts/BetterAstralParty.dll'; Assembly='BetterAstralParty' }
        @{ Path='BetterAstralParty-Launcher.exe'; Source='artifacts/BetterAstralParty-Launcher.exe'; Assembly='BetterAstralParty-Launcher' }
        @{ Path='BetterAstralParty.compatibility.json'; Source='artifacts/BetterAstralParty.compatibility.json' }
        foreach ($name in @('BetterAstralParty-Mod.cmd','AstralParty-Vanilla.cmd','check-compatibility.ps1','BetterAstralParty-Steam.ps1','steam-shortcut.ps1')) {
            @{ Path=$name; Source="tools/$name" }
        }
        foreach ($name in @('BepInEx/config/kr.betterastralparty.mod.cfg','BepInEx/BetterAstralParty-Diagnostics/current.log',
            'BepInEx/BetterAstralParty-Diagnostics/previous.log','BepInEx/BetterAstralParty-Diagnostics/combat-mismatch.log',
            'BepInEx/BetterAstralParty-Diagnostics/combat-mismatch-previous.log','BetterAstralParty-Compatibility.log','BetterAstralParty-LaunchError.log')) {
            @{ Path=$name; Exclusive=$true }
        }
    )
    $targets = @(); $plannedHashes = @{}; $keptReceiptFile = $false
    $helperPath = Join-Path $GameRoot 'BetterAstralParty-UpdateHelper.exe'
    $helperReceipt = Join-Path $GameRoot 'BetterAstralParty.helper.receipt'
    if ((Test-Path -LiteralPath $helperPath) -or (Test-Path -LiteralPath $helperReceipt)) {
        Assert-InstallTarget $GameRoot $helperPath
        Assert-InstallTarget $GameRoot $helperReceipt
        $helperHash = $null
        $gateAssembly = Get-BapInstallVerifier $externalBundle
        $helperMethod = $gateAssembly.GetType('SteamLauncher').GetMethod('CheckOwnedHelper', [Reflection.BindingFlags]'Public,Static')
        if ($helperMethod) { $helperHash = $helperMethod.Invoke($null, @([string]$GameRoot)) }
        if ($helperHash -is [string] -and $helperHash -cmatch '\A([A-F0-9]{64}):([A-F0-9]{64})\z' -and (Test-Path -LiteralPath $helperPath -PathType Leaf) -and (Test-Path -LiteralPath $helperReceipt -PathType Leaf)) {
            $verifiedImage = $Matches[1]; $verifiedReceipt = $Matches[2]
            if ((Get-UninstallHash $helperPath) -ceq $verifiedImage -and (Get-UninstallHash $helperReceipt) -ceq $verifiedReceipt) {
                $targets += @($helperPath,$helperReceipt)
                $plannedHashes[$helperPath] = $verifiedImage; $plannedHashes[$helperReceipt] = $verifiedReceipt
            } else { throw '검증 후 helper 파일이 바뀌었습니다. 제거하지 않고 중단합니다. / Helper changed after verification.' }
        } else { Write-Warning 'helper의 소유를 확인할 수 없어 파일·기록을 보존합니다. / Unverified update helper and receipt retained.' }
    }
    foreach ($file in $files) {
        $path = Join-Path $GameRoot $file.Path
        Assert-InstallTarget $GameRoot $path
        if (!(Test-Path -LiteralPath $path)) { continue }
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Expected a file: $path" }
        $hash = Get-UninstallHash $path
        $owned = $file.Exclusive -or ($hashes.ContainsKey($file.Path) -and $hashes[$file.Path] -eq $hash)
        # A current receipt takes precedence over an older package and assembly name.
        if (!$owned -and !$receipt -and $file.Source -and (Test-Path -LiteralPath (Join-Path $root $file.Source) -PathType Leaf)) {
            $owned = (Get-UninstallHash (Join-Path $root $file.Source)) -eq $hash
        }
        if ($owned) { $targets += $path; $plannedHashes[$path] = $hash }
        else {
            if ($receipt) { $keptReceiptFile = $true }
            Write-Warning "보존: 소유 확인 불가 또는 변경된 파일 / Kept unknown or modified file: $path"
        }
    }
    if ($receipt -and !$keptReceiptFile) { $targets += $receiptPath; $plannedHashes[$receiptPath] = Get-UninstallHash $receiptPath }
    elseif ($keptReceiptFile) { Write-Warning '소유권이 불확실하거나 수정된 파일과 설치 기록을 보존합니다. / Unknown or modified files and their receipt are retained.' }
    if (!$targets.Count -and !$removeShortcut) { Write-Host '제거할 확인된 파일·바로가기가 없습니다. / No verified mod files or shortcut to remove.'; return }
    $loaderPath = Join-Path $GameRoot 'doorstop_config.ini'
    Assert-InstallTarget $GameRoot $loaderPath
    $loaderText = $null; $loaderUpdated = $null
    if ($targets.Count -and (Test-Path -LiteralPath $loaderPath -PathType Leaf)) {
        $loaderText = [IO.File]::ReadAllText($loaderPath)
        $enabled = [regex]::Matches($loaderText, '(?m)^enabled\s*=\s*(true|false)\s*$')
        if ($enabled.Count -gt 1) { throw 'Ambiguous loader configuration; nothing removed.' }
        if ($enabled.Count -eq 1) {
            $restore = if ($RestoreLoader -eq 'Recorded') {
                # Installer only writes false. A later true is a user's/another mod's change.
                if ($enabled[0].Groups[1].Value -eq 'true') { $true } else { $receipt.loaderBefore }
            } else { $RestoreLoader -eq 'Enabled' }
            if ($restore -isnot [bool]) {
                Write-Host '설치 전 로더 상태 기록이 없습니다. / Original loader state was not recorded.'
                if ($WhatIfPreference) {
                    Write-Host '실제 제거 시 로더 활성화/비활성화를 선택해야 합니다. / Actual removal requires an explicit loader choice.'
                } else {
                    Write-Host '1: 다른 BepInEx 모드 실행 유지 (로더 켜기, 권장) / Keep other BepInEx mods running (enable loader, recommended)'
                    Write-Host '2: 모든 BepInEx 모드 실행 끄기 / Disable all BepInEx mods'
                    try { $choice = Read-Host '1 / 2 선택, Enter는 취소 / Choose 1 / 2, Enter cancels' }
                    catch { throw 'Interactive selection unavailable. Specify -RestoreLoader Enabled or Disabled. Nothing removed.' }
                    if ($choice -notin @('1','2')) {
                        Write-Host '취소했습니다. 아무것도 변경하지 않았습니다. / Cancelled; nothing changed.'
                        return
                    }
                    $restore = $choice -eq '1'
                }
            }
            if ($restore -is [bool]) {
                $loaderUpdated = [regex]::Replace($loaderText, '(?m)^enabled\s*=\s*(true|false)\s*$', ('enabled=' + $restore.ToString().ToLowerInvariant()))
            }
        }
    }
    Write-Host "제거 대상 / Files to remove ($GameRoot):"
    $targets | ForEach-Object { Write-Host "  $_" }
    if ($loaderUpdated -ne $null) { Write-Host "Restore loader enabled=$restore (other settings preserved)" }
    if ($removeShortcut) { Write-Host 'Remove the matching Astral Party - Mod shortcut only; keep all other Steam entries.' }
    Write-Host '전용 설정·로그도 백업 후 제거합니다. 공용 로더·다른 모드는 유지합니다. / Includes mod settings/logs; shared loader and other mods stay.'
    if (!$PSCmdlet.ShouldProcess($GameRoot, 'BetterAstralParty 파일 백업 및 제거 / Back up and remove listed BetterAstralParty files')) { return }
    $diagnosticOwner=Start-BapInstallerDiagnostics $externalBundle
    Write-BapInstallerStage $externalBundle 'Uninstall' 'Begin'
    $journal = Get-InstallJournalPath $GameRoot
    $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $journal))
    $lock = [IO.File]::Open(($journal + '.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    if (Test-Path -LiteralPath $journal) { throw "미완료 복구를 먼저 해결하세요. / Pending recovery: $journal" }
    if ($removeShortcut) {
        $steamRoot = (Get-ItemProperty 'HKCU:/Software/Valve/Steam').SteamPath
        Stop-BapSteam $steamRoot $steamSession
        # Re-read after Steam flushes its file, keeping the selected account pinned.
        $removeShortcut = & (Join-Path $PSScriptRoot 'steam-shortcut.ps1') -Mode PlanRemove -GameRoot $GameRoot -AccountId $AccountId
    }
    if (@(Get-BapGameTargets $GameRoot).Count) { throw 'Game/launcher started; removal cancelled.' }
    if ($removeShortcut -and (Get-Process steam -ErrorAction SilentlyContinue)) { throw 'Steam restarted; removal cancelled. Nothing removed.' }
    $backupTargets = @($targets)
    if ($loaderUpdated -ne $null) { $backupTargets += $loaderPath; $plannedHashes[$loaderPath] = Get-UninstallHash $loaderPath }
    $transaction = Start-InstallTransaction $GameRoot $backupTargets 0
    $state = Get-Content -LiteralPath $transaction -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host "복구용 백업 / Recovery backup: $($state.BackupDir)"
    foreach ($entry in $state.Entries) {
        if ($entry.Hash -ne $plannedHashes[$entry.Target]) { throw "File changed since confirmation preview: $($entry.Target)" }
    }
    foreach ($entry in $state.Entries) {
        Assert-InstallTarget $GameRoot $entry.Target
        if ((Get-UninstallHash $entry.Target) -ne $entry.Hash) { throw "File changed during uninstall: $($entry.Target)" }
        if ($entry.Target -eq $loaderPath) {
            if ([IO.File]::ReadAllText($loaderPath) -ne $loaderText) { throw 'Loader configuration changed during uninstall.' }
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $rollbackExpected[$loaderPath] = [BitConverter]::ToString($sha.ComputeHash([Text.UTF8Encoding]::new($false).GetBytes($loaderUpdated))).Replace('-', '') }
            finally { $sha.Dispose() }
            [IO.File]::WriteAllText($loaderPath, $loaderUpdated, [Text.UTF8Encoding]::new($false))
        } else { $rollbackExpected[$entry.Target] = $null; [IO.File]::Delete($entry.Target) }
    }
    if ($removeShortcut) {
        $removedShortcut = & (Join-Path $PSScriptRoot 'steam-shortcut.ps1') -Mode Remove -GameRoot $GameRoot -AccountId $AccountId
    }
    Complete-InstallTransaction $GameRoot -AllowedTargets $backupTargets -VerifiedBundle $externalBundle
    $transaction = $null
    Write-BapInstallerStage $externalBundle 'Uninstall' 'Completed'
    foreach ($relative in @('BepInEx/plugins/BetterAstralParty','BepInEx/BetterAstralParty-Diagnostics')) {
        $dir = Join-Path $GameRoot $relative
        Assert-InstallTarget $GameRoot $dir
        if ((Test-Path -LiteralPath $dir -PathType Container) -and @(Get-ChildItem -LiteralPath $dir -Force).Count -eq 0) {
            try { [IO.Directory]::Delete($dir, $false) } catch { Write-Warning "Empty folder retained: $dir" }
        }
    }
    if ($keptReceiptFile -or !$receipt) {
        Write-Host '확인된 파일 제거를 마쳤습니다. 미확인·변경 파일과 기록은 남아 있습니다. / Verified files removed; unknown or modified files and evidence remain.'
    } else {
        Write-Host '제거 완료. 로더 실행 설정과 모드 바로가기를 정리했습니다. 다른 모드 파일은 유지합니다. / Removed mod files and launch split; other mods preserved.'
    }
} catch {
    $failure = $_
    Write-BapInstallerError $externalBundle $diagnosticPhase $_.Exception
    if ($removedShortcut) {
        try {
        if (!(Get-Process steam -ErrorAction SilentlyContinue) -and
            (Get-UninstallHash $removedShortcut.Path) -eq $removedShortcut.Hash) {
            [IO.File]::Copy($removedShortcut.Backup, $removedShortcut.Path, $true)
            foreach ($art in $removedShortcut.Artwork) {
                if (!(Test-Path -LiteralPath $art.Path) -and (Get-UninstallHash $art.Backup) -eq $art.Hash) {
                    [IO.File]::Copy($art.Backup, $art.Path, $false)
                } else { Write-Warning "Steam artwork recovery needs review; backup: $($art.Backup)" }
            }
        } else { Write-Warning "Steam shortcut recovery needs manual review; backup: $($removedShortcut.Backup)" }
        } catch { Write-Warning "Steam shortcut recovery failed; backup: $($removedShortcut.Backup)" }
    }
    if ($transaction) {
        try { Write-BapInstallerStage $externalBundle 'Rollback' 'Begin';Restore-InstallTransaction $GameRoot -ExpectedCurrent $rollbackExpected -AllowedTargets $backupTargets -VerifiedBundle $externalBundle;Write-BapInstallerStage $externalBundle 'Rollback' 'Completed' }
        catch { Write-Warning "복구 미완료: 백업을 보관하세요. / Recovery incomplete; retain $transaction. $($_.Exception.Message)" }
    }
    $null = Write-InstallFailure $failure 'Uninstall' $root $GameRoot -Operation 'Uninstall'
    throw $failure
} finally {
    if($diagnosticOwner){$diagnosticOwner.Dispose()}
    if ($lock) { $lock.Dispose() }
    if ($updaterMaintenance) { $updaterMaintenance.Dispose() }
    if ($externalBundle) { foreach ($lease in $externalBundle.Leases) { $lease.Dispose() } }
    Resume-BapSteam $steamSession
}
