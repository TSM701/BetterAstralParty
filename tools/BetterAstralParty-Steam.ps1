$ErrorActionPreference = 'Stop'
$updateLaunchLease = $null
try {
    # Steam can inherit PowerShell 7's module path; the launcher uses Windows PowerShell.
    $env:PSModulePath = Join-Path $PSHOME 'Modules'
    if (!$env:SteamGameId) { throw 'Open Astral Party - Mod from your Steam library.' }
    if (Get-Process AstralParty_INT,AstralParty_CN -ErrorAction SilentlyContinue) { throw 'Close Astral Party before launching the mod.' }
    # Shared read-only recovery gate and common updater lock survive until the game exits.
    # Loading a byte snapshot keeps the launcher file available to later maintenance.
    $launchAssembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'BetterAstralParty-Launcher.exe')))
    $guardMethod = $launchAssembly.GetType('SteamLauncher').GetMethod('GuardModLaunch', [Reflection.BindingFlags]'Public,Static')
    if (!$guardMethod) { throw '업데이트 안전 검사 런처가 필요합니다. / Upgrade the launcher before mod launch.' }
    $updateLaunchLease = $guardMethod.Invoke($null, @($PSScriptRoot))
    # The checker runs in a child process because it uses exit codes.
    $checkStart = [Diagnostics.ProcessStartInfo]::new()
    $checkStart.FileName = "$PSHOME/powershell.exe"
    $checkStart.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + (Join-Path $PSScriptRoot 'check-compatibility.ps1') + '" -GameRoot "' + $PSScriptRoot + '"'
    $checkStart.UseShellExecute = $false
    $checkStart.CreateNoWindow = $true
    $checkStart.RedirectStandardOutput = $true
    $checkStart.RedirectStandardError = $true
    $check = [Diagnostics.Process]::Start($checkStart)
    try {
        $stdout = $check.StandardOutput.ReadToEndAsync()
        $stderr = $check.StandardError.ReadToEndAsync()
        $check.WaitForExit()
        if ($check.ExitCode -ne 0) { throw "Compatibility check failed (exit $($check.ExitCode)). $($stderr.Result) $($stdout.Result)" }
    } finally { $check.Dispose() }
    # Match Steamworks AND the overlay before the game creates its rendering device.
    # Otherwise Steam injects an overlay for the non-Steam shortcut, not the payment's App ID.
    # These values affect only this launcher process and its game child, never Steam settings.
    $env:SteamAppId = '2622000'
    $env:SteamGameId = $env:SteamAppId
    $env:SteamOverlayGameId = $env:SteamAppId
    $env:BAP_COMPATIBILITY_CHECKED = '1'
    $game = Start-Process -FilePath (Join-Path $PSScriptRoot 'AstralParty_INT.exe') -WorkingDirectory $PSScriptRoot -ArgumentList '--doorstop-enabled true --bap-compatibility-checked' -PassThru
    $game.WaitForExit()
    if ($game.ExitCode -ne 0) { throw "Game exited with code $($game.ExitCode). Include BepInEx/LogOutput.log and Player.log when reporting a crash." }
    $game.Dispose()
} catch {
    # The windowless parent captures stderr, logs it and displays one error dialog.
    [Console]::Error.WriteLine(($_ | Out-String))
    exit 1
} finally { if ($updateLaunchLease) { $updateLaunchLease.Dispose() } }
