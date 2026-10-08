param(
    [ValidateSet('Register','Find','Launch','ResolveAccount','PlanRegister','PlanRemove','Remove')][string]$Mode = 'Find',
    [string]$GameRoot = $PSScriptRoot,
    [string]$SteamRoot = (Get-ItemProperty 'HKCU:/Software/Valve/Steam').SteamPath,
    [uint32]$AccountId = 0
)
$ErrorActionPreference = 'Stop'

function Resolve-SteamAccount([string]$SteamRoot, [uint32]$RequestedId, [uint32]$ActiveId, [string]$AutoLoginUser) {
    # Resolve a shortcut destination, NOT an authentication/session assertion.
    function Has-Profile([uint32]$Id) {
        return $Id -ne 0 -and (Test-Path -LiteralPath (Join-Path $SteamRoot "userdata/$Id/config") -PathType Container)
    }
    if ($RequestedId) {
        if (!(Has-Profile $RequestedId)) { throw 'Requested Steam profile has no local configuration directory.' }
        return [pscustomobject]@{AccountId=$RequestedId; Source='explicit'}
    }
    if (Has-Profile $ActiveId) { return [pscustomobject]@{AccountId=$ActiveId; Source='active registry'} }
    $saved = Join-Path $SteamRoot 'config/loginusers.vdf'
    $automatic = @(); $recent = @()
    if (Test-Path -LiteralPath $saved -PathType Leaf) {
        if ((Get-Item -LiteralPath $saved).Length -gt 1048576) { throw 'Steam profile metadata is too large; specify -AccountId explicitly.' }
        $text = [IO.File]::ReadAllText($saved)
        # Only flat user records; never log names, timestamps or the full file.
        foreach ($match in [regex]::Matches($text, '"(?<id>\d{17})"\s*\{(?<body>[^{}]*)\}')) {
            $steamId = [uint64]$match.Groups['id'].Value
            # Public individual SteamID64 = 76561197960265728 + uint32 account ID.
            if ($steamId -le 76561197960265728 -or $steamId -gt 76561202255233023) { continue }
            $id = [uint32]($steamId - 76561197960265728)
            if (!(Has-Profile $id)) { continue }
            $body = $match.Groups['body'].Value
            $names = [regex]::Matches($body, '"AccountName"\s*"([^"\r\n]*)"')
            $flags = [regex]::Matches($body, '"MostRecent"\s*"([01])"')
            if ($names.Count -gt 1 -or $flags.Count -gt 1) { throw 'Ambiguous Steam profile metadata; specify -AccountId explicitly.' }
            if ($AutoLoginUser -and $names.Count -eq 1 -and $names[0].Groups[1].Value -eq $AutoLoginUser) { $automatic += $id }
            if ($flags.Count -eq 1 -and $flags[0].Groups[1].Value -eq '1') { $recent += $id }
        }
    }
    if ($automatic.Count -gt 1 -or $recent.Count -gt 1 -or
        ($automatic.Count -eq 1 -and $recent.Count -eq 1 -and $automatic[0] -ne $recent[0])) {
        throw 'Conflicting Steam profile metadata; specify -AccountId explicitly. No files changed.'
    }
    if ($automatic.Count -eq 1) { return [pscustomobject]@{AccountId=$automatic[0]; Source='saved auto-login profile'} }
    if ($recent.Count -eq 1) { return [pscustomobject]@{AccountId=$recent[0]; Source='saved most-recent profile'} }
    $profiles = @(Get-ChildItem -LiteralPath (Join-Path $SteamRoot 'userdata') -Directory -ErrorAction SilentlyContinue | Where-Object {
        $number = [uint32]0
        [uint32]::TryParse($_.Name, [ref]$number) -and $_.Name -eq $number.ToString() -and (Has-Profile $number)
    })
    if ($profiles.Count -eq 1) { return [pscustomobject]@{AccountId=[uint32]$profiles[0].Name; Source='only local profile'} }
    throw 'Cannot uniquely identify the Steam shortcut profile (this does not mean signed out). Specify -AccountId using the intended userdata folder number. No files changed.'
}

