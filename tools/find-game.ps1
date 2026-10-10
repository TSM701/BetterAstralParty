# Read-only installation discovery. Never pick arbitrarily between real installs.
function Resolve-IntGameDirectory([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    $Path = $Path.Trim().Trim('"').Replace('/', '\')
    # Discovery must tolerate disconnected drives recorded by Steam.
    foreach ($candidate in @($Path, [IO.Path]::Combine($Path, '8vJXnINT'))) {
        if (Test-Path -LiteralPath ([IO.Path]::Combine($candidate, 'AstralParty_INT.exe')) -PathType Leaf) {
            return [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $candidate).ProviderPath).TrimEnd('\')
        }
    }
}

function Find-IntGameDirectories([string]$SteamRoot, [string]$PackageRoot) {
    $libraries = @($SteamRoot)
    $libraryFile = [IO.Path]::Combine($SteamRoot, 'steamapps/libraryfolders.vdf')
    if (Test-Path -LiteralPath $libraryFile -PathType Leaf) {
        $libraries += [regex]::Matches((Get-Content -LiteralPath $libraryFile -Raw), '"path"\s+"([^"]+)"') |
            ForEach-Object { $_.Groups[1].Value.Replace('\\', '\') }
    }
    $candidates = @(
        Resolve-IntGameDirectory $PackageRoot
        Resolve-IntGameDirectory (Split-Path -Parent $PackageRoot)
        foreach ($library in $libraries) {
            $library = $library.Replace('/', '\')
            if (!(Test-Path -LiteralPath $library -PathType Container)) { continue }
            Resolve-IntGameDirectory (Join-Path $library 'steamapps/common/Astral Party')
            $manifest = Join-Path $library 'steamapps/appmanifest_2622000.acf'
            if (Test-Path -LiteralPath $manifest -PathType Leaf) {
                $match = [regex]::Match((Get-Content -LiteralPath $manifest -Raw), '"installdir"\s+"([^"]+)"')
                # Steam installdir is a single directory name, not an arbitrary path.
                if ($match.Success -and $match.Groups[1].Value -notmatch '[\\/:]|^\.{1,2}$') {
                    Resolve-IntGameDirectory (Join-Path $library ('steamapps/common/' + $match.Groups[1].Value))
                }
            }
        }
    )
    $candidates | Sort-Object -Unique
}
