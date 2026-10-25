@echo off
setlocal DisableDelayedExpansion
"%SystemRoot%\System32\tasklist.exe" /FI "IMAGENAME eq AstralParty_INT.exe" /NH | "%SystemRoot%\System32\find.exe" /I "AstralParty_INT.exe" >nul
if not errorlevel 1 (
    echo Close Astral Party before switching between Vanilla and Mod.
    pause
    exit /b 1
)
start "" "steam://launch/2622000/dialog"
