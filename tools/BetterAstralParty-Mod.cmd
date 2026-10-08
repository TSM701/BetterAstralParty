@echo off
setlocal
rem Steam launches the dedicated shortcut. No saved options on the original game.
if not exist "%~dp0steam-shortcut.ps1" (
    echo Steam shortcut helper missing. Reinstall BetterAstralParty.
    pause
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0steam-shortcut.ps1" -Mode Launch -GameRoot "%~dp0."
if errorlevel 1 (
    pause
    exit /b 1
)
