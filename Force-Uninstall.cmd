@echo off
setlocal DisableDelayedExpansion
set "BAP_ENTRYROOT=%~dp0"
set "BAP_MAINTENANCE_CWD=%CD%"
"%SystemRoot%\System32\chcp.com" 65001 >nul
set "BAP_PREVIEW=0"
:arguments
if "%~1"=="" goto bootstrap
if /i "%~1"=="-WhatIf" set "BAP_PREVIEW=1"
if /i "%~1"=="--WhatIf" set "BAP_PREVIEW=1"
if /i "%~1"=="-CheckOnly" set "BAP_PREVIEW=1"
if /i "%~1"=="--CheckOnly" set "BAP_PREVIEW=1"
shift /1
goto arguments
:bootstrap
if "%BAP_PREVIEW%"=="1" goto native
set "BAP_BOOTLOG=%LOCALAPPDATA%\BetterAstralParty\Logs"
if not exist "%BAP_BOOTLOG%" mkdir "%BAP_BOOTLOG%" 2>nul
if not exist "%BAP_BOOTLOG%" set "BAP_BOOTLOG=%TEMP%\BetterAstralParty"
if not exist "%BAP_BOOTLOG%" mkdir "%BAP_BOOTLOG%" 2>nul
set "BAP_BOOTLOG=%BAP_BOOTLOG%\Bootstrap-%RANDOM%-%RANDOM%.log"
>"%BAP_BOOTLOG%" echo BAP-BOOTSTRAP-START: Precompiled native emergency removal.
:native
if not exist "\\?\%BAP_ENTRYROOT%artifacts\BetterAstralParty-Launcher.exe" goto missing
pushd "%BAP_ENTRYROOT%artifacts"
if errorlevel 1 goto missing
if exist "\\?\%BAP_ENTRYROOT%package-manifest.json" goto packaged
"BetterAstralParty-Launcher.exe" --development --force-uninstall %*
goto result
:packaged
"BetterAstralParty-Launcher.exe" --force-uninstall %*
:result
set "BAP_EXIT=%errorlevel%"
popd
if "%BAP_PREVIEW%"=="1" goto preview
if not "%BAP_EXIT%"=="0" (
    >>"%BAP_BOOTLOG%" echo BAP-BOOTSTRAP-FAILED: Emergency removal stopped. Deleted files cannot be restored; retain remaining files and logs.
    echo Emergency removal stopped. Deleted files cannot be restored. Keep the remaining files and logs for diagnosis.
    echo Bootstrap log: "%BAP_BOOTLOG%"
    pause
    exit /b %BAP_EXIT%
)
>>"%BAP_BOOTLOG%" echo BAP-BOOTSTRAP-OK
pause
exit /b 0
:missing
if "%BAP_PREVIEW%"=="1" (
    echo Native emergency remover missing. No maintenance executed.
    exit /b 1
)
>>"%BAP_BOOTLOG%" echo BAP-PACKAGE-INCOMPLETE: artifacts\BetterAstralParty-Launcher.exe missing.
echo Native emergency remover missing. Use the complete official ZIP; no game files were removed.
echo Bootstrap log: "%BAP_BOOTLOG%"
pause
exit /b 1
:preview
pause
exit /b %BAP_EXIT%
