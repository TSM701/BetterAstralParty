@echo off
setlocal DisableDelayedExpansion
rem Starts Astral Party once without BetterAstralParty, or once without any mod. Steam itself starts the owned app 2622000.
rem 1: a short-lived request file keeps BetterAstralParty off for that launch; the shared loader and other mods run.
rem 2: the loader switch passed as launch arguments keeps the shared loader itself off for that launch, so no mod runs,
rem    also when the loader fails before it loads any plugin. Neither choice changes doorstop_config.ini.
rem The screen is plain text in two boxes with console colour and cursor-column sequences (Windows 10 and later); the
rem right edge is placed by column, since Korean glyphs are two columns wide. 1, 2 or Q and Enter choose.
rem chcp reads standard input when it is a file, which would eat the first typed line; it gets none.
"%SystemRoot%\System32\chcp.com" 65001 <nul >nul
title Astral Party - BetterAstralParty
set "E="
set "C=%E%[96m"
set "Y=%E%[93m"
set "W=%E%[97m"
set "D=%E%[90m"
set "R=%E%[0m"
set "BAP_VERSION=-"
if exist "%~dp0BetterAstralParty.update-v5.version" for /f "usebackq tokens=1,* delims==" %%a in ("%~dp0BetterAstralParty.update-v5.version") do if "%%a"=="version" set "BAP_VERSION=%%b"
echo.
echo  %C%┌─ BetterAstralParty ──────────────────────────────────────────┐%R%
echo  %C%│%R% 모드 버전 / Mod version%E%[36G%W%%BAP_VERSION%%R%%E%[65G%C%│%R%
echo  %C%│%R% 패키지 버전 / Package version%E%[36G%W%%BAP_VERSION%%R%%E%[65G%C%│%R%
echo  %C%└──────────────────────────────────────────────────────────────┘%R%
"%SystemRoot%\System32\tasklist.exe" /FI "IMAGENAME eq AstralParty_INT.exe" /NH | "%SystemRoot%\System32\find.exe" /I "AstralParty_INT.exe" >nul
if not errorlevel 1 (
    echo.
    echo  %Y%Astral Party가 실행 중입니다. 게임을 종료한 뒤 다시 실행하세요.%R%
    echo  %D%Astral Party is running. Close it, then run this again.%R%
    echo.
    pause
    exit /b 1
)
echo.
echo  %C%┌─ 실행 방법 / Launch ─────────────────────────────────────────┐%R%
echo  %C%│%R%%E%[65G%C%│%R%
echo  %C%│%R%  %Y%[1]%R% %W%BetterAstralParty만 끄고 실행%R%%E%[65G%C%│%R%
echo  %C%│%R%      한글 패치 같은 다른 모드는 켜집니다.%E%[65G%C%│%R%
echo  %C%│%R%      %D%Start without BetterAstralParty. Other mods still run.%R%%E%[65G%C%│%R%
echo  %C%│%R%%E%[65G%C%│%R%
echo  %C%│%R%  %Y%[2]%R% %W%모든 모드를 끄고 순정으로 실행%R%%E%[65G%C%│%R%
echo  %C%│%R%      Steam이 실행 옵션을 확인합니다.%E%[65G%C%│%R%
echo  %C%│%R%      %D%Start the game without any mod. Steam asks to confirm.%R%%E%[65G%C%│%R%
echo  %C%│%R%%E%[65G%C%│%R%
echo  %C%│%R%  %Y%[Q]%R% 닫기 %D%/ Close%R%%E%[65G%C%│%R%
echo  %C%│%R%%E%[65G%C%│%R%
echo  %C%└──────────────────────────────────────────────────────────────┘%R%
echo  %D%게임 폴더 / Game folder  %~dp0%R%
echo.
rem Typed input confirmed with Enter (user-chosen 2026-11-19): choice.exe beeped at every other key and after Alt+Tab,
rem and set /p is the only built-in input without a beep. The prompt starts with a colour sequence so its spaces stay.
:ask
set "BAP_CHOICE="
set /p "BAP_CHOICE=%R%  > "
if not defined BAP_CHOICE goto again
rem Typed text is compared only inside quotes once its own quotes are gone, so no character in it can run.
set "BAP_CHOICE=%BAP_CHOICE:"=%"
if "%BAP_CHOICE%"=="2" goto unmodded
if /i "%BAP_CHOICE%"=="q" exit /b 0
if "%BAP_CHOICE%"=="1" goto withoutbap
:again
rem Anything else: clear the typed line and ask again, silently; input that never comes (no console) ends it.
set /a BAP_TRIES+=1
if %BAP_TRIES% GEQ 50 exit /b 0
<nul set /p "=%E%[1A%E%[2K"
goto ask

:withoutbap
set "REQUEST=%LOCALAPPDATA%\BetterAstralParty\Runtime"
if not exist "%REQUEST%" mkdir "%REQUEST%" 2>nul
type nul > "%REQUEST%\vanilla-launch.request" 2>nul
if errorlevel 1 (
    echo.
    echo  %Y%BetterAstralParty를 끌 준비를 하지 못해 아무것도 실행하지 않았습니다.%R%
    echo  %D%Could not prepare a launch without BetterAstralParty. Nothing was started.%R%
    echo.
    pause
    exit /b 1
)
start "" "steam://launch/2622000/dialog"
exit /b 0

:unmodded
rem Steam asks to confirm launch arguments each time.
start "" "steam://run/2622000//--doorstop-enabled false/"
exit /b 0
