@echo off
setlocal DisableDelayedExpansion
set "BAP_ENTRYROOT=%~dp0"
set "BAP_MAINTENANCE_CWD=%CD%"
"%SystemRoot%\System32\chcp.com" 65001 >nul
set "BAP_PREVIEW=0"
set "BAP_WINDOW="
if "%~1"=="" set "BAP_WINDOW=--window"
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
>"%BAP_BOOTLOG%" echo BAP-BOOTSTRAP-START: Precompiled native installer.
if not exist "%BAP_BOOTLOG%" (
    if not exist "%TEMP%\BetterAstralParty" mkdir "%TEMP%\BetterAstralParty" 2>nul
    set "BAP_BOOTLOG=%TEMP%\BetterAstralParty\Bootstrap-%RANDOM%-%RANDOM%.log"
)
if not exist "%BAP_BOOTLOG%" >"%BAP_BOOTLOG%" echo BAP-BOOTSTRAP-START: Capture console errors if detailed logs are unavailable.
:native
if not exist "\\?\%BAP_ENTRYROOT%artifacts\BetterAstralParty-Setup.exe" goto missing
pushd "%BAP_ENTRYROOT%artifacts"
if errorlevel 1 goto missing
if defined BAP_WINDOW goto window
if exist "\\?\%BAP_ENTRYROOT%package-manifest.json" goto packaged
".\BetterAstralParty-Setup.exe" --development --install %*
goto result
:packaged
".\BetterAstralParty-Setup.exe" --install %*
:result
set "BAP_EXIT=%errorlevel%"
popd
if "%BAP_PREVIEW%"=="1" goto preview
if not "%BAP_EXIT%"=="0" (
    >>"%BAP_BOOTLOG%" echo BAP-BOOTSTRAP-FAILED: Native installer did not complete. See the console and detailed logs.
    echo Bootstrap log: "%BAP_BOOTLOG%"
    echo Installation was not completed. See the error above.
    pause
    exit /b %BAP_EXIT%
)
>>"%BAP_BOOTLOG%" echo BAP-BOOTSTRAP-OK
pause
exit /b 0
:missing
if "%BAP_PREVIEW%"=="1" (
    echo Native installer missing. No maintenance executed.
    exit /b 1
)
if exist "\\?\%BAP_ENTRYROOT%src\Plugin.cs" goto source
>>"%BAP_BOOTLOG%" echo BAP-PACKAGE-INCOMPLETE: artifacts\BetterAstralParty-Setup.exe missing.
echo Native installer missing. Extract the complete official ZIP again.
echo Bootstrap log: "%BAP_BOOTLOG%"
pause
exit /b 1
:source
rem GitHub's Code - Download ZIP gives this source tree (BetterAstralParty-main.zip), which holds no installer.
>>"%BAP_BOOTLOG%" echo BAP-SOURCE-CHECKOUT: source code, not an installation package.
echo 이 폴더는 소스 코드라서 설치할 수 없습니다. GitHub의 Code - Download ZIP으로 받은 파일은 소스 코드입니다.
echo Releases 페이지에서 BetterAstralParty-^<버전^>-install.zip을 받아 새 폴더에 압축을 풀고 Install.cmd를 실행하세요.
echo This folder is source code and cannot be installed. GitHub's Code - Download ZIP gives the source code.
echo Download BetterAstralParty-^<version^>-install.zip from the Releases page, extract it to a new folder and run Install.cmd.
echo https://github.com/TSM701/BetterAstralParty/releases
pause
exit /b 1
:preview
pause
exit /b %BAP_EXIT%
:window
rem The window shows the result and adds its exit to this log, so the console closes as soon as the window starts.
set "BAP_WINDOW_MODE=--development"
if exist "\\?\%BAP_ENTRYROOT%package-manifest.json" set "BAP_WINDOW_MODE="
start "" ".\BetterAstralParty-Setup.exe" --window %BAP_WINDOW_MODE% --install
set "BAP_EXIT=%errorlevel%"
popd
>>"%BAP_BOOTLOG%" echo BAP-BOOTSTRAP-WINDOW: started %BAP_EXIT%
if "%BAP_EXIT%"=="0" exit /b 0
echo Native installer could not start. No maintenance executed.
echo Bootstrap log: "%BAP_BOOTLOG%"
pause
exit /b %BAP_EXIT%