# Only the binary KeyValues types used by shortcuts are accepted. Unknown formats
# fail without writing, rather than risking another application's shortcuts.
function Read-ZeroString($reader) {
    $bytes = [Collections.Generic.List[byte]]::new()
    while (($b = $reader.ReadByte()) -ne 0) {
        $bytes.Add($b)
        if ($bytes.Count -gt 1048576) { throw 'Invalid shortcut string length' }
    }
    return [Text.UTF8Encoding]::new($false, $true).GetString($bytes.ToArray())
}
function Read-VdfObject($reader, [int]$depth = 0) {
    if ($depth -gt 16) { throw 'Invalid shortcut nesting' }
    $result = [ordered]@{}
    while (($type = $reader.ReadByte()) -ne 8) {
        $key = Read-ZeroString $reader
        if ($result.Contains($key)) { throw 'Duplicate shortcut key' }
        switch ($type) {
            0 { $result[$key] = Read-VdfObject $reader ($depth + 1) }
            1 { $result[$key] = Read-ZeroString $reader }
            2 { $result[$key] = $reader.ReadUInt32() }
            default { throw "Unsupported shortcut field type: $type" }
        }
    }
    return $result
}
function Write-ZeroString($writer, [string]$value) {
    $writer.Write([Text.Encoding]::UTF8.GetBytes($value)); $writer.Write([byte]0)
}
function Write-VdfObject($writer, $value) {
    foreach ($key in $value.Keys) {
        $item = $value[$key]
        if ($item -is [Collections.IDictionary]) { $type = 0 }
        elseif ($item -is [string]) { $type = 1 }
        elseif ($item -is [uint32]) { $type = 2 }
        else { throw 'Unsupported shortcut value' }
        $writer.Write([byte]$type); Write-ZeroString $writer $key
        switch ($type) {
            0 { Write-VdfObject $writer $item }
            1 { Write-ZeroString $writer $item }
            2 { $writer.Write([uint32]$item) }
        }
    }
    $writer.Write([byte]8)
}
function Read-Shortcuts([byte[]]$bytes) {
    $stream = [IO.MemoryStream]::new($bytes, $false)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        $root = Read-VdfObject $reader
        if ($stream.Position -ne $stream.Length -or $root.Count -ne 1 -or
            $root['shortcuts'] -isnot [Collections.IDictionary]) { throw 'Invalid shortcuts root' }
        return $root
    } finally { $reader.Dispose() }
}
function ConvertTo-ShortcutBytes($root) {
    $stream = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($stream)
    try { Write-VdfObject $writer $root; return ,$stream.ToArray() }
    finally { $writer.Dispose() }
}

