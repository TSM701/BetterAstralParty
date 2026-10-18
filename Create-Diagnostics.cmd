@echo off
setlocal DisableDelayedExpansion
"%SystemRoot%\System32\chcp.com" 65001 >nul
if not exist "%~dp0artifacts\BetterAstralParty-Setup.exe" (
    echo BAP-DIAGNOSTICS-TOOL-MISSING: 설치 패키지를 다시 확인하세요. / Check the complete installation package.
    pause
    exit /b 1
)
"%~dp0artifacts\BetterAstralParty-Setup.exe" --diagnostics collect %*
set "BAP_DIAGNOSTICS_EXIT=%ERRORLEVEL%"
pause
exit /b %BAP_DIAGNOSTICS_EXIT%
