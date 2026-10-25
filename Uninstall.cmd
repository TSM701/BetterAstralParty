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
>"%BAP_BOOTLOG%" echo BAP-BOOTSTRAP-START: Precompiled native uninstaller.
:native
if not exist "\\?\%BAP_ENTRYROOT%artifacts\BetterAstralParty-Setup.exe" goto missing
pushd "%BAP_ENTRYROOT%artifacts"
if errorlevel 1 goto missing
if exist "\\?\%BAP_ENTRYROOT%package-manifest.json" goto packaged
"BetterAstralParty-Setup.exe" --development --uninstall %*
goto result
:packaged
"BetterAstralParty-Setup.exe" --uninstall %*
:result
set "BAP_EXIT=%errorlevel%"
popd
if "%BAP_PREVIEW%"=="1" goto preview
if not "%BAP_EXIT%"=="0" (
    >>"%BAP_BOOTLOG%" echo BAP-BOOTSTRAP-FAILED: Native uninstaller did not complete. Retain the console error and recovery backups.
    echo Uninstall failed. Keep the error and backup path shown above.
    echo Bootstrap log: "%BAP_BOOTLOG%"
    pause
    exit /b %BAP_EXIT%
)
>>"%BAP_BOOTLOG%" echo BAP-BOOTSTRAP-OK
pause
exit /b 0
:missing
if "%BAP_PREVIEW%"=="1" (
    echo Native uninstaller missing. No maintenance executed.
    exit /b 1
)
>>"%BAP_BOOTLOG%" echo BAP-PACKAGE-INCOMPLETE: artifacts\BetterAstralParty-Setup.exe missing.
echo Native uninstaller missing. Use the complete official ZIP; no game files were removed.
echo Bootstrap log: "%BAP_BOOTLOG%"
pause
exit /b 1
:preview
pause
exit /b %BAP_EXIT%
