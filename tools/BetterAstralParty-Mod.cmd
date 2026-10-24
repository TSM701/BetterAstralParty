@echo off
setlocal DisableDelayedExpansion
rem Steam's "Astral Party - Mod" shortcut. Steam itself starts the owned app 2622000, so the overlay and
rem in-game checkout bind to the real game. A short-lived request file activates BetterAstralParty for that launch.
if not exist "%~dp0AstralParty_INT.exe" (
    echo Astral Party not found next to this file. Reinstall BetterAstralParty.
    pause
    exit /b 1
)
"%SystemRoot%\System32\tasklist.exe" /FI "IMAGENAME eq AstralParty_INT.exe" /NH | "%SystemRoot%\System32\find.exe" /I "AstralParty_INT.exe" >nul
if not errorlevel 1 (
    echo Astral Party is already running. Close it, then start "Astral Party - Mod" again.
    pause
    exit /b 1
)
rem The shared loader is OFF in its own settings: pass the loader switch as launch arguments instead.
"%SystemRoot%\System32\findstr.exe" /R /I /C:"^ *enabled *= *true" "%~dp0doorstop_config.ini" >nul 2>&1
if errorlevel 1 goto arguments
set "REQUEST=%LOCALAPPDATA%\BetterAstralParty\Runtime"
if not exist "%REQUEST%" mkdir "%REQUEST%" 2>nul
type nul > "%REQUEST%\mod-launch.request" 2>nul
if errorlevel 1 goto arguments
start "" "steam://rungameid/2622000"
exit /b 0

:arguments
rem Steam asks to confirm launch arguments each time.
start "" "steam://run/2622000//--doorstop-enabled true --bap-compatibility-checked/"
exit /b 0
