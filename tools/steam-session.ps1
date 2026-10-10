# Shared lifecycle for shortcut writes only. Force requires fresh interactive consent.
function Get-BapSteamTargets([string]$SteamRoot) {
    $steamPath = [IO.Path]::GetFullPath($SteamRoot).TrimEnd('\') + '\'
    $libraries = @($SteamRoot)
    $libraryFile = Join-Path $SteamRoot 'steamapps/libraryfolders.vdf'
    if (Test-Path -LiteralPath $libraryFile -PathType Leaf) {
        $libraries += [regex]::Matches([IO.File]::ReadAllText($libraryFile), '"path"\s+"([^"]+)"') |
            ForEach-Object { $_.Groups[1].Value.Replace('\\', '\') }
    }
    $gameDirs = @{}
    foreach ($library in ($libraries | Sort-Object -Unique)) {
        if (![IO.Path]::IsPathRooted($library)) { throw 'Invalid Steam library path; nothing stopped.' }
        if (!(Test-Path -LiteralPath $library -PathType Container)) { continue }
        foreach ($manifest in Get-ChildItem -LiteralPath (Join-Path $library 'steamapps') -Filter 'appmanifest_*.acf' -ErrorAction SilentlyContinue) {
            $dir = [regex]::Matches([IO.File]::ReadAllText($manifest.FullName), '"installdir"\s+"([^"]+)"')
            if ($dir.Count -ne 1 -or $dir[0].Groups[1].Value -match '[\\/:]|^\.{1,2}$') { throw 'Invalid Steam app directory; nothing stopped.' }
            $path = [IO.Path]::GetFullPath((Join-Path $library ('steamapps/common/' + $dir[0].Groups[1].Value))).TrimEnd('\') + '\'
            $gameDirs[$path] = $true
        }
    }
    $session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    foreach ($process in Get-Process -ErrorAction Stop) {
        if ($process.Id -eq $PID) { continue }
        try {
            if ($process.HasExited) { continue }
            if ($process.SessionId -ne $session) { continue }
            $path = $process.Path
            if (!$path) { continue }
            $path = [IO.Path]::GetFullPath($path)
            $client = $process.ProcessName -in @('steam','steamwebhelper','GameOverlayUI','steamerrorreporter','steamerrorreporter64') -and
                $path.StartsWith($steamPath, [StringComparison]::OrdinalIgnoreCase)
            $game = @($gameDirs.Keys | Where-Object { $path.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
            if ($client -or $game) {
                [pscustomobject]@{Id=$process.Id; Path=$path; Started=$process.StartTime; Name=$process.ProcessName; Client=$client}
            }
        } catch {
            if ($process.HasExited) { continue }
            throw "Cannot verify process $($process.Id); nothing stopped. $($_.Exception.Message)"
        }
    }
}

function Stop-BapVerifiedProcess($Target) {
    $current = Get-Process -Id $Target.Id -ErrorAction SilentlyContinue
    if (!$current) { return }
    try {
        if ($current.HasExited) { return }
        $path = $current.Path
        $started = $current.StartTime
        if (!$path -or !$started -or $path -ne $Target.Path -or $started -ne $Target.Started) {
            throw "Process identity changed ($($Target.Id)); not terminated. PathAvailable=$([bool]$path); StartAvailable=$([bool]$started); PathMatch=$($path -eq $Target.Path); StartMatch=$($started -eq $Target.Started)."
        }
        Stop-Process -InputObject $current -Force -ErrorAction Stop
        if (!$current.WaitForExit(5000)) { throw "Process did not exit ($($Target.Id))." }
    } catch {
        # Steam's children may exit after enumeration, during identity reads or termination.
        # Suppress only a confirmed exit on this process object, never an unknown/live PID.
        if ($current.HasExited) { return }
        throw
    }
}

function Confirm-BapForce($Targets, [switch]$AllowRecheck) {
    Write-Warning '목록의 게임/앱을 강제 종료합니다. 저장하지 않은 진행 상황과 동기화 중인 데이터가 손실될 수 있습니다. 게임은 다시 실행하지 않습니다. / Force-close the listed processes. Unsaved progress and syncing data may be lost. Games will not restart.'
    $Targets | ForEach-Object { Write-Host "  $($_.Name) (PID $($_.Id))" }
    $prompt = if ($AllowRecheck) { '직접 종료 후 r로 재확인, y로 목록 강제 종료, Enter로 취소 / r: recheck, y: force-close listed processes, Enter: cancel' } else { '전부 강제 종료 후 진행? / Force-close all listed processes and continue? [y/N]' }
    try { $consent = Read-Host $prompt }
    catch { throw '강제 종료는 대화형 동의가 필요합니다. / Force-close requires interactive consent. Nothing stopped.' }
    if ($AllowRecheck -and $consent -eq 'r') { return $false }
    if ($consent -ne 'y') { throw '취소했습니다. / Cancelled; nothing stopped.' }
    if ($AllowRecheck) { return $true }
}

function Get-BapGameTargets([string]$GameRoot) {
    $directory = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
    foreach ($process in Get-Process -Name AstralParty_INT,AstralParty_CN,BetterAstralParty-Launcher -ErrorAction SilentlyContinue) {
        try {
        if ($process.HasExited) { continue }
        # An identically named executable outside the selected installation is not ours.
        if (!$process.Path) { throw '게임 프로세스 경로 확인 불가 / Cannot verify game process path; nothing stopped.' }
        if ((Split-Path -Parent $process.Path) -ne $directory -or
            $process.SessionId -ne [Diagnostics.Process]::GetCurrentProcess().SessionId) { continue }
        if ((Split-Path -Leaf $process.Path) -notin @('AstralParty_INT.exe','AstralParty_CN.exe','BetterAstralParty-Launcher.exe')) { continue }
        [pscustomobject]@{Id=$process.Id; Path=$process.Path; Started=$process.StartTime; Name=$process.ProcessName}
        } catch {
            if ($process.HasExited) { continue }
            throw
        }
    }
}

function Wait-BapUpdateHelper([string]$GameRoot) {
    $directory = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
    while ($true) {
        $running = @()
        foreach ($process in Get-Process -Name BetterAstralParty-UpdateHelper -ErrorAction SilentlyContinue) {
            if ($process.HasExited) { continue }
            if (!$process.Path) { throw '업데이트 helper 경로를 확인할 수 없습니다. / Cannot verify update helper; nothing stopped.' }
            if ((Split-Path -Parent $process.Path) -eq $directory -and
                $process.SessionId -eq [Diagnostics.Process]::GetCurrentProcess().SessionId) { $running += $process }
        }
        if (!$running.Count) { return }
        Write-Host '업데이트 helper가 실행 중입니다. 정상 종료를 기다리세요. / Update helper is running; wait for it to finish.'
        try { $choice = Read-Host 'r: 종료 후 재확인, Enter: 취소 / r: recheck, Enter: cancel' }
        catch { throw '업데이트 helper가 종료된 뒤 다시 시도하세요. / Wait for the helper to finish; nothing changed.' }
        if ($choice -cne 'r') { throw '취소했습니다. 기존 설치와 helper를 보존합니다. / Cancelled; installation and helper retained.' }
    }
}
function Stop-BapGame([string]$GameRoot) {
    Wait-BapUpdateHelper $GameRoot
    while ($true) {
        $targets = @(Get-BapGameTargets $GameRoot)
        if (!$targets.Count) { return }
        if (Confirm-BapForce $targets -AllowRecheck) { break }
    }
    $fresh = @(Get-BapGameTargets $GameRoot)
    foreach ($target in $fresh) {
        if (!($targets | Where-Object { $_.Id -eq $target.Id -and $_.Started -eq $target.Started -and $_.Path -eq $target.Path })) {
            throw 'Process list changed; retry to confirm the new list. Nothing stopped.'
        }
    }
    foreach ($target in ($fresh | Sort-Object @{Expression={ if ($_.Name -eq 'BetterAstralParty-Launcher') {0} else {1} }})) { Stop-BapVerifiedProcess $target }
    if (@(Get-BapGameTargets $GameRoot).Count) { throw 'Game processes remain or restarted; operation cancelled.' }
}

function Stop-BapSteam([string]$SteamRoot, [hashtable]$State) {
    if (!(Get-Process steam -ErrorAction SilentlyContinue)) { return }
    $exe = Join-Path $SteamRoot 'steam.exe'
    if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Steam executable missing; no changes made.' }
    $targets = @(Get-BapSteamTargets $SteamRoot)
    if (!($targets | Where-Object { $_.Client -and $_.Name -eq 'steam' })) { throw 'Cannot verify Steam process ownership; nothing stopped.' }
    Confirm-BapForce $targets
    # Do not terminate new games that were absent from the approved list.
    $fresh = @(Get-BapSteamTargets $SteamRoot)
    foreach ($target in $fresh) {
        if (!($targets | Where-Object { $_.Id -eq $target.Id -and $_.Started -eq $target.Started -and $_.Path -eq $target.Path })) {
            throw 'Process list changed; retry to confirm the new list. Nothing stopped.'
        }
    }
    $State.Root = $SteamRoot
    # Stop the client first so it cannot relaunch helpers/games while the approved list is closed.
    foreach ($target in ($fresh | Sort-Object @{Expression={ if ($_.Name -eq 'steam' -and $_.Client) {0} elseif ($_.Client) {1} else {2} }})) {
        Stop-BapVerifiedProcess $target
        if ($target.Client -and $target.Name -eq 'steam') { $State.Restart = $true }
    }
    $State.Restart = $true
    if (@(Get-BapSteamTargets $SteamRoot).Count) { throw 'Steam/game processes remain or restarted; installation/removal cancelled.' }
}

function Resume-BapSteam([hashtable]$State) {
    if (!$State.Restart) { return }
    try {
        if (!(Get-Process steam -ErrorAction SilentlyContinue)) {
            Start-Process -FilePath (Join-Path $State.Root 'steam.exe') -WindowStyle Hidden
        }
    } catch { Write-Warning "Steam을 직접 실행하세요. / Steam restart failed; start Steam manually. $($_.Exception.Message)" }
}
