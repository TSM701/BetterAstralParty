param([string]$GameRoot, [switch]$RestartSteam, [uint32]$AccountId = 0, [switch]$UpdateOnly, [string]$RecoveryPlan)
$ErrorActionPreference = 'Stop'
$env:PSModulePath = Join-Path $PSHOME 'Modules'
$root = Split-Path -Parent $PSScriptRoot
$env:BAP_INSTALL_RUN = [guid]::NewGuid().ToString('N')
$env:BAP_INSTALL_VERSION = 'unknown'
$stage = 'Bootstrap / package validation'
try {
    . (Join-Path $PSScriptRoot 'install-log.ps1')
    $inventory = Join-Path $root 'package-manifest.json'
    if (Test-Path -LiteralPath $inventory) {
        $package = Get-Content -LiteralPath $inventory -Raw | ConvertFrom-Json
        if ($package.version -isnot [string] -or $package.kind -isnot [string]) { throw '설치 manifest의 version/kind는 문자열이어야 합니다. / Manifest version/kind must be scalar strings.' }
        if (@($package.files).Count -lt 10) { throw 'Invalid package manifest.' }
        $env:BAP_INSTALL_VERSION = $package.version
        $seen = @{}
        foreach ($file in $package.files) {
            $name = [string]$file.path
            if (!$name -or [IO.Path]::IsPathRooted($name) -or $name -match '(^|[\\/])\.\.([\\/]|$)|:' -or $seen.ContainsKey($name)) { throw 'Unsafe/duplicate package path.' }
            $seen[$name] = $true
            $path = Join-Path $root $name
            if (!(Test-Path -LiteralPath $path -PathType Leaf) -or
                (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) {
                throw "Missing or damaged package file: $name. Extract the complete ZIP again."
            }
            if ([IO.Path]::GetExtension($path) -eq '.ps1') {
                $tokens = $null; $parseErrors = $null
                $null = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors)
                if ($parseErrors.Count) { throw "Invalid package script: $name. $($parseErrors[0].Message)" }
            }
        }
        if (!$seen.ContainsKey('tools/install-policy.ps1')) { throw 'Version policy missing from verified package inventory.' }
        . (Join-Path $PSScriptRoot 'install-policy.ps1')
        $null = Get-BapInstallVersion $package.version
    } else {
        throw '검증된 전체 설치 묶음이 필요합니다. 소스 폴더·예전 install.cmd로 다시 설치하지 마세요. / Verified installation manifest required; use the current complete package.'
    }
    $stage = 'Installer startup / parsing'
    & (Join-Path $PSScriptRoot 'install.ps1') -GameRoot $GameRoot -RestartSteam:$RestartSteam -AccountId $AccountId -UpdateOnly:$UpdateOnly -RecoveryPlan $RecoveryPlan
    Write-Host "Installation completed. Version: $env:BAP_INSTALL_VERSION; run: $env:BAP_INSTALL_RUN"
} catch {
    $failure = $_
    if (Get-Command Write-InstallFailure -ErrorAction SilentlyContinue) {
        $null = Write-InstallFailure $failure $stage $root $GameRoot
    } else {
        # Self-contained fallback: a missing/broken helper must still leave a log.
        $message = "BAP-BOOTSTRAP-FAILED`r`nRun: $env:BAP_INSTALL_RUN`r`n" + $failure.Exception.Message
        if ($env:USERPROFILE) { $message = $message.Replace($env:USERPROFILE, '%USERPROFILE%') }
        foreach ($dir in @("$env:LOCALAPPDATA/BetterAstralParty/Logs", "$env:TEMP/BetterAstralParty")) {
            try {
                $null = New-Item -ItemType Directory -Force -Path $dir
                $path = Join-Path $dir ("Bootstrap-$env:BAP_INSTALL_RUN.log")
                [IO.File]::WriteAllText($path, $message)
                Write-Host "Debug log: $path"
                break
            } catch { Write-Host 'Cannot save fallback log; capture this console.' }
        }
    }
    Write-Host $failure.Exception.Message
    exit 1
}