# Compatibility cleanup for banners installed by older versions. New installs do not set artwork.
function Get-LegacyHeroPaths([string]$Config, [uint32]$Id) {
    $image = Join-Path $Config "grid/${Id}_hero.png"
    return [pscustomobject]@{ Image=$image; Receipt=($image + '.BetterAstralParty.json') }
}
function Remove-LegacyHero([string]$Config, [uint32]$Id, [string]$GameRoot) {
    $hero = Get-LegacyHeroPaths $Config $Id
    $moved = @()
    try {
        if (!(Test-Path -LiteralPath $hero.Receipt -PathType Leaf)) { return }
        foreach ($path in @($Config, (Join-Path $Config 'grid'), $hero.Receipt, $hero.Image)) {
            if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { return }
        }
        if ((Get-Item -LiteralPath $hero.Receipt).Length -gt 4096) { return }
        $receipt = Get-Content -LiteralPath $hero.Receipt -Raw | ConvertFrom-Json
        if ($receipt -isnot [pscustomobject] -or @($receipt.PSObject.Properties).Count -ne 4 -or
            $receipt.Owner -isnot [string] -or $receipt.Owner -cne 'kr.betterastralparty.mod' -or
            $receipt.GameRoot -isnot [string] -or $receipt.GameRoot -ne $GameRoot -or
            ($receipt.AppId -isnot [int] -and $receipt.AppId -isnot [long] -and $receipt.AppId -isnot [uint32]) -or
            $receipt.AppId -ne $Id -or $receipt.Hash -isnot [string] -or $receipt.Hash -cnotmatch '^[A-Fa-f0-9]{64}$') { return }
        $targets = @($hero.Receipt)
        if ((Test-Path -LiteralPath $hero.Image -PathType Leaf) -and
            (Get-FileHash -LiteralPath $hero.Image).Hash -eq $receipt.Hash) { $targets = @($hero.Image) + $targets }
        foreach ($target in $targets) {
            $backup = $target + '.BetterAstralParty-' + [guid]::NewGuid().ToString('N') + '.bak'
            [IO.File]::Move($target, $backup)
            # Record the move before fallible reads. Verify the object actually moved, since
            # another writer may replace the path after the eligibility hash was read.
            $item = [pscustomobject]@{Path=$target; Backup=$backup; Hash=$null}
            $moved += $item
            $stream = [IO.File]::Open($backup, 'Open', 'Read', 'Read')
            try {
                $sha = [Security.Cryptography.SHA256]::Create()
                try { $item.Hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
                finally { $sha.Dispose() }
            } finally { $stream.Dispose() }
            if ($target -eq $hero.Image -and $item.Hash -cne $receipt.Hash.ToUpperInvariant()) {
                # A custom replacement is returned only to an empty original name. Never
                # overwrite a newer image; a conflicting path leaves the backup for review.
                if (!(Test-Path -LiteralPath $target)) { [IO.File]::Copy($backup, $target, $false) }
                return
            }
        }
        return $moved
    } catch {
        foreach ($item in $moved) {
            if (!(Test-Path -LiteralPath $item.Path)) { [IO.File]::Copy($item.Backup, $item.Path, $false) }
        }
        Write-Warning "Steam 배경 정리는 보류되었습니다. / Hero cleanup retained for review: $($_.Exception.Message)"
    }
}

$registry = Get-ItemProperty 'HKCU:/Software/Valve/Steam' -ErrorAction SilentlyContinue
$active = Get-ItemProperty 'HKCU:/Software/Valve/Steam/ActiveProcess' -ErrorAction SilentlyContinue
$resolved = Resolve-SteamAccount $SteamRoot $AccountId ([uint32]$active.ActiveUser) $registry.AutoLoginUser
$AccountId = $resolved.AccountId
if ($Mode -eq 'ResolveAccount') {
    Write-Host "Steam shortcut destination: $($resolved.Source). This is not a live login check."
    return $AccountId
}
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
$entry = Join-Path $GameRoot 'BetterAstralParty-Steam.ps1'
if ($Mode -notin @('PlanRegister','PlanRemove','Remove') -and !(Test-Path -LiteralPath $entry)) { throw "Missing Steam entry launcher: $entry" }
$path = Join-Path $SteamRoot "userdata/$AccountId/config/shortcuts.vdf"
$original = if (Test-Path -LiteralPath $path) { [IO.File]::ReadAllBytes($path) } else { $null }
$root = if ($null -ne $original) { Read-Shortcuts $original } else { [ordered]@{shortcuts=[ordered]@{}} }
$items = $root['shortcuts']
$legacyExe = '"' + (Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe') + '"'
$legacyOptions = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $entry + '"'
$launcher = Join-Path $GameRoot 'BetterAstralParty-Launcher.exe'
$exe = '"' + $launcher + '"'
$launchOptions = ''
$owned = @($items.Values | Where-Object {
    $_['AppName'] -eq 'Astral Party - Mod' -and (
        ($_['exe'] -eq $exe -and $_['LaunchOptions'] -eq $launchOptions) -or
        ($_['exe'] -eq $legacyExe -and $_['LaunchOptions'] -eq $legacyOptions))
})
if ($owned.Count -gt 1) { throw 'Duplicate mod shortcuts; refusing to choose one.' }
$configDirectory = Split-Path -Parent $path
if ($Mode -eq 'PlanRegister') {
    return ($owned.Count -ne 1 -or $owned[0]['exe'] -ne $exe)
}
if ($Mode -eq 'PlanRemove') { return ($owned.Count -eq 1) }
if ($Mode -eq 'Remove' -and $owned.Count -eq 0) { return }
if ($Mode -in @('Find','Launch')) {
    if ($owned.Count -ne 1) { throw 'Mod Steam shortcut missing. Run the installer first.' }
    $gameId = (([uint64]$owned[0]['appid'] -shl 32) -bor [uint64]33554432).ToString()
    if ($Mode -eq 'Find') { return $gameId }
    if (Get-Process AstralParty_INT,AstralParty_CN -ErrorAction SilentlyContinue) { throw 'Close Astral Party first.' }
    Start-Process "steam://rungameid/$gameId" -WindowStyle Hidden
    return
}
if (Get-Process steam -ErrorAction SilentlyContinue) { throw 'Close Steam normally before registering the mod shortcut.' }
if ($Mode -eq 'Remove') {
    foreach ($key in @($items.Keys)) {
        if ([object]::ReferenceEquals($items[$key], $owned[0])) { $items.Remove($key); break }
    }
} else {
if (!(Test-Path -LiteralPath $launcher -PathType Leaf)) { throw "Missing windowless launcher: $launcher" }
if ($owned.Count -eq 1 -and $owned[0]['exe'] -eq $exe) {
    Write-Host 'Mod Steam shortcut already registered.'; return
}
if ($owned.Count -eq 1) {
    # Preserve appid, overlay settings, artwork associations and user fields.
    $owned[0]['exe'] = $exe
    $owned[0]['LaunchOptions'] = $launchOptions
} else {
$idBytes = [guid]::NewGuid().ToByteArray()
$appId = [BitConverter]::ToUInt32($idBytes, 0) -bor [uint32]2147483648
while (@($items.Values | Where-Object { $_['appid'] -eq $appId }).Count) { $appId++ }
$index = 0
while ($items.Contains($index.ToString())) { $index++ }
$items[$index.ToString()] = [ordered]@{
    appid=[uint32]$appId; AppName='Astral Party - Mod'; exe=$exe
    StartDir=('"' + $GameRoot + '"'); icon=(Join-Path $GameRoot 'AstralParty_INT.exe')
    ShortcutPath=''; LaunchOptions=$launchOptions; IsHidden=[uint32]0
    AllowDesktopConfig=[uint32]1; AllowOverlay=[uint32]1; OpenVR=[uint32]0
    Devkit=[uint32]0; DevkitGameID=''; LastPlayTime=[uint32]0; tags=[ordered]@{}
}
}
}
$bytes = ConvertTo-ShortcutBytes $root
$null = Read-Shortcuts $bytes
$directory = Split-Path -Parent $path
if (!(Test-Path -LiteralPath $directory)) { throw 'Steam account configuration directory missing.' }
$temp = $path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
[IO.File]::WriteAllBytes($temp, $bytes)
if (Get-Process steam -ErrorAction SilentlyContinue) { throw "Steam restarted; registration cancelled. Temporary file: $temp" }
if ($null -ne $original) {
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -ne [Convert]::ToBase64String($original)) {
        throw 'Steam shortcuts changed during registration; cancelled.'
    }
    $backup = $path + '.BetterAstralParty-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.bak'
    [IO.File]::Replace($temp, $path, $backup)
    Write-Host "Existing shortcuts backed up: $backup"
} else { [IO.File]::Move($temp, $path) }
if ($Mode -eq 'Remove') {
    # Return recovery metadata only to the uninstaller, not to a persistent install receipt.
    $artwork = @(Remove-LegacyHero $configDirectory $owned[0]['appid'] $GameRoot)
    return [pscustomobject]@{ Path=$path; Backup=$backup; Hash=(Get-FileHash -LiteralPath $path).Hash; Artwork=$artwork }
}
if ($owned.Count -eq 1) { $appId = $owned[0]['appid'] }
Write-Host 'Registered Astral Party - Mod. Restart Steam to load the shortcut.'
