# Building BetterAstralParty

The root MIT LICENSE covers BetterAstralParty-owned code. Third-party libraries
retain their own licenses; see THIRD-PARTY-NOTICES/component-matrix.json and
THIRD-PARTY-NOTICES/SOURCES.md. Game assemblies, generated interop assemblies, Unity reference
DLLs, fonts and Steam artwork are supplied locally by their authorized owners.

## Local prerequisites

The verified toolchain is Windows x64, .NET SDK 8.0.425, PowerShell 7 and the
local Windows Framework64/v4.0.30319 reference assemblies. The plugin targets
.NET 6 and requires Microsoft.NETCore.App.Ref 6.0.36 locally; install the
corresponding .NET 6 SDK/targeting pack before an offline restore. The source
export contains neither an SDK nor proprietary references. global.json pins
the tested compiler. The helper also explicitly selects Roslyn 8.0.425.

Legally install the matching INT(Global) game and initialize its supported
BepInEx loader to generate game interop references. Obtain the unchanged
BepInEx Unity IL2CPP Windows x64 build 788 (6.0.0-be.788+5b766a3) from
https://builds.bepinex.dev/projects/bepinex_be . Match the original archive
SHA-256 F4CC496BD098A0DF4164B81E3737297707F13A47C2478DBA2F60EEFAB784817A
and component versions/hashes recorded in component-matrix.json. Keep the
clean loader and the installed game's BepInEx/interop directory separate.
Do not substitute assemblies from an unrelated game or loader version.

## Plugin, launcher and helper

Use the explicit compilation commands below for a public source checkout. They
build the plugin, launcher and helper without the developer workspace's local
maintenance checks or extracted game-research files. The source archive is not
an installation package. Compilation does not install files, modify Steam,
start the game, sign a release or establish in-game correctness.

Run the individual tools from the source root. Replace the following example paths with
your local SDK, installed INT root, clean loader root and fresh output paths:

```powershell
$dotnet = 'C:\LocalSdk\dotnet.exe'
$game = 'C:\LocalGame\INT'
$loader = 'C:\LocalLoader'
& $dotnet restore .\src\BetterAstralParty.csproj --configfile .\offline-nuget.config "-p:GameRoot=$game" "-p:BepInExRoot=$loader" -p:NuGetAudit=false
& $dotnet build .\src\BetterAstralParty.csproj -c Release --no-restore "-p:GameRoot=$game" "-p:BepInExRoot=$loader"
.\tools\build-launcher.ps1 -DotnetRoot 'C:\LocalSdk' -OutputDirectory 'C:\LocalBuild\launcher'
.\tools\build-update-helper.ps1 -DotnetRoot 'C:\LocalSdk' -OutputDirectory 'C:\LocalBuild\helper'
```

Builds map source paths to /src/BetterAstralParty and are deterministic. Inspect
CodeView and embedded PDB document names before publication; do not patch debug
records in compiled images. All required BetterAstralParty compilation units
are included. Licensed game/Unity references remain external build inputs.

## Source and release packages

Public source contains BetterAstralParty-owned compilation units and build
instructions, not a prepared loader installation, game references or a private
signing key. Use a verified whole release package for ordinary installation.
The application source, third-party notices and corresponding source archives
must be delivered for the exact release they describe. A source review snapshot
does not establish that a GA release has been published or tested in game.

## LGPL component replacement

To rebuild or replace LGPL components, extract the matching THIRD-PARTY-SOURCE
archives and follow their upstream build instructions. SOURCES.md documents the
removed proprietary reference DLLs and replacement/relinking procedure. No game
license is supplied by this source export, and BetterAstralParty imposes no extra
restriction on modifying or debugging those libraries.
