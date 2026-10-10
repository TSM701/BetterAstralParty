function Write-InstallFailure($Failure, [string]$Stage, [string]$PackageRoot, [string]$GameRoot, [string]$Operation = 'Installation') {
    # No transcript, registry dump, Steam account ID or environment dump.
    $text = @(
        "BetterAstralParty $Operation failure"
        "Time: $([DateTimeOffset]::Now.ToString('o'))"
        "Run: $env:BAP_INSTALL_RUN"
        "Version: $env:BAP_INSTALL_VERSION"
        'Code: BAP-INSTALL-FAILED'
        "Stage: $Stage"
        "PowerShell: $($PSVersionTable.PSVersion)"
        "Package: $PackageRoot"
        "Game: $GameRoot"
        "ErrorType: $($Failure.Exception.GetType().FullName)"
        "HResult: $('0x{0:X8}' -f $Failure.Exception.HResult)"
        "ErrorId: $($Failure.FullyQualifiedErrorId)"
        "Message: $($Failure.Exception.Message)"
        "Location: $($Failure.InvocationInfo.ScriptName):$($Failure.InvocationInfo.ScriptLineNumber)"
        "Stack: $($Failure.ScriptStackTrace)"
        "Exception: $($Failure.Exception.ToString())"
    ) -join [Environment]::NewLine
    $text += [Environment]::NewLine
    if ($env:USERPROFILE) {
        $text = [regex]::Replace($text, [regex]::Escape($env:USERPROFILE), '%USERPROFILE%', 'IgnoreCase')
        $text = [regex]::Replace($text, [regex]::Escape($env:USERPROFILE.Replace('\', '/')), '%USERPROFILE%', 'IgnoreCase')
    }
    $name = 'BetterAstralParty-InstallError-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.log'
    foreach ($directory in @((Join-Path $env:LOCALAPPDATA 'BetterAstralParty/Logs'), (Join-Path ([IO.Path]::GetTempPath()) 'BetterAstralParty'))) {
        try {
            New-Item -ItemType Directory -Force -Path $directory -ErrorAction Stop | Out-Null
            $path = Join-Path $directory $name
            [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($true))
            Write-Host "$Operation failed. Debug log: $path"
            return $path
        } catch { } # Logging failure must never replace the original installation error.
    }
    Write-Host 'Could not save the installation log. Please capture the console error.'
}
