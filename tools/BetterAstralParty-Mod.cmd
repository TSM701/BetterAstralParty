@echo off
setlocal DisableDelayedExpansion
rem Steam's dedicated shortcut targets the same precompiled launcher.
if not exist "%~dp0BetterAstralParty-Launcher.exe" (
    echo Mod launcher missing. Reinstall BetterAstralParty.
    pause
    exit /b 1
)
"%~dp0BetterAstralParty-Launcher.exe"
if errorlevel 1 (
    pause
    exit /b 1
)
