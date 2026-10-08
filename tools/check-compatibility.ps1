param(
    [string]$GameRoot = $PSScriptRoot,
    [string]$CacheRoot = (Join-Path $env:USERPROFILE 'AppData/LocalLow/feimo/AstralParty_INT/com.unity.addressables'),
    [string]$Manifest = (Join-Path $PSScriptRoot 'BetterAstralParty.compatibility.json'),
    [string]$LoaderRoot,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'

function Get-CompatibilityReport {
    $issues = [Collections.Generic.List[string]]::new()
    $warnings = [Collections.Generic.List[string]]::new()
    try {
        $baseline = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
        if ($baseline.schema -ne 1 -or @($baseline.files).Count -lt 5) { throw 'Invalid or empty compatibility manifest' }
        $seen = @{}
        foreach ($entry in $baseline.files) {
            if ($entry.root -notin @('game', 'cache') -or [IO.Path]::IsPathRooted($entry.path) -or
                $entry.path -match '(^|[\\/])\.\.([\\/]|$)' -or $entry.path -match ':' -or
                $entry.sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Invalid manifest entry' }
            $key = $entry.root + '/' + $entry.path
            if ($seen.ContainsKey($key)) { throw 'Duplicate manifest entry' }
            $seen[$key] = $true
            $root = if ($entry.root -eq 'game') { $GameRoot } else { $CacheRoot }
            $path = Join-Path $root $entry.path
            # Fresh installer only: validate the exact bundled loader before copying it.
            # Never replace a present mismatching file or substitute game/cache assets.
            if ($LoaderRoot -and !(Test-Path -LiteralPath (Join-Path $GameRoot 'doorstop_config.ini')) -and
                !(Test-Path -LiteralPath $path) -and $entry.root -eq 'game' -and
                ($entry.path -eq 'winhttp.dll' -or $entry.path -match '^BepInEx/core/[^/\\]+\.dll$')) {
                $path = Join-Path $LoaderRoot $entry.path
            }
            if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
                $issues.Add("Missing [$($entry.kind)]: $key")
            } else {
                # Only this resource bundle is advisory; manifest kind cannot downgrade executable checks.
                $resource = $key -eq 'game/AstralParty_INT_Data/data.unity3d'
                if ($resource) {
                    $stream = [IO.File]::OpenRead($path)
                    try {
                        $header = New-Object byte[] 8
                        if ($stream.Length -lt 32 -or $stream.Read($header, 0, 8) -ne 8 -or
                            [Text.Encoding]::ASCII.GetString($header) -ne "UnityFS`0") {
                            $issues.Add("Invalid resource bundle: $key")
                        }
                    } finally { $stream.Dispose() }
                }
                if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) {
                    if ($resource) { $warnings.Add("Resource changed; runtime UI guards remain active: $key") }
                    else { $issues.Add("Changed [$($entry.kind)]: $key") }
                }
            }
        }
        foreach ($required in @('game/AstralParty_INT.exe', 'game/GameAssembly.dll', 'game/UnityPlayer.dll',
            'game/winhttp.dll', 'game/AstralParty_INT_Data/StreamingAssets/aa/catalog.bundle',
            'game/AstralParty_INT_Data/data.unity3d')) {
            if (!$seen.ContainsKey($required)) { throw "Missing required manifest entry: $required" }
        }
        # Detect new catalog versions, not just changes to the previously known filenames.
        $catalogs = @(Get-ChildItem -LiteralPath $CacheRoot -File -ErrorAction Stop |
            Where-Object { $_.Name -match '^catalog.*\.(json|hash)$' } | ForEach-Object Name | Sort-Object)
        if (($catalogs -join '|') -ne (@($baseline.cacheCatalogs) -join '|')) { $issues.Add('Changed [catalog]: downloaded catalog inventory') }
    } catch { $issues.Add("Check failed: $($_.Exception.Message)") }
    [pscustomobject]@{ Allowed = ($issues.Count -eq 0); Issues = @($issues.ToArray()); Warnings = @($warnings.ToArray()) }
}

$report = Get-CompatibilityReport
if ($CheckOnly) { return $report }
$lines = @("BetterAstralParty compatibility check: $(Get-Date -Format o)", "Allowed: $($report.Allowed)") + $report.Issues
$lines += @($report.Warnings | ForEach-Object { "Warning: $_" })
try { $lines | Set-Content -LiteralPath (Join-Path $GameRoot 'BetterAstralParty-Compatibility.log') -Encoding UTF8 }
catch { Write-Warning 'Could not save compatibility log.' }
foreach ($warning in $report.Warnings) { Write-Warning $warning }
if (!$report.Allowed) {
    $lines | ForEach-Object { Write-Host $_ }
    Write-Host 'Mod launch blocked. No game files were changed.'
    Write-Host 'Use AstralParty-Vanilla.cmd, or install a mod build reviewed for this game/translation version.'
    exit 2
}
exit 0
