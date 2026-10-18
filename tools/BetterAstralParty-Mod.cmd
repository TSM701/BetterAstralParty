@echo off
setlocal DisableDelayedExpansion
rem Steam's "Astral Party - Mod" shortcut: start the game with the shared loader and BetterAstralParty active.
if not exist "%~dp0AstralParty_INT.exe" (
    echo Astral Party not found next to this file. Reinstall BetterAstralParty.
    pause
    exit /b 1
)
set SteamAppId=2622000
set SteamGameId=2622000
set SteamOverlayGameId=2622000
set BAP_COMPATIBILITY_CHECKED=1
cd /d "%~dp0"
start "" "%~dp0AstralParty_INT.exe" --doorstop-enabled true --bap-compatibility-checked
