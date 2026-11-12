@echo off
setlocal DisableDelayedExpansion
rem Starts Astral Party once without BetterAstralParty. Steam itself starts the owned app 2622000, and a short-lived
rem request file keeps BetterAstralParty off for that launch. The shared loader and other mods are unchanged.
"%SystemRoot%\System32\tasklist.exe" /FI "IMAGENAME eq AstralParty_INT.exe" /NH | "%SystemRoot%\System32\find.exe" /I "AstralParty_INT.exe" >nul
if not errorlevel 1 (
    echo Close Astral Party before switching between Vanilla and Mod.
    pause
    exit /b 1
)
set "REQUEST=%LOCALAPPDATA%\BetterAstralParty\Runtime"
if not exist "%REQUEST%" mkdir "%REQUEST%" 2>nul
type nul > "%REQUEST%\vanilla-launch.request" 2>nul
if errorlevel 1 (
    echo Could not prepare a launch without BetterAstralParty. Nothing was started.
    pause
    exit /b 1
)
start "" "steam://launch/2622000/dialog"
